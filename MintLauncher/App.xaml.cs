using Microsoft.Win32;
using MintLauncher.Composition;
using MintLauncher.Presentation;
using System.Windows;
using System.Windows.Media;

namespace MintLauncher;

public partial class App : Application
{
    public bool IsDarkMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var appearance = LauncherAppearancePreferences.Load();
        ApplyThemePreference(appearance.ThemePreference);
        base.OnStartup(e);
        var services = AppCompositionRoot.CreatePrototype();
        var window = new MainWindow(services.MainWindow, appearance);
        MainWindow = window;
        window.Show();
    }

    public void ApplyWindowsTheme() => ApplyThemePreference("System");

    public void ApplyThemePreference(string preference)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var isDark = preference switch
        {
            "Dark" => true,
            "Light" => false,
            _ => Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0
        };
        IsDarkMode = isDark;

        var palette = isDark
            ? new Dictionary<string, string>
            {
                ["TextPrimary"] = "#EDF5FC", ["TextSecondary"] = "#96ACC0",
                ["BrandBlue"] = "#3A9CF3", ["BrandMint"] = "#69D3C5",
                ["WindowSurface"] = "#111D2B", ["WindowBorder"] = "#42536A80",
                ["TitleBarSurface"] = "#78142335", ["NavSelectedSurface"] = "#F01C3147",
                ["NavHoverSurface"] = "#49243B52", ["SoftSurface"] = "#E81A2A3D",
                ["SoftHoverSurface"] = "#FF21374E", ["InstancePillSurface"] = "#CE182A3D",
                ["ModalSurface"] = "#FF152536", ["SelectionSurface"] = "#FF1A354B",
                ["SecondarySurface"] = "#FF1A2C3E", ["SectionSurface"] = "#F0162638",
                ["TileBlueSurface"] = "#FF172D42", ["TileMintSurface"] = "#FF183630",
                ["TilePinkSurface"] = "#FF352635", ["IconBlueSurface"] = "#FF1B4562",
                ["IconMintSurface"] = "#FF1E4B42", ["IconPinkSurface"] = "#FF503247",
                ["AvatarSurface"] = "#FF513849", ["AvatarText"] = "#FFF2DFE8",
                ["IconMuted"] = "#91A9BD", ["StatusText"] = "#8EA6BA",
                ["DimmerSurface"] = "#94040A12", ["LaunchTrack"] = "#3B3A9CF3",
                ["HomePreviewSurface"] = "#FF19283A"
            }
            : new Dictionary<string, string>
            {
                ["TextPrimary"] = "#17283B", ["TextSecondary"] = "#60788E",
                ["BrandBlue"] = "#1686DA", ["BrandMint"] = "#69D3C5",
                ["WindowSurface"] = "#F7FBFE", ["WindowBorder"] = "#35FFFFFF",
                ["TitleBarSurface"] = "#48FFFFFF", ["NavSelectedSurface"] = "#EFFFFFFF",
                ["NavHoverSurface"] = "#120D609C", ["SoftSurface"] = "#EFFFFFFF",
                ["SoftHoverSurface"] = "#FFFFFFFF", ["InstancePillSurface"] = "#A9FFFFFF",
                ["ModalSurface"] = "#FBFFFFFF", ["SelectionSurface"] = "#ECF7FF",
                ["SecondarySurface"] = "#F7FAFC", ["SectionSurface"] = "#D9FFFFFF",
                ["TileBlueSurface"] = "#EFF8FF", ["TileMintSurface"] = "#F0FAF7",
                ["TilePinkSurface"] = "#FFF3F7", ["IconBlueSurface"] = "#D8EEFF",
                ["IconMintSurface"] = "#DDF5EE", ["IconPinkSurface"] = "#FFE0E9",
                ["AvatarSurface"] = "#FFE3EA", ["AvatarText"] = "#486073",
                ["IconMuted"] = "#7890A5", ["StatusText"] = "#678097",
                ["DimmerSurface"] = "#3814273A", ["LaunchTrack"] = "#251686DA",
                ["HomePreviewSurface"] = "#FFF8FCFF"
            };

        foreach (var (name, value) in palette)
            Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));

        Resources["BackdropOverlay"] = isDark
            ? new LinearGradientBrush(
                [new GradientStop((Color)ColorConverter.ConvertFromString("#F20A1420"), 0),
                 new GradientStop((Color)ColorConverter.ConvertFromString("#E6102235"), 0.48),
                 new GradientStop((Color)ColorConverter.ConvertFromString("#DC103331"), 1)],
                new Point(0, 0), new Point(1, 1))
            : new LinearGradientBrush(
                [new GradientStop((Color)ColorConverter.ConvertFromString("#F4F9FE"), 0),
                 new GradientStop((Color)ColorConverter.ConvertFromString("#DDEFFB"), 0.48),
                 new GradientStop((Color)ColorConverter.ConvertFromString("#DAF5F0"), 1)],
                new Point(0, 0), new Point(1, 1));
        Resources["BackdropImageOpacity"] = isDark ? 0.10d : 0.20d;
    }
}
