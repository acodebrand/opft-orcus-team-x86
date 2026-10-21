using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Orcus.Core;

namespace Orcus.App;

internal static class RuntimeSafety
{
    public static bool Simulation { get; private set; }
    public static void EnableSimulation() => Simulation = true;
    public static void DemandNative()
    {
        if (Simulation) throw new InvalidOperationException("La simulación bloquea el acceso al sistema real.");
    }
}

internal static class PowerShellRunner
{
    public static async Task<string> RunResource(string file, string parameters = "", int timeoutSeconds = 100)
    {
        RuntimeSafety.DemandNative();
        using var stream = typeof(PowerShellRunner).Assembly.GetManifestResourceStream("Orcus.App.Services." + file)
            ?? throw new IOException("No se encontró el recurso integrado: " + file);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var script = "& {\n" + await reader.ReadToEndAsync() + "\n} " + parameters;
        var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new IOException("No se pudo iniciar la operación.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException("No se pudo confirmar el resultado a tiempo. No se aplicarán optimizaciones. Verifica el estado desde Inicio.");
        }
        if (process.ExitCode != 0) throw new IOException((await error).Trim());
        return await output;
    }
}

internal sealed class WindowsRestorePoints : IRestorePointService
{
    private static async Task<RestorePointReceipt> Get(string mode)
    {
        RuntimeSafety.DemandNative();
        if (!WindowsAccess.IsAdmin) throw new InvalidOperationException("Abre OPTF como administrador para crear o verificar puntos de restauración.");
        var json = await PowerShellRunner.RunResource("RestorePoint.ps1", "-Mode " + mode, 180);
        return JsonSerializer.Deserialize<RestorePointReceipt>(json) ?? throw new IOException("Windows no devolvió un punto de restauración.");
    }
    public Task<RestorePointReceipt> CreateAsync() => Get("Create");
    public Task<RestorePointReceipt> LatestAsync() => Get("Latest");
    public async Task<bool> ExistsAsync(RestorePointReceipt receipt)
    {
        var json = await PowerShellRunner.RunResource("RestorePoint.ps1", "-Mode Verify -Sequence " + receipt.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var found = JsonSerializer.Deserialize<RestorePointReceipt>(json);
        return found == receipt;
    }
}

internal sealed class AutoElevatingRestorePoints : IRestorePointService
{
    private readonly WindowsRestorePoints native = new();
    private RestorePointReceipt? helperVerified;
    public Task<RestorePointReceipt> CreateAsync() => Run("create");
    public Task<RestorePointReceipt> LatestAsync() => Run("latest");
    public async Task<bool> ExistsAsync(RestorePointReceipt receipt)
    {
        if (helperVerified == receipt) return true;
        if (!WindowsAccess.IsAdmin) return false;
        return await native.ExistsAsync(receipt);
    }
    private async Task<RestorePointReceipt> Run(string mode)
    {
        if (WindowsAccess.IsAdmin) return mode == "create" ? await native.CreateAsync() : await native.LatestAsync();
        var response = Path.Combine(Path.GetTempPath(), "optf-restore-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            foreach (var arg in new[] { "--restore-point-helper", mode, "--response", response }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info) ?? throw new IOException("No se pudo solicitar permiso de administrador.");
            await process.WaitForExitAsync();
            if (!File.Exists(response)) throw new InvalidOperationException("No se recibió confirmación. La solicitud de administrador pudo cancelarse.");
            var result = JsonSerializer.Deserialize<HelperResult>(await File.ReadAllTextAsync(response)) ?? throw new IOException("Respuesta de elevación no válida.");
            if (!result.Success || result.Receipt == null) throw new InvalidOperationException(result.Error);
            helperVerified = result.Receipt;
            return result.Receipt;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new InvalidOperationException("Permiso de administrador cancelado. No se creó ningún punto."); }
        finally { try { if (File.Exists(response)) File.Delete(response); } catch { } }
    }
    private sealed record HelperResult(bool Success, RestorePointReceipt? Receipt, string Error);
}
