using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace LuminiTray;

/// <summary>
/// Motor de integração com a Área de Trabalho do Windows adaptado do Lively Wallpaper (v2.2.1.0).
/// Suporta nativamente Windows 11 Moderno ("Raised Desktop" com WS_EX_NOREDIRECTIONBITMAP)
/// e Windows 10 clássico, garantindo que os ícones do Desktop permaneçam 100% responsivos,
/// visíveis e clicáveis sem qualquer interferência na superfície do Windows Explorer.
/// </summary>
public sealed class LivelyDesktopCore
{
    private static readonly Lazy<LivelyDesktopCore> _instance = new(() => new LivelyDesktopCore());
    public static LivelyDesktopCore Instance => _instance.Value;

    private IntPtr _progman = IntPtr.Zero;
    private IntPtr _workerW = IntPtr.Zero;
    private IntPtr _shellDllDefView = IntPtr.Zero;
    private bool _isRaisedDesktopWithLayeredShellView;

    public IntPtr DesktopWorkerW => _workerW;
    public IntPtr DesktopProgman => _progman;
    public IntPtr ShellDllDefView => _shellDllDefView;
    public bool IsRaisedDesktop => _isRaisedDesktopWithLayeredShellView;

    #region Win32 Constants
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private const uint WS_CHILD = 0x40000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    private const uint LWA_ALPHA = 0x2;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    #endregion

