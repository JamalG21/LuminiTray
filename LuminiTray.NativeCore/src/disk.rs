use std::collections::HashMap;
use std::ffi::OsString;
use std::os::windows::ffi::{OsStrExt, OsStringExt};
use std::path::Path;
use std::sync::atomic::{AtomicU8, Ordering};

const INVALID_HANDLE_VALUE: isize = -1;
const FILE_ATTRIBUTE_DIRECTORY: u32 = 0x10;
const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x400;
const FIND_EX_INFO_BASIC: i32 = 1;
const FIND_EX_SEARCH_NAME_MATCH: i32 = 0;
const FIND_FIRST_EX_LARGE_FETCH: u32 = 2;
const ERROR_NO_MORE_FILES: u32 = 18;

#[repr(C)]
#[derive(Clone, Copy)]
struct FileTime {
    dw_low_date_time: u32,
    dw_high_date_time: u32,
}

#[repr(C)]
struct Win32FindDataW {
    dw_file_attributes: u32,
    ft_creation_time: FileTime,
    ft_last_access_time: FileTime,
    ft_last_write_time: FileTime,
    n_file_size_high: u32,
    n_file_size_low: u32,
    dw_reserved0: u32,
    dw_reserved1: u32,
    c_file_name: [u16; 260],
    c_alternate_file_name: [u16; 14],
}

#[link(name = "kernel32")]
extern "system" {
    fn FindFirstFileExW(
        lp_file_name: *const u16,
        f_info_level_id: i32,
        lp_find_file_data: *mut Win32FindDataW,
        f_search_op: i32,
        lp_search_filter: *const u8,
        dw_additional_flags: u32,
    ) -> isize;
    fn FindNextFileW(h_find_file: isize, lp_find_file_data: *mut Win32FindDataW) -> i32;
    fn FindClose(h_find_file: isize) -> i32;
    fn GetLastError() -> u32;
}

pub type ProgressCallback = unsafe extern "C" fn(i32);

struct FileEntry {
    name: String,
    size: u64,
}

pub struct DirNode {
    pub name: String,
    pub total_size: u64,
    pub file_count: i32,
    pub folder_count: i32,
    files: Vec<FileEntry>,
    pub subdirs: Vec<DirNode>,
}

pub struct ScanOutput {
    pub root: DirNode,
    pub access_denied_count: i32,
    pub reparse_count: i32,
    pub extensions: HashMap<String, (u64, i32)>,
}

struct FindHandle(isize);
impl Drop for FindHandle {
    fn drop(&mut self) {
        if self.0 != INVALID_HANDLE_VALUE && self.0 != 0 {
            unsafe {
                FindClose(self.0);
            }
        }
    }
}

fn utf16_name_slice(buf: &[u16; 260]) -> &[u16] {
    let len = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
    &buf[..len]
}

fn extract_lowercase_ext(name: &str) -> String {
    match name.rfind('.') {
        Some(idx) if idx < name.len() => name[idx..].to_lowercase(),
        _ => String::new(),
    }
}

fn make_search_pattern(dir: &Path) -> Vec<u16> {
    let mut wide: Vec<u16> = dir.as_os_str().encode_wide().collect();
    if let Some(&last) = wide.last() {
        if last != b'\\' as u16 && last != b'/' as u16 {
            wide.push(b'\\' as u16);
        }
    }
    wide.push(b'*' as u16);
    wide.push(0);
    wide
}

fn is_cancelled(cancel_ptr: *const u8) -> bool {
    if cancel_ptr.is_null() {
        return false;
    }
    unsafe { (&*(cancel_ptr as *const AtomicU8)).load(Ordering::Relaxed) != 0 }
}

