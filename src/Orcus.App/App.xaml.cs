using System.Windows;
using System.IO;
using System.Text.Json;
using System.ComponentModel;
using System.Diagnostics;
using Orcus.Core;

namespace Orcus.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (TryRunElevatedRestorePoint(e.Args)) return;
        if (!e.Args.Contains("--ui-test") && !e.Args.Contains("--smoke-test") && !WindowsAccess.IsAdmin)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
                foreach (var argument in e.Args) start.ArgumentList.Add(argument);
                Process.Start(start);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                MessageBox.Show("OPTF necesita permisos de administrador para analizar y aplicar la configuración.", "OPTF · Permiso cancelado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) => {
            if (e.Args.Contains("--ui-test") || e.Args.Contains("--smoke-test"))
            {
                int i = Array.FindIndex(e.Args, x => x is "--ui-test" or "--smoke-test");
                var output = Path.GetFullPath(e.Args.ElementAtOrDefault(i + 1) ?? "artifacts/qa-simulated");
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "ui-results.txt"), "FAIL " + args.Exception);
                args.Handled = true; Shutdown(1); return;
            }
            MessageBox.Show(args.Exception.Message, "OPTF · Operación no completada", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        new MainWindow(e.Args).Show();
    }

    private bool TryRunElevatedRestorePoint(string[] args)
    {
        int command = Array.IndexOf(args, "--restore-point-helper");
        if (command < 0) return false;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        int response = Array.IndexOf(args, "--response");
        var responsePath = response >= 0 ? Path.GetFullPath(args.ElementAtOrDefault(response + 1) ?? "") : "";
        try
        {
            if (string.IsNullOrWhiteSpace(responsePath)) throw new InvalidOperationException("Falta la ruta de respuesta.");
            var mode = args.ElementAtOrDefault(command + 1);
            IRestorePointService service = new WindowsRestorePoints();
            var receipt = mode == "create" ? service.CreateAsync().GetAwaiter().GetResult() : service.LatestAsync().GetAwaiter().GetResult();
            if (!service.ExistsAsync(receipt).GetAwaiter().GetResult()) throw new InvalidOperationException("Windows no confirmó el punto de restauración.");
            File.WriteAllText(responsePath, JsonSerializer.Serialize(new HelperResponse(true, receipt, "")));
            Shutdown(0);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(responsePath)) File.WriteAllText(responsePath, JsonSerializer.Serialize(new HelperResponse(false, null, ex.Message)));
            Shutdown(2);
        }
        return true;
    }
    private sealed record HelperResponse(bool Success, RestorePointReceipt? Receipt, string Error);
}
