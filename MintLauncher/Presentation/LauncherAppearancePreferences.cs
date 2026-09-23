using System.IO;
using System.Text.Json;

namespace MintLauncher.Presentation;

public sealed class LauncherAppearancePreferences
{
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MintLauncher", "appearance.json");

    public string ThemePreference { get; set; } = "System";
    public string? SkinPath { get; set; }
    public string SkinModel { get; set; } = "Steve";
    public string? BackgroundPath { get; set; }
    public double BackgroundOverlayOpacity { get; set; } = 0.72;
    public bool ReducedMotion { get; set; }

    public static LauncherAppearancePreferences Load()
    {
        try
        {
            if (File.Exists(PreferencesPath))
                return JsonSerializer.Deserialize<LauncherAppearancePreferences>(File.ReadAllText(PreferencesPath)) ?? new();
        }
        catch (Exception)
        {
            // A missing, moved or corrupt preference file must never prevent startup.
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        var temporaryPath = PreferencesPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, PreferencesPath, true);
    }
}
