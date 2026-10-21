using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Orcus.Core;

namespace Orcus.App;

public sealed class OptionRow(Tweak tweak, Action changed) : INotifyPropertyChanged
{
    public Tweak Tweak { get; } = tweak;
    private bool selected;
    private bool enabled = true;
    private string availability = "Se comprobará antes de aplicar.";
    public bool Selected { get => selected; set { if (selected == value) return; selected = value; Notify(nameof(Selected)); Notify(nameof(SelectionLabel)); changed(); } }
    public string SelectionLabel => Selected ? "SELECCIONADA" : "OPCIONAL";
    public bool Enabled { get => enabled; set { if (enabled == value) return; enabled = value; Notify(nameof(Enabled)); } }
    public string Availability { get => availability; set { availability = value; Notify(nameof(Availability)); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
}

public partial class MainWindow : Window
{
    private readonly ISystemAccess access;
    private readonly Engine engine;
    private readonly RestoreProtection protection;
    private readonly bool isSimulation;
    private readonly string simulationOutput;
    private bool batchSelection;
    private bool IsAdmin => isSimulation || WindowsAccess.IsAdmin;
    private MachineContext Context => isSimulation ? new(true, false, true, "simulation:fictional-user") : inventory?.Context ?? new(IsAdmin, false, false, WindowsAccess.Identity);
    private readonly List<OptionRow> rows = [];
    private readonly string[] arguments;
    private Inventory? inventory;
    private string page = "home";
    private bool ready;
    private bool busy;
    private string? lastAppliedPage;
    private string? lastAppliedTitle;
    private string? lastAppliedDetail;
    private readonly string settingsFile;
    public MainWindow(string[] args)
    {
        arguments = args;
        isSimulation = args.Contains("--ui-test") || args.Contains("--smoke-test");
        if (isSimulation) RuntimeSafety.EnableSimulation();
        var simulationIndex = Array.FindIndex(args, a => a is "--ui-test" or "--smoke-test");
        simulationOutput = Path.GetFullPath(isSimulation ? args.ElementAtOrDefault(simulationIndex + 1) ?? "artifacts/qa-simulated" : "artifacts/unused");
        access = isSimulation ? new SimulatedSystem() : new WindowsAccess();
        protection = new RestoreProtection(isSimulation ? new SimulatedRestorePoints() : new AutoElevatingRestorePoints());
        var data = isSimulation ? Path.Combine(simulationOutput, "state") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OPTF Orcus Team");
        settingsFile = Path.Combine(data, "preferences.json");
        engine = new Engine(access, Path.Combine(data, "Backups"));
        InitializeComponent();
        MinWidth = Math.Min(MinWidth, SystemParameters.WorkArea.Width);
        MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height);
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 32));
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32));
        foreach (var tweak in Catalog.All) { OptionRow? row = null; row = new OptionRow(tweak, () => OptionChanged(row!)); rows.Add(row); }
        ready = true;
        PermissionText.Text = isSimulation ? "SIMULACIÓN · Sin acceso a Windows" : IsAdmin ? "Permisos de administrador" : "Modo de consulta";
        AdminButton.Visibility = IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        SourceInitialized += (_, _) => { if (!isSimulation) WindowChrome.Apply(this); };
        Closing += (_, e) => {
            if (busy) { e.Cancel = true; StatusText.Text = "Espera a que termine la operación antes de cerrar."; return; }
            try { SavePreferences(); } catch { /* Preferences are not part of the configuration backup. */ }
        };
        SelectDefaults();
        LoadPreferences();
        ShowPage("home");
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (isSimulation) { await SmokeTest(); return; }
        await Scan();
    }
    private async Task Scan()
    {
        if (busy) return;
        SetBusy(true, "Analizando hardware, servicios, inicio y aplicaciones…");
        try
        {
            inventory = isSimulation ? SimulatedSystem.Inventory() : await Inventory.Scan();
            RamMetric.Text = $"{inventory.UsedGB:0.0} GB";
            RamDetail.Text = $"de {inventory.RamGB:0.0} GB instalados";
            CpuMetric.Text = $"{inventory.CpuPercent:0}%";
            ProcessMetric.Text = inventory.Processes.Count.ToString();
            SampleLabel.Text = isSimulation ? "DATOS SIMULADOS" : $"Muestra de {DateTime.Now:HH:mm}";
            RecommendationText.Text = inventory.Managed ? "Equipo administrado · cambios bloqueados" : $"Punto de partida sugerido: {inventory.Recommendation}";
            StatusText.Text = isSimulation ? "Vista de prueba · todos los datos son ficticios." : $"Análisis de {DateTime.Now:HH:mm}. {inventory.Apps.Count} apps · {inventory.Services.Count} servicios. Sin cambios.";
            UpdateAvailability();
        }
        catch (Exception ex) { inventory = null; Report(ex); }
        finally { SetBusy(false); }
    }
    private async void ScanClick(object sender, RoutedEventArgs e) => await Scan();

    private void UpdateAvailability()
    {
        foreach (var row in rows)
        {
            try
            {
                var state = access.Read(row.Tweak);
                bool missing = row.Tweak.Kind == TargetKind.Service && !state.Exists;
                bool locked = row.Tweak.Developer && DeveloperUnlock.IsChecked != true;
                bool edition = row.Tweak.ProOnly && inventory?.ProEdition != true;
                bool managed = inventory?.Managed == true;
                bool maintenance = row.Tweak.Developer && inventory?.Servicing == true;
                row.Enabled = !missing && !locked && !edition && !managed && !maintenance;
                row.Availability = missing ? "No instalado en este Windows · se omite" : managed ? "Equipo administrado · bloqueado" : maintenance ? "Mantenimiento o reinicio pendiente · bloqueado" : edition ? "Esta directiva no está disponible en la edición detectada" : locked ? "Desbloquea el modo desarrollador" : $"Actual: {FormatState(row.Tweak, state)}";
                if (!row.Enabled) row.Selected = false;
            }
            catch (Exception ex) { row.Enabled = false; row.Selected = false; row.Availability = "No disponible: " + ex.Message; }
        }
    }
    private void Navigate(object sender, RoutedEventArgs e) { if (!busy && sender is Button b) ShowPage(b.Tag?.ToString() ?? "home"); }
    private void ShowPage(string target)
    {
        page = target;
        HomePanel.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        OptionsPanel.Visibility = page is "profiles" or "extras" or "developer" ? Visibility.Visible : Visibility.Collapsed;
        RecoveryPanel.Visibility = page == "recovery" ? Visibility.Visible : Visibility.Collapsed;
        ProfileControls.Visibility = page == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.Visibility = page is "profiles" or "extras" or "developer" ? Visibility.Visible : Visibility.Collapsed;
        DeveloperControls.Visibility = page == "developer" ? Visibility.Visible : Visibility.Collapsed;
        ExtrasTools.Visibility = page == "extras" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = page switch { "profiles" => "Perfiles de optimización", "extras" => "Extras", "developer" => "Modo desarrollador", "recovery" => "Recuperación", _ => "Inicio" };
        OptionsHeading.Text = page switch { "extras" => "Hardware y funciones opcionales", "developer" => "Windows Update · experimental", _ => "Configuración del perfil" };
        ProfileHint.Visibility = page == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        OptionsIntro.Text = page switch {
            "extras" => "Opciones manuales según tu hardware. Inicio, limpieza, desinstalación y energía se abren en las herramientas de Windows. No se borran apps ni archivos automáticamente.",
            "developer" => "Windows Update permanece intacto en Normal, Media, Ultra y Ultra Max. Desbloquea el modo, elige un único método y pulsa Aplicar cambios.",
            _ => "Elige una base y el uso del equipo. Después ajusta la selección de abajo. Cambiar de perfil aquí todavía no modifica Windows." };
        ListHeading.Text = page switch { "extras" => "Extras disponibles", "developer" => "Opciones de Windows Update", _ => "Opciones incluidas en el perfil" };
        ApplyButton.Content = page switch { "extras" => "Aplicar extras  →", "developer" => "Aplicar cambio de desarrollador  →", _ => "Aplicar perfil  →" };
        AppliedBanner.Visibility = lastAppliedPage == page ? Visibility.Visible : Visibility.Collapsed;
        if (lastAppliedPage == page) { AppliedTitle.Text = lastAppliedTitle; AppliedDetail.Text = lastAppliedDetail; }
        foreach (var button in new[] { NavHome, NavProfiles, NavExtras, NavDeveloper, NavRecovery })
        {
            button.SetResourceReference(BackgroundProperty, button.Tag?.ToString() == page ? "Raised" : "Panel");
            button.SetResourceReference(ForegroundProperty, button.Tag?.ToString() == page ? "Accent" : "Muted");
        }
        SearchBox.Text = "";
        RefreshOptions();
        if (page == "recovery") RefreshHistory();
        MainScroll.ScrollToTop();
        if (!isSimulation && SystemParameters.ClientAreaAnimation)
        {
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            PageContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = easing });
            PageOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
        }
    }
    private void RefreshOptions()
    {
        if (!ready) return;
        var query = SearchBox.Text.Trim();
        var visible = rows.Where(r => page switch {
            "developer" => r.Tweak.Developer,
            "extras" => !r.Tweak.Developer && r.Tweak.Level == 99,
            _ => !r.Tweak.Developer && r.Tweak.Level != 99
        }).Where(r => (r.Tweak.Title + " " + r.Tweak.Path + " " + r.Tweak.Description + " " + r.Tweak.Category).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        OptionList.ItemsSource = visible;
        EmptyOptions.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) => RefreshOptions();
    private void PresetChanged(object sender, SelectionChangedEventArgs e) { if (ready) SelectDefaults(); }
    private void ResetSelection(object sender, RoutedEventArgs e) => SelectDefaults();
    private void ClearSelection(object sender, RoutedEventArgs e) { batchSelection = true; foreach (var row in rows) row.Selected = false; batchSelection = false; UpdateCount(); }
    private void SelectDefaults()
    {
        batchSelection = true;
        foreach (var r in rows) r.Selected = r.Enabled && Catalog.DefaultSelected(r.Tweak, (Profile)ProfileBox.SelectedIndex, (Usage)UsageBox.SelectedIndex);
        batchSelection = false;
        ProfileHint.Text = ProfileBox.SelectedIndex switch { 1 => "Media · Menos actividad secundaria; revisa las funciones que utilizas.", 2 => "Ultra · Añade recortes de búsqueda y compatibilidad. Lee cada efecto.", 3 => "Ultra Max · Para equipos dedicados. Los módulos de hardware se eligen en Extras.", _ => "Normal · Cambios moderados para conservar la mayor compatibilidad." };
        UpdateCount();
    }
    private void OptionChanged(OptionRow row)
    {
        if (!batchSelection && row.Selected && row.Tweak.Developer)
            foreach (var other in rows.Where(x => x != row && x.Tweak.Developer)) other.Selected = false;
        UpdateCount();
    }
    private void UnlockChanged(object sender, RoutedEventArgs e) { if (ready) { UpdateAvailability(); RefreshOptions(); } }
    private void UpdateCount()
    {
        if (!ready || batchSelection) return;
        SelectionText.Text = $"{rows.Count(r => r.Selected)} opciones seleccionadas";
        ApplyButton.IsEnabled = !busy && SelectedRowsForPage().Any();
    }
    private static string FormatState(Tweak t, ValueState s) => !s.Exists ? "No configurado" : t.Kind == TargetKind.Service ? s.Data switch { "2" => "Automático", "3" => "Manual", "4" => "Deshabilitado", _ => s.Data } : $"{s.Type} {s.Data}";

    private IEnumerable<OptionRow> SelectedRowsForPage() => rows.Where(r => r.Selected && (page switch {
        "developer" => r.Tweak.Developer,
        "extras" => !r.Tweak.Developer && r.Tweak.Level == 99,
        "profiles" => !r.Tweak.Developer && r.Tweak.Level != 99,
        _ => false
    }));

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (inventory == null) await Scan();
        if (inventory == null) return;
        try
        {
            var dev = DeveloperUnlock.IsChecked == true;
            var selected = SelectedRowsForPage().ToArray();
            if (selected.Length == 0) { StatusText.Text = "Selecciona al menos una opción para aplicar."; return; }
            var plan = engine.Preview(selected.Select(r => r.Tweak.Id), Context, dev);
            if (!IsAdmin) throw new InvalidOperationException("Abre OPTF como administrador para aplicar los cambios. Puedes usar el botón de la barra lateral.");
            SetBusy(true, "Comprobando el equipo antes de aplicar…");
            // Fresh management/servicing flags, not the potentially stale dashboard snapshot.
            inventory = isSimulation ? SimulatedSystem.Inventory() : await Inventory.Scan();
            var progress = new Progress<string>(s => StatusText.Text = s);
            var currentContext = Context;
            var path = await RunLocked(() => engine.Apply(plan, currentContext, dev, s => ((IProgress<string>)progress).Report(s), reapply: true));
            var appliedName = page switch { "profiles" => $"Perfil {((ComboBoxItem)ProfileBox.SelectedItem).Content}", "extras" => "Extras seleccionados", _ => "Modo desarrollador" };
            var reaffirmed = plan.Count(p => !p.Applicable && (p.Tweak.Kind != TargetKind.Service || p.Before.Exists));
            var omitted = plan.Count - plan.Count(p => p.Applicable) - reaffirmed;
            AppliedTitle.Text = "✓  Cambios aplicados";
            AppliedDetail.Text = $"{appliedName} · {plan.Count(p => p.Applicable)} cambios nuevos y {reaffirmed} valores reafirmados. Reinicia la computadora para completar la configuración.";
            lastAppliedPage = page; lastAppliedTitle = AppliedTitle.Text; lastAppliedDetail = AppliedDetail.Text;
            AppliedBanner.Visibility = Visibility.Visible;
            StatusText.Text = $"Aplicado exitosamente · {appliedName}. Recomendación: reinicia la computadora.";
            var restartNow = ShowApplySuccess(appliedName, plan.Count(p => p.Applicable), reaffirmed, omitted, path);
            UpdateAvailability();
            if (restartNow) RestartComputer();
        }
        catch (Exception ex) { Report(ex); }
        finally { SetBusy(false); UpdateProtectionStatus(); }
    }
    private Task<T> RunLocked<T>(Func<T> operation) => isSimulation ? Task.Run(operation) : Locked(operation);
    private void RestartComputer()
    {
        if (isSimulation) return;
        try
        {
            RuntimeSafety.DemandNative();
            Process.Start(new ProcessStartInfo("shutdown.exe") { UseShellExecute = false, ArgumentList = { "/r", "/t", "0", "/d", "p:2:4", "/c", "OPTF ORCUS TEAM: aplicar perfil" } });
        }
        catch (Exception ex) { Report(ex); }
    }
    private static Task<T> Locked<T>(Func<T> operation) => Task.Run(() => {
        using var mutex = new Mutex(false, @"Global\OPTF.OrcusTeam.Configuration");
        bool held = false;
        try {
            try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
            if (!held) throw new InvalidOperationException("Otra instancia está aplicando cambios. Espera a que termine.");
            return operation();
        } finally { if (held) mutex.ReleaseMutex(); }
    });
    private async void RestoreClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            var path = engine.Restorable().FirstOrDefault();
            if (path == null) { StatusText.Text = "No hay cambios pendientes de restaurar."; return; }
            var b = Engine.ReadBackup(path);
            var detail = string.Join("\n\n", b.Entries.Select(x => $"{Catalog.Get(x.Id).Title}\nRestaurar: {FormatState(Catalog.Get(x.Id), x.Before)}"));
            if (!IsAdmin) throw new InvalidOperationException("Abre como administrador para restaurar los cambios.");
            if (!ShowDocument("Restaurar configuración anterior", detail, "Restaurar esta operación", "Restaurar los valores originales de la operación más reciente.")) return;
            SetBusy(true, "Restaurando y verificando valores originales…");
            var progress = new Progress<string>(s => StatusText.Text = s);
            var context = Context;
            await RunLocked(() => { engine.Restore(path, context, s => ((IProgress<string>)progress).Report(s)); return true; });
            StatusText.Text = "Configuración original restaurada. Reinicia manualmente para recargar los servicios.";
            UpdateAvailability();
            RefreshHistory();
        }
        catch (Exception ex) { Report(ex); RefreshHistory(); }
        finally { SetBusy(false); }
    }
    private void RefreshHistory()
    {
        try
        {
            var files = engine.Files().ToList();
            HistoryText.Text = files.Count == 0 ? "Todavía no hay cambios aplicados.\n\nLos respaldos se crean al aplicar una selección. Analizar y revisar no cambian la configuración." : string.Join("\n\n", files.Select(p => { var b = Engine.ReadBackup(p); return $"{b.CreatedUtc.ToLocalTime():g} · {StatusName(b.Status)} · {b.Entries.Count} valores\n{Path.GetFileName(p)}\n{b.Error}"; }));
            RestoreButton.IsEnabled = !busy && engine.Restorable().Any();
        }
        catch (Exception ex) { HistoryText.Text = "No se pudo leer el historial: " + ex.Message; }
    }
    private static string StatusName(string value) => value switch { "applied" => "Aplicado", "restored" => "Restaurado", "rolled-back" => "Revertido tras un error", "recovery-needed" => "Requiere recuperación", "prepared" => "Preparado", "applying" => "Aplicación interrumpida", "restoring" => "Restauración interrumpida", _ => value };
    private void ShowInventory(object sender, RoutedEventArgs e)
    {
        if (inventory == null) { StatusText.Text = "Primero analiza tu equipo."; return; }
        ShowDocument("Inventario del equipo", inventory.Summary + "\n\nPROGRAMAS DE INICIO (registros y carpetas de inicio; no incluye todas las tareas)\n" + string.Join("\n", inventory.Startup) + "\n\nPROCESOS (memoria de trabajo; incluye memoria compartida)\n" + string.Join("\n", inventory.Processes) + "\n\nSERVICIOS\n" + string.Join("\n", inventory.Services) + "\n\nAPLICACIONES DETECTADAS\n" + string.Join("\n", inventory.Apps));
    }
    private bool ShowDocument(string title, string content, string? action = null, string? acknowledgement = null)
    {
        var window = new Window { Title = "OPTF · " + title, Owner = this, Width = Math.Min(830, ActualWidth - 35), Height = Math.Min(700, ActualHeight - 35), MinWidth = 600, MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SourceInitialized += (_, _) => { if (!isSimulation) WindowChrome.Apply(window); };
        var grid = new Grid { Margin = new Thickness(22) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new());
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) };
        grid.Children.Add(heading);
        var text = new TextBox { Text = content, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new("Consolas"), FontSize = 12 };
        Grid.SetRow(text, 1); grid.Children.Add(text);
        var bottom = new StackPanel { Margin = new(0, 16, 0, 0) }; Grid.SetRow(bottom, 2); grid.Children.Add(bottom);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = new Button { Content = action == null ? "Cerrar" : "Volver", MinWidth = 90, Margin = new(0, 0, 12, 0), IsCancel = true };
        close.Click += (_, _) => window.Close();
        if (action != null)
        {
            var check = new CheckBox { Content = new TextBlock { Text = acknowledgement, TextWrapping = TextWrapping.Wrap }, Margin = new(0, 0, 0, 15) };
            bottom.Children.Add(check);
            var apply = new Button { Content = action, Style = (Style)FindResource("Primary"), IsEnabled = false };
            check.Checked += (_, _) => apply.IsEnabled = true;
            check.Unchecked += (_, _) => apply.IsEnabled = false;
            apply.Click += (_, _) => window.DialogResult = true;
            buttons.Children.Add(close); buttons.Children.Add(apply);
        }
        else buttons.Children.Add(close);
        bottom.Children.Add(buttons); window.Content = grid;
        return window.ShowDialog() == true;
    }
    private bool ShowApplySuccess(string appliedName, int changed, int reaffirmed, int omitted, string backupPath)
    {
        var window = new Window {
            Title = "OPTF · Aplicado exitosamente", Owner = this,
            Width = Math.Min(680, Math.Max(520, ActualWidth - 60)), Height = Math.Min(540, Math.Max(460, ActualHeight - 60)),
            MinWidth = 500, MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize, Background = (Brush)FindResource("Bg"), Foreground = (Brush)FindResource("Ink")
        };
        window.SourceInitialized += (_, _) => { if (!isSimulation) WindowChrome.Apply(window); };
        var root = new Grid { Margin = new Thickness(28) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new());
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });

        var hero = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 22) };
        hero.Children.Add(new Border { Width = 46, Height = 46, CornerRadius = new(23), Background = (Brush)FindResource("Selected"), BorderBrush = (Brush)FindResource("Good"), BorderThickness = new(1), Child = new TextBlock { Text = "✓", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Good"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        var heroText = new StackPanel { Margin = new(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        heroText.Children.Add(new TextBlock { Text = "Aplicado exitosamente", FontSize = 23, FontWeight = FontWeights.SemiBold });
        heroText.Children.Add(new TextBlock { Text = appliedName + " fue escrito y verificado.", Foreground = (Brush)FindResource("Muted"), FontSize = 12, Margin = new(0, 4, 0, 0) });
        hero.Children.Add(heroText); root.Children.Add(hero);

        var metrics = new UniformGrid { Columns = 3, Margin = new(0, 0, 0, 18) }; Grid.SetRow(metrics, 1);
        metrics.Children.Add(SummaryMetric("CAMBIOS NUEVOS", changed.ToString(), "Good"));
        metrics.Children.Add(SummaryMetric("REAFIRMADOS", reaffirmed.ToString(), "Accent"));
        metrics.Children.Add(SummaryMetric("NO DISPONIBLES", omitted.ToString(), "Warning"));
        root.Children.Add(metrics);

        var body = new StackPanel(); Grid.SetRow(body, 2);
        var restartCard = new Border { Style = (Style)FindResource("Card"), Padding = new(18), Margin = new(0, 0, 0, 12) };
        restartCard.Child = new StackPanel { Children = {
            new TextBlock { Text = "Reinicio recomendado", FontSize = 16, FontWeight = FontWeights.SemiBold },
            new TextBlock { Text = "Reinicia para que los servicios y componentes carguen la configuración nueva. Puedes hacerlo ahora o cuando termines tu trabajo.", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap }
        }};
        body.Children.Add(restartCard);
        var details = new Expander { Header = "Ver respaldo técnico  +", Foreground = (Brush)FindResource("Muted") };
        details.Content = new TextBlock { Text = backupPath, FontFamily = new("Consolas"), FontSize = 10.5, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 3, 0, 0) };
        body.Children.Add(details); root.Children.Add(body);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 22, 0, 0) }; Grid.SetRow(actions, 3);
        var later = new Button { Content = "Reiniciar después", MinWidth = 135, Margin = new(0, 0, 10, 0), IsCancel = true };
        later.Click += (_, _) => { StatusText.Text = "Aplicado exitosamente. Reinicia después para completar los cambios."; window.DialogResult = false; };
        var now = new Button { Content = "Reiniciar ahora", MinWidth = 135, Style = (Style)FindResource("Primary") };
        now.Click += (_, _) => window.DialogResult = true;
        actions.Children.Add(later); actions.Children.Add(now); root.Children.Add(actions);
        window.Content = root;
        return window.ShowDialog() == true;
    }
    private Border SummaryMetric(string label, string value, string colorResource)
    {
        var card = new Border { Style = (Style)FindResource("Card"), Padding = new(14), Margin = new(0, 0, 8, 0) };
        card.Child = new StackPanel { Children = {
            new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Muted") },
            new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(colorResource), Margin = new(0, 7, 0, 0) }
        }};
        return card;
    }
    private async void CreatePointClick(object sender, RoutedEventArgs e) => await PrepareRestorePoint(true);
    private async void VerifyPointClick(object sender, RoutedEventArgs e) => await PrepareRestorePoint(false);
    private async Task PrepareRestorePoint(bool createNew)
    {
        if (busy) return;
        SetBusy(true, createNew ? "Creando y verificando el punto de restauración…" : "Comprobando un punto reciente…");
        try
        {
            await protection.PrepareAsync(createNew);
            StatusText.Text = createNew ? "Punto creado y verificado. Ya puedes preparar tus cambios." : "Punto reciente verificado. Ya puedes preparar tus cambios.";
        }
        catch (Exception ex) { Report(ex); }
        finally { SetBusy(false); UpdateProtectionStatus(); }
    }
    private void UpdateProtectionStatus()
    {
        ProtectionStatus.Text = protection.Current is { } point
            ? $"✓  Punto verificado · {point.CreatedUtc.ToLocalTime():g} · {point.Description}"
            : "Sin punto verificado. Es opcional; puedes aplicar cambios de todas formas.";
        ProtectionStatus.SetResourceReference(TextBlock.ForegroundProperty, protection.Current != null ? "Good" : "Muted");
    }
    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        try { RuntimeSafety.DemandNative(); Process.Start(new ProcessStartInfo(b.Tag!.ToString()!) { UseShellExecute = true }); }
        catch (Exception ex) { Report(ex); }
    }
    private void OpenProtection(object sender, RoutedEventArgs e)
    {
        try { RuntimeSafety.DemandNative(); Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SystemPropertiesProtection.exe")) { UseShellExecute = true }); }
        catch (Exception ex) { Report(ex); }
    }
    private void OpenBackups(object sender, RoutedEventArgs e)
    {
        try { RuntimeSafety.DemandNative(); Directory.CreateDirectory(engine.BackupDirectory); Process.Start(new ProcessStartInfo("explorer.exe", engine.BackupDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { Report(ex); }
    }
    private void Elevate(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try { RuntimeSafety.DemandNative(); SavePreferences(); Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" }); Close(); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { StatusText.Text = "Elevación cancelada. Puedes seguir analizando."; }
        catch (Exception ex) { Report(ex); }
    }
    private void SetBusy(bool value, string? message = null)
    {
        busy = value;
        BusyBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        PageContent.IsEnabled = !value;
        ScanButton.IsEnabled = !value;
        AdminButton.IsEnabled = !value;
        if (message != null) StatusText.Text = message;
        UpdateCount();
    }
    private void Report(Exception ex) { if (isSimulation) throw new InvalidOperationException("Simulation failed", ex); StatusText.Text = ex.Message.Split('\n')[0]; ShowDocument("Operación no completada", ex.Message); }
    private void WindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SidebarColumn != null) SidebarColumn.Width = new GridLength(ActualWidth < 820 ? 164 : ActualWidth < 1030 ? 190 : 208);
    }

    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox == null) return;
        var palette = ThemeBox.SelectedIndex switch {
            1 => new[] { "#131112", "#1D191B", "#292326", "#3C3034", "#CEC3C5", "#AD969B", "#D18186", "#231315", "#87B3A2", "#CAA078", "#332328", "#3B2A30" },
            2 => new[] { "#10161B", "#171F26", "#202C35", "#30414B", "#BBC9CD", "#8FA8B1", "#86B5BF", "#122126", "#87B5A1", "#C5AC89", "#233942", "#2C424C" },
            _ => new[] { "#101115", "#17181E", "#202129", "#30313C", "#C8C9D2", "#9698AB", "#AE99D8", "#19141F", "#84B8A2", "#C9A27B", "#272234", "#30303D" }
        };
        string[] keys = ["Bg", "Panel", "Raised", "Line", "Ink", "Muted", "Accent", "AccentInk", "Good", "Warning", "Selected", "Hover"];
        for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
        if (ready && !isSimulation) WindowChrome.Apply(this);
    }
    private void SavePreferences()
    {
        if (isSimulation) return;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        File.WriteAllText(settingsFile, JsonSerializer.Serialize(new Preferences(ThemeBox.SelectedIndex, ProfileBox.SelectedIndex, UsageBox.SelectedIndex, rows.Where(r => r.Selected && !r.Tweak.Developer).Select(r => r.Tweak.Id).ToArray())));
    }
    private void LoadPreferences()
    {
        if (isSimulation || !File.Exists(settingsFile)) return;
        try {
            var p = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settingsFile));
            if (p == null) return;
            ThemeBox.SelectedIndex = Math.Clamp(p.Theme, 0, 2); ProfileBox.SelectedIndex = Math.Clamp(p.Profile, 0, 3); UsageBox.SelectedIndex = Math.Clamp(p.Usage, 0, 3);
            foreach (var row in rows) row.Selected = !row.Tweak.Developer && p.Selected.Contains(row.Tweak.Id);
        } catch { StatusText.Text = "Preferencias no válidas; se usan valores iniciales."; }
    }
    private sealed record Preferences(int Theme, int Profile, int Usage, string[] Selected);

}
