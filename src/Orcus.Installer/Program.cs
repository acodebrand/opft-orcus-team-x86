using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ComponentModel;

namespace Orcus.Installer;

internal static class Program
{
    private const string Product = "OPTF Orcus Team";
    private static readonly string DefaultInstallDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Product);
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Orcus.Payload.exe");
            var validDefault = Path.IsPathRooted(DefaultInstallDirectory) && Path.GetPathRoot(DefaultInstallDirectory) != DefaultInstallDirectory;
            Environment.ExitCode = payload is { Length: > 1_000_000 } && validDefault ? 0 : 2;
            return;
        }
        if (!IsAdministrator())
        {
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
                foreach (var argument in args) start.ArgumentList.Add(argument);
                System.Diagnostics.Process.Start(start);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
            return;
        }
        ApplicationConfiguration.Initialize();
        if (args.Contains("--uninstall")) { Uninstall(); return; }
        Application.Run(new SetupForm());
    }
    private sealed class SetupForm : Form
    {
        private readonly Button install = new() { Text = "Instalar", Width = 130, Height = 38 };
        private readonly TextBox destination = new() { Text = DefaultInstallDirectory, Width = 375 };
        private readonly CheckBox desktop = new() { Text = "Crear acceso directo en el escritorio", AutoSize = true, Checked = true };
        private readonly Label status = new() { AutoSize = false, Height = 42, Dock = DockStyle.Bottom, ForeColor = Color.FromArgb(170, 170, 185) };
        public SetupForm()
        {
            Text = "OPTF Orcus Team · Instalador"; Width = 620; Height = 420; MinimumSize = new(560, 390); StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(16, 17, 21); ForeColor = Color.FromArgb(205, 202, 215); Font = new("Segoe UI", 10); Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            var title = new Label { Text = "OPTF ORCUS TEAM", Font = new("Segoe UI Semibold", 22), ForeColor = Color.FromArgb(174, 153, 216), AutoSize = true };
            var text = new Label { Text = "Instala la aplicación para tu usuario. El programa y sus dependencias quedan empaquetados; este instalador no incluye el código fuente.", MaximumSize = new(470, 0), AutoSize = true, ForeColor = Color.FromArgb(180, 180, 195) };
            var pathLabel = new Label { Text = "CARPETA DE INSTALACIÓN", AutoSize = true, ForeColor = Color.FromArgb(140, 142, 160) };
            destination.BackColor = Color.FromArgb(32, 33, 41); destination.ForeColor = Color.FromArgb(205, 202, 215); destination.BorderStyle = BorderStyle.FixedSingle;
            var browse = new Button { Text = "Cambiar…", Width = 95, Height = destination.Height + 4, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(32, 33, 41), ForeColor = Color.FromArgb(205, 202, 215) };
            browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { Description = "Elige la carpeta donde se instalará OPTF Orcus Team", SelectedPath = destination.Text, UseDescriptionForTitle = true }; if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = Path.Combine(dialog.SelectedPath, Product); };
            var destinationRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false }; destinationRow.Controls.Add(destination); destinationRow.Controls.Add(browse);
            install.BackColor = Color.FromArgb(174, 153, 216); install.ForeColor = Color.FromArgb(25, 20, 31); install.FlatStyle = FlatStyle.Flat; install.FlatAppearance.BorderSize = 0;
            install.Click += async (_, _) => await InstallAsync();
            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(30), AutoScroll = true };
            layout.Controls.Add(title); layout.SetFlowBreak(title, true); title.Margin = new(0, 0, 0, 15); layout.Controls.Add(text); text.Margin = new(0, 0, 0, 20); layout.Controls.Add(pathLabel); pathLabel.Margin = new(0, 0, 0, 6); layout.Controls.Add(destinationRow); destinationRow.Margin = new(0, 0, 0, 18); layout.Controls.Add(desktop); desktop.Margin = new(0, 0, 0, 18); layout.Controls.Add(install); layout.Controls.Add(status); Controls.Add(layout);
        }
        private async Task InstallAsync()
        {
            install.Enabled = false; status.Text = "Instalando…";
            try
            {
                var installDirectory = ValidateInstallDirectory(destination.Text);
                var installedExe = Path.Combine(installDirectory, "OPTF.OrcusTeam.exe");
                Directory.CreateDirectory(installDirectory);
                using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Orcus.Payload.exe") ?? throw new IOException("El instalador no contiene la aplicación.");
                await using var target = new FileStream(installedExe + ".new", FileMode.Create, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(target); await target.FlushAsync(); target.Close();
                File.Move(installedExe + ".new", installedExe, true);
                File.Copy(Environment.ProcessPath!, Path.Combine(installDirectory, "Desinstalar OPTF.exe"), true);
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", Product + ".lnk"), installedExe, installDirectory, "OPTF Orcus Team");
                if (desktop.Checked) CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Product + ".lnk"), installedExe, installDirectory, "OPTF Orcus Team");
                WriteUninstallRegistration(installDirectory, installedExe);
                status.Text = "Instalación completa.";
                if (MessageBox.Show(this, "OPTF Orcus Team fue instalado. ¿Abrir ahora?", Product, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installedExe) { UseShellExecute = true });
                Close();
            }
            catch (Exception ex) { status.Text = ex.Message; MessageBox.Show(this, ex.Message, "No se pudo instalar", MessageBoxButtons.OK, MessageBoxIcon.Error); install.Enabled = true; }
        }
    }
    private static void Uninstall()
    {
        if (MessageBox.Show("¿Desinstalar OPTF Orcus Team? Los respaldos del usuario se conservarán.", Product, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var installDirectory = Path.GetDirectoryName(Environment.ProcessPath!)!;
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", Product + ".lnk"));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Product + ".lnk"));
        Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\OPTFOrcusTeam", false);
        var cmd = $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{installDirectory}\"";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
    }
    private static void WriteUninstallRegistration(string installDirectory, string installedExe)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\OPTFOrcusTeam");
        key.SetValue("DisplayName", Product); key.SetValue("DisplayVersion", "1.5.0"); key.SetValue("Publisher", "Orcus Team");
        key.SetValue("DisplayIcon", installedExe); key.SetValue("InstallLocation", installDirectory);
        key.SetValue("UninstallString", $"\"{Path.Combine(installDirectory, "Desinstalar OPTF.exe")}\" --uninstall"); key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
    }
    private static void CreateShortcut(string path, string target, string workingDirectory, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new COMException("Windows Script Host no está disponible.");
        dynamic shell = Activator.CreateInstance(shellType)!; dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = target; shortcut.WorkingDirectory = workingDirectory; shortcut.Description = description; shortcut.IconLocation = target + ",0"; shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell);
    }
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private static string ValidateInstallDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Selecciona una carpeta de instalación.");
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim()));
        if (Path.GetPathRoot(path)?.Equals(path, StringComparison.OrdinalIgnoreCase) == true) throw new InvalidOperationException("No se puede instalar directamente en la raíz del disco.");
        if (File.Exists(path)) throw new InvalidOperationException("La ruta seleccionada es un archivo.");
        return path;
    }
}
