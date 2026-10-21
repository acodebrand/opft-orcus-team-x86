using System.Text.Json;
using Orcus.Core;

var count = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); count++; }
    catch (Exception e) { Console.Error.WriteLine("FAIL " + name + "\n" + e); Environment.Exit(1); }
}
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected refusal/failure"); }
var context = new MachineContext(true, false, true, "test-machine:test-user");
var testRoot = Path.GetFullPath(Path.Combine("artifacts", "tests", Guid.NewGuid().ToString("N")));
(Engine Engine, FakeSystem Fake, string Dir) Fixture()
{
    var dir = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
    var fake = new FakeSystem();
    return (new Engine(fake, dir), fake, dir);
}
List<PlanItem> Plan(Engine engine, params string[] ids) => engine.Preview(ids, context, false);

Test("Unique IDs and no protected service in the catalog", () => {
    Check(Catalog.All.Select(t => t.Id).Distinct().Count() == Catalog.All.Count);
    Check(Catalog.All.All(t => t.Kind != TargetKind.Service || !Catalog.ProtectedServices.Contains(t.Path)));
    Check(Catalog.All.Single(t => t.Path == "wuauserv").Developer);
});
Test("All profiles and usage presets preserve Windows Update", () => {
    foreach (var p in Enum.GetValues<Profile>()) foreach (var u in Enum.GetValues<Usage>())
        Check(Catalog.All.Where(t => Catalog.DefaultSelected(t, p, u)).All(t => !t.Developer));
    Check(!Catalog.DefaultSelected(Catalog.Get("svc-WSearch"), Profile.UltraMax, Usage.Programacion));
    Check(Catalog.DefaultSelected(Catalog.Get("game-mode"), Profile.Normal, Usage.Gaming));
    Check(!Catalog.All.Any(t => t.Path is "SharedAccess" or "MSiSCSI" or "WlanSvc" or "Netlogon"));
});
Test("Preview is read-only, deduplicates and skips absent services", () => {
    var (e, f, _) = Fixture(); f.Values["svc-Fax"] = ValueState.Missing;
    var plan = Plan(e, "svc-Fax", "transparency", "transparency");
    Check(plan.Count == 2 && !plan[0].Applicable && plan[1].Applicable && f.Writes.Count == 0);
});
Test("Unknown and protected IDs cannot be requested", () => {
    var (e, _, _) = Fixture(); Throws(() => Plan(e, "svc-WinDefend")); Throws(() => Plan(e, "arbitrary-command"));
});
Test("Developer mode must be explicit, methods are exclusive", () => {
    var (e, f, _) = Fixture(); Throws(() => Plan(e, "update-service"));
    Throws(() => e.Preview(["update-service", "update-manual"], context, true));
    Check(e.Preview(["update-service"], context, true).Count == 1 && f.Writes.Count == 0);
});
Test("Managed PCs, unsupported editions and servicing are blocked", () => {
    var (e, _, _) = Fixture();
    Throws(() => e.Preview(["transparency"], context with { Managed = true }, false));
    Throws(() => e.Preview(["update-manual"], context with { ProEdition = false }, true));
    Throws(() => e.Preview(["update-service"], context with { Servicing = true }, true));
});
Test("Read-only user cannot apply", () => {
    var (e, f, _) = Fixture(); var p = Plan(e, "transparency");
    Throws(() => e.Apply(p, context with { Administrator = false }, false)); Check(f.Writes.Count == 0);
});
Test("Unsupported original types and driver start modes are refused", () => {
    var (e, f, _) = Fixture(); f.Values["transparency"] = new(true, "String", "foo"); Throws(() => Plan(e, "transparency"));
    f.Values["svc-Fax"] = ValueState.Dword(0); Throws(() => Plan(e, "svc-Fax")); Check(f.Writes.Count == 0);
});
Test("Original snapshot and write-ahead intent are durable before writing", () => {
    var (e, f, dir) = Fixture();
    f.BeforeWrite = (_, _) => { var b = Engine.ReadBackup(Directory.GetFiles(dir, "*.json").Single()); Check(b.Entries.Count == 2 && b.Entries.Any(x => x.Attempted)); };
    var path = e.Apply(Plan(e, "transparency", "svc-Fax"), context, false);
    Check(Engine.ReadBackup(path).Status == "applied" && f.Writes.Count == 2);
});
Test("Apply and restore recover absent registry values and original service modes", () => {
    var (e, f, _) = Fixture(); var path = e.Apply(Plan(e, "transparency", "svc-Fax"), context, false);
    Check(f.Values["transparency"] == ValueState.Dword(0));
    e.Restore(path, context);
    Check(f.Values["transparency"] == ValueState.Missing && f.Values["svc-Fax"] == ValueState.Dword(3));
    Check(Engine.ReadBackup(path).Status == "restored" && !e.Restorable().Any());
});
Test("Existing registry values are restored exactly", () => {
    var (e, f, _) = Fixture(); f.Values["transparency"] = ValueState.Dword(7);
    var path = e.Apply(Plan(e, "transparency"), context, false); e.Restore(path, context);
    Check(f.Values["transparency"] == ValueState.Dword(7));
});
Test("Service Update can be applied only through developer gate and restored", () => {
    var (e, f, _) = Fixture(); var plan = e.Preview(["update-service"], context, true);
    Throws(() => e.Apply(plan, context, false)); Check(f.Writes.Count == 0);
    var path = e.Apply(plan, context, true); e.Restore(path, context);
    Check(f.Values["update-service"] == ValueState.Dword(3));
});
Test("Changed state after preview prevents all mutations", () => {
    var (e, f, _) = Fixture(); var plan = Plan(e, "transparency"); f.Values["transparency"] = ValueState.Dword(1);
    Throws(() => e.Apply(plan, context, false)); Check(f.Writes.Count == 0);
});
Test("Forged preview with altered target is refused", () => {
    var (e, f, _) = Fixture(); var plan = Plan(e, "transparency");
    plan[0] = plan[0] with { Tweak = plan[0].Tweak with { Path = "HKLM\\bogus" } };
    Throws(() => e.Apply(plan, context, false)); Check(f.Writes.Count == 0);
});
Test("No effective changes produces no backup or write", () => {
    var (e, f, _) = Fixture(); f.Values["transparency"] = ValueState.Dword(0);
    Throws(() => e.Apply(Plan(e, "transparency"), context, false)); Check(f.Writes.Count == 0 && !e.Files().Any());
});
Test("Reapply rewrites and verifies values that already match", () => {
    var (e, f, _) = Fixture(); f.Values["transparency"] = ValueState.Dword(0);
    var path = e.Apply(Plan(e, "transparency"), context, false, reapply: true);
    Check(f.Writes.SequenceEqual(["transparency"]) && Engine.ReadBackup(path).Status == "applied");
});
Test("Snapshot storage failure prevents configuration writes", () => {
    var (e, f, dir) = Fixture(); Directory.CreateDirectory(Path.GetDirectoryName(dir)!); File.WriteAllText(dir, "not a directory");
    Throws(() => e.Apply(Plan(e, "transparency"), context, false)); Check(f.Writes.Count == 0);
});
Test("Failure before second write rolls back first write", () => {
    var (e, f, _) = Fixture(); bool failed = false;
    f.BeforeWrite = (t, _) => { if (t.Id == "svc-Fax" && !failed) { failed = true; throw new IOException("simulated failure"); } };
    Throws(() => e.Apply(Plan(e, "transparency", "svc-Fax"), context, false));
    Check(f.Values["transparency"] == ValueState.Missing && Engine.ReadBackup(e.Files().Single()).Status == "rolled-back");
});
Test("Failure after a partial write is also rolled back", () => {
    var (e, f, _) = Fixture(); bool failed = false;
    f.AfterWrite = (_, _) => { if (!failed) { failed = true; throw new IOException("write took effect, then failed"); } };
    Throws(() => e.Apply(Plan(e, "transparency"), context, false));
    Check(f.Values["transparency"] == ValueState.Missing && !e.Unfinished().Any());
});
Test("Failed rollback remains recoverable and blocks new optimizations", () => {
    var (e, f, _) = Fixture();
    f.BeforeWrite = (t, v) => { if (t.Id == "svc-Fax" || !v.Exists) throw new IOException("access denied"); };
    Throws(() => e.Apply(Plan(e, "transparency", "svc-Fax"), context, false));
    var path = e.Unfinished().Single(); Check(Engine.ReadBackup(path).Status == "recovery-needed");
    Throws(() => e.Apply(Plan(e, "dvr"), context, false));
    f.BeforeWrite = null; e.Restore(path, context);
    Check(f.Values["transparency"] == ValueState.Missing && !e.Unfinished().Any());
});
Test("External edits are not overwritten during restore", () => {
    var (e, f, _) = Fixture(); var path = e.Apply(Plan(e, "transparency"), context, false);
    f.Values["transparency"] = ValueState.Dword(42); Throws(() => e.Restore(path, context));
    Check(f.Values["transparency"] == ValueState.Dword(42) && Engine.ReadBackup(path).Status == "recovery-needed");
});
Test("Backups restore in reverse order", () => {
    var (e, f, _) = Fixture(); var first = e.Apply(Plan(e, "transparency"), context, false);
    var second = e.Apply(Plan(e, "dvr"), context, false);
    Throws(() => e.Restore(first, context)); e.Restore(second, context); e.Restore(first, context);
    Check(f.Values["transparency"] == ValueState.Missing && f.Values["dvr"] == ValueState.Missing);
});
Test("Backups from different identities or directories are refused", () => {
    var (e, _, _) = Fixture(); var path = e.Apply(Plan(e, "transparency"), context, false);
    Throws(() => e.Restore(path, context with { Identity = "other-machine" }));
    Throws(() => e.Restore(Path.Combine(testRoot, "outside.json"), context));
});
Test("Interrupted journal recovers a write even without an applied status", () => {
    var (e, f, dir) = Fixture(); Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "interrupted.json");
    var b = new Backup { Identity = context.Identity, Status = "applying", Entries = [new() { Id = "transparency", Before = ValueState.Missing, After = ValueState.Dword(0), Attempted = true }] };
    File.WriteAllText(path, JsonSerializer.Serialize(b)); f.Values["transparency"] = ValueState.Dword(0);
    e.Restore(path, context); Check(f.Values["transparency"] == ValueState.Missing);
});
Test("Tampered protected targets in backup cannot be restored", () => {
    var (e, f, dir) = Fixture(); Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "bad.json");
    File.WriteAllText(path, JsonSerializer.Serialize(new Backup { Identity = context.Identity, Status = "applied", Entries = [new() { Id = "svc-WinDefend", Before = ValueState.Dword(4), After = ValueState.Dword(4), Attempted = true }] }));
    Throws(() => e.Restore(path, context)); Check(f.Writes.Count == 0);
});
var clock = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
Test("Restore-point verification rejects an unprepared receipt", () => {
    var fake = new FakePoints(new(10, "fake", clock)); var gate = new RestoreProtection(fake, () => clock);
    Throws(() => gate.EnsureReadyAsync().GetAwaiter().GetResult()); Check(gate.Current == null);
});
Test("Creating a point verifies its existence", () => {
    var fake = new FakePoints(new(10, "fake", clock)); var gate = new RestoreProtection(fake, () => clock);
    var point = gate.PrepareAsync(true).GetAwaiter().GetResult();
    Check(point.Sequence == 10 && fake.Creates == 1 && fake.Verifications == 1);
    gate.EnsureReadyAsync().GetAwaiter().GetResult(); Check(fake.Verifications == 2);
});
Test("Existing recent point is verified without creating another", () => {
    var fake = new FakePoints(new(10, "fake", clock.AddHours(-2))); var gate = new RestoreProtection(fake, () => clock);
    gate.PrepareAsync(false).GetAwaiter().GetResult(); Check(fake.Creates == 0 && gate.Current != null);
});
Test("Creation reported but unverified never opens the gate", () => {
    var fake = new FakePoints(new(10, "fake", clock)) { Exists = false }; var gate = new RestoreProtection(fake, () => clock);
    Throws(() => gate.PrepareAsync(true).GetAwaiter().GetResult()); Check(gate.Current == null);
});
Test("Old points and invalid receipts are rejected", () => {
    foreach (var point in new[] { new RestorePointReceipt(10, "old", clock.AddHours(-25)), new(0, "bad", clock), new(10, "future", clock.AddHours(1)), new(10, "", clock) }) {
        var gate = new RestoreProtection(new FakePoints(point), () => clock);
        Throws(() => gate.PrepareAsync(false).GetAwaiter().GetResult()); Check(gate.Current == null);
    }
});
Test("Deleted restore point invalidates the gate before apply", () => {
    var fake = new FakePoints(new(10, "fake", clock)); var gate = new RestoreProtection(fake, () => clock);
    gate.PrepareAsync(true).GetAwaiter().GetResult(); fake.Exists = false;
    Throws(() => gate.EnsureReadyAsync().GetAwaiter().GetResult()); Check(gate.Current == null);
});
Test("Expired point must be re-created or re-selected", () => {
    var moment = clock; var gate = new RestoreProtection(new FakePoints(new(10, "fake", clock)), () => moment);
    gate.PrepareAsync(true).GetAwaiter().GetResult(); moment = clock.AddHours(25);
    Throws(() => gate.EnsureReadyAsync().GetAwaiter().GetResult()); Check(gate.Current == null);
});
Test("System restore failure does not preserve previous authorization", () => {
    var fake = new FakePoints(new(10, "fake", clock)); var gate = new RestoreProtection(fake, () => clock);
    gate.PrepareAsync(true).GetAwaiter().GetResult(); fake.Fail = true;
    Throws(() => gate.PrepareAsync(true).GetAwaiter().GetResult()); Check(gate.Current == null);
});
Console.WriteLine($"\n{count} tests passed. Fake system only; no Windows configuration was changed.");

