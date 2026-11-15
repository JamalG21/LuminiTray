using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LuminiTray.Installer
{
    internal static class Program
    {
        internal const string Repo = "JamalG21/LuminiTray";
        internal const string AppName = "Lumini Tray";
        internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LuminiTray";

        internal static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LuminiTray");

        [STAThread]
        private static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Uninstaller.Run(args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase)));
                return;
            }
            Application.Run(new InstallerForm());
        }
    }

    internal static class Uninstaller
    {
        public static void Run(bool silent)
        {
            if (!silent && MessageBox.Show("Deseja remover o Lumini Tray deste computador?\n\nSuas configurações em %AppData% serão mantidas.",
                    "Desinstalar Lumini Tray", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            KillApp();
            try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallKey, false); } catch { }
            try
            {
                using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    run?.DeleteValue("LuminiTray", false);
            }
            catch { }
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Lumini Tray.lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lumini Tray.lnk"));

            // Remove a pasta depois que este processo (que pode estar nela) encerrar.
            string dir = Program.InstallDir;
            string cmd = "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\"";
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
            }
            catch { }
            if (!silent)
                MessageBox.Show("Lumini Tray removido com sucesso.", "Desinstalar Lumini Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        internal static void KillApp()
        {
            foreach (var name in new[] { "LuminiTray", "LuminiTray.Wallpaper", "LuminiTray.Fans", "LuminiTray.Metrics", "LuminiTray.Suppressor" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    internal sealed class InstallerForm : Form
    {
        private readonly Label _title = new Label();
        private readonly Label _status = new Label();
        private readonly ProgressBar _bar = new ProgressBar();
        private readonly CheckBox _chkDesktop = new CheckBox();
        private readonly CheckBox _chkStart = new CheckBox();
        private readonly CheckBox _chkRun = new CheckBox();
        private readonly Button _btnInstall = new Button();
        private readonly Button _btnClose = new Button();

        public InstallerForm()
        {
            Text = "Instalador do Lumini Tray";
            Size = new Size(480, 340);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 24, 27);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _title.Text = "LUMINI TRAY";
            _title.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
            _title.SetBounds(24, 18, 420, 40);

            _status.Text = "Pronto para baixar e instalar a versão mais recente direto do GitHub.";
            _status.SetBounds(26, 66, 420, 40);
            _status.ForeColor = Color.FromArgb(180, 180, 190);

            _chkDesktop.Text = "Criar atalho na Área de Trabalho";
            _chkDesktop.Checked = true;
            _chkDesktop.SetBounds(28, 112, 400, 24);
            _chkStart.Text = "Criar atalho no Menu Iniciar";
            _chkStart.Checked = true;
            _chkStart.SetBounds(28, 138, 400, 24);
            _chkRun.Text = "Executar o Lumini Tray ao finalizar";
            _chkRun.Checked = true;
            _chkRun.SetBounds(28, 164, 400, 24);

            _bar.SetBounds(28, 204, 410, 12);
            _bar.Visible = false;

            _btnInstall.Text = "Instalar";
            _btnInstall.SetBounds(228, 246, 100, 32);
            _btnInstall.FlatStyle = FlatStyle.Flat;
            _btnInstall.BackColor = Color.FromArgb(16, 185, 129);
            _btnInstall.ForeColor = Color.Black;
            _btnInstall.Click += async (s, e) => await InstallAsync();

            _btnClose.Text = "Fechar";
            _btnClose.SetBounds(338, 246, 100, 32);
            _btnClose.FlatStyle = FlatStyle.Flat;
            _btnClose.BackColor = Color.FromArgb(48, 48, 54);
            _btnClose.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { _title, _status, _chkDesktop, _chkStart, _chkRun, _bar, _btnInstall, _btnClose });
        }

        private async Task InstallAsync()
        {
            _btnInstall.Enabled = false;
            _chkDesktop.Enabled = _chkStart.Enabled = _chkRun.Enabled = false;
            _bar.Visible = true;
            try
            {
                SetStatus("Consultando a versão mais recente...");
                string zipUrl;
                string version;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("LuminiTray-Installer");
                    string json = await http.GetStringAsync("https://api.github.com/repos/" + Program.Repo + "/releases?per_page=15");
                    // Ignora a release rolante "latest": usa o primeiro pacote publicado sob uma tag de versão (vX.Y.Z).
                    var asset = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"(https://[^\"]*/download/v([0-9][^/\"]*)/LuminiTray-Portable\\.zip)\"");
                    if (!asset.Success) throw new InvalidOperationException("Pacote LuminiTray-Portable.zip não encontrado nas releases.");
                    zipUrl = asset.Groups[1].Value;
                    version = asset.Groups[2].Value;
                }

                string tmp = Path.Combine(Path.GetTempPath(), "LuminiTray_Setup");
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                Directory.CreateDirectory(tmp);
                string zipFile = Path.Combine(tmp, "LuminiTray-Portable.zip");

                SetStatus("Baixando Lumini Tray v" + version + "...");
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("LuminiTray-Installer");
                    using (var resp = await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        resp.EnsureSuccessStatusCode();
                        long total = resp.Content.Headers.ContentLength ?? -1;
                        using (var src = await resp.Content.ReadAsStreamAsync())
                        using (var dst = File.Create(zipFile))
                        {
                            var buf = new byte[81920];
                            long read = 0;
                            int n;
                            while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0)
                            {
                                await dst.WriteAsync(buf, 0, n);
                                read += n;
                                if (total > 0) _bar.Value = (int)Math.Min(100, read * 100 / total);
                            }
                        }
                    }
                }

                SetStatus("Instalando...");
                Uninstaller.KillApp();
                string dir = Program.InstallDir;
                Directory.CreateDirectory(dir);
                using (var zip = ZipFile.OpenRead(zipFile))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                        if (!target.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, true);
                    }
                }
                File.WriteAllText(Path.Combine(dir, ".installed"), version);

                string uninstaller = Path.Combine(dir, "unins000.exe");
                try { File.Copy(Application.ExecutablePath, uninstaller, true); } catch { }

                string exe = Path.Combine(dir, "LuminiTray.exe");
                if (_chkDesktop.Checked)
                    CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Lumini Tray.lnk"), exe, dir);
                if (_chkStart.Checked)
                    CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lumini Tray.lnk"), exe, dir);

                using (var key = Registry.CurrentUser.CreateSubKey(Program.UninstallKey))
                {
                    key.SetValue("DisplayName", Program.AppName);
                    key.SetValue("DisplayVersion", version);
                    key.SetValue("Publisher", Program.AppName);
                    key.SetValue("InstallLocation", dir);
                    key.SetValue("DisplayIcon", exe);
                    key.SetValue("UninstallString", "\"" + uninstaller + "\" --uninstall");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }

                try { Directory.Delete(tmp, true); } catch { }
                _bar.Value = 100;
                SetStatus("Instalação concluída com sucesso! Tudo certo.");
                if (_chkRun.Checked)
                    Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = true });
                _btnInstall.Visible = false;
                _btnClose.Text = "Concluir";
            }
            catch (Exception ex)
            {
                SetStatus("Falha na instalação: " + ex.Message);
                _btnInstall.Enabled = true;
                _chkDesktop.Enabled = _chkStart.Enabled = _chkRun.Enabled = true;
            }
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
            Application.DoEvents();
        }

        private static void CreateShortcut(string lnkPath, string target, string workDir)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { workDir });
                st.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "Lumini Tray" });
                st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { }
        }
    }
}
