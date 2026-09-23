using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.VersionMetadata;
using CmlLib.Core.VersionLoader;

namespace MintLauncher.Operational;

public sealed record InstalledGame(string Id, string Description)
{
    public override string ToString() => Description;
}

public sealed record AvailableGame(string Id, string Description)
{
    public override string ToString() => Description;
}

public sealed class MissingModLoaderException : InvalidOperationException
{
    public string VersionDirectory { get; }

    public MissingModLoaderException(string versionId, string versionDirectory)
        : base($"{versionId} 的 MOD 文件还在，但版本入口已是原版，且无法从现有 MOD 识别加载器类型。请确认导入的是原来的游戏目录；薄荷启动器没有修改此实例的文件。")
    {
        VersionDirectory = versionDirectory;
    }
}

public sealed class FabricRepairException : InvalidOperationException
{
    public string VersionDirectory { get; }

    public FabricRepairException(string versionDirectory, Exception cause)
        : base($"自动补全 Fabric 未完成：{cause.Message}。原实例没有被覆盖；检查网络后直接再次点击“启动游戏”即可重试。", cause)
    {
        VersionDirectory = versionDirectory;
    }
}

public sealed class WorkspacePreferences
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MintLauncher", "workspace.json");

    public string PlayerName { get; set; } = "Player";
    public string? SelectedMicrosoftUuid { get; set; }
    public string? SelectedGameId { get; set; }
    public string GameDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MintLauncher", ".minecraft");

    public static WorkspacePreferences Load() => LoadFrom(SettingsPath);

    internal static WorkspacePreferences LoadFrom(string settingsPath)
    {
        try
        {
            if (File.Exists(settingsPath))
                return JsonSerializer.Deserialize<WorkspacePreferences>(File.ReadAllText(settingsPath)) ?? new();
        }
        catch (Exception)
        {
            // Corrupt preferences must not prevent the launcher from opening.
        }
        return new();
    }

    public void Save() => SaveTo(SettingsPath);

    internal void SaveTo(string settingsPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporary = settingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, settingsPath, true);
    }
}

