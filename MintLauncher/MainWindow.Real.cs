using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CmlLib.Core.Auth;
using MintLauncher.Operational;

namespace MintLauncher;

public partial class MainWindow
{
    private readonly LauncherWorkspace _workspace = new();
    private readonly WorkspacePreferences _workspacePreferences = WorkspacePreferences.Load();
    private readonly MicrosoftAccountService _microsoftAccounts = new();
    private string? _selectedRealGameId;
    private bool _refreshingRealGames;
    private CancellationTokenSource? _installCancellation;
    private Process? _gameProcess;
    private string? _launchIssueDirectory;
    private bool _accountSignInInProgress;

    private void InitializeRealLauncher()
    {
        RefreshRealAccount();
        RefreshRealGames();
    }

    private void RefreshRealAccount()
    {
        var microsoft = _microsoftAccounts.GetAccounts().FirstOrDefault(account =>
            string.Equals(account.Uuid, _workspacePreferences.SelectedMicrosoftUuid, StringComparison.OrdinalIgnoreCase));
        if (microsoft is not null)
        {
            HomeAccountName.Text = AccountOptionName.Text = microsoft.Name;
            AccountOptionSubtitle.Text = "微软账户 · 当前账户";
            EnvironmentStatus.Text = $"微软账户 {microsoft.Name} · 启动前会验证游戏身份";
            SwitchToOfflineButton.Visibility = Visibility.Visible;
        }
        else if (_workspacePreferences.SelectedMicrosoftUuid is not null)
        {
            HomeAccountName.Text = AccountOptionName.Text = "微软账户需要重新登录";
            AccountOptionSubtitle.Text = "登录信息不可用";
            EnvironmentStatus.Text = "此前的微软账户登录信息不可用；请重新登录或切换离线档案。";
            SwitchToOfflineButton.Visibility = Visibility.Visible;
        }
        else
        {
            HomeAccountName.Text = AccountOptionName.Text = _workspacePreferences.PlayerName;
            AccountOptionSubtitle.Text = "离线档案 · 当前账户";
            EnvironmentStatus.Text = _workspacePreferences.PlayerName.Equals("Player", StringComparison.OrdinalIgnoreCase)
                ? "离线档案 Player · 建议改名；正版验证服务器不可使用"
                : $"离线档案 {_workspacePreferences.PlayerName} · 启动前会检查游戏文件";
            SwitchToOfflineButton.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshRealGames(string? preferredId = null)
    {
        var games = LauncherWorkspace.ScanInstalled(_workspacePreferences.GameDirectory);
        var requestedId = preferredId ?? _selectedRealGameId ?? _workspacePreferences.SelectedGameId;
        var selected = games.FirstOrDefault(game => game.Id == requestedId) ?? games.FirstOrDefault();
        _selectedRealGameId = selected?.Id;
        TryRememberRealGame(_selectedRealGameId);
        _refreshingRealGames = true;
        try
        {
            RealInstalledList.ItemsSource = games;
            RealInstalledList.SelectedItem = selected;
        }
        finally
        {
            _refreshingRealGames = false;
        }
        RealDirectoryText.Text = _workspacePreferences.GameDirectory;
        RealDirectoryText.ToolTip = _workspacePreferences.GameDirectory;
        RealLibraryCountText.Text = games.Count == 0 ? "这台电脑上的游戏" : $"这台电脑上的游戏 · {games.Count} 个";
        RealSelectedGameText.Text = selected is null ? "还没有游戏" : selected.Description;
        var listHeight = Math.Clamp(games.Count * 56, 70, 280);
        RealInstalledList.Height = listHeight;
        InstanceMorphCard.Height = 285 + listHeight;
        InstanceMorphShadow.Height = InstanceMorphCard.Height;
        RealEmptyLibraryText.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RealUseGameButton.IsEnabled = selected is not null;
        GameSummary.Text = selected is null ? "未安装游戏 · 点击选择" : $"Minecraft {selected.Description}";
        if (games.Count == 0)
            EnvironmentStatus.Text = "当前目录没有已安装游戏 · 可先安装原版或导入现有目录";
    }

    private void RealInstalledList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RealUseGameButton is not null)
            RealUseGameButton.IsEnabled = RealInstalledList.SelectedItem is InstalledGame;
        if (RealSelectedGameText is not null)
            RealSelectedGameText.Text = RealInstalledList.SelectedItem is InstalledGame game ? game.Description : "还没有游戏";
        if (_refreshingRealGames || RealInstalledList.SelectedItem is not InstalledGame selected) return;
        _selectedRealGameId = selected.Id;
        var saved = TryRememberRealGame(selected.Id);
        GameSummary.Text = $"Minecraft {selected.Description}";
        EnvironmentStatus.Text = saved
            ? $"已选择 {selected.Id} · 启动前会检查游戏文件与 Java"
            : $"已选择 {selected.Id}，但无法保存选择；下次启动可能需要重新选择";
    }

