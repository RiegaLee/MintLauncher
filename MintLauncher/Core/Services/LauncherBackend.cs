using MintLauncher.Core.Abstractions;
using MintLauncher.Core.Models;

namespace MintLauncher.Core.Services;

public sealed class LauncherBackend(
    IAccountProvider accounts,
    IGameLibraryProvider games,
    IRuntimeProvider runtimes,
    IDownloadSourceProvider downloads,
    IDiagnosticProvider diagnostics,
    ILaunchEngine launchEngine,
    ISkinProvider skins,
    IVersionCatalogProvider versions) : ILauncherBackend
{
    public async Task<LauncherSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var accountTask = accounts.GetAccountsAsync(cancellationToken);
        var gameTask = games.GetGamesAsync(cancellationToken);
        var runtimeTask = runtimes.DetectAsync(cancellationToken);
        var sourceTask = downloads.GetSourcesAsync(
            DownloadCapability.MinecraftMetadata | DownloadCapability.MinecraftFiles,
            cancellationToken);

        await Task.WhenAll(accountTask, gameTask, runtimeTask, sourceTask);
        var gameList = await gameTask;
        var runtimeList = await runtimeTask;
        var issues = gameList.Count == 0
            ? []
            : await diagnostics.InspectAsync(gameList[0], runtimeList.FirstOrDefault(), cancellationToken);

        return new LauncherSnapshot(
            await accountTask,
            gameList,
            runtimeList,
            await sourceTask,
            issues);
    }

    public async Task<LaunchPlan> PrepareLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        var game = await games.FindAsync(request.GameId, cancellationToken)
            ?? throw new InvalidOperationException("找不到所选游戏。");
        var account = (await accounts.GetAccountsAsync(cancellationToken))
            .FirstOrDefault(item => item.Id == request.AccountId)
            ?? throw new InvalidOperationException("找不到所选账户。");
        var runtime = await runtimes.ResolveAsync(game, cancellationToken)
            ?? throw new InvalidOperationException("没有可用于该游戏的 Java 运行环境。");
        var sources = await downloads.GetSourcesAsync(
            DownloadCapability.MinecraftMetadata | DownloadCapability.MinecraftFiles | DownloadCapability.JavaRuntime,
            cancellationToken);
        var issues = await diagnostics.InspectAsync(game, runtime, cancellationToken);

        if (!request.AllowAutomaticRepair && issues.Count > 0)
            throw new InvalidOperationException("游戏环境需要修复，但本次请求禁止自动修复。");

        return new LaunchPlan(game, account, runtime, sources, issues);
    }

    public async Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        => await launchEngine.LaunchAsync(await PrepareLaunchAsync(request, cancellationToken), cancellationToken);

    public async Task<DownloadPlan> CreateDownloadPlanAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        var capability = request.Kind == DownloadTargetKind.Modpack
            ? DownloadCapability.MinecraftMetadata | DownloadCapability.MinecraftFiles | DownloadCapability.Modpack | DownloadCapability.ModLoader
            : DownloadCapability.MinecraftMetadata | DownloadCapability.MinecraftFiles | DownloadCapability.JavaRuntime | DownloadCapability.ModLoader;
        var sources = await downloads.GetSourcesAsync(capability, cancellationToken);
        var target = request.Kind switch
        {
            DownloadTargetKind.Modpack => "整合包导入与依赖修复",
            DownloadTargetKind.Recommended => "兼容性推荐方案",
            _ => "纯净 Minecraft"
        };
        return new DownloadPlan(request, sources, $"{target} / {request.MinecraftVersion} / {sources.Count} 个候选数据源");
    }

    public Task<IReadOnlyList<MinecraftVersionEntry>> GetMinecraftVersionsAsync(CancellationToken cancellationToken = default)
        => versions.GetVersionsAsync(cancellationToken);

    public Task<SkinPreview> PreviewSkinAsync(string filePath, SkinModelType model, CancellationToken cancellationToken = default)
        => skins.PreviewAsync(filePath, model, cancellationToken);

    public async Task<SkinApplyResult> ApplySkinAsync(Guid accountId, SkinPreview preview, CancellationToken cancellationToken = default)
    {
        var account = (await accounts.GetAccountsAsync(cancellationToken)).FirstOrDefault(item => item.Id == accountId);
        return account is null
            ? new SkinApplyResult(false, "找不到当前账户。")
            : await skins.ApplyAsync(account, preview, cancellationToken);
    }
}
