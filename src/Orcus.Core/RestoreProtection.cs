namespace Orcus.Core;

public sealed record RestorePointReceipt(int Sequence, string Description, DateTime CreatedUtc);
public interface IRestorePointService
{
    Task<RestorePointReceipt> CreateAsync();
    Task<RestorePointReceipt> LatestAsync();
    Task<bool> ExistsAsync(RestorePointReceipt receipt);
}

// A verified receipt is kept in memory only. It must be revalidated before every apply.
public sealed class RestoreProtection(IRestorePointService service, Func<DateTime>? utcNow = null)
{
    private readonly Func<DateTime> now = utcNow ?? (() => DateTime.UtcNow);
    public RestorePointReceipt? Current { get; private set; }
    public async Task<RestorePointReceipt> PrepareAsync(bool createNew)
    {
        Current = null;
        var receipt = createNew ? await service.CreateAsync() : await service.LatestAsync();
        Validate(receipt);
        if (!await service.ExistsAsync(receipt)) throw new InvalidOperationException("Windows no confirmó el punto de restauración. No se habilitarán cambios.");
        return Current = receipt;
    }
    public async Task EnsureReadyAsync()
    {
        try
        {
            var receipt = Current ?? throw new InvalidOperationException("Primero crea o verifica un punto de restauración desde Inicio.");
            Validate(receipt);
            if (!await service.ExistsAsync(receipt)) throw new InvalidOperationException("El punto de restauración ya no está disponible. Vuelve a verificarlo en Inicio.");
        }
        catch { Current = null; throw; }
    }
    private void Validate(RestorePointReceipt receipt)
    {
        if (receipt.Sequence <= 0 || string.IsNullOrWhiteSpace(receipt.Description)) throw new InvalidOperationException("Windows devolvió un punto no válido.");
        var age = now() - receipt.CreatedUtc.ToUniversalTime();
        if (age < TimeSpan.FromMinutes(-5) || age > TimeSpan.FromHours(24))
            throw new InvalidOperationException("Necesitas un punto de restauración de las últimas 24 horas. Crea uno nuevo desde Inicio.");
    }
}
