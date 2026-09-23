using MintLauncher.Core.Abstractions;
using MintLauncher.Core.Services;
using MintLauncher.Infrastructure.Prototype;
using MintLauncher.Presentation;

namespace MintLauncher.Composition;

public sealed record AppServices(
    ILauncherBackend Backend,
    MainWindowViewModel MainWindow);

public static class AppCompositionRoot
{
    public static AppServices CreatePrototype()
    {
        IAccountProvider accounts = new PrototypeAccountProvider();
        IGameLibraryProvider games = new PrototypeGameLibraryProvider();
        IRuntimeProvider runtimes = new PrototypeRuntimeProvider();
        IDownloadSourceProvider downloads = new PublicDownloadSourceCatalog();
        IDiagnosticProvider diagnostics = new PrototypeDiagnosticProvider();
        ILaunchEngine launchEngine = new PrototypeLaunchEngine();
        ISkinProvider skins = new PrototypeSkinProvider();
        IVersionCatalogProvider versions = new PrototypeVersionCatalogProvider();

        ILauncherBackend backend = new LauncherBackend(
            accounts,
            games,
            runtimes,
            downloads,
            diagnostics,
            launchEngine,
            skins,
            versions);

        return new AppServices(backend, new MainWindowViewModel(backend));
    }
}
