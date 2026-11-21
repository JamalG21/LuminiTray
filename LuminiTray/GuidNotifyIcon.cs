using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace LuminiTray;

/// <summary>
/// Ícone de bandeja Win32 nativo com suporte explícito a NIF_GUID (Shell_NotifyIconW).
/// Cada instância recebe um GUID determinístico imutável, impedindo que o Windows 11
/// vincule ou mova a posição de um ícone quando outro ícone do mesmo executável é movido.
/// </summary>
public sealed class GuidNotifyIcon : IDisposable
{
    private const uint WM_USER = 0x0400;
    private const uint WM_TRAYICON = WM_USER + 0x51;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_GUID = 0x00000020;

    private const uint NOTIFYICON_VERSION_4 = 4;

    private static readonly uint WM_TASKBARCREATED = RegisterWindowMessageW("TaskbarCreated");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private sealed class MessageWindow : Forms.NativeWindow, IDisposable
    {
        private readonly GuidNotifyIcon _owner;

        public MessageWindow(GuidNotifyIcon owner)
        {
            _owner = owner;
            CreateHandle(new Forms.CreateParams { Caption = "LuminiTrayGuidIconHost" });
        }

        protected override void WndProc(ref Forms.Message m)
        {
            if (m.Msg == (int)WM_TRAYICON)
            {
                int evt = (int)(m.LParam.ToInt64() & 0xFFFF);
                if (evt == WM_LBUTTONDBLCLK)
                {
                    _owner.DoubleClick?.Invoke(_owner, EventArgs.Empty);
                }
                else if (evt == WM_LBUTTONUP)
                {
                    _owner.LeftClick?.Invoke(_owner, EventArgs.Empty);
                }
                else if (evt == WM_RBUTTONUP || evt == WM_CONTEXTMENU)
                {
                    _owner.ShowContextMenu();
                }
                return;
            }
            if (WM_TASKBARCREATED != 0 && m.Msg == (int)WM_TASKBARCREATED)
            {
                _owner.RecreateIfVisible();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                DestroyHandle();
        }
    }

    private readonly MessageWindow _window;
    private readonly Guid _guid;
    private Drawing.Icon? _icon;
    private string _text = "";
    private bool _visible;
    private bool _added;
    private bool _disposed;

    public event EventHandler? DoubleClick;
    public event EventHandler? LeftClick;
    public Forms.ContextMenuStrip? ContextMenuStrip { get; set; }
    public Guid IconGuid => _guid;
    public string LastIconKey { get; set; } = "";

    public GuidNotifyIcon(string scopeKey)
    {
        _guid = CreateDeterministicGuid(scopeKey);
        _window = new MessageWindow(this);
    }

    public static Guid CreateDeterministicGuid(string scopeKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("LuminiTray.Tray:" + (scopeKey ?? "").Trim().ToLowerInvariant()));
        var bytes = new byte[16];
        Array.Copy(hash, 0, bytes, 0, 16);
        // Versão 5 / variante RFC 4122
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    public Drawing.Icon? Icon
    {
        get => _icon;
        set
        {
            if (_disposed) return;
            var old = _icon;
            _icon = value;
            if (_added) UpdateShellIcon();
            try { old?.Dispose(); } catch { }
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_disposed) return;
            string clean = value ?? "";
            _text = clean.Length > 127 ? clean[..124] + "..." : clean;
            if (_added) UpdateShellIcon();
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_disposed || _visible == value) return;
            _visible = value;
            if (_visible) AddShellIcon();
            else DeleteShellIcon();
        }
    }

    private NOTIFYICONDATAW BuildData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = 0,
        uFlags = flags | NIF_GUID,
        uCallbackMessage = WM_TRAYICON,
        hIcon = _icon?.Handle ?? IntPtr.Zero,
        szTip = _text,
        szInfo = "",
        szInfoTitle = "",
        guidItem = _guid
    };

    private void AddShellIcon()
    {
        if (_disposed || _added || _window.Handle == IntPtr.Zero) return;
        uint flags = NIF_MESSAGE | NIF_TIP;
        if (_icon != null) flags |= NIF_ICON;

        var data = BuildData(flags);
        if (!Shell_NotifyIconW(NIM_ADD, ref data))
        {
            // Se um GUID antigo ficou órfão de crash anterior, remove e tenta novamente
            Shell_NotifyIconW(NIM_DELETE, ref data);
            if (!Shell_NotifyIconW(NIM_ADD, ref data))
                return;
        }
        _added = true;

        var ver = BuildData(0);
        ver.uVersionOrTimeout = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref ver);
    }

    private void UpdateShellIcon()
    {
        if (_disposed || !_added || _window.Handle == IntPtr.Zero) return;
        uint flags = NIF_MESSAGE | NIF_TIP;
        if (_icon != null) flags |= NIF_ICON;
        var data = BuildData(flags);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void DeleteShellIcon()
    {
        if (!_added || _window.Handle == IntPtr.Zero) return;
        var data = BuildData(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _added = false;
    }

    private void RecreateIfVisible()
    {
        if (_disposed || !_visible) return;
        _added = false;
        AddShellIcon();
    }

    private void ShowContextMenu()
    {
        if (_disposed || ContextMenuStrip == null || _window.Handle == IntPtr.Zero) return;
        try
        {
            SetForegroundWindow(_window.Handle);
            ContextMenuStrip.Show(Forms.Cursor.Position);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DeleteShellIcon();
        try { _icon?.Dispose(); } catch { }
        _icon = null;
        try { ContextMenuStrip?.Dispose(); } catch { }
        ContextMenuStrip = null;
        try { _window.Dispose(); } catch { }
    }
}
