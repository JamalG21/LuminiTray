using System;
using System.Drawing;
using System.Windows.Forms;

namespace LuminiTray;

/// <summary>
/// Janela container nativa Win32 (WinForms) acoplada ao Desktop para hospedar o MPV Player.
/// Fornece um HWND puro sem as restrições de composição e layout do WPF, permitindo
/// acoplamento perfeito em Progman/WorkerW atrás dos ícones da Área de Trabalho.
/// </summary>
public sealed class LivelyWallpaperForm : Form
{
    public LivelyWallpaperForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        ControlBox = false;
        Text = "Lumini Lively MPV Host";
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000
            cp.ExStyle |= 0x08000080;
            return cp;
        }
    }
}
