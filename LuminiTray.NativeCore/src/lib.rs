use std::cell::RefCell;
use std::ffi::OsString;
use std::os::windows::ffi::OsStringExt;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::path::PathBuf;

pub mod disk;
pub mod dsp;
pub mod frames;

use disk::{serialize_scan_output, ProgressCallback};
use dsp::{DspParams, VoiceDspState, FRAME_SAMPLES};
use frames::{compute_metrics_slice, FrameStore};

pub const ABI_VERSION: u32 = 2;
pub const MAX_ALLOC_BYTES: usize = 64 * 1024 * 1024;

pub const ERR_INVALID_ARG: i32 = -1;
pub const ERR_PANIC: i32 = -2;
pub const ERR_CANCELLED: i32 = -3;

thread_local! {
    static LAST_ERROR: RefCell<String> = const { RefCell::new(String::new()) };
}

pub fn set_last_error(msg: impl Into<String>) {
    let s = msg.into();
    LAST_ERROR.with(|cell| {
        *cell.borrow_mut() = s;
    });
}

fn clear_last_error() {
    LAST_ERROR.with(|cell| {
        cell.borrow_mut().clear();
    });
}

fn ffi_guard<F, R>(on_err: R, f: F) -> R
where
    F: FnOnce() -> R,
{
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(val) => val,
        Err(_) => {
            set_last_error("rust panic intercepted at FFI boundary");
            on_err
        }
    }
}

#[no_mangle]
pub extern "C" fn nc_abi_version() -> u32 {
    ABI_VERSION
}

#[no_mangle]
pub extern "C" fn nc_ping(value: i32) -> i32 {
    ffi_guard(ERR_PANIC, || {
        clear_last_error();
        if value == i32::MIN {
            set_last_error("invalid ping sentinel");
            return ERR_INVALID_ARG;
        }
        value.wrapping_mul(3).wrapping_add(7)
    })
}

/// # Safety
/// `out_ptr` deve apontar para um `*mut u8` válido.
#[no_mangle]
pub unsafe extern "C" fn nc_alloc_buffer(size: usize, out_ptr: *mut *mut u8) -> i32 {
    ffi_guard(ERR_PANIC, || {
        clear_last_error();
        if out_ptr.is_null() || size == 0 || size > MAX_ALLOC_BYTES {
            set_last_error("invalid allocation size or null output pointer");
            return ERR_INVALID_ARG;
        }
        let mut buf = vec![0u8; size].into_boxed_slice();
        let ptr = buf.as_mut_ptr();
        std::mem::forget(buf);
        *out_ptr = ptr;
        0
    })
}

/// # Safety
/// `ptr` e `size` devem corresponder ao retorno de uma alocação nativa `LuminiTray.NativeCore`.
#[no_mangle]
pub unsafe extern "C" fn nc_free_buffer(ptr: *mut u8, size: usize) -> i32 {
    ffi_guard(ERR_PANIC, || {
        if ptr.is_null() || size == 0 {
            return 0;
        }
        if size > MAX_ALLOC_BYTES {
            set_last_error("invalid free size");
            return ERR_INVALID_ARG;
        }
        let slice = std::slice::from_raw_parts_mut(ptr, size);
        drop(Box::from_raw(slice as *mut [u8]));
        0
    })
}

/// # Safety
/// `buf_utf16` deve comportar ao menos `cap_chars` elementos `u16`.
#[no_mangle]
pub unsafe extern "C" fn nc_last_error(buf_utf16: *mut u16, cap_chars: usize) -> i32 {
    ffi_guard(0, || {
        if buf_utf16.is_null() || cap_chars == 0 {
            return 0;
        }
        LAST_ERROR.with(|cell| {
            let msg = cell.borrow();
            let mut written = 0usize;
            for ch in msg.encode_utf16().take(cap_chars - 1) {
                *buf_utf16.add(written) = ch;
                written += 1;
            }
            *buf_utf16.add(written) = 0;
            written as i32
        })
    })
}

// ---------------- Módulo 1: Varredura de Disco Nativa ----------------

