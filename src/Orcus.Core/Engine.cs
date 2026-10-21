using System.Text.Json;

namespace Orcus.Core;

public sealed record ValueState(bool Exists, string Type, string Data)
{
    public static ValueState Missing => new(false, "", "");
    public static ValueState Dword(int value) => new(true, "DWord", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
public interface ISystemAccess
{
    ValueState Read(Tweak tweak);
    void Write(Tweak tweak, ValueState state);
}
public sealed record MachineContext(bool Administrator, bool Managed, bool ProEdition, string Identity, bool Servicing = false);
public sealed record PlanItem(Tweak Tweak, ValueState Before, ValueState After, string Note, bool Applicable);
public sealed class BackupEntry
{
    public string Id { get; set; } = "";
    public ValueState Before { get; set; } = ValueState.Missing;
    public ValueState After { get; set; } = ValueState.Missing;
    public bool Attempted { get; set; }
    public bool Reverted { get; set; }
}
public sealed class Backup
{
    public int Version { get; set; } = 1;
    public string Identity { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "prepared";
    public string Error { get; set; } = "";
    public List<BackupEntry> Entries { get; set; } = [];
}
public sealed class Engine(ISystemAccess system, string backupDirectory)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string BackupDirectory => backupDirectory;

    public List<PlanItem> Preview(IEnumerable<string> ids, MachineContext context, bool developer)
    {
        var tweaks = ids.Distinct().Select(Catalog.Get).ToArray();
        if (tweaks.Any(t => t.Id == "update-manual") && tweaks.Any(t => t.Id == "update-service"))
            throw new InvalidOperationException("Elige un solo método de Windows Update.");
        return tweaks.Select(t =>
        {
            Validate(t, context, developer);
            var before = system.Read(t);
            var after = ValueState.Dword(t.Desired);
            if (t.Kind == TargetKind.Service && !before.Exists)
                return new PlanItem(t, before, after, "Omitido: servicio no instalado.", false);
            ValidateState(t, before);
            if (before == after) return new PlanItem(t, before, after, "Sin cambios: ya tiene este valor.", false);
            return new PlanItem(t, before, after, t.Kind == TargetKind.Service ? "Se aplicará en próximos inicios; no se detienen procesos." : "Cambio para el usuario o equipo indicado; puede requerir cerrar sesión.", true);
        }).ToList();
    }

    public string Apply(IReadOnlyList<PlanItem> preview, MachineContext context, bool developer, Action<string>? progress = null, bool reapply = false)
    {
        if (!context.Administrator) throw new InvalidOperationException("Abre la aplicación como administrador para aplicar cambios.");
        if (Unfinished().Any()) throw new InvalidOperationException("Existe una operación incompleta. Restáurala antes de continuar.");
        // Rebuild from trusted IDs, and reject changes since the user reviewed the plan.
        var plan = Preview(preview.Select(x => x.Tweak.Id), context, developer);
        if (!plan.SequenceEqual(preview)) throw new InvalidOperationException("El estado cambió desde la vista previa. Vuelve a revisar el plan.");
        var changes = plan.Where(x => x.Applicable || (reapply && (x.Tweak.Kind != TargetKind.Service || x.Before.Exists))).ToList();
        if (changes.Count == 0) throw new InvalidOperationException("No hay cambios pendientes en esta selección.");
        Directory.CreateDirectory(backupDirectory);
        var file = Path.Combine(backupDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.json");
        var backup = new Backup { Identity = context.Identity, Entries = changes.Select(p => new BackupEntry { Id = p.Tweak.Id, Before = p.Before, After = p.After }).ToList() };
        Save(file, backup); // No system mutation until the entire original state is durable.
        try
        {
            backup.Status = "applying";
            Save(file, backup);
            foreach (var e in backup.Entries)
            {
                var t = Catalog.Get(e.Id);
                if (system.Read(t) != e.Before) throw new InvalidOperationException($"{t.Title}: cambió durante la operación.");
                e.Attempted = true;
                Save(file, backup); // Write-ahead journal also covers interrupted writes.
                progress?.Invoke(t.Title);
                system.Write(t, e.After);
                if (system.Read(t) != e.After) throw new IOException($"No se pudo verificar: {t.Title}");
            }
            backup.Status = "applied";
            Save(file, backup);
            return file;
        }
        catch (Exception ex)
        {
            backup.Error = ex.Message;
            var errors = RevertEntries(backup, progress);
            backup.Status = errors.Count == 0 ? "rolled-back" : "recovery-needed";
            backup.Error += string.Join("; ", errors);
            Save(file, backup);
            throw new InvalidOperationException($"No se completó: {ex.Message}\n" + (errors.Count == 0 ? "Los cambios intentados fueron revertidos." : "Hay cambios pendientes de restaurar. Consulta Recuperación.") + $"\nRespaldo: {file}", ex);
        }
    }

    public void Restore(string path, MachineContext context, Action<string>? progress = null)
    {
        if (!context.Administrator) throw new InvalidOperationException("La restauración requiere administrador.");
        var file = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(file), Path.GetFullPath(backupDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El respaldo debe pertenecer a esta aplicación.");
        var latest = Restorable().FirstOrDefault();
        if (!string.Equals(file, latest, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Restaura primero la operación más reciente.");
        var backup = ReadBackup(file);
        if (backup.Identity != context.Identity) throw new InvalidOperationException("El respaldo pertenece a otro equipo o usuario.");
        foreach (var e in backup.Entries)
        {
            var t = Catalog.Get(e.Id);
            if (t.Kind == TargetKind.Service && Catalog.ProtectedServices.Contains(t.Path)) throw new InvalidOperationException("Servicio protegido.");
            if (e.After != ValueState.Dword(t.Desired)) throw new InvalidOperationException("Respaldo no válido: destino distinto al catálogo.");
            ValidateState(t, e.Before);
        }
        backup.Status = "restoring";
        Save(file, backup);
        var errors = RevertEntries(backup, progress, () => Save(file, backup));
        backup.Status = errors.Count == 0 ? "restored" : "recovery-needed";
        backup.Error = string.Join("\n", errors);
        Save(file, backup);
        if (errors.Count > 0) throw new InvalidOperationException("Restauración parcial:\n" + backup.Error);
    }

    private List<string> RevertEntries(Backup backup, Action<string>? progress, Action? checkpoint = null)
    {
        var errors = new List<string>();
        foreach (var e in backup.Entries.Where(x => x.Attempted && !x.Reverted).Reverse())
        {
            try
            {
                var t = Catalog.Get(e.Id);
                var current = system.Read(t);
                if (current != e.Before && current != e.After)
                    throw new InvalidOperationException("La configuración cambió fuera de la app; no se sobrescribe.");
                progress?.Invoke("Restaurando: " + t.Title);
                if (current != e.Before) system.Write(t, e.Before);
                if (system.Read(t) != e.Before) throw new IOException("Falló la verificación del estado original.");
                e.Reverted = true;
                checkpoint?.Invoke();
            }
            catch (Exception ex) { errors.Add($"{e.Id}: {ex.Message}"); }
        }
        return errors;
    }

    public IEnumerable<string> Restorable() => Files().Where(p => ReadBackup(p).Status is "applied" or "applying" or "prepared" or "restoring" or "recovery-needed");
    public IEnumerable<string> Unfinished() => Files().Where(p => ReadBackup(p).Status is "applying" or "prepared" or "restoring" or "recovery-needed");
    public IEnumerable<string> Files() => Directory.Exists(backupDirectory) ? Directory.GetFiles(backupDirectory, "*.json").OrderByDescending(x => x, StringComparer.Ordinal) : [];
    public static Backup ReadBackup(string path)
    {
        var b = JsonSerializer.Deserialize<Backup>(File.ReadAllText(path)) ?? throw new IOException("Respaldo vacío.");
        if (b.Version != 1 || b.Entries.Count > Catalog.All.Count || b.Entries.Select(e => e.Id).Distinct().Count() != b.Entries.Count)
            throw new IOException("Formato de respaldo no válido.");
        return b;
    }
    private static void Save(string path, Backup backup)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(backup, Json);
        using (var f = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { f.Write(bytes); f.Flush(true); }
        File.Move(path + ".tmp", path, true);
    }
    public static void ValidateState(Tweak t, ValueState state)
    {
        if (!state.Exists && t.Kind == TargetKind.Registry) return;
        if (state.Type != "DWord" || !int.TryParse(state.Data, out var v)) throw new InvalidOperationException("Tipo de configuración no compatible; no se modificó.");
        if (t.Kind == TargetKind.Service && (!state.Exists || v is < 2 or > 4)) throw new InvalidOperationException("Modo de servicio no compatible.");
    }
    private static void Validate(Tweak t, MachineContext c, bool developer)
    {
        if (c.Managed) throw new InvalidOperationException("Equipo administrado: usa las herramientas de tu organización. Optimización bloqueada.");
        if (t.Kind == TargetKind.Service && Catalog.ProtectedServices.Contains(t.Path)) throw new InvalidOperationException("Servicio esencial protegido.");
        if (t.Developer && !developer) throw new InvalidOperationException("Desbloquea el modo desarrollador para esta opción.");
        if (t.Developer && c.Servicing) throw new InvalidOperationException("Hay mantenimiento activo o un reinicio pendiente. Termínalo y vuelve a analizar antes de cambiar Windows Update.");
        if (t.ProOnly && !c.ProEdition) throw new InvalidOperationException("Esta directiva requiere Windows Pro, Enterprise o Education. En Home usa Pausar en Configuración.");
        if (t.Path.Equals("wuauserv", StringComparison.OrdinalIgnoreCase) && (!t.Developer || t.Id != "update-service")) throw new InvalidOperationException("Windows Update está protegido fuera del modo desarrollador.");
    }
}
