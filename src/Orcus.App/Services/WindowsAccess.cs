using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Orcus.Core;

namespace Orcus.App;

public sealed class WindowsAccess : ISystemAccess
{
    public ValueState Read(Tweak tweak)
    {
        RuntimeSafety.DemandNative();
        var t = Trusted(tweak);
        if (t.Kind == TargetKind.Service)
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + t.Path);
            return ReadValue(key, "Start");
        }
        var (hive, path) = Split(t.Path);
        using var reg = hive.OpenSubKey(path);
        return ReadValue(reg, t.ValueName);
    }
    public void Write(Tweak tweak, ValueState state)
    {
        RuntimeSafety.DemandNative();
        var t = Trusted(tweak);
        Engine.ValidateState(t, state);
        if (!IsAdmin) throw new InvalidOperationException("Se requieren permisos de administrador.");
        if (t.Kind == TargetKind.Service)
        {
            var manager = OpenSCManager(null, null, 1);
            if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var svc = OpenService(manager, t.Path, 2);
                if (svc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    if (!ChangeServiceConfig(svc, uint.MaxValue, uint.Parse(state.Data), uint.MaxValue, null, null, IntPtr.Zero, null, null, null, null))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally { CloseServiceHandle(svc); }
            }
            finally { CloseServiceHandle(manager); }
        }
        else
        {
            var (hive, path) = Split(t.Path);
            if (state.Exists)
            {
                using var key = hive.CreateSubKey(path, true) ?? throw new IOException("No se puede abrir el registro.");
                key.SetValue(t.ValueName, int.Parse(state.Data), RegistryValueKind.DWord);
                key.Flush();
            }
            else
            {
                using var key = hive.OpenSubKey(path, true);
                key?.DeleteValue(t.ValueName, false);
                key?.Flush();
            }
        }
    }
    private static Tweak Trusted(Tweak t)
    {
        if (Catalog.Get(t.Id) != t) throw new InvalidOperationException("Configuración fuera del catálogo.");
        if (t.Kind == TargetKind.Service && Catalog.ProtectedServices.Contains(t.Path)) throw new InvalidOperationException("Servicio protegido.");
        return t;
    }
    private static ValueState ReadValue(RegistryKey? key, string name)
    {
        if (key == null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return ValueState.Missing;
        var kind = key.GetValueKind(name);
        if (kind != RegistryValueKind.DWord) throw new InvalidOperationException($"{name}: tipo de registro inesperado; no se modificará.");
        return ValueState.Dword((int)key.GetValue(name)!);
    }
    private static (RegistryKey, string) Split(string path) => path.StartsWith("HKCU\\", StringComparison.Ordinal)
        ? (Registry.CurrentUser, path[5..]) : path.StartsWith("HKLM\\", StringComparison.Ordinal)
            ? (Registry.LocalMachine, path[5..]) : throw new InvalidOperationException("Ruta no admitida.");
    public static bool IsAdmin { get { RuntimeSafety.DemandNative(); return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
    public static string Identity { get { RuntimeSafety.DemandNative(); return Environment.MachineName + ":" + WindowsIdentity.GetCurrent().User?.Value; } }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig(IntPtr service, uint type, uint start, uint error, string? binary, string? group, IntPtr tag, string? dependencies, string? account, string? password, string? display);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
}

public sealed class Inventory
{
    public string Os { get; set; } = "Sin analizar";
    public string Edition { get; set; } = "";
    public string Cpu { get; set; } = "";
    public double RamGB { get; set; }
    public double UsedGB { get; set; }
    public double CpuPercent { get; set; }
    public string Gpu { get; set; } = "";
    public bool Managed { get; set; }
    public bool Servicing { get; set; }
    public List<string> Disks { get; set; } = [];
    public List<string> Volumes { get; set; } = [];
    public List<string> Apps { get; set; } = [];
    public List<string> Startup { get; set; } = [];
    public List<string> Processes { get; set; } = [];
    public List<string> Services { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool ProEdition => Edition.Contains("Professional") || Edition.Contains("Enterprise") || Edition.Contains("Education");
    public MachineContext Context => new(WindowsAccess.IsAdmin, Managed, ProEdition, WindowsAccess.Identity, Servicing);
    public string Recommendation => RamGB <= 8.5 ? "Media" : "Normal";
    public string Summary => $"{Os}\n{Cpu}\nGPU: {Gpu}\nRAM: {UsedGB:0.0} / {RamGB:0.0} GB · CPU: {CpuPercent:0}% (muestra puntual)\n\nALMACENAMIENTO\n{string.Join("\n", Disks)}\n{string.Join("\n", Volumes)}\n\n{(Managed ? "Equipo administrado: cambios bloqueados.\n" : "")}{(Servicing ? "Mantenimiento o reinicio pendiente: Windows Update bloqueado.\n" : "")}{string.Join("\n", Warnings)}";
    public static async Task<Inventory> Scan()
    {
        RuntimeSafety.DemandNative();
        var output = await PowerShellRunner.RunResource("Inventory.ps1");
        return JsonSerializer.Deserialize<Inventory>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("Inventario vacío.");
    }
}
