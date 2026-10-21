using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orcus.Core;

namespace Orcus.App;

public partial class MainWindow
{
    private async Task SmokeTest()
    {
        Directory.CreateDirectory(simulationOutput);
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add("PASS " + name); }
        async Task Flush() { await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout(); }
        async Task Capture(string name, FrameworkElement? visual = null)
        {
            await Flush();
            var surface = visual ?? (FrameworkElement)Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(simulationOutput, name)); encoder.Save(stream);
        }
        try
        {
            Check(RuntimeSafety.Simulation && access is SimulatedSystem, "Native Windows backend is excluded");
            bool refused = false;
            try { new WindowsAccess().Read(Catalog.Get("transparency")); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "Native registry read is blocked before any OS access");
            refused = false;
            try { await Inventory.Scan(); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "Native inventory is blocked before launching PowerShell");
            refused = false;
            try { await new WindowsRestorePoints().CreateAsync(); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "Native restore point creation is blocked");
            Check(protection.Current == null, "No invented restore point on launch");
            Check(CreatePointButton.IsDescendantOf(HomePanel), "Restore point creation belongs to Home");
            Check(ApplyButton.Content?.ToString()?.Contains("Aplicar") == true, "Every configurable page has a direct apply action");
            Check(!CreatePointButton.IsDescendantOf(RecoveryPanel), "Undo history does not create restore points");
            var resources = typeof(MainWindow).Assembly.GetManifestResourceNames();
            Check(resources.Any(n => n.EndsWith("Services.Inventory.ps1")) && resources.Any(n => n.EndsWith("Services.RestorePoint.ps1")), "Operational scripts are embedded");
            var fonts = Fonts.GetFontFamilies(new Uri("pack://application:,,,/Assets/Fonts/"));
            Check(fonts.Any(), "Bundled gothic font resolves without installation");
            await Scan();
            Check(inventory!.Cpu == "Procesador de ejemplo", "Dashboard uses fictional inventory");
            Check(SampleLabel.Text.Contains("SIMULADOS"), "Startup analysis populates the dashboard");
            await Capture("01-inicio-default.png");
            foreach (var theme in new[] { 0, 1, 2 })
            {
                ThemeBox.SelectedIndex = theme;
                foreach (var key in new[] { "Bg", "Panel", "Raised", "Selected", "Hover" })
                {
                    var c = ((SolidColorBrush)FindResource(key)).Color;
                    Check(Math.Max(c.R, Math.Max(c.G, c.B)) < 100, $"Theme {theme}: {key} is dark");
                }
            }
            ThemeBox.SelectedIndex = 0; ShowPage("profiles"); ProfileBox.SelectedIndex = 1;
            await Capture("02-perfiles-default.png");
            Check(rows.All(r => !r.Tweak.Developer || !r.Selected), "Presets keep developer tweaks off");
            SearchBox.Text = "telemetría";
            Check(OptionList.Items.Count == 1, "Search filters the catalogue");
            await Flush();
            var presenter = (ContentPresenter)OptionList.ItemContainerGenerator.ContainerFromIndex(0);
            var card = (Border)VisualTreeHelper.GetChild(presenter, 0);
            var row = (OptionRow)OptionList.Items[0];
            var box = FindChild<CheckBox>(card)!;
            box.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            await Flush();
            Check(row.Selected && ((SolidColorBrush)card.Background).Color == ((SolidColorBrush)FindResource("Selected")).Color, "Checking an option changes its card color");
            box.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            await Flush();
            Check(!row.Selected && ((SolidColorBrush)card.Background).Color == ((SolidColorBrush)FindResource("Panel")).Color, "Unchecking restores the neutral card");
            var details = FindChild<Expander>(card)!;
            details.IsExpanded = true; await Flush();
            Check(details.IsExpanded, "Technical descriptions remain accessible");
            SearchBox.Text = "no-match-xyz"; Check(EmptyOptions.Visibility == Visibility.Visible, "Search empty state");
            SearchBox.Text = "";
            ProfileBox.SelectedIndex = 2; UsageBox.SelectedIndex = 2;
            Check(!rows.Single(r => r.Tweak.Id == "svc-WSearch").Selected, "Programming keeps Search in the default selection");
            ThemeBox.SelectedIndex = 1; await Capture("03-perfiles-obsidiana.png");
            _ = Dispatcher.BeginInvoke(async () => {
                var dialog = Application.Current.Windows.OfType<Window>().First(w => w != this);
                await Capture("03b-confirmacion-aplicada.png", (FrameworkElement)dialog.Content);
                dialog.DialogResult = false;
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(!ShowApplySuccess("Perfil Ultra", 8, 14, 0, @"C:\Datos simulados\respaldo.json"), "Success dialog defaults to restart later");
            ProfileBox.IsDropDownOpen = true; await Flush();
            var popup = (Popup)ProfileBox.Template.FindName("PART_Popup", ProfileBox);
            Check(popup.IsOpen && popup.Child is Border, "Custom selector opens correctly");
            await Capture("04-selector-oscuro.png", (FrameworkElement)popup.Child);
            ProfileBox.IsDropDownOpen = false;
            ThemeBox.SelectedIndex = 2; ShowPage("developer");
            Check(rows.Where(r => r.Tweak.Developer).All(r => !r.Enabled), "Developer mode starts locked");
            DeveloperUnlock.IsChecked = true;
            rows.Single(r => r.Tweak.Id == "update-manual").Selected = true;
            rows.Single(r => r.Tweak.Id == "update-service").Selected = true;
            Check(rows.Count(r => r.Selected && r.Tweak.Developer) == 1, "Update options are mutually exclusive in the UI");
            await Capture("05-desarrollador-perla.png");
            ThemeBox.SelectedIndex = 0; ShowPage("home");
            await PrepareRestorePoint(true);
            Check(protection.Current != null && ProtectionStatus.Text.Contains("SIMULADO"), "Simulated restore receipt is clearly labeled");
            await protection.EnsureReadyAsync();
            await Capture("06-restauracion-simulada.png");
            ShowPage("recovery"); await Capture("07-recuperacion.png");
            Width = 800; Height = 620; ShowPage("profiles"); await Capture("08-perfiles-compacto.png");
            ShowPage("home"); await Capture("09-inicio-compacto.png");
            Check(((SimulatedSystem)access).WriteCount == 0, "UI tests do not apply any optimization, even to the fake backend");
            checks.Add("All machine data is fictional. No Windows inventory, elevation, services, registry, restore points or personal preferences were accessed.");
        }
        catch (Exception ex) { checks.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally
        {
            File.WriteAllLines(Path.Combine(simulationOutput, "ui-results.txt"), checks);
            busy = false; Close();
        }
    }
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