    private void UseRealGame_Click(object sender, RoutedEventArgs e)
    {
        if (RealInstalledList.SelectedItem is not InstalledGame game) return;
        HideLaunchIssue();
        _selectedRealGameId = game.Id;
        var saved = TryRememberRealGame(game.Id);
        GameSummary.Text = $"Minecraft {game.Description}";
        EnvironmentStatus.Text = saved
            ? $"已选择 {game.Id} · 启动前会检查游戏文件与 Java"
            : $"已选择 {game.Id}，但无法保存选择；下次启动可能需要重新选择";
        CloseInstance();
    }

    private bool TryRememberRealGame(string? gameId)
    {
        if (_workspacePreferences.SelectedGameId == gameId) return true;
        var previous = _workspacePreferences.SelectedGameId;
        _workspacePreferences.SelectedGameId = gameId;
        try
        {
            _workspacePreferences.Save();
            return true;
        }
        catch
        {
            _workspacePreferences.SelectedGameId = previous;
            return false;
        }
    }

    private void ImportRealDirectory_Click(object sender, RoutedEventArgs e) => ImportRealDirectory();

    private void ImportRealDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 PCL 或 HMCL 使用的 .minecraft 游戏目录",
            InitialDirectory = Directory.Exists(_workspacePreferences.GameDirectory)
                ? _workspacePreferences.GameDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _workspacePreferences.GameDirectory = LauncherWorkspace.ValidateImportedDirectory(dialog.FolderName);
            _workspacePreferences.SelectedGameId = null;
            _workspacePreferences.Save();
            HideLaunchIssue();
            _selectedRealGameId = null;
            RefreshRealGames();
            EnvironmentStatus.Text = "游戏目录已导入；未复制或修改原文件";
            SectionSubtitle.Text = $"已导入 {_workspacePreferences.GameDirectory}";
        }
        catch (Exception exception)
        {
            EnvironmentStatus.Text = exception.GetBaseException().Message;
            SectionSubtitle.Text = exception.GetBaseException().Message;
        }
    }

    private void OpenRealDownload_Click(object sender, RoutedEventArgs e)
    {
        CloseInstance();
        NavDownload.IsChecked = true;
    }

    private void CancelRealInstall_Click(object sender, RoutedEventArgs e) => _installCancellation?.Cancel();

    private async Task InstallSelectedRealGameAsync()
    {
        if (_installCancellation is not null || DownloadVersionList.SelectedItem is not MintLauncher.Core.Models.MinecraftVersionEntry version)
            return;
        using var cancellation = new CancellationTokenSource();
        _installCancellation = cancellation;
        RealInstallButton.IsEnabled = false;
        RealCancelInstallButton.Visibility = Visibility.Visible;
        NavLaunch.IsEnabled = NavInstances.IsEnabled = NavSettings.IsEnabled = false;
        SectionBackButton.IsEnabled = false;
        DownloadPlanActionText.Text = "正在安装…";
        try
        {
            var progress = new Progress<string>(message => VersionCountText.Text = message);
            await _workspace.InstallAsync(_workspacePreferences.GameDirectory, version.Id, progress, cancellation.Token);
            RefreshRealGames(version.Id);
            SectionSubtitle.Text = $"Minecraft {version.Id} 已安装，返回启动页即可开始。";
            DownloadPlanActionText.Text = "安装完成";
        }
        catch (OperationCanceledException)
        {
            SectionSubtitle.Text = "安装已取消；已下载的有效文件会保留。";
            DownloadPlanActionText.Text = "继续安装";
        }
        catch (Exception exception)
        {
            SectionSubtitle.Text = $"安装失败：{exception.GetBaseException().Message}";
            DownloadPlanActionText.Text = "重试安装";
        }
        finally
        {
            _installCancellation = null;
            RealInstallButton.IsEnabled = true;
            RealCancelInstallButton.Visibility = Visibility.Collapsed;
            NavLaunch.IsEnabled = NavInstances.IsEnabled = NavSettings.IsEnabled = true;
            SectionBackButton.IsEnabled = true;
        }
    }

    private async Task LaunchSelectedRealGameAsync()
    {
        if (_isLaunching || _gameProcess is not null) return;
        HideLaunchIssue();
        if (_selectedRealGameId is null)
        {
            EnvironmentStatus.Text = "还没有可启动的游戏；先安装原版或导入现有目录。";
            return;
        }
        _isLaunching = true;
        LaunchButton.IsEnabled = false;
        var buttonText = FindVisualChild<TextBlock>(LaunchButton, "LaunchButtonText");
        if (buttonText is not null) buttonText.Text = "正在准备游戏…";
        EnvironmentStatus.Text = "正在准备游戏…";
        StartLaunchProgress();
        try
        {
            MSession session;
            if (_workspacePreferences.SelectedMicrosoftUuid is { } uuid)
            {
                EnvironmentStatus.Text = "正在验证微软账户…";
                session = await _microsoftAccounts.GetLaunchSessionAsync(uuid);
            }
            else
            {
                session = MSession.CreateOfflineSession(LauncherWorkspace.ValidatePlayerName(_workspacePreferences.PlayerName));
            }
            var progress = new Progress<LaunchPreparationProgress>(update =>
            {
                if (!_isLaunching || _gameProcess is not null) return;
                EnvironmentStatus.Text = update.Message;
                LaunchProgress.Value = Math.Clamp(update.Fraction * 100, 0, 100);
            });
            var versionId = _selectedRealGameId;
            var result = await _workspace.LaunchAsync(_workspacePreferences.GameDirectory, versionId, session, progress, CancellationToken.None);
            var process = result.Process;
            if (result.VersionId != versionId)
                RefreshRealGames(result.VersionId);
            _gameProcess = process;
            EnvironmentStatus.Text = "游戏正在运行";
            if (buttonText is not null) buttonText.Text = "游戏正在运行";
            _ = MonitorGameProcessAsync(process);
        }
        catch (Exception exception)
        {
            var cause = exception.GetBaseException();
            if (exception is FabricRepairException repair)
                ShowLaunchIssue("自动补全未完成", repair.Message, repair.VersionDirectory);
            else if (cause is MissingModLoaderException missingLoader)
                ShowLaunchIssue("无法识别此实例的加载器", missingLoader.Message, missingLoader.VersionDirectory);
            else
                ShowLaunchIssue("启动未完成", cause.Message, null);
        }
        finally
        {
            StopLaunchProgress();
            if (_gameProcess is null)
            {
                if (buttonText is not null) buttonText.Text = "启动游戏";
                LaunchButton.IsEnabled = true;
            }
            _isLaunching = false;
        }
    }

    private void ShowLaunchIssue(string title, string detail, string? directory)
    {
        EnvironmentStatus.Text = title;
        LaunchIssueTitle.Text = title;
        LaunchIssueDetail.Text = detail;
        _launchIssueDirectory = directory;
        OpenIssueDirectoryButton.Visibility = directory is not null && Directory.Exists(directory)
            ? Visibility.Visible : Visibility.Collapsed;
        LaunchIssueButton.Visibility = Visibility.Visible;
    }

    private void HideLaunchIssue()
    {
        LaunchIssueOverlay.Visibility = Visibility.Collapsed;
        LaunchIssueButton.Visibility = Visibility.Collapsed;
        _launchIssueDirectory = null;
    }

    private void LaunchIssueButton_Click(object sender, RoutedEventArgs e) => LaunchIssueOverlay.Visibility = Visibility.Visible;

    private void CloseLaunchIssue_Click(object sender, RoutedEventArgs e) => LaunchIssueOverlay.Visibility = Visibility.Collapsed;

    private void OpenIssueDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (_launchIssueDirectory is null || !Directory.Exists(_launchIssueDirectory)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_launchIssueDirectory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            LaunchIssueDetail.Text = $"无法打开实例目录：{exception.GetBaseException().Message}\n\n{LaunchIssueDetail.Text}";
        }
    }

    private void StartLaunchProgress()
    {
        LaunchProgress.Value = 5;
        if (_appearance.ReducedMotion)
        {
            LaunchProgressOutline.Visibility = Visibility.Collapsed;
            LaunchProgress.Visibility = Visibility.Visible;
            return;
        }
        LaunchProgress.Visibility = Visibility.Collapsed;
        LaunchProgressOutline.Visibility = Visibility.Visible;
        LaunchProgressOutline.BeginAnimation(Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(0, -350, TimeSpan.FromSeconds(2.6)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void StopLaunchProgress()
    {
        LaunchProgressOutline.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        LaunchProgressOutline.Visibility = Visibility.Collapsed;
        LaunchProgress.Visibility = Visibility.Collapsed;
    }

    private async Task MonitorGameProcessAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync();
            if (!Dispatcher.HasShutdownStarted)
                EnvironmentStatus.Text = process.ExitCode == 0 ? "游戏已结束，可以再次启动" : "游戏已退出；如有问题可查看游戏日志";
        }
        catch (Exception exception)
        {
            if (!Dispatcher.HasShutdownStarted)
                ShowLaunchIssue("无法确认游戏状态", exception.GetBaseException().Message, null);
        }
        finally
        {
            process.Dispose();
            if (!Dispatcher.HasShutdownStarted && ReferenceEquals(_gameProcess, process))
            {
                _gameProcess = null;
                var buttonText = FindVisualChild<TextBlock>(LaunchButton, "LaunchButtonText");
                if (buttonText is not null) buttonText.Text = "启动游戏";
                LaunchButton.IsEnabled = true;
            }
        }
    }
}