/// # Safety
/// `path_ptr` deve apontar para `path_len` chars UTF-16 válidos; `out_buf` e `out_len` devem ser válidos.
#[no_mangle]
pub unsafe extern "C" fn nc_disk_scan_utf16(
    path_ptr: *const u16,
    path_len: usize,
    max_files_per_dir: usize,
    cancel_ptr: *const u8,
    progress_cb: Option<ProgressCallback>,
    out_buf: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    ffi_guard(ERR_PANIC, || {
        clear_last_error();
        if path_ptr.is_null() || path_len == 0 || out_buf.is_null() || out_len.is_null() {
            set_last_error("invalid arguments to nc_disk_scan_utf16");
            return ERR_INVALID_ARG;
        }
        let wide = std::slice::from_raw_parts(path_ptr, path_len);
        let path = PathBuf::from(OsString::from_wide(wide));

        match disk::scan_tree(&path, max_files_per_dir, cancel_ptr, progress_cb) {
            Some(res) => {
                let bytes = serialize_scan_output(&res).into_boxed_slice();
                let len = bytes.len();
                let ptr = Box::into_raw(bytes) as *mut u8;
                *out_buf = ptr;
                *out_len = len;
                0
            }
            None => {
                set_last_error("disk scan cancelled");
                ERR_CANCELLED
            }
        }
    })
}

// ---------------- Módulo 2: Frame Monitor Nativo ----------------

#[no_mangle]
pub extern "C" fn nc_frames_create() -> *mut FrameStore {
    ffi_guard(std::ptr::null_mut(), || {
        Box::into_raw(Box::new(FrameStore::new()))
    })
}

/// # Safety
/// `ptr` deve ser um handle válido retornado por `nc_frames_create` ou nulo.
#[no_mangle]
pub unsafe extern "C" fn nc_frames_destroy(ptr: *mut FrameStore) -> i32 {
    ffi_guard(ERR_PANIC, || {
        if !ptr.is_null() {
            drop(Box::from_raw(ptr));
        }
        0
    })
}

/// # Safety
/// `ptr` deve ser um handle válido retornado por `nc_frames_create`.
#[no_mangle]
pub unsafe extern "C" fn nc_frames_clear(ptr: *mut FrameStore) -> i32 {
    ffi_guard(ERR_PANIC, || {
        if ptr.is_null() {
            return ERR_INVALID_ARG;
        }
        (&*ptr).clear();
        0
    })
}

/// # Safety
/// `ptr` deve ser um handle válido retornado por `nc_frames_create`.
#[no_mangle]
pub unsafe extern "C" fn nc_frames_record(
    ptr: *mut FrameStore,
    pid: i32,
    timestamp_ms: f64,
    host_now_ms: f64,
) -> i32 {
    ffi_guard(ERR_PANIC, || {
        if ptr.is_null() {
            return ERR_INVALID_ARG;
        }
        (&*ptr).record(pid, timestamp_ms, host_now_ms);
        0
    })
}

/// # Safety
/// Ponteiros de saída válidos; retorna 1 se métricas disponíveis, 0 se indisponíveis/expiradas.
#[no_mangle]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn nc_frames_query(
    ptr: *mut FrameStore,
    pid: i32,
    host_now_ms: f64,
    out_fps: *mut f64,
    out_ft_ms: *mut f64,
    out_low1: *mut f64,
    out_recent: *mut f64,
    max_recent: usize,
    out_recent_len: *mut usize,
) -> i32 {
    ffi_guard(ERR_PANIC, || {
        if ptr.is_null() || out_fps.is_null() || out_ft_ms.is_null() || out_low1.is_null() {
            return ERR_INVALID_ARG;
        }
        let mut recent = Vec::with_capacity(max_recent.min(256));
        match (&*ptr).query(pid, host_now_ms, max_recent, &mut recent) {
            Some((fps, ft_ms, low1)) => {
                *out_fps = fps;
                *out_ft_ms = ft_ms;
                *out_low1 = low1;
                if !out_recent.is_null() && max_recent > 0 {
                    let count = recent.len().min(max_recent);
                    std::ptr::copy_nonoverlapping(recent.as_ptr(), out_recent, count);
                    if !out_recent_len.is_null() {
                        *out_recent_len = count;
                    }
                } else if !out_recent_len.is_null() {
                    *out_recent_len = 0;
                }
                1
            }
            None => 0,
        }
    })
}

