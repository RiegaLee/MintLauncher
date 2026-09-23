using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CmlLib.Core;
using CmlLib.Core.Installers;

namespace MintLauncher.Operational;

/// <summary>
/// Adds a side-by-side Fabric launch profile for an imported instance. The imported
/// version JSON, mods, saves and HMCL settings are never rewritten by this class.
/// </summary>
public static class FabricRecovery
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly Regex FabricLogPattern = new(
        @"Loading Minecraft (?<game>[^\s]+) with Fabric Loader (?<loader>[^\s]+)", RegexOptions.Compiled);

    public sealed record Detection(string GameVersion, string? LoaderVersion);

    public static Detection? Detect(string versionId, string runDirectory)
    {
        var mods = Path.Combine(runDirectory, "mods");
        if (!Directory.Exists(mods)) return null;

        var fabricModFound = false;
        foreach (var modPath in Directory.EnumerateFiles(mods, "*.jar"))
        {
            try
            {
                using var archive = ZipFile.OpenRead(modPath);
                if (archive.GetEntry("fabric.mod.json") is not null)
                {
                    fabricModFound = true;
                    break;
                }
            }
            catch (InvalidDataException) { }
            catch (IOException) { }
        }
        if (!fabricModFound) return null;

        var logPath = Path.Combine(runDirectory, "logs", "latest.log");
        if (File.Exists(logPath))
        {
            try
            {
                using var reader = new StreamReader(logPath);
                for (var lineNumber = 0; lineNumber < 150 && !reader.EndOfStream; lineNumber++)
                {
                    var match = FabricLogPattern.Match(reader.ReadLine() ?? string.Empty);
                    if (match.Success)
                        return new Detection(match.Groups["game"].Value, match.Groups["loader"].Value);
                }
            }
            catch (IOException) { }
        }
        return new Detection(versionId, null);
    }

    public static async Task<string> CreateAndInstallAsync(
        string gameDirectory, string sourceVersionId, Detection detection,
        MinecraftLauncher launcher, IProgress<LaunchPreparationProgress> progress, CancellationToken cancellationToken)
    {
        var loaderVersion = detection.LoaderVersion ?? await GetLatestStableLoaderAsync(detection.GameVersion, cancellationToken);
        var profileId = $"mint-fabric-{loaderVersion}-{sourceVersionId}";
        if (!Regex.IsMatch(profileId, @"^[A-Za-z0-9._-]+$") || profileId.Length > 180)
            throw new InvalidOperationException("导入版本的名称无法用于自动补全 Fabric，请为该版本使用简短的英文名称。");

        var sourceJson = Path.Combine(gameDirectory, "versions", sourceVersionId, sourceVersionId + ".json");
        var profileDirectory = Path.Combine(gameDirectory, "versions", profileId);
        var profilePath = Path.Combine(profileDirectory, profileId + ".json");
        if (!File.Exists(sourceJson)) throw new FileNotFoundException("原版本配置不存在。", sourceJson);

        if (File.Exists(profilePath))
        {
            ValidateOwnedProfile(profilePath, sourceVersionId, loaderVersion);
        }
        else
        {
            progress.Report(new("正在补全 Fabric 启动组件…", 0.2));
            var officialProfile = await GetProfileAsync(detection.GameVersion, loaderVersion, cancellationToken);
            var newProfile = BuildProfile(officialProfile, profileId, sourceVersionId, loaderVersion);

            // A backup is kept even though the imported version itself is not modified.
            var backup = sourceJson + ".mint-backup";
            if (!File.Exists(backup)) File.Copy(sourceJson, backup, overwrite: false);
            Directory.CreateDirectory(profileDirectory);
            var temporary = profilePath + ".tmp";
            await File.WriteAllTextAsync(temporary, newProfile, cancellationToken);
            try { File.Move(temporary, profilePath, overwrite: false); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        await EnsureDependenciesAsync(launcher, profileId, progress, cancellationToken);
        return profileId;
    }

    public static async Task EnsureDependenciesAsync(
        MinecraftLauncher launcher, string profileId,
        IProgress<LaunchPreparationProgress> progress, CancellationToken cancellationToken)
    {
        progress.Report(new("正在检查并补全游戏依赖…", 0.35));
        var reportedBucket = -1;
        var progressGate = new object();
        launcher.FileProgressChanged += (_, change) =>
        {
            if (change.EventType != InstallerEventType.Done || change.TotalTasks <= 0) return;
            var bucket = Math.Clamp(change.ProgressedTasks * 20 / change.TotalTasks, 0, 20);
            lock (progressGate)
            {
                if (bucket <= reportedBucket) return;
                reportedBucket = bucket;
            }
            progress.Report(new("正在补全游戏环境…", 0.35 + bucket * 0.02));
        };
        await launcher.InstallAsync(profileId, cancellationToken);
    }

    public static string BuildProfile(string officialProfile, string profileId, string sourceVersionId, string loaderVersion)
    {
        var profile = JsonNode.Parse(officialProfile) as JsonObject
            ?? throw new InvalidDataException("Fabric 官方启动配置格式无效。");
        var mainClass = profile["mainClass"]?.GetValue<string>();
        var libraries = profile["libraries"] as JsonArray;
        if (mainClass is null || !mainClass.StartsWith("net.fabricmc.", StringComparison.Ordinal) ||
            libraries is null || !libraries.Any(item => item?["name"]?.GetValue<string>()?.StartsWith("net.fabricmc:fabric-loader:", StringComparison.Ordinal) == true))
            throw new InvalidDataException("Fabric 官方启动配置缺少加载器入口或依赖，未写入本机。");

        profile["id"] = profileId;
        profile["inheritsFrom"] = sourceVersionId;
        profile["mintSourceVersion"] = sourceVersionId;
        profile["mintFabricLoaderVersion"] = loaderVersion;
        return profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void ValidateOwnedProfile(string path, string sourceVersionId, string loaderVersion)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!root.TryGetProperty("mintSourceVersion", out var source) || source.GetString() != sourceVersionId ||
            !root.TryGetProperty("mintFabricLoaderVersion", out var loader) || loader.GetString() != loaderVersion ||
            !root.TryGetProperty("mainClass", out var mainClass) ||
            mainClass.GetString()?.StartsWith("net.fabricmc.", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("目标版本名称已被其他配置占用；没有覆盖任何文件。");
    }

    private static async Task<string> GetLatestStableLoaderAsync(string gameVersion, CancellationToken cancellationToken)
    {
        var uri = $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}";
        using var response = await Http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var loader = item.GetProperty("loader");
            if (loader.GetProperty("stable").GetBoolean())
                return loader.GetProperty("version").GetString()!;
        }
        throw new InvalidOperationException($"没有找到适用于 Minecraft {gameVersion} 的稳定 Fabric Loader。");
    }

    private static async Task<string> GetProfileAsync(string gameVersion, string loaderVersion, CancellationToken cancellationToken)
    {
        var uri = $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";
        using var response = await Http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
