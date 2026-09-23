using System.ComponentModel;
using System.Runtime.CompilerServices;
using MintLauncher.Core.Abstractions;
using MintLauncher.Core.Models;

namespace MintLauncher.Presentation;

public sealed class MainWindowViewModel(ILauncherBackend backend) : INotifyPropertyChanged
{
    private LauncherSnapshot? _snapshot;
    private AccountProfile? _currentAccount;
    private GameProfile? _currentGame;
    private string _backendStatus = "正在初始化后端接口…";

    public string CurrentAccountName => _currentAccount?.DisplayName ?? "未选择账户";
    public string CurrentGameSummary => _currentGame is null
        ? "未选择游戏"
        : $"{_currentGame.Name} · Minecraft {_currentGame.MinecraftVersion}";
    public string BackendStatus
    {
        get => _backendStatus;
        private set => SetField(ref _backendStatus, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _snapshot = await backend.InitializeAsync(cancellationToken);
        _currentAccount = _snapshot.Accounts.FirstOrDefault();
        _currentGame = _snapshot.Games.FirstOrDefault();
        BackendStatus = $"后端骨架已连接 · {_snapshot.DownloadSources.Count} 个可替换数据源适配器";
        OnPropertyChanged(nameof(CurrentAccountName));
        OnPropertyChanged(nameof(CurrentGameSummary));
    }

    public void SelectGame(string displayName)
    {
        if (_snapshot is null) return;
        _currentGame = _snapshot.Games.FirstOrDefault(game => game.Name == displayName) ?? _currentGame;
        OnPropertyChanged(nameof(CurrentGameSummary));
    }

    public async Task<LaunchResult> PreviewLaunchAsync(CancellationToken cancellationToken = default)
    {
        if (_currentGame is null || _currentAccount is null)
            return new LaunchResult(false, "请先选择账户和游戏。");

        return await backend.LaunchAsync(new LaunchRequest(_currentGame.Id, _currentAccount.Id), cancellationToken);
    }

    public Task<DownloadPlan> CreateDownloadPlanAsync(DownloadRequest request, CancellationToken cancellationToken = default)
        => backend.CreateDownloadPlanAsync(request, cancellationToken);

    public Task<IReadOnlyList<MinecraftVersionEntry>> GetMinecraftVersionsAsync(CancellationToken cancellationToken = default)
        => backend.GetMinecraftVersionsAsync(cancellationToken);

    public Task<SkinPreview> PreviewSkinAsync(string filePath, SkinModelType model, CancellationToken cancellationToken = default)
        => backend.PreviewSkinAsync(filePath, model, cancellationToken);

    public Task<SkinApplyResult> ApplySkinAsync(SkinPreview preview, CancellationToken cancellationToken = default)
        => _currentAccount is null
            ? Task.FromResult(new SkinApplyResult(false, "请先选择账户。"))
            : backend.ApplySkinAsync(_currentAccount.Id, preview, cancellationToken);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }
}