sealed class FakePoints(RestorePointReceipt point) : IRestorePointService
{
    public bool Exists = true;
    public bool Fail;
    public int Creates;
    public int Verifications;
    public Task<RestorePointReceipt> CreateAsync() { Creates++; if (Fail) throw new IOException("Simulated protection disabled or frequency limit"); return Task.FromResult(point); }
    public Task<RestorePointReceipt> LatestAsync() => Task.FromResult(point);
    public Task<bool> ExistsAsync(RestorePointReceipt receipt) { Verifications++; return Task.FromResult(Exists && receipt == point); }
}

sealed class FakeSystem : ISystemAccess
{
    public Dictionary<string, ValueState> Values { get; } = [];
    public List<string> Writes { get; } = [];
    public Action<Tweak, ValueState>? BeforeWrite { get; set; }
    public Action<Tweak, ValueState>? AfterWrite { get; set; }
    public ValueState Read(Tweak tweak) => Values.GetValueOrDefault(tweak.Id, tweak.Kind == TargetKind.Service ? ValueState.Dword(3) : ValueState.Missing);
    public void Write(Tweak tweak, ValueState value)
    {
        BeforeWrite?.Invoke(tweak, value);
        Values[tweak.Id] = value; Writes.Add(tweak.Id);
        AfterWrite?.Invoke(tweak, value);
    }
}
