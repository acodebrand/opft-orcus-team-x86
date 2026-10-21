using Orcus.Core;

namespace Orcus.App;

internal sealed class SimulatedSystem : ISystemAccess
{
    private readonly Dictionary<string, ValueState> values = [];
    public int WriteCount { get; private set; }
    public ValueState Read(Tweak tweak) => values.GetValueOrDefault(tweak.Id,
        tweak.Kind == TargetKind.Service ? ValueState.Dword(3) : ValueState.Missing);
    public void Write(Tweak tweak, ValueState state) { values[tweak.Id] = state; WriteCount++; }
    public static Inventory Inventory() => new()
    {
        Os = "Windows 11 Pro · EQUIPO FICTICIO", Edition = "Professional", Cpu = "Procesador de ejemplo", Gpu = "GPU de ejemplo",
        RamGB = 16, UsedGB = 5.8, CpuPercent = 7,
        Processes = Enumerable.Range(1, 132).Select(i => $"Proceso simulado {i}").ToList(),
        Services = ["Servicio simulado"], Apps = ["Aplicación de ejemplo"], Disks = ["SSD ficticio"], Startup = [], Volumes = []
    };
}
internal sealed class SimulatedRestorePoints : IRestorePointService
{
    private RestorePointReceipt? point;
    public Task<RestorePointReceipt> CreateAsync() => Task.FromResult(point = new(101, "Punto SIMULADO · no existe en Windows", DateTime.UtcNow));
    public Task<RestorePointReceipt> LatestAsync() => Task.FromResult(point ?? throw new InvalidOperationException("Sin punto simulado."));
    public Task<bool> ExistsAsync(RestorePointReceipt receipt) => Task.FromResult(receipt == point);
}