/// # Safety
/// `ts_ptr` deve apontar para `count` valores `f64` e os ponteiros de saída devem ser válidos.
#[no_mangle]
pub unsafe extern "C" fn nc_frames_compute_metrics(
    ts_ptr: *const f64,
    count: usize,
    out_fps: *mut f64,
    out_ft_ms: *mut f64,
    out_low1: *mut f64,
) -> i32 {
    ffi_guard(ERR_PANIC, || {
        if ts_ptr.is_null() || out_fps.is_null() || out_ft_ms.is_null() || out_low1.is_null() {
            return ERR_INVALID_ARG;
        }
        let slice = std::slice::from_raw_parts(ts_ptr, count);
        if let Some((fps, ft, low1)) = compute_metrics_slice(slice) {
            *out_fps = fps;
            *out_ft_ms = ft;
            *out_low1 = low1;
            0
        } else {
            *out_fps = 0.0;
            *out_ft_ms = 0.0;
            *out_low1 = 0.0;
            0
        }
    })
}

// ---------------- Módulo 3: Voice DSP 48 kHz em Tempo Real ----------------

#[no_mangle]
pub extern "C" fn nc_dsp_create() -> *mut VoiceDspState {
    ffi_guard(std::ptr::null_mut(), || {
        Box::into_raw(Box::new(VoiceDspState::new()))
    })
}

/// # Safety
/// `ptr` deve ser um handle válido retornado por `nc_dsp_create` ou nulo.
#[no_mangle]
pub unsafe extern "C" fn nc_dsp_destroy(ptr: *mut VoiceDspState) -> i32 {
    ffi_guard(ERR_PANIC, || {
        if !ptr.is_null() {
            drop(Box::from_raw(ptr));
        }
        0
    })
}

#[no_mangle]
pub extern "C" fn nc_dsp_soft_limit(x: f32) -> f32 {
    dsp::soft_limit(x)
}

/// # Safety
/// `samples480` e `rnn_in480` devem ter tamanho 480; `params` e ponteiros de saída devem ser válidos.
#[no_mangle]
pub unsafe extern "C" fn nc_dsp_pre_process(
    ptr: *mut VoiceDspState,
    samples480: *mut f32,
    rnn_in480: *mut f32,
    params: *const DspParams,
    out_in_peak_db: *mut f32,
    out_in_rms_db: *mut f32,
) -> i32 {
    ffi_guard(ERR_PANIC, || {
        if ptr.is_null()
            || samples480.is_null()
            || rnn_in480.is_null()
            || params.is_null()
            || out_in_peak_db.is_null()
            || out_in_rms_db.is_null()
        {
            return ERR_INVALID_ARG;
        }
        let samples: &mut [f32; FRAME_SAMPLES] = &mut *(samples480 as *mut [f32; FRAME_SAMPLES]);
        let rnn_in: &mut [f32; FRAME_SAMPLES] = &mut *(rnn_in480 as *mut [f32; FRAME_SAMPLES]);
        let (pk, rms) = (&mut *ptr).pre_process(samples, rnn_in, &*params);
        *out_in_peak_db = pk;
        *out_in_rms_db = rms;
        0
    })
}

/// # Safety
/// `samples480` e `rnn_out480` devem ter tamanho 480; `params` deve ser válido.
#[no_mangle]
pub unsafe extern "C" fn nc_dsp_post_rnnoise(
    ptr: *mut VoiceDspState,
    samples480: *mut f32,
    rnn_out480: *const f32,
    vad_prob: f32,
    params: *const DspParams,
) -> i32 {
    ffi_guard(ERR_PANIC, || {
        if ptr.is_null() || samples480.is_null() || rnn_out480.is_null() || params.is_null() {
            return ERR_INVALID_ARG;
        }
        let samples: &mut [f32; FRAME_SAMPLES] = &mut *(samples480 as *mut [f32; FRAME_SAMPLES]);
        let rnn_out: &[f32; FRAME_SAMPLES] = &*(rnn_out480 as *const [f32; FRAME_SAMPLES]);
        (&mut *ptr).post_rnnoise(samples, rnn_out, vad_prob, &*params);
        0
    })
}