    #region P/Invoke
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref RECT lpPoints, uint cPoints);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : (IntPtr)GetWindowLong32(hWnd, nIndex);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : (IntPtr)SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());
    #endregion

    private LivelyDesktopCore() { }

    /// <summary>
    /// Inicializa e descobre a hierarquia de janelas da Área de Trabalho do Windows.
    /// Segue a arquitetura exata de detecção do Lively Wallpaper 2.2.1.0.
    /// </summary>
    public void SetupDesktopLayer()
    {
        try
        {
            _progman = FindWindow("Progman", null);
            if (_progman == IntPtr.Zero)
            {
                _progman = FindWindow("Progman", "Program Manager");
            }

            if (_progman != IntPtr.Zero)
            {
                long progmanExStyle = GetWindowLongPtr(_progman, GWL_EXSTYLE).ToInt64();
                _isRaisedDesktopWithLayeredShellView = (progmanExStyle & WS_EX_NOREDIRECTIONBITMAP) != 0;
            }
            else
            {
                _isRaisedDesktopWithLayeredShellView = false;
            }

            // Dispara a mensagem 0x052C com wParam=0xD e lParam=0x1 para solicitar ao Progman a criação da camada de wallpaper
            if (_progman != IntPtr.Zero)
            {
                SendMessageTimeout(_progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0x0000, 1000, out _);
                // Também envia (0, 0) para compatibilidade universal em todas as edições do Windows
                SendMessageTimeout(_progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x0000, 1000, out _);
            }

            // Enumera todas as janelas para localizar onde reside SHELLDLL_DefView (a lista de ícones da área de trabalho)
            IntPtr folderView = IntPtr.Zero;
            IntPtr siblingWorkerW = IntPtr.Zero;

            EnumWindows((tophandle, _) =>
            {
                IntPtr p = FindWindowEx(tophandle, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (p != IntPtr.Zero)
                {
                    folderView = p;
                    // Pega a janela WorkerW irmã que fica imediatamente após a janela que contém SHELLDLL_DefView
                    siblingWorkerW = FindWindowEx(IntPtr.Zero, tophandle, "WorkerW", null);
                }
                return true;
            }, IntPtr.Zero);

            _shellDllDefView = folderView;

            if (_isRaisedDesktopWithLayeredShellView && _progman != IntPtr.Zero)
            {
                // No Windows 11 Raised Desktop, o WorkerW de renderização fica diretamente sob o Progman
                _workerW = FindWindowEx(_progman, IntPtr.Zero, "WorkerW", null);
                if (_workerW == IntPtr.Zero)
                {
                    _workerW = siblingWorkerW;
                }
            }
            else
            {
                _workerW = siblingWorkerW;
            }

            Debug.WriteLine($"[LivelyDesktopCore] SetupLayer -> Progman: {_progman}, WorkerW: {_workerW}, DefView: {_shellDllDefView}, Raised: {_isRaisedDesktopWithLayeredShellView}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LivelyDesktopCore] Erro em SetupDesktopLayer: {ex.Message}");
        }
    }

    /// <summary>
    /// Anexa a janela de vídeo à Área de Trabalho com Z-Order seguro, garantindo que
    /// os ícones do Desktop permaneçam sempre em primeiro plano e 100% clicáveis.
    /// Em janelas filhas sob Progman ou WorkerW, o posicionamento é sempre relativo ao
    /// cliente do container (0, 0, w, h) cobrindo todos os monitores sem qualquer deslocamento negativo.
    /// </summary>
    public bool AttachToDesktop(IntPtr hwnd, int x, int y, int width, int height)
    {
        if (hwnd == IntPtr.Zero) return false;

        SetupDesktopLayer();

        try
        {
            IntPtr targetParent = IntPtr.Zero;
            IntPtr insertAfter = HWND_BOTTOM;

            if (_isRaisedDesktopWithLayeredShellView && _progman != IntPtr.Zero)
            {
                // No Windows 11 Raised Desktop: a janela de vídeo deve ser filha de Progman
                // e posicionada imediatamente atrás de SHELLDLL_DefView (a lista de ícones da Área de Trabalho)
                targetParent = _progman;
                insertAfter = _shellDllDefView != IntPtr.Zero ? _shellDllDefView : HWND_BOTTOM;
            }
            else if (_workerW != IntPtr.Zero)
            {
                // No Windows 10 clássico / WorkerW: anexa dentro do WorkerW de fundo
                targetParent = _workerW;
                insertAfter = HWND_BOTTOM;
            }
            else if (_progman != IntPtr.Zero)
            {
                // Fallback para Progman
                targetParent = _progman;
                insertAfter = _shellDllDefView != IntPtr.Zero ? _shellDllDefView : HWND_BOTTOM;
            }

            if (targetParent == IntPtr.Zero) return false;

            // Converte as coordenadas globais de tela do monitor para coordenadas de cliente de targetParent
            RECT targetRect = new RECT
            {
                Left = x,
                Top = y,
                Right = x + width,
                Bottom = y + height
            };
            MapWindowPoints(IntPtr.Zero, targetParent, ref targetRect, 2);

            int destX = targetRect.Left;
            int destY = targetRect.Top;
            int destW = width > 0 ? width : Math.Abs(targetRect.Right - targetRect.Left);
            int destH = height > 0 ? height : Math.Abs(targetRect.Bottom - targetRect.Top);

            // Oculta a janela temporariamente durante a transição de pai para evitar flickering
            ShowWindow(hwnd, SW_HIDE);

            // Anexa como janela filha do container do Desktop
            SetParent(hwnd, targetParent);

            // Aplica estilos de janela filha (WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS)
            // e remove bordas, caixas de diálogo ou barras de título
            long currentStyle = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
            long newStyle = currentStyle | WS_CHILD | 0x10000000L /* WS_VISIBLE */ | 0x04000000L /* WS_CLIPSIBLINGS */;
            newStyle &= ~(0x00C00000L /* WS_CAPTION */ | 0x00040000L /* WS_THICKFRAME */ | 0x00800000L /* WS_BORDER */);
            SetWindowLongPtr(hwnd, GWL_STYLE, (IntPtr)newStyle);

            // Aplica estilos estendidos essenciais (WS_EX_LAYERED, WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW)
            // No Windows 11 Raised Desktop, o DWM só renderiza janelas filhas sob Progman se possuírem WS_EX_LAYERED!
            long currentEx = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            long newEx = currentEx | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)newEx);

            // Define opacidade total (255) para permitir apresentação acelerada via Direct3D/MediaElement
            SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);

            // Posiciona exatamente nas coordenadas e dimensões mapeadas do monitor selecionado
            SetWindowPos(hwnd, insertAfter, destX, destY, destW, destH,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

            // Garante exibição explícita exigida pela API Win32 após SetParent
            ShowWindow(hwnd, SW_SHOW);

            EnsureWorkerWZOrder();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LivelyDesktopCore] Erro ao anexar janela ao desktop: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Desanexa a janela da Área de Trabalho.
    /// </summary>
    public void DetachFromDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            SetParent(hwnd, IntPtr.Zero);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch { }
    }

    /// <summary>
    /// Garante que o WorkerW de fundo não fique sobrepondo outras camadas no Windows 11.
    /// </summary>
    public void EnsureWorkerWZOrder()
    {
        if (!_isRaisedDesktopWithLayeredShellView || _workerW == IntPtr.Zero) return;
        try
        {
            SetWindowPos(_workerW, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    /// <summary>
    /// Força o Windows a redesenhar a Área de Trabalho, limpando qualquer superfície estática restante.
    /// No Windows 11 Raised Desktop, NÃO chamamos SPI_SETDESKWALLPAPER para evitar que o Windows destrua o WorkerW.
    /// </summary>
    public void RefreshDesktop()
    {
        if (_isRaisedDesktopWithLayeredShellView) return;

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null, 0);
            }
            catch { }
        });
    }
}