/// <summary>
/// Real Minecraft operations. Imported launchers' version metadata is read-only here;
/// installing is a separate explicit operation, never a side effect of launching.
/// </summary>
public sealed class LauncherWorkspace
{
    private static readonly Regex PlayerNamePattern = new("^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);

    public static string ValidatePlayerName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length < 3 || trimmed.Length > 16)
            throw new InvalidOperationException("玩家 ID 需为 3–16 个字符。");
        if (name != trimmed || !PlayerNamePattern.IsMatch(trimmed))
            throw new InvalidOperationException("玩家 ID 只能使用英文字母、数字和下划线（_），不能含空格、中文或其他符号。");
        return trimmed;
    }

    public static string ValidateImportedDirectory(string selectedPath)
    {
        var root = Path.GetFullPath(selectedPath);
        if (Path.GetFileName(root).Equals("versions", StringComparison.OrdinalIgnoreCase))
            root = Directory.GetParent(root)?.FullName ?? root;
        if (!Directory.Exists(Path.Combine(root, "versions")))
        {
            var nested = Path.Combine(root, ".minecraft");
            if (Directory.Exists(Path.Combine(nested, "versions"))) root = nested;
        }

        if (ScanInstalled(root).Count == 0)
            throw new InvalidOperationException("没有找到 versions/<版本>/<版本>.json。请选择 PCL 或 HMCL 使用的 .minecraft 游戏目录。");
        return root;
    }

    public static IReadOnlyList<InstalledGame> ScanInstalled(string gameDirectory)
    {
        var versions = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versions)) return [];
        var games = new List<InstalledGame>();
        foreach (var directory in Directory.EnumerateDirectories(versions))
        {
            var id = Path.GetFileName(directory);
            var json = Path.Combine(directory, id + ".json");
            if (!File.Exists(json)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(json));
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
                var parent = root.TryGetProperty("inheritsFrom", out var parentValue) ? parentValue.GetString() : null;
                if (root.TryGetProperty("mintSourceVersion", out var sourceValue) &&
                    sourceValue.ValueKind == JsonValueKind.String &&
                    root.TryGetProperty("mintFabricLoaderVersion", out var loaderValue) &&
                    loaderValue.ValueKind == JsonValueKind.String)
                {
                    games.Add(new InstalledGame(id, $"{sourceValue.GetString()} · Fabric {loaderValue.GetString()}"));
                    continue;
                }
                var suffix = !string.IsNullOrWhiteSpace(parent) ? $" · 基于 {parent}" :
                    !string.IsNullOrWhiteSpace(type) ? $" · {type}" : string.Empty;
                games.Add(new InstalledGame(id, id + suffix));
            }
            catch (Exception)
            {
                // A malformed version must never be offered as a launchable game.
            }
        }
        return games.OrderByDescending(game => game.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string ResolveRunDirectory(string gameDirectory, string versionId) =>
        ResolveRunDirectoryCore(gameDirectory, versionId, 0);

    private static string ResolveRunDirectoryCore(string gameDirectory, string versionId, int depth)
    {
        if (depth > 4) throw new InvalidOperationException("版本实例引用层级过深。");
        var root = Path.GetFullPath(gameDirectory);
        var instanceRoot = Path.GetFullPath(Path.Combine(root, "versions", versionId));
        if (!instanceRoot.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("游戏版本路径无效。");

        var metadataPath = Path.Combine(instanceRoot, versionId + ".json");
        if (File.Exists(metadataPath))
        {
            using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
            if (metadata.RootElement.TryGetProperty("mintSourceVersion", out var sourceValue) &&
                sourceValue.ValueKind == JsonValueKind.String)
            {
                var sourceId = sourceValue.GetString();
                if (string.IsNullOrWhiteSpace(sourceId) || sourceId == versionId ||
                    Path.GetFileName(sourceId) != sourceId)
                    throw new InvalidOperationException("薄荷生成的 Fabric 配置没有有效的原实例引用。");
                return ResolveRunDirectoryCore(root, sourceId, depth + 1);
            }
        }
        if (File.Exists(Path.Combine(instanceRoot, "modpack.cfg"))) return instanceRoot;

        var settingsPath = Path.Combine(instanceRoot, ".hmcl", "config", "instance-game-settings.json");
        if (!File.Exists(settingsPath)) return root;
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var settings = document.RootElement;
        if (!settings.TryGetProperty("overrideProperties", out var overrides) ||
            overrides.ValueKind != JsonValueKind.Array ||
            !overrides.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == "runningDirectory"))
            return root;

        var custom = settings.TryGetProperty("runningDirectory", out var runningDirectory) &&
                     runningDirectory.ValueKind == JsonValueKind.String ? runningDirectory.GetString() : null;
        if (string.IsNullOrWhiteSpace(custom)) return instanceRoot;
        if (!Path.IsPathFullyQualified(custom))
            throw new InvalidOperationException("HMCL 使用了相对路径的独立游戏目录；请先在 HMCL 中确认其绝对路径。");
        var resolved = Path.GetFullPath(custom);
        if (!Directory.Exists(resolved))
            throw new InvalidOperationException("HMCL 设置的游戏运行目录不存在，请先在 HMCL 中检查路径。");
        return resolved;
    }

    private static bool NeedsModLoader(string gameDirectory, string versionId, string runDirectory)
    {
        var mods = Path.Combine(runDirectory, "mods");
        if (!Directory.Exists(mods) || !Directory.EnumerateFiles(mods, "*.jar").Any()) return false;
        var versionJson = Path.Combine(gameDirectory, "versions", versionId, versionId + ".json");
        var metadata = File.ReadAllText(versionJson);
        if (metadata.Contains("net.fabricmc", StringComparison.OrdinalIgnoreCase) ||
            metadata.Contains("neoforge", StringComparison.OrdinalIgnoreCase) ||
            metadata.Contains("minecraftforge", StringComparison.OrdinalIgnoreCase) ||
            metadata.Contains("org.quiltmc", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string? GetMintFabricSource(string gameDirectory, string versionId)
    {
        var versionJson = Path.Combine(gameDirectory, "versions", versionId, versionId + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(versionJson));
        var root = document.RootElement;
        return root.TryGetProperty("mintFabricLoaderVersion", out _) &&
               root.TryGetProperty("mintSourceVersion", out var source) &&
               source.ValueKind == JsonValueKind.String ? source.GetString() : null;
    }

    private static bool HasLocalParentChain(string gameDirectory, string versionId, HashSet<string> visited)
    {
        if (!visited.Add(versionId)) return false;
        var path = Path.Combine(gameDirectory, "versions", versionId, versionId + ".json");
        if (!File.Exists(path)) return false;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!root.TryGetProperty("inheritsFrom", out var parent) || parent.ValueKind != JsonValueKind.String)
            return true;
        var parentId = parent.GetString();
        return string.IsNullOrWhiteSpace(parentId) ||
               HasLocalParentChain(gameDirectory, parentId, visited);
    }

    internal static MinecraftLauncher CreateLocalFirstLauncher(string gameDirectory, string versionId)
    {
        var path = new MinecraftPath(gameDirectory);
        var parameters = MinecraftLauncherParameters.CreateDefault(path);
        var local = new LocalJsonVersionLoader(path);
        if (HasLocalParentChain(gameDirectory, versionId, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
        {
            parameters.VersionLoader = local;
            return new MinecraftLauncher(parameters);
        }
        var loaders = new VersionLoaderCollection
        {
            local,
            parameters.VersionLoader!
        };
        parameters.VersionLoader = loaders;
        return new MinecraftLauncher(parameters);
    }

    public async Task<IReadOnlyList<AvailableGame>> GetReleasesAsync(string gameDirectory, CancellationToken cancellationToken)
    {
        var launcher = new MinecraftLauncher(new MinecraftPath(gameDirectory));
        var versions = await launcher.GetAllVersionsAsync(cancellationToken);
        return versions
            .Where(version => version.GetVersionType().ToString().Equals("Release", StringComparison.OrdinalIgnoreCase))
            .Select(version => new AvailableGame(version.Name, version.Name))
            .DistinctBy(version => version.Id)
            .ToArray();
    }

    public async Task InstallAsync(string gameDirectory, string versionId, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (ScanInstalled(gameDirectory).Any(game => game.Id == versionId))
            throw new InvalidOperationException($"{versionId} 已在游戏库中。为保护导入的版本配置，本次不会覆盖安装；请从游戏库选择它。");
        Directory.CreateDirectory(gameDirectory);
        var launcher = new MinecraftLauncher(new MinecraftPath(gameDirectory));
        launcher.FileProgressChanged += (_, change) =>
        {
            if (!string.IsNullOrWhiteSpace(change.Name)) progress.Report($"正在准备：{change.Name}");
        };
        progress.Report($"正在检查并下载 Minecraft {versionId}；首次安装可能需要几分钟…");
        await launcher.InstallAsync(versionId, cancellationToken);
        progress.Report($"Minecraft {versionId} 安装与文件检查完成。");
    }

    public async Task<LaunchResult> LaunchAsync(string gameDirectory, string versionId, string playerName, IProgress<LaunchPreparationProgress> progress, CancellationToken cancellationToken)
        => await LaunchAsync(gameDirectory, versionId,
            MSession.CreateOfflineSession(ValidatePlayerName(playerName)), progress, cancellationToken);

    public async Task<LaunchResult> LaunchAsync(string gameDirectory, string versionId, MSession session, IProgress<LaunchPreparationProgress> progress, CancellationToken cancellationToken)
    {
        if (!ScanInstalled(gameDirectory).Any(game => game.Id == versionId))
            throw new InvalidOperationException("所选版本尚未安装，请先点击“安装所选版本”。");
        if (string.IsNullOrWhiteSpace(session.Username) || string.IsNullOrWhiteSpace(session.UUID))
            throw new InvalidOperationException("游戏账户没有有效的玩家档案，请重新登录或选择离线档案。");
        var runDirectory = ResolveRunDirectory(gameDirectory, versionId);
        var launcher = CreateLocalFirstLauncher(gameDirectory, versionId);
        var repaired = false;
        if (NeedsModLoader(gameDirectory, versionId, runDirectory))
        {
            var detection = FabricRecovery.Detect(versionId, runDirectory);
            if (detection is null)
                throw new MissingModLoaderException(versionId, Path.Combine(gameDirectory, "versions", versionId));
            try
            {
                versionId = await FabricRecovery.CreateAndInstallAsync(gameDirectory, versionId, detection, launcher, progress, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                throw new FabricRepairException(Path.Combine(gameDirectory, "versions", versionId), exception);
            }
            runDirectory = ResolveRunDirectory(gameDirectory, versionId);
            repaired = true;
        }
        else if (GetMintFabricSource(gameDirectory, versionId) is { } sourceId)
        {
            try
            {
                await FabricRecovery.EnsureDependenciesAsync(launcher, versionId, progress, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                throw new FabricRepairException(Path.Combine(gameDirectory, "versions", sourceId), exception);
            }
            repaired = true;
        }
        progress.Report(new("正在准备游戏…", repaired ? 0.8 : 0.15));
        var process = await launcher.BuildProcessAsync(versionId, new MLaunchOption
        {
            Session = session,
            MaximumRamMb = 4096,
            GameLauncherName = "MintLauncher",
            GameLauncherVersion = "0.1",
            ArgumentDictionary = new Dictionary<string, string> { ["game_directory"] = runDirectory }
        }, cancellationToken);
        process.StartInfo.WorkingDirectory = runDirectory;
        progress.Report(new("正在启动游戏…", 0.88));
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Java 进程未能启动。");
        }
        catch
        {
            process.Dispose();
            throw;
        }
        progress.Report(new("游戏已启动", 1));
        return new LaunchResult(process, versionId);
    }
}

public sealed record LaunchPreparationProgress(string Message, double Fraction);
public sealed record LaunchResult(Process Process, string VersionId);