/// # Safety
/// `samples480` deve ter tamanho 480; `params` e `out_vad_prob` devem ser válidos.
#[no_mangle]
pub unsafe extern "C" fn nc_dsp_fallback_process(
    ptr: *mut VoiceDspState,
    samples480: *mut f32,
    in_peak_db: f32,
    in_rms_db: f32,
    params: *const DspParams,
    out_vad_prob: *mut f32,
) -> i32 {
    ffi_guard(ERR_PANIC, || {
        if ptr.is_null() || samples480.is_null() || params.is_null() || out_vad_prob.is_null() {
            return ERR_INVALID_ARG;
        }
        let samples: &mut [f32; FRAME_SAMPLES] = &mut *(samples480 as *mut [f32; FRAME_SAMPLES]);
        *out_vad_prob = (&mut *ptr).fallback_process(samples, in_peak_db, in_rms_db, &*params);
        0
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn abi_and_ping_work() {
        assert_eq!(nc_abi_version(), ABI_VERSION);
        assert_eq!(nc_ping(10), 37);
        assert_eq!(nc_ping(i32::MIN), ERR_INVALID_ARG);
        let mut buf = [0u16; 64];
        let len = unsafe { nc_last_error(buf.as_mut_ptr(), buf.len()) };
        assert!(len > 0);
        assert!(String::from_utf16_lossy(&buf[..len as usize]).contains("invalid ping sentinel"));

        unsafe {
            let mut ptr: *mut u8 = std::ptr::null_mut();
            assert_eq!(nc_alloc_buffer(4096, &mut ptr), 0);
            assert!(!ptr.is_null());
            assert_eq!(nc_free_buffer(ptr, 4096), 0);
        }
    }

    #[test]
    fn disk_scan_temp_dir_works() {
        let dir = std::env::temp_dir().join("lumini_rust_disk_test");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(dir.join("sub")).unwrap();
        std::fs::write(dir.join("a.bin"), vec![1u8; 1024]).unwrap();
        std::fs::write(dir.join("sub").join("b.txt"), vec![2u8; 512]).unwrap();

        let out = disk::scan_tree(&dir, 40, std::ptr::null(), None).unwrap();
        assert_eq!(out.root.file_count, 2);
        assert_eq!(out.root.folder_count, 1);
        assert_eq!(out.root.total_size, 1536);

        let serialized = serialize_scan_output(&out);
        assert_eq!(&serialized[..4], &0x4C54_4453u32.to_le_bytes());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn frame_metrics_and_store_work() {
        let mut ts = Vec::with_capacity(61);
        for i in 0..61 {
            ts.push(i as f64 * 10.0);
        }
        let (fps, ft, low1) = compute_metrics_slice(&ts).unwrap();
        assert!((fps - 100.0).abs() < 0.01);
        assert!((ft - 10.0).abs() < 0.01);
        assert!((low1 - 100.0).abs() < 0.01);

        let store = FrameStore::new();
        for (idx, t) in ts.iter().enumerate() {
            store.record(4242, *t, 5000.0 + idx as f64 * 10.0);
        }
        let mut recent = Vec::new();
        let q = store.query(4242, 5650.0, 120, &mut recent).unwrap();
        assert!((q.0 - 100.0).abs() < 0.01);
        assert_eq!(recent.len(), 60);
    }

    #[test]
    fn dsp_pipeline_works() {
        let mut st = VoiceDspState::new();
        let params = DspParams {
            suppression_intensity: 1.0,
            gain_db: 0.0,
            gate_threshold_db: -52.0,
            gate_release_ms: 150.0,
            gate_hysteresis_db: 8.0,
            gate_hold_ms: 140.0,
            vad_threshold: 0.35,
            vad_hold_ms: 180.0,
            low_eq_db: 0.0,
            mid_eq_db: 0.0,
            high_eq_db: 0.0,
            limiter_enabled: 1,
            deesser_enabled: 1,
        };
        let mut samples = [0.25f32; FRAME_SAMPLES];
        let mut rnn_in = [0.0f32; FRAME_SAMPLES];
        let (pk, rms) = st.pre_process(&mut samples, &mut rnn_in, &params);
        let vad = st.fallback_process(&mut samples, pk, rms, &params);
        assert!((0.0..=1.0).contains(&vad));
        for &s in samples.iter() {
            assert!(s.abs() <= 1.0);
        }
        assert!(dsp::soft_limit(2.0) <= 1.0);
    }
}
