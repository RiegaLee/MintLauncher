namespace MintLauncher.Core.Models;

public enum AccountKind
{
    Microsoft,
    Offline
}

public sealed record AccountProfile(
    Guid Id,
    string DisplayName,
    AccountKind Kind,
    bool IsAuthenticated);

public sealed record GameProfile(
    Guid Id,
    string Name,
    string MinecraftVersion,
    string? ModLoader,
    string GameDirectory,
    bool IsReady);

public sealed record JavaRuntime(
    string ExecutablePath,
    int MajorVersion,
    string Architecture,
    bool IsManaged);

[Flags]
public enum DownloadCapability
{
    None = 0,
    MinecraftMetadata = 1,
    MinecraftFiles = 2,
    JavaRuntime = 4,
    ModLoader = 8,
    Mod = 16,
    Modpack = 32
}

public sealed record DownloadSourceDescriptor(
    string Id,
    string DisplayName,
    Uri BaseAddress,
    DownloadCapability Capabilities,
    int Priority,
    bool RequiresApiKey = false);

public enum DownloadTargetKind
{
    Vanilla,
    Modpack,
    Recommended
}

public sealed record DownloadRequest(
    DownloadTargetKind Kind,
    string MinecraftVersion,
    string? ModLoader = null);

public sealed record MinecraftVersionEntry(
    string Id,
    string Category,
    string Description,
    bool IsSnapshot = false);

public sealed record DownloadPlan(
    DownloadRequest Request,
    IReadOnlyList<DownloadSourceDescriptor> Sources,
    string Summary,
    bool IsPreview = true);

public enum SkinModelType
{
    Steve,
    Alex
}

public sealed record SkinPreview(
    string FilePath,
    SkinModelType Model,
    int PixelWidth,
    int PixelHeight,
    bool IsValid,
    string Message);

public sealed record SkinApplyResult(bool Success, string Message);

public sealed record DiagnosticIssue(
    string Code,
    string Title,
    string Description,
    bool CanAutoRepair);

public sealed record LaunchRequest(
    Guid GameId,
    Guid AccountId,
    bool AllowAutomaticRepair = true);

public sealed record LaunchPlan(
    GameProfile Game,
    AccountProfile Account,
    JavaRuntime Runtime,
    IReadOnlyList<DownloadSourceDescriptor> Sources,
    IReadOnlyList<DiagnosticIssue> Issues);

public sealed record LaunchResult(
    bool Success,
    string Message,
    int? ProcessId = null);

public sealed record LauncherSnapshot(
    IReadOnlyList<AccountProfile> Accounts,
    IReadOnlyList<GameProfile> Games,
    IReadOnlyList<JavaRuntime> Runtimes,
    IReadOnlyList<DownloadSourceDescriptor> DownloadSources,
    IReadOnlyList<DiagnosticIssue> Diagnostics);
