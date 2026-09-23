using MintLauncher.Core.Abstractions;
using MintLauncher.Core.Models;
using System.IO;
using System.Windows.Media.Imaging;

namespace MintLauncher.Infrastructure.Prototype;

public sealed class PrototypeAccountProvider : IAccountProvider
{
    private readonly List<AccountProfile> _accounts =
    [
        new(Guid.Parse("30a3b6b4-3b44-44f6-aee0-d99ce02fe71c"), "_RiegaLee_", AccountKind.Microsoft, true)
    ];

    public Task<IReadOnlyList<AccountProfile>> GetAccountsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AccountProfile>>(_accounts);

    public Task<AccountProfile> CreateOfflineAsync(string displayName, CancellationToken cancellationToken = default)
    {
        var account = new AccountProfile(Guid.NewGuid(), displayName.Trim(), AccountKind.Offline, true);
        _accounts.Add(account);
        return Task.FromResult(account);
    }
}

public sealed class PrototypeGameLibraryProvider : IGameLibraryProvider
{
    private readonly List<GameProfile> _games =
    [
        new(Guid.Parse("c49b85dc-87f6-414d-9a62-19e7b7c687be"), "薄荷生存", "1.21.1", "NeoForge", @"Games\MintSurvival", true),
        new(Guid.Parse("1874d0e2-483b-49fd-95c2-40f957603c2f"), "原版世界", "1.21.11", null, @"Games\Vanilla", true),
        new(Guid.Parse("8049be50-10fd-4922-968b-97a3f0638cb1"), "暮色远征", "1.20.1", "Fabric", @"Games\Twilight", false)
    ];

    public Task<IReadOnlyList<GameProfile>> GetGamesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<GameProfile>>(_games);

    public Task<GameProfile?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_games.FirstOrDefault(game => game.Id == id));
}

public sealed class PrototypeRuntimeProvider : IRuntimeProvider
{
    private static readonly JavaRuntime Runtime = new(@"runtime\java-runtime-delta\bin\javaw.exe", 21, "x64", true);

    public Task<IReadOnlyList<JavaRuntime>> DetectAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<JavaRuntime>>([Runtime]);

    public Task<JavaRuntime?> ResolveAsync(GameProfile game, CancellationToken cancellationToken = default)
        => Task.FromResult<JavaRuntime?>(Runtime);
}

public sealed class PublicDownloadSourceCatalog : IDownloadSourceProvider
{
    private static readonly DownloadSourceDescriptor[] Sources =
    [
        new("mojang", "Mojang 官方", new Uri("https://piston-meta.mojang.com/"),
            DownloadCapability.MinecraftMetadata | DownloadCapability.MinecraftFiles | DownloadCapability.JavaRuntime, 100),
        new("bmclapi", "BMCLAPI", new Uri("https://bmclapi2.bangbang93.com/"),
            DownloadCapability.MinecraftMetadata | DownloadCapability.MinecraftFiles | DownloadCapability.JavaRuntime | DownloadCapability.ModLoader, 80),
        new("modrinth", "Modrinth", new Uri("https://api.modrinth.com/v2/"),
            DownloadCapability.Mod | DownloadCapability.Modpack, 70),
        new("curseforge", "CurseForge", new Uri("https://api.curseforge.com/v1/"),
            DownloadCapability.Mod | DownloadCapability.Modpack, 60, RequiresApiKey: true)
    ];

    public Task<IReadOnlyList<DownloadSourceDescriptor>> GetSourcesAsync(
        DownloadCapability requiredCapabilities,
        CancellationToken cancellationToken = default)
    {
        var result = Sources
            .Where(source => (source.Capabilities & requiredCapabilities) != 0)
            .OrderByDescending(source => source.Priority)
            .ToArray();
        return Task.FromResult<IReadOnlyList<DownloadSourceDescriptor>>(result);
    }
}

public sealed class PrototypeVersionCatalogProvider : IVersionCatalogProvider
{
    // Layout fixture only. A network-backed implementation will replace this list.
    private static readonly MinecraftVersionEntry[] Versions =
    [
        new("1.21.4", "正式版", "1.21 系列示例"),
        new("1.21.3", "正式版", "1.21 系列示例"),
        new("1.21.1", "正式版", "推荐演示版本"),
        new("1.21", "正式版", "1.21 系列示例"),
        new("1.20.6", "正式版", "1.20 系列示例"),
        new("1.20.4", "正式版", "1.20 系列示例"),
        new("1.20.2", "正式版", "1.20 系列示例"),
        new("1.20.1", "正式版", "常用演示版本"),
        new("1.19.4", "正式版", "历史版本示例"),
        new("1.19.2", "正式版", "历史版本示例"),
        new("1.18.2", "正式版", "历史版本示例"),
        new("1.16.5", "正式版", "历史版本示例"),
        new("1.12.2", "正式版", "历史版本示例"),
        new("24w14a", "快照示例", "仅用于界面演示", true),
        new("23w51b", "快照示例", "仅用于界面演示", true)
    ];

    public Task<IReadOnlyList<MinecraftVersionEntry>> GetVersionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MinecraftVersionEntry>>(Versions);
}

public sealed class PrototypeDiagnosticProvider : IDiagnosticProvider
{
    public Task<IReadOnlyList<DiagnosticIssue>> InspectAsync(
        GameProfile game,
        JavaRuntime? runtime,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DiagnosticIssue> result = game.IsReady && runtime is not null
            ? []
            : [new("environment.incomplete", "运行环境尚未完成", "原型后端将把缺失文件加入自动修复队列。", true)];
        return Task.FromResult(result);
    }
}

public sealed class PrototypeLaunchEngine : ILaunchEngine
{
    public Task<LaunchResult> LaunchAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        => Task.FromResult(new LaunchResult(
            true,
            $"启动计划已生成：{plan.Game.Name} / Java {plan.Runtime.MajorVersion}。原型不会创建真实进程。"));
}

public sealed class PrototypeSkinProvider : ISkinProvider
{
    public Task<SkinPreview> PreviewAsync(string filePath, SkinModelType model, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return Task.FromResult(new SkinPreview(filePath, model, 0, 0, false, "找不到所选皮肤文件。"));

        try
        {
            using var stream = File.OpenRead(filePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var valid = frame.PixelWidth == 64 && (frame.PixelHeight == 64 || frame.PixelHeight == 32);
            var message = valid ? "皮肤尺寸有效，可以预览。" : "皮肤应为 64×64 或旧版 64×32 PNG。";
            return Task.FromResult(new SkinPreview(filePath, model, frame.PixelWidth, frame.PixelHeight, valid, message));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new SkinPreview(filePath, model, 0, 0, false, $"无法读取皮肤：{exception.Message}"));
        }
    }

    public Task<SkinApplyResult> ApplyAsync(AccountProfile account, SkinPreview preview, CancellationToken cancellationToken = default)
    {
        if (!preview.IsValid)
            return Task.FromResult(new SkinApplyResult(false, preview.Message));
        return Task.FromResult(new SkinApplyResult(true,
            account.Kind == AccountKind.Microsoft
                ? "上传请求已通过皮肤接口校验；原型不会修改微软账户。"
                : "离线皮肤设置已通过接口校验；原型不会写入游戏目录。"));
    }
}
