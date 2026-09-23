using MintLauncher.Core.Models;

namespace MintLauncher.Core.Abstractions;

public interface IAccountProvider
{
    Task<IReadOnlyList<AccountProfile>> GetAccountsAsync(CancellationToken cancellationToken = default);
    Task<AccountProfile> CreateOfflineAsync(string displayName, CancellationToken cancellationToken = default);
}

public interface IGameLibraryProvider
{
    Task<IReadOnlyList<GameProfile>> GetGamesAsync(CancellationToken cancellationToken = default);
    Task<GameProfile?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IRuntimeProvider
{
    Task<IReadOnlyList<JavaRuntime>> DetectAsync(CancellationToken cancellationToken = default);
    Task<JavaRuntime?> ResolveAsync(GameProfile game, CancellationToken cancellationToken = default);
}

public interface IDownloadSourceProvider
{
    Task<IReadOnlyList<DownloadSourceDescriptor>> GetSourcesAsync(
        DownloadCapability requiredCapabilities,
        CancellationToken cancellationToken = default);
}

public interface IVersionCatalogProvider
{
    Task<IReadOnlyList<MinecraftVersionEntry>> GetVersionsAsync(CancellationToken cancellationToken = default);
}

public interface ISkinProvider
{
    Task<SkinPreview> PreviewAsync(
        string filePath,
        SkinModelType model,
        CancellationToken cancellationToken = default);

    Task<SkinApplyResult> ApplyAsync(
        AccountProfile account,
        SkinPreview preview,
        CancellationToken cancellationToken = default);
}

public interface IDiagnosticProvider
{
    Task<IReadOnlyList<DiagnosticIssue>> InspectAsync(
        GameProfile game,
        JavaRuntime? runtime,
        CancellationToken cancellationToken = default);
}

public interface ILaunchEngine
{
    Task<LaunchResult> LaunchAsync(LaunchPlan plan, CancellationToken cancellationToken = default);
}

public interface ILauncherBackend
{
    Task<LauncherSnapshot> InitializeAsync(CancellationToken cancellationToken = default);
    Task<LaunchPlan> PrepareLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default);
    Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default);
    Task<DownloadPlan> CreateDownloadPlanAsync(DownloadRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MinecraftVersionEntry>> GetMinecraftVersionsAsync(CancellationToken cancellationToken = default);
    Task<SkinPreview> PreviewSkinAsync(string filePath, SkinModelType model, CancellationToken cancellationToken = default);
    Task<SkinApplyResult> ApplySkinAsync(Guid accountId, SkinPreview preview, CancellationToken cancellationToken = default);
}