#[allow(clippy::too_many_arguments)]
fn scan_dir(
    dir_path: &Path,
    node_name: String,
    max_files_per_dir: usize,
    scanned_items: &mut i32,
    access_denied_count: &mut i32,
    reparse_count: &mut i32,
    extensions: &mut HashMap<String, (u64, i32)>,
    cancel_ptr: *const u8,
    progress_cb: Option<ProgressCallback>,
) -> Option<DirNode> {
    if is_cancelled(cancel_ptr) {
        return None;
    }

    *scanned_items += 1;
    if *scanned_items % 150 == 0 {
        if let Some(cb) = progress_cb {
            unsafe { cb(*scanned_items) };
        }
    }

    let pattern = make_search_pattern(dir_path);
    let mut data = std::mem::MaybeUninit::<Win32FindDataW>::zeroed();
    let handle = unsafe {
        FindFirstFileExW(
            pattern.as_ptr(),
            FIND_EX_INFO_BASIC,
            data.as_mut_ptr(),
            FIND_EX_SEARCH_NAME_MATCH,
            std::ptr::null(),
            FIND_FIRST_EX_LARGE_FETCH,
        )
    };

    let mut total_size: u64 = 0;
    let mut file_count: i32 = 0;
    let mut folder_count: i32 = 0;
    let mut files: Vec<FileEntry> = Vec::new();
    let mut subdirs: Vec<DirNode> = Vec::new();
    let mut pending_subpaths: Vec<(std::path::PathBuf, String)> = Vec::new();

    if handle == INVALID_HANDLE_VALUE {
        *access_denied_count += 1;
    } else {
        let _guard = FindHandle(handle);
        loop {
            if is_cancelled(cancel_ptr) {
                return None;
            }

            let entry = unsafe { data.assume_init_ref() };
            let name_u16 = utf16_name_slice(&entry.c_file_name);
            let is_dot = name_u16 == [b'.' as u16] || name_u16 == [b'.' as u16, b'.' as u16];

            if !is_dot && !name_u16.is_empty() {
                let name = OsString::from_wide(name_u16).to_string_lossy().into_owned();
                let attrs = entry.dw_file_attributes;

                if (attrs & FILE_ATTRIBUTE_DIRECTORY) != 0 {
                    if (attrs & FILE_ATTRIBUTE_REPARSE_POINT) != 0 {
                        *reparse_count += 1;
                    } else {
                        pending_subpaths.push((dir_path.join(&name), name));
                    }
                } else {
                    let fsize =
                        ((entry.n_file_size_high as u64) << 32) | (entry.n_file_size_low as u64);
                    total_size = total_size.saturating_add(fsize);
                    file_count = file_count.saturating_add(1);

                    let ext = extract_lowercase_ext(&name);
                    extensions
                        .entry(ext)
                        .and_modify(|e| {
                            e.0 = e.0.saturating_add(fsize);
                            e.1 = e.1.saturating_add(1);
                        })
                        .or_insert((fsize, 1));

                    files.push(FileEntry { name, size: fsize });
                }
            }

            let ok = unsafe { FindNextFileW(handle, data.as_mut_ptr()) };
            if ok == 0 {
                let err = unsafe { GetLastError() };
                if err != ERROR_NO_MORE_FILES && err != 0 {
                    *access_denied_count += 1;
                }
                break;
            }
        }
    }

    // Ordena arquivos antes de podar para manter apenas os maiores na árvore se max_files_per_dir > 0,
    // preservando total_size e file_count exatos.
    if max_files_per_dir > 0 && files.len() > max_files_per_dir {
        files.sort_unstable_by_key(|a| std::cmp::Reverse(a.size));
        files.truncate(max_files_per_dir);
    }

    for (sub_path, sub_name) in pending_subpaths {
        folder_count = folder_count.saturating_add(1);
        let sub_node = scan_dir(
            &sub_path,
            sub_name,
            max_files_per_dir,
            scanned_items,
            access_denied_count,
            reparse_count,
            extensions,
            cancel_ptr,
            progress_cb,
        )?;
        total_size = total_size.saturating_add(sub_node.total_size);
        file_count = file_count.saturating_add(sub_node.file_count);
        folder_count = folder_count.saturating_add(sub_node.folder_count);
        subdirs.push(sub_node);
    }

    subdirs.sort_unstable_by_key(|a| std::cmp::Reverse(a.total_size));

    Some(DirNode {
        name: node_name,
        total_size,
        file_count,
        folder_count,
        files,
        subdirs,
    })
}

pub fn scan_tree(
    root_path: &Path,
    max_files_per_dir: usize,
    cancel_ptr: *const u8,
    progress_cb: Option<ProgressCallback>,
) -> Option<ScanOutput> {
    let root_name = root_path
        .file_name()
        .and_then(|s| s.to_str())
        .map(|s| s.to_string())
        .filter(|s| !s.is_empty())
        .unwrap_or_else(|| root_path.to_string_lossy().into_owned());

    let mut scanned_items: i32 = 0;
    let mut access_denied_count: i32 = 0;
    let mut reparse_count: i32 = 0;
    let mut extensions: HashMap<String, (u64, i32)> = HashMap::new();

    let root = scan_dir(
        root_path,
        root_name,
        max_files_per_dir,
        &mut scanned_items,
        &mut access_denied_count,
        &mut reparse_count,
        &mut extensions,
        cancel_ptr,
        progress_cb,
    )?;

    Some(ScanOutput {
        root,
        access_denied_count,
        reparse_count,
        extensions,
    })
}

fn write_str(out: &mut Vec<u8>, s: &str) {
    let bytes = s.as_bytes();
    let len = bytes.len().min(u16::MAX as usize);
    out.extend_from_slice(&(len as u16).to_le_bytes());
    out.extend_from_slice(&bytes[..len]);
}

fn write_node(out: &mut Vec<u8>, node: &DirNode) {
    write_str(out, &node.name);
    out.extend_from_slice(&node.total_size.to_le_bytes());
    out.extend_from_slice(&node.file_count.to_le_bytes());
    out.extend_from_slice(&node.folder_count.to_le_bytes());

    out.extend_from_slice(&(node.files.len() as u32).to_le_bytes());
    for f in &node.files {
        write_str(out, &f.name);
        out.extend_from_slice(&f.size.to_le_bytes());
    }

    out.extend_from_slice(&(node.subdirs.len() as u32).to_le_bytes());
    for sub in &node.subdirs {
        write_node(out, sub);
    }
}

pub fn serialize_scan_output(output: &ScanOutput) -> Vec<u8> {
    let mut buf = Vec::with_capacity(64 * 1024);
    buf.extend_from_slice(&0x4C54_4453u32.to_le_bytes()); // "LTDS"
    buf.extend_from_slice(&output.access_denied_count.to_le_bytes());
    buf.extend_from_slice(&output.reparse_count.to_le_bytes());
    write_node(&mut buf, &output.root);

    let mut exts: Vec<(&String, &(u64, i32))> = output.extensions.iter().collect();
    exts.sort_unstable_by_key(|a| std::cmp::Reverse(a.1 .0));
    let take_ext = exts.len().min(15);
    buf.extend_from_slice(&(take_ext as u32).to_le_bytes());
    for (ext, (sz, cnt)) in exts.into_iter().take(take_ext) {
        write_str(&mut buf, ext);
        buf.extend_from_slice(&sz.to_le_bytes());
        buf.extend_from_slice(&cnt.to_le_bytes());
    }
    buf
}
