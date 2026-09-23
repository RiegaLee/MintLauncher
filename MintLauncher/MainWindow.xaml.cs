using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using MintLauncher.Core.Models;
using MintLauncher.Operational;
using MintLauncher.Presentation;

namespace MintLauncher;

public partial class MainWindow : Window
{
    private static readonly IEasingFunction MorphEase = new CubicEase { EasingMode = EasingMode.EaseOut };
    private bool _accountOpen;
    private bool _instanceOpen;
    private bool _isLaunching;
    private bool _accountAdding;
    private bool _skinEditing;
    private int _selectedGameCard;
    private string _sectionMode = "download";
    private DownloadTargetKind _downloadKind = DownloadTargetKind.Vanilla;
    private string _downloadVersion = string.Empty;
    private SkinModelType _skinModel = SkinModelType.Steve;
    private SkinPreview? _skinPreview;
    private readonly AxisAngleRotation3D _skinYaw = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _skinPitch = new(new Vector3D(1, 0, 0), 0);
    private readonly AxisAngleRotation3D _homeSkinYaw = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _homeHeadYaw = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _homeLeftArmSwing = new(new Vector3D(1, 0, 0), 0);
    private readonly AxisAngleRotation3D _homeRightArmSwing = new(new Vector3D(1, 0, 0), 0);
    private readonly TranslateTransform3D _homeSkinBob = new();
    private Point? _homeSkinDragStart;
    private int _homeIdleVersion;
    private Point? _skinDragStart;
    private int _selectedSettingsCard;
    private int _accountTransitionVersion;
    private int _instanceTransitionVersion;
    private int _sectionTransitionVersion;
    private int _dimmerTransitionVersion;
    private int _skinRenderVersion;
    private int _versionCatalogVersion;
    private readonly MainWindowViewModel _viewModel;
    private readonly LauncherAppearancePreferences _appearance;
    private readonly DispatcherTimer _appearanceSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Stopwatch _navigationClock = Stopwatch.StartNew();
    private readonly NavigationIndicatorMotion _navigationMotion = new();
    private bool _navigationRendering;
    private ImageSource? _defaultBackdropImage;

    public MainWindow(MainWindowViewModel viewModel, LauncherAppearancePreferences appearance)
    {
        InitializeComponent();
        _appearance = appearance;
        _skinModel = Enum.TryParse<SkinModelType>(appearance.SkinModel, out var preferredModel)
            ? preferredModel : SkinModelType.Steve;
        foreach (var card in new[] { SectionCard1, SectionCard2, SectionCard3 })
        {
            card.BorderBrush = (Brush)FindResource("WindowBorder");
            card.BorderThickness = new Thickness(1);
            card.MouseEnter += SectionCard_MouseEnter;
            card.MouseLeave += SectionCard_MouseLeave;
        }
        SteveModelButton.Height = AlexModelButton.Height = 46;
        SteveModelButton.FontSize = AlexModelButton.FontSize = 13;
        _viewModel = viewModel;
        DataContext = viewModel;
        _defaultBackdropImage = BackdropImage.Source;
        _appearanceSaveTimer.Tick += (_, _) => { _appearanceSaveTimer.Stop(); TrySaveAppearance(); };
        BuildSkinModel(null, 0, _skinModel == SkinModelType.Alex);
        MotionToggle.IsChecked = !_appearance.ReducedMotion;
        MotionToggle.Checked += MotionToggle_Changed;
        MotionToggle.Unchecked += MotionToggle_Changed;
        if (!_appearance.ReducedMotion) StartHomeSkinIdleAnimation();
        SetDefaultSkinLabels();
        RefreshSkinModelButtons();
        BackgroundOverlaySlider.Value = Math.Clamp(_appearance.BackgroundOverlayOpacity * 100, 55, 95);
        BackgroundOverlayValue.Text = $"{BackgroundOverlaySlider.Value:0}%";
        ApplyAppearanceBackground();
        RefreshThemeButtons();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => StopNavigationRendering();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        MoveNavigationIndicator(NavLaunch);
        InitializeRealLauncher();
        try
        {
            if (!string.IsNullOrWhiteSpace(_appearance.SkinPath))
                await LoadSkinPreviewAsync(_appearance.SkinPath, remember: false);
        }
        catch (Exception exception)
        {
            EnvironmentStatus.Text = $"皮肤预览失败 · {exception.Message}";
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var preference = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        var borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        _ = DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private static DoubleAnimation Number(double to, int milliseconds = 520, int delay = 0) => new(to, new Duration(TimeSpan.FromMilliseconds(milliseconds)))
    {
        BeginTime = TimeSpan.FromMilliseconds(delay),
        EasingFunction = MorphEase
    };

    private static void SetMorphState(ScaleTransform scale, TranslateTransform translate, double scaleX, double scaleY, double translateY)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        scale.ScaleX = scaleX;
        scale.ScaleY = scaleY;
        translate.Y = translateY;
    }

    private static void AnimateMorph(ScaleTransform scale, TranslateTransform translate, double scaleX, double scaleY, double translateY, int milliseconds = 460, Action? completed = null)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Number(scaleX, milliseconds));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Number(scaleY, milliseconds));
        var move = Number(translateY, milliseconds);
        if (completed is not null) move.Completed += (_, _) => completed();
        translate.BeginAnimation(TranslateTransform.YProperty, move);
    }

    private void Fade(UIElement element, double to, int milliseconds = 260, int delay = 0)
        => element.BeginAnimation(OpacityProperty, Number(to, milliseconds, delay));

    private void ShowDimmer()
    {
        _dimmerTransitionVersion++;
        Dimmer.Visibility = Visibility.Visible;
        Fade(Dimmer, 1, 250);
        Fade(HomePanel, 0.16, 300);
    }

    private void HideDimmer(bool restoreHome = true)
    {
        var version = ++_dimmerTransitionVersion;
        Fade(Dimmer, 0, 220);
        if (restoreHome) Fade(HomePanel, 1, 300, 90);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (version == _dimmerTransitionVersion && !_accountOpen && !_instanceOpen)
                Dimmer.Visibility = Visibility.Collapsed;
        };
        timer.Start();
    }

    private void AccountButton_Click(object sender, RoutedEventArgs e) => OpenAccount();

    private void HomeChangeSkin_Click(object sender, RoutedEventArgs e)
    {
        OpenAccount();
        OpenSkinEditor();
    }

    private void OpenAccount()
    {
        if (_accountOpen) return;
        var resumeClosing = AccountMorphCard.Visibility == Visibility.Visible;
        _accountTransitionVersion++;
        if (_instanceOpen) CloseInstance(false);
        _accountOpen = true;
        ShowDimmer();
        ShowAccountChooser(false);
        AccountButton.Opacity = 0;
        if (!resumeClosing)
        {
            SetMorphState(AccountCardScale, AccountCardTranslate, 440d / 760d, 72d / 382d, -78);
            SetMorphState(AccountShadowScale, AccountShadowTranslate, 440d / 760d, 72d / 382d, -78);
            AccountOptions.Opacity = 0;
        }
        AccountMorphShadow.Visibility = Visibility.Visible;
        AccountMorphCard.Visibility = Visibility.Visible;
        AnimateMorph(AccountCardScale, AccountCardTranslate, 1, 1, 0);
        AnimateMorph(AccountShadowScale, AccountShadowTranslate, 1, 1, 0);
        Fade(AccountOptions, 1, 280, 155);
    }

    private void CloseAccount_Click(object sender, RoutedEventArgs e) => CloseAccount();
    private void ChooseAccount_Click(object sender, RoutedEventArgs e) => CloseAccount();

    private void CloseAccount(bool restoreDimmer = true)
    {
        if (!_accountOpen) return;
        _accountOpen = false;
        var version = ++_accountTransitionVersion;
        Fade(_skinEditing ? SkinEditorPanel : _accountAdding ? AccountAddPanel : AccountOptions, 0, 120);
        AnimateMorph(AccountCardScale, AccountCardTranslate, 440d / 760d, 72d / 382d, -78, 360);
        AnimateMorph(AccountShadowScale, AccountShadowTranslate, 440d / 760d, 72d / 382d, -78, 360);
        if (restoreDimmer) HideDimmer();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(390) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (version != _accountTransitionVersion || _accountOpen) return;
            AccountMorphCard.Visibility = Visibility.Collapsed;
            AccountMorphShadow.Visibility = Visibility.Collapsed;
            AccountButton.Opacity = 1;
            ShowAccountChooser(false);
        };
        timer.Start();
    }

    private void OpenAccountAdd_Click(object sender, RoutedEventArgs e)
    {
        _accountAdding = true;
        AccountHeaderAvatar.Visibility = Visibility.Collapsed;
        AccountBackButton.Visibility = Visibility.Visible;
        AccountHeaderTitle.Text = "添加账户";
        AccountHeaderSubtitle.Text = "微软账户会在系统浏览器中安全登录";
        AccountOptions.Visibility = Visibility.Collapsed;
        AccountAddPanel.Visibility = Visibility.Visible;
        AccountAddPanel.Opacity = 0;
        OfflineNameInput.Text = _workspacePreferences.PlayerName;
        MicrosoftLoginError.Visibility = Visibility.Collapsed;
        SelectAccountMethod("Microsoft");
        Fade(AccountAddPanel, 1, 240);
    }

    private void OpenSkinEditor_Click(object sender, RoutedEventArgs e) => OpenSkinEditor();

    private void OpenSkinEditor()
    {
        _skinEditing = true;
        _accountAdding = false;
        AccountHeaderAvatar.Visibility = Visibility.Collapsed;
        AccountBackButton.Visibility = Visibility.Visible;
        AccountHeaderTitle.Text = "皮肤工作台";
        AccountHeaderSubtitle.Text = "预览、校验并交给可替换的皮肤服务";
        AccountOptions.Visibility = Visibility.Collapsed;
        AccountAddPanel.Visibility = Visibility.Collapsed;
        SkinEditorPanel.Visibility = Visibility.Visible;
        SkinEditorPanel.Opacity = 0;
        SelectSkinModel(_skinModel);
        Fade(SkinEditorPanel, 1, 240);
    }

    private void SelectSkinModel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && Enum.TryParse<SkinModelType>(button.Tag?.ToString(), out var model))
            SelectSkinModel(model);
    }

    private async void SelectSkinModel(SkinModelType model)
    {
        if (model == _skinModel && SkinModelVisual.Content is not null) return;
        _skinModel = model;
        _appearance.SkinModel = model.ToString();
        TrySaveAppearance();
        RefreshSkinModelButtons();
        if (_skinPreview is not null)
            await LoadSkinPreviewAsync(_skinPreview.FilePath);
        else
        {
            BuildSkinModel(null, 0, model == SkinModelType.Alex);
            SetDefaultSkinLabels();
        }
    }

    private void SetDefaultSkinLabels()
    {
        SkinFileName.Text = "薄荷默认模型 · 无内置皮肤图片";
        SkinStatusText.Text = "默认模型正面预览 · 尚未读取账户皮肤";
        HomeChangeSkinButton.ToolTip = "当前展示薄荷默认模型；可以选择本地皮肤";
    }

    private void RefreshSkinModelButtons()
    {
        SteveModelButton.SetResourceReference(Button.BackgroundProperty, _skinModel == SkinModelType.Steve ? "SelectionSurface" : "SecondarySurface");
        AlexModelButton.SetResourceReference(Button.BackgroundProperty, _skinModel == SkinModelType.Alex ? "SelectionSurface" : "SecondarySurface");
    }

    private async void ChooseSkinFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 Minecraft 皮肤",
            Filter = "Minecraft 皮肤 (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            await LoadSkinPreviewAsync(dialog.FileName);
    }

    private async Task LoadSkinPreviewAsync(string path, bool remember = true)
    {
        var renderVersion = ++_skinRenderVersion;
        SkinStatusText.Text = "正在校验皮肤…";
        var preview = await _viewModel.PreviewSkinAsync(path, _skinModel);
        await Dispatcher.Yield(DispatcherPriority.Background);
        if (renderVersion != _skinRenderVersion) return;
        SkinStatusText.Text = preview.Message;
        SkinStatusText.Foreground = (Brush)FindResource(preview.IsValid ? "BrandMint" : "TextSecondary");
        SkinFileName.Text = System.IO.Path.GetFileName(path);
        SkinApplyActionText.Text = "应用到当前账户（演示）";
        if (!preview.IsValid)
        {
            SkinTexturePreview.Source = null;
            SkinViewport.Visibility = Visibility.Collapsed;
            SkinTexturePreview.Visibility = Visibility.Collapsed;
            SkinPreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            if (renderVersion != _skinRenderVersion) return;
            _skinPreview = preview;
            SkinTexturePreview.Source = bitmap;
            BuildSkinModel(bitmap, preview.PixelHeight, _skinModel == SkinModelType.Alex);
            SkinViewport.Visibility = Visibility.Visible;
            SkinTexturePreview.Visibility = Visibility.Collapsed;
            SkinPreviewPlaceholder.Visibility = Visibility.Collapsed;
            HomeChangeSkinButton.ToolTip = $"当前本地预览：{Path.GetFileName(path)}；尚未上传到账户";
            if (remember)
            {
                _appearance.SkinPath = path;
                TrySaveAppearance();
            }
        }
        catch (Exception exception)
        {
            SkinStatusText.Text = $"无法渲染皮肤：{exception.Message}";
            SkinViewport.Visibility = Visibility.Collapsed;
            SkinPreviewPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private readonly record struct SkinUv(double X, double Y, double Width, double Height);

    private sealed record SkinFaces(SkinUv Front, SkinUv Back, SkinUv Left, SkinUv Right, SkinUv Top, SkinUv Bottom);

    private sealed record CutoutSkinAtlas(byte[] Alpha, int Width, int Height, Material Material);

    private void StartHomeSkinIdleAnimation()
    {
        HomeSkinModelVisual.Transform = new Transform3DGroup
        {
            Children = new Transform3DCollection
            {
                new RotateTransform3D(_homeSkinYaw),
                _homeSkinBob
            }
        };
        StartHomeYawIdleAnimation();
        _homeSkinBob.BeginAnimation(TranslateTransform3D.OffsetYProperty, new DoubleAnimation(-0.32, 0.32, TimeSpan.FromSeconds(2.9))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
        _homeHeadYaw.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(5, -5, TimeSpan.FromSeconds(4.4))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
        _homeLeftArmSwing.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(-4, 5, TimeSpan.FromSeconds(2.9))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
        _homeRightArmSwing.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(4, -5, TimeSpan.FromSeconds(2.9))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    private void MotionToggle_Changed(object sender, RoutedEventArgs e)
    {
        _appearance.ReducedMotion = MotionToggle.IsChecked != true;
        if (_appearance.ReducedMotion)
        {
            _homeSkinYaw.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _homeHeadYaw.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _homeLeftArmSwing.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _homeRightArmSwing.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _homeSkinBob.BeginAnimation(TranslateTransform3D.OffsetYProperty, null);
            _homeSkinYaw.Angle = 0;
            _homeHeadYaw.Angle = 0;
            _homeLeftArmSwing.Angle = 0;
            _homeRightArmSwing.Angle = 0;
            _homeSkinBob.OffsetY = 0;
        }
        else StartHomeSkinIdleAnimation();
        if (_isLaunching)
        {
            StopLaunchProgress();
            StartLaunchProgress();
        }
        TrySaveAppearance();
    }

    private void StartHomeYawIdleAnimation()
    {
        _homeSkinYaw.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(-20, 20, TimeSpan.FromSeconds(4.4))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    private void HomeSkinViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _homeIdleVersion++;
        var currentAngle = _homeSkinYaw.Angle;
        _homeSkinYaw.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        _homeSkinYaw.Angle = currentAngle;
        _homeSkinDragStart = e.GetPosition(HomeSkinViewport);
        HomeSkinViewport.CaptureMouse();
    }

    private void HomeSkinViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_homeSkinDragStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(HomeSkinViewport);
        _homeSkinYaw.Angle = Math.Clamp(_homeSkinYaw.Angle + (current.X - start.X) * 0.8, -70, 70);
        _homeSkinDragStart = current;
    }

    private void HomeSkinViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_homeSkinDragStart is null) return;
        _homeSkinDragStart = null;
        HomeSkinViewport.ReleaseMouseCapture();
        var version = ++_homeIdleVersion;
        var settle = new DoubleAnimation(_homeSkinYaw.Angle, 0, TimeSpan.FromMilliseconds(550))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        settle.Completed += (_, _) =>
        {
            if (version == _homeIdleVersion && _homeSkinDragStart is null)
                StartHomeYawIdleAnimation();
        };
        _homeSkinYaw.BeginAnimation(AxisAngleRotation3D.AngleProperty, settle);
    }

    private void BuildSkinModel(BitmapSource? texture, int textureHeight, bool slim)
    {
        var model = new Model3DGroup();
        model.Children.Add(new AmbientLight(Color.FromRgb(170, 181, 194)));
        model.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.5, -0.8, -1)));
        var armWidth = slim ? 3d : 4d;
        var head = new Model3DGroup { Transform = new RotateTransform3D(_homeHeadYaw, new Point3D(0, 12, 0)) };
        var leftArmCenter = -(4.45 + armWidth / 2);
        var rightArmCenter = 4.45 + armWidth / 2;
        var leftArm = new Model3DGroup { Transform = new RotateTransform3D(_homeLeftArmSwing, new Point3D(leftArmCenter, 8, 0)) };
        var rightArm = new Model3DGroup { Transform = new RotateTransform3D(_homeRightArmSwing, new Point3D(rightArmCenter, 8, 0)) };
        if (texture is null)
        {
            // The default mannequin is made entirely from code-defined materials.
            // No Minecraft or third-party skin image is bundled with the launcher.
            AddSolidCuboid(head, new Point3D(0, 12, 0), new Vector3D(8, 8, 8), Color.FromRgb(213, 250, 241));
            AddSolidCuboid(head, new Point3D(0, 15.55, 0), new Vector3D(8.2, 0.9, 8.2), Color.FromRgb(70, 194, 171));
            AddSolidCuboid(head, new Point3D(-1.5, 12.6, 4.1), new Vector3D(0.7, 0.7, 0.16), Color.FromRgb(27, 75, 81));
            AddSolidCuboid(head, new Point3D(1.5, 12.6, 4.1), new Vector3D(0.7, 0.7, 0.16), Color.FromRgb(27, 75, 81));
            AddSolidCuboid(head, new Point3D(0, 10.8, 4.1), new Vector3D(0.9, 0.18, 0.16), Color.FromRgb(65, 126, 125));
            AddSolidCuboid(model, new Point3D(0, 2, 0), new Vector3D(8, 12, 4), Color.FromRgb(46, 173, 155));
            AddSolidCuboid(model, new Point3D(0, 2.5, 2.1), new Vector3D(0.7, 8, 0.16), Color.FromRgb(187, 247, 229));
            AddSolidCuboid(leftArm, new Point3D(leftArmCenter, 2, 0), new Vector3D(armWidth, 12, 4), Color.FromRgb(87, 205, 184));
            AddSolidCuboid(rightArm, new Point3D(rightArmCenter, 2, 0), new Vector3D(armWidth, 12, 4), Color.FromRgb(87, 205, 184));
            AddSolidCuboid(model, new Point3D(-2, -10, 0), new Vector3D(4, 12, 4), Color.FromRgb(47, 76, 91));
            AddSolidCuboid(model, new Point3D(2, -10, 0), new Vector3D(4, 12, 4), Color.FromRgb(47, 76, 91));
            AddSolidCuboid(model, new Point3D(-2, -14.5, 0.08), new Vector3D(4.05, 3, 4.25), Color.FromRgb(28, 48, 62));
            AddSolidCuboid(model, new Point3D(2, -14.5, 0.08), new Vector3D(4.05, 3, 4.25), Color.FromRgb(28, 48, 62));
        }
        else
        {
            var oldSkin = textureHeight == 32;
            AddSkinCuboid(head, new Point3D(0, 12, 0), new Vector3D(8, 8, 8), HeadFaces(), texture);
            AddSkinCuboid(model, new Point3D(0, 2, 0), new Vector3D(8, 12, 4), BodyFaces(), texture);
            AddSkinCuboid(leftArm, new Point3D(leftArmCenter, 2, 0), new Vector3D(armWidth, 12, 4), ArmFaces(false, slim), texture);
            AddSkinCuboid(rightArm, new Point3D(rightArmCenter, 2, 0), new Vector3D(armWidth, 12, 4), oldSkin ? ArmFaces(false, slim) : ArmFaces(true, slim), texture);
            AddSkinCuboid(model, new Point3D(-2, -10, 0), new Vector3D(4, 12, 4), LegFaces(false), texture);
            AddSkinCuboid(model, new Point3D(2, -10, 0), new Vector3D(4, 12, 4), oldSkin ? LegFaces(false) : LegFaces(true), texture);

            if (!oldSkin)
            {
                // Draw only occupied overlay pixels, with stable depth testing.
                var overlay = CreateCutoutSkinAtlas(texture);
                AddCutoutSkinCuboid(head, new Point3D(0, 12, 0), new Vector3D(8.7, 8.7, 8.7), HeadOverlayFaces(), overlay);
                AddCutoutSkinCuboid(model, new Point3D(0, 2, 0), new Vector3D(8.45, 12.45, 4.45), BodyOverlayFaces(), overlay);
                AddCutoutSkinCuboid(leftArm, new Point3D(leftArmCenter, 2, 0), new Vector3D(armWidth + 0.45, 12.45, 4.45), ArmOverlayFaces(false, slim), overlay);
                AddCutoutSkinCuboid(rightArm, new Point3D(rightArmCenter, 2, 0), new Vector3D(armWidth + 0.45, 12.45, 4.45), ArmOverlayFaces(true, slim), overlay);
                AddCutoutSkinCuboid(model, new Point3D(-2, -10, 0), new Vector3D(4.25, 12.25, 4.25), LegOverlayFaces(false), overlay);
                AddCutoutSkinCuboid(model, new Point3D(2, -10, 0), new Vector3D(4.25, 12.25, 4.25), LegOverlayFaces(true), overlay);
            }
        }

        model.Children.Add(head);
        model.Children.Add(leftArm);
        model.Children.Add(rightArm);

        HomeSkinModelVisual.Content = model;
        var editorModel = model.Clone();
        editorModel.Transform = new Transform3DGroup
        {
            Children = new Transform3DCollection
            {
                new RotateTransform3D(_skinPitch),
                new RotateTransform3D(_skinYaw)
            }
        };
        SkinModelVisual.Content = editorModel;
    }

    private static SkinFaces HeadFaces() => new(
        new(8, 8, 8, 8), new(24, 8, 8, 8), new(16, 8, 8, 8), new(0, 8, 8, 8), new(8, 0, 8, 8), new(16, 0, 8, 8));
    private static SkinFaces HeadOverlayFaces() => new(
        new(40, 8, 8, 8), new(56, 8, 8, 8), new(48, 8, 8, 8), new(32, 8, 8, 8), new(40, 0, 8, 8), new(48, 0, 8, 8));
    private static SkinFaces BodyFaces() => new(
        new(20, 20, 8, 12), new(32, 20, 8, 12), new(28, 20, 4, 12), new(16, 20, 4, 12), new(20, 16, 8, 4), new(28, 16, 8, 4));
    private static SkinFaces BodyOverlayFaces() => new(
        new(20, 36, 8, 12), new(32, 36, 8, 12), new(28, 36, 4, 12), new(16, 36, 4, 12), new(20, 32, 8, 4), new(28, 32, 8, 4));
    private static SkinFaces ArmFaces(bool left, bool slim)
    {
        var x = left ? 32d : 40d;
        var y = left ? 52d : 20d;
        var topY = left ? 48d : 16d;
        var width = slim ? 3d : 4d;
        return new SkinFaces(new(x + 4, y, width, 12), new(x + 8 + width, y, width, 12), new(x + 4 + width, y, 4, 12), new(x, y, 4, 12), new(x + 4, topY, width, 4), new(x + 4 + width, topY, width, 4));
    }
    private static SkinFaces LegFaces(bool left)
    {
        var x = left ? 16d : 0d;
        var y = left ? 52d : 20d;
        var topY = left ? 48d : 16d;
        return new SkinFaces(new(x + 4, y, 4, 12), new(x + 12, y, 4, 12), new(x + 8, y, 4, 12), new(x, y, 4, 12), new(x + 4, topY, 4, 4), new(x + 8, topY, 4, 4));
    }
    private static SkinFaces ArmOverlayFaces(bool left, bool slim)
    {
        var x = left ? 48d : 40d;
        var y = left ? 52d : 36d;
        var topY = left ? 48d : 32d;
        var width = slim ? 3d : 4d;
        return new SkinFaces(new(x + 4, y, width, 12), new(x + 8 + width, y, width, 12), new(x + 4 + width, y, 4, 12), new(x, y, 4, 12), new(x + 4, topY, width, 4), new(x + 4 + width, topY, width, 4));
    }
    private static SkinFaces LegOverlayFaces(bool left)
    {
        var y = left ? 52d : 36d;
        var topY = left ? 48d : 32d;
        return new SkinFaces(new(4, y, 4, 12), new(12, y, 4, 12), new(8, y, 4, 12), new(0, y, 4, 12), new(4, topY, 4, 4), new(8, topY, 4, 4));
    }

    private static void AddSolidCuboid(Model3DGroup group, Point3D center, Vector3D size, Color color)
    {
        var hx = size.X / 2; var hy = size.Y / 2; var hz = size.Z / 2;
        var x0 = center.X - hx; var x1 = center.X + hx;
        var y0 = center.Y - hy; var y1 = center.Y + hy;
        var z0 = center.Z - hz; var z1 = center.Z + hz;
        var mesh = new MeshGeometry3D();
        AppendSolidFace(mesh, [new(x0,y1,z1), new(x1,y1,z1), new(x1,y0,z1), new(x0,y0,z1)]);
        AppendSolidFace(mesh, [new(x1,y1,z0), new(x0,y1,z0), new(x0,y0,z0), new(x1,y0,z0)]);
        AppendSolidFace(mesh, [new(x0,y1,z0), new(x0,y1,z1), new(x0,y0,z1), new(x0,y0,z0)]);
        AppendSolidFace(mesh, [new(x1,y1,z1), new(x1,y1,z0), new(x1,y0,z0), new(x1,y0,z1)]);
        AppendSolidFace(mesh, [new(x0,y1,z0), new(x1,y1,z0), new(x1,y1,z1), new(x0,y1,z1)]);
        AppendSolidFace(mesh, [new(x0,y0,z1), new(x1,y0,z1), new(x1,y0,z0), new(x0,y0,z0)]);
        mesh.Freeze();
        var material = new DiffuseMaterial(new SolidColorBrush(color));
        material.Freeze();
        group.Children.Add(new GeometryModel3D(mesh, material));
    }

    private static void AppendSolidFace(MeshGeometry3D mesh, Point3D[] corners)
    {
        var offset = mesh.Positions.Count;
        foreach (var corner in corners) mesh.Positions.Add(corner);
        mesh.TriangleIndices.Add(offset);
        mesh.TriangleIndices.Add(offset + 3);
        mesh.TriangleIndices.Add(offset + 2);
        mesh.TriangleIndices.Add(offset);
        mesh.TriangleIndices.Add(offset + 2);
        mesh.TriangleIndices.Add(offset + 1);
    }

    private static void AddSkinCuboid(Model3DGroup group, Point3D center, Vector3D size, SkinFaces uv, BitmapSource texture)
    {
        var hx = size.X / 2; var hy = size.Y / 2; var hz = size.Z / 2;
        var x0 = center.X - hx; var x1 = center.X + hx;
        var y0 = center.Y - hy; var y1 = center.Y + hy;
        var z0 = center.Z - hz; var z1 = center.Z + hz;
        AddSkinFace(group, [new(x0,y1,z1), new(x1,y1,z1), new(x1,y0,z1), new(x0,y0,z1)], uv.Front, texture);
        AddSkinFace(group, [new(x1,y1,z0), new(x0,y1,z0), new(x0,y0,z0), new(x1,y0,z0)], uv.Back, texture);
        AddSkinFace(group, [new(x0,y1,z0), new(x0,y1,z1), new(x0,y0,z1), new(x0,y0,z0)], uv.Left, texture);
        AddSkinFace(group, [new(x1,y1,z1), new(x1,y1,z0), new(x1,y0,z0), new(x1,y0,z1)], uv.Right, texture);
        AddSkinFace(group, [new(x0,y1,z0), new(x1,y1,z0), new(x1,y1,z1), new(x0,y1,z1)], uv.Top, texture);
        AddSkinFace(group, [new(x0,y0,z1), new(x1,y0,z1), new(x1,y0,z0), new(x0,y0,z0)], uv.Bottom, texture);
    }

    private static void AddSkinFace(Model3DGroup group, Point3D[] points, SkinUv uv, BitmapSource texture)
    {
        var crop = new CroppedBitmap(texture, new Int32Rect((int)uv.X, (int)uv.Y, (int)uv.Width, (int)uv.Height));
        var pixelated = ExpandPixels(crop, 24);
        var brush = new ImageBrush(pixelated) { Stretch = Stretch.Fill, TileMode = TileMode.None };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(points),
            TriangleIndices = new Int32Collection([0, 3, 2, 0, 2, 1]),
            TextureCoordinates = new PointCollection
            {
                new(0, 0), new(1, 0), new(1, 1), new(0, 1)
            }
        };
        var material = new DiffuseMaterial(brush);
        group.Children.Add(new GeometryModel3D(mesh, material));
    }

    private static CutoutSkinAtlas CreateCutoutSkinAtlas(BitmapSource texture)
    {
        var converted = new FormatConvertedBitmap(texture, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        var alpha = new byte[width * height];
        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = pixels[i * 4 + 3];
            pixels[i * 4 + 3] = 255;
        }
        var opaqueTexture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        opaqueTexture.Freeze();
        var brush = new ImageBrush(opaqueTexture) { Stretch = Stretch.Fill, TileMode = TileMode.None };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        return new CutoutSkinAtlas(alpha, width, height, material);
    }

    private static void AddCutoutSkinCuboid(Model3DGroup group, Point3D center, Vector3D size, SkinFaces uv, CutoutSkinAtlas atlas)
    {
        var hx = size.X / 2; var hy = size.Y / 2; var hz = size.Z / 2;
        var x0 = center.X - hx; var x1 = center.X + hx;
        var y0 = center.Y - hy; var y1 = center.Y + hy;
        var z0 = center.Z - hz; var z1 = center.Z + hz;
        var mesh = new MeshGeometry3D();
        AddCutoutSkinFace(mesh, [new(x0,y1,z1), new(x1,y1,z1), new(x1,y0,z1), new(x0,y0,z1)], uv.Front, atlas);
        AddCutoutSkinFace(mesh, [new(x1,y1,z0), new(x0,y1,z0), new(x0,y0,z0), new(x1,y0,z0)], uv.Back, atlas);
        AddCutoutSkinFace(mesh, [new(x0,y1,z0), new(x0,y1,z1), new(x0,y0,z1), new(x0,y0,z0)], uv.Left, atlas);
        AddCutoutSkinFace(mesh, [new(x1,y1,z1), new(x1,y1,z0), new(x1,y0,z0), new(x1,y0,z1)], uv.Right, atlas);
        AddCutoutSkinFace(mesh, [new(x0,y1,z0), new(x1,y1,z0), new(x1,y1,z1), new(x0,y1,z1)], uv.Top, atlas);
        AddCutoutSkinFace(mesh, [new(x0,y0,z1), new(x1,y0,z1), new(x1,y0,z0), new(x0,y0,z0)], uv.Bottom, atlas);
        if (mesh.TriangleIndices.Count > 0)
        {
            mesh.Freeze();
            group.Children.Add(new GeometryModel3D(mesh, atlas.Material));
        }
    }

    private static void AddCutoutSkinFace(MeshGeometry3D mesh, Point3D[] corners, SkinUv uv, CutoutSkinAtlas atlas)
    {
        var left = (int)uv.X;
        var top = (int)uv.Y;
        var width = (int)uv.Width;
        var height = (int)uv.Height;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pixelX = left + x;
            var pixelY = top + y;
            if (atlas.Alpha[pixelY * atlas.Width + pixelX] < 128) continue;
            var u0 = (double)x / width;
            var u1 = (double)(x + 1) / width;
            var v0 = (double)y / height;
            var v1 = (double)(y + 1) / height;
            var pixelCenter = new Point((pixelX + 0.5) / atlas.Width, (pixelY + 0.5) / atlas.Height);
            var offset = mesh.Positions.Count;
            mesh.Positions.Add(SkinFacePoint(corners, u0, v0));
            mesh.Positions.Add(SkinFacePoint(corners, u1, v0));
            mesh.Positions.Add(SkinFacePoint(corners, u1, v1));
            mesh.Positions.Add(SkinFacePoint(corners, u0, v1));
            for (var i = 0; i < 4; i++) mesh.TextureCoordinates.Add(pixelCenter);
            mesh.TriangleIndices.Add(offset);
            mesh.TriangleIndices.Add(offset + 3);
            mesh.TriangleIndices.Add(offset + 2);
            mesh.TriangleIndices.Add(offset);
            mesh.TriangleIndices.Add(offset + 2);
            mesh.TriangleIndices.Add(offset + 1);
        }
    }

    private static Point3D SkinFacePoint(Point3D[] corners, double u, double v)
    {
        var top = corners[0] + (corners[1] - corners[0]) * u;
        var bottom = corners[3] + (corners[2] - corners[3]) * u;
        return top + (bottom - top) * v;
    }

    private static BitmapSource ExpandPixels(BitmapSource source, int factor)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var inputStride = width * 4;
        var input = new byte[inputStride * height];
        converted.CopyPixels(input, inputStride, 0);
        var outputWidth = width * factor;
        var outputHeight = height * factor;
        var outputStride = outputWidth * 4;
        var output = new byte[outputStride * outputHeight];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sourceOffset = y * inputStride + x * 4;
                for (var dx = 0; dx < factor; dx++)
                    Buffer.BlockCopy(input, sourceOffset, output, y * factor * outputStride + (x * factor + dx) * 4, 4);
            }
            for (var dy = 1; dy < factor; dy++)
                Buffer.BlockCopy(output, y * factor * outputStride, output, (y * factor + dy) * outputStride, outputStride);
        }
        var bitmap = BitmapSource.Create(outputWidth, outputHeight, 96, 96, PixelFormats.Bgra32, null, output, outputStride);
        bitmap.Freeze();
        return bitmap;
    }

    private void SkinViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _skinDragStart = e.GetPosition(SkinViewport);
        SkinViewport.CaptureMouse();
    }

    private void SkinViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_skinDragStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(SkinViewport);
        _skinYaw.Angle += current.X - start.X;
        _skinPitch.Angle = Math.Clamp(_skinPitch.Angle - (current.Y - start.Y) * 0.55, -35, 35);
        _skinDragStart = current;
    }

    private void SkinViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _skinDragStart = null;
        SkinViewport.ReleaseMouseCapture();
    }

    private async void ApplySkin_Click(object sender, RoutedEventArgs e)
    {
        if (_skinPreview is null)
        {
            SkinStatusText.Text = "请先选择 64×64 或 64×32 PNG。";
            return;
        }
        var result = await _viewModel.ApplySkinAsync(_skinPreview);
        SkinStatusText.Text = result.Message;
        SkinApplyActionText.Text = result.Success ? "接口校验完成 · 未真实上传" : "无法应用";
    }

    private void BackToAccounts_Click(object sender, RoutedEventArgs e) => ShowAccountChooser(true);

    private void ShowAccountChooser(bool animate)
    {
        _accountAdding = false;
        _skinEditing = false;
        AccountHeaderAvatar.Visibility = Visibility.Visible;
        AccountBackButton.Visibility = Visibility.Collapsed;
        AccountHeaderTitle.Text = "选择账户";
        AccountHeaderSubtitle.Text = "启动前使用的游戏身份";
        AccountAddPanel.Visibility = Visibility.Collapsed;
        SkinEditorPanel.Visibility = Visibility.Collapsed;
        AccountOptions.Visibility = Visibility.Visible;
        AccountOptions.Opacity = animate ? 0 : 1;
        if (animate) Fade(AccountOptions, 1, 220);
    }

    private void SelectAccountMethod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) SelectAccountMethod(button.Tag?.ToString() ?? "Microsoft");
    }

    private void SelectAccountMethod(string method)
    {
        var microsoft = method == "Microsoft";
        MicrosoftMethodButton.Background = (Brush)FindResource(microsoft ? "SelectionSurface" : "SecondarySurface");
        OfflineMethodButton.Background = (Brush)FindResource(microsoft ? "SecondarySurface" : "SelectionSurface");
        MicrosoftLoginPreview.Visibility = microsoft ? Visibility.Visible : Visibility.Collapsed;
        OfflineLoginPreview.Visibility = microsoft ? Visibility.Collapsed : Visibility.Visible;
        AccountAddActionText.Text = microsoft ? "使用微软账户登录" : "保存离线档案";
    }

    private void OfflineNameInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OfflineNameHint is null) return;
        var name = OfflineNameInput.Text;
        try
        {
            LauncherWorkspace.ValidatePlayerName(name);
            OfflineNameHint.Text = name.Equals("Player", StringComparison.OrdinalIgnoreCase)
                ? "Player 是通用默认名，容易重名；离线档案无法进入要求正版验证的服务器。"
                : "ID 格式正确。离线档案仅保存在本机，无法替代正版登录。";
        }
        catch (InvalidOperationException exception)
        {
            OfflineNameHint.Text = exception.Message;
        }
    }

    private async void AccountAddDemo_Click(object sender, RoutedEventArgs e)
    {
        if (_accountSignInInProgress) return;
        if (MicrosoftLoginPreview.Visibility == Visibility.Visible)
        {
            _accountSignInInProgress = true;
            AccountAddActionButton.IsEnabled = false;
            AccountAddActionText.Text = "等待微软登录…";
            MicrosoftLoginError.Visibility = Visibility.Collapsed;
            try
            {
                var account = await _microsoftAccounts.SignInAsync();
                _workspacePreferences.SelectedMicrosoftUuid = account.Uuid;
                _workspacePreferences.Save();
                RefreshRealAccount();
                AccountAddActionText.Text = "登录成功";
                CloseAccount();
            }
            catch (OperationCanceledException)
            {
                AccountHeaderSubtitle.Text = "已取消登录，随时可以重试";
                AccountAddActionText.Text = "重试微软登录";
            }
            catch (Exception exception)
            {
                AccountHeaderSubtitle.Text = "登录未完成";
                MicrosoftLoginError.Text = _microsoftAccounts.DescribeSignInFailure(exception);
                MicrosoftLoginError.Visibility = Visibility.Visible;
                AccountAddActionText.Text = "重试微软登录";
            }
            finally
            {
                _accountSignInInProgress = false;
                AccountAddActionButton.IsEnabled = true;
            }
            return;
        }
        try
        {
            _workspacePreferences.PlayerName = LauncherWorkspace.ValidatePlayerName(OfflineNameInput.Text);
            _workspacePreferences.SelectedMicrosoftUuid = null;
            _workspacePreferences.Save();
            RefreshRealAccount();
            AccountAddActionText.Text = "离线档案已保存";
            CloseAccount();
        }
        catch (Exception exception)
        {
            AccountAddActionText.Text = exception.Message;
        }
    }

    private void SwitchToOffline_Click(object sender, RoutedEventArgs e)
    {
        _workspacePreferences.SelectedMicrosoftUuid = null;
        _workspacePreferences.Save();
        RefreshRealAccount();
        CloseAccount();
    }

    private void InstanceButton_Click(object sender, RoutedEventArgs e) => OpenInstance();

    private void OpenInstance()
    {
        if (_instanceOpen) return;
        var resumeClosing = InstanceMorphCard.Visibility == Visibility.Visible;
        _instanceTransitionVersion++;
        if (_accountOpen) CloseAccount(false);
        _instanceOpen = true;
        ShowDimmer();
        ShowGameLibrary(false);
        InstanceButton.BeginAnimation(OpacityProperty, null);
        InstanceButton.Opacity = 0;
        InstanceMorphCard.BeginAnimation(OpacityProperty, null);
        InstanceMorphShadow.BeginAnimation(OpacityProperty, null);
        InstanceMorphCard.Opacity = 1;
        InstanceMorphShadow.Opacity = 1;
        var targetY = InstanceMorphTargetY();
        if (!resumeClosing)
        {
            SetMorphState(InstanceCardScale, InstanceCardTranslate, 440d / InstanceMorphCard.Width, 48d / InstanceMorphCard.Height, targetY);
            SetMorphState(InstanceShadowScale, InstanceShadowTranslate, 440d / InstanceMorphShadow.Width, 48d / InstanceMorphShadow.Height, targetY);
            RealLibraryPanel.Opacity = 0;
        }
        InstanceMorphShadow.Visibility = Visibility.Visible;
        InstanceMorphCard.Visibility = Visibility.Visible;
        AnimateMorph(InstanceCardScale, InstanceCardTranslate, 1, 1, 0);
        AnimateMorph(InstanceShadowScale, InstanceShadowTranslate, 1, 1, 0);
        Fade(RealLibraryPanel, 1, 300, 155);
    }

    private void CloseInstance_Click(object sender, RoutedEventArgs e)
    {
        CloseInstance();
        if (NavInstances.IsChecked == true) NavLaunch.IsChecked = true;
    }
    private void ChooseInstance_Click(object sender, RoutedEventArgs e)
    {
        EnvironmentStatus.Text = "已选择游戏  ·  环境已就绪  ·  无需额外设置";
        CloseInstance();
    }

    private void GameCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border card && int.TryParse(card.Tag?.ToString(), out var index))
            SelectGameCard(index, true);
    }

    private void SelectGameCard(int index, bool animate)
    {
        var next = Math.Clamp(index, 0, 2);
        if (animate && next == _selectedGameCard) return;
        _selectedGameCard = next;
        Border[] cards = [GameCard1, GameCard2, GameCard3];
        UIElement[] details = [GameCard1Details, GameCard2Details, GameCard3Details];
        Border[] accents = [GameCard1Accent, GameCard2Accent, GameCard3Accent];
        TextBlock[] states = [GameCard1State, GameCard2State, GameCard3State];
        string[] inactiveStates = ["当前游戏 · 点击展开", "环境已就绪 · 点击展开", "需要修复 1 项 · 点击展开"];
        double[][] tops =
        [
            [0, 214, 272],
            [0, 58, 272],
            [0, 58, 116]
        ];

        for (var i = 0; i < cards.Length; i++)
        {
            var active = i == _selectedGameCard;
            var targetTop = tops[_selectedGameCard][i];
            Panel.SetZIndex(cards[i], active ? 5 : i + 1);
            cards[i].BorderBrush = (Brush)FindResource(active ? "BrandBlue" : "WindowBorder");
            cards[i].BorderThickness = new Thickness(active ? 2 : 1);
            cards[i].Opacity = active ? 1 : 0.90;
            accents[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            states[i].Text = active ? "● 正在查看" : inactiveStates[i];
            states[i].Foreground = (Brush)FindResource(active ? "BrandBlue" : "TextSecondary");
            states[i].FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            if (animate)
            {
                cards[i].BeginAnimation(Canvas.TopProperty, Number(targetTop, 360));
                Fade(details[i], active ? 1 : 0, active ? 260 : 120, active ? 100 : 0);
            }
            else
            {
                cards[i].BeginAnimation(Canvas.TopProperty, null);
                Canvas.SetTop(cards[i], targetTop);
                details[i].Opacity = active ? 1 : 0;
            }
        }
    }

    private void UseGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var game = button.Tag?.ToString() ?? "所选游戏";
        _viewModel.SelectGame(game);
        EnvironmentStatus.Text = $"已选择 {game}  ·  启动前将自动检查环境";
        e.Handled = true;
        CloseInstance();
    }

    private void OpenGameAdd_Click(object sender, RoutedEventArgs e)
    {
        GameBackButton.Visibility = Visibility.Visible;
        GameHeaderText.Margin = new Thickness(48, 0, 0, 0);
        GameHeaderTitle.Text = "添加游戏";
        GameHeaderSubtitle.Text = "选择玩法，版本、加载器与 Java 将自动配置";
        InstanceOptions.Visibility = Visibility.Collapsed;
        GameAddPanel.Visibility = Visibility.Visible;
        GameAddPanel.Opacity = 0;
        SelectGameTemplate("Vanilla");
        Fade(GameAddPanel, 1, 240);
    }

    private void BackToGameLibrary_Click(object sender, RoutedEventArgs e) => ShowGameLibrary(true);

    private void ShowGameLibrary(bool animate)
    {
        GameBackButton.Visibility = Visibility.Collapsed;
        GameHeaderText.Margin = new Thickness(0);
        GameHeaderTitle.Text = "选一个世界";
        GameHeaderSubtitle.Text = "本机游戏就在这里，不用先找文件夹";
        GameAddPanel.Visibility = Visibility.Collapsed;
        InstanceOptions.Visibility = Visibility.Collapsed;
        RealLibraryPanel.Visibility = Visibility.Visible;
        RealLibraryPanel.Opacity = animate ? 0 : 1;
        if (animate) Fade(RealLibraryPanel, 1, 220);
    }

    private void SelectGameTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) SelectGameTemplate(button.Tag?.ToString() ?? "Vanilla");
    }

    private void SelectGameTemplate(string template)
    {
        VanillaTemplateButton.Background = (Brush)FindResource(template == "Vanilla" ? "SelectionSurface" : "SecondarySurface");
        ModpackTemplateButton.Background = (Brush)FindResource(template == "Modpack" ? "SelectionSurface" : "SecondarySurface");
        RecommendedTemplateButton.Background = (Brush)FindResource(template == "Recommended" ? "SelectionSurface" : "SecondarySurface");

        (GameAddHint.Text, GameVersionValue.Text, GameLoaderValue.Text, GameJavaValue.Text) = template switch
        {
            "Modpack" => ("选择整合包文件，之后可识别版本并修复缺失依赖。", "自动识别", "随整合包", "自动匹配"),
            "Recommended" => ("从兼容性已经验证的方案开始，减少第一次启动失败。", "1.21.1", "NeoForge", "Java 21"),
            _ => ("选择 Minecraft 版本，其余环境随后自动处理。", "1.21.1", "自动选择", "自动下载")
        };
        CreateGameActionText.Text = "创建游戏（演示）";
    }

    private void CreateGameDemo_Click(object sender, RoutedEventArgs e)
        => CreateGameActionText.Text = "配置检查完成 · 尚未写入磁盘";

    private void CloseInstance(bool restoreDimmer = true)
    {
        if (!_instanceOpen) return;
        _instanceOpen = false;
        var version = ++_instanceTransitionVersion;
        var targetY = InstanceMorphTargetY();
        Fade(RealLibraryPanel, 0, 120);
        AnimateMorph(InstanceCardScale, InstanceCardTranslate, 440d / InstanceMorphCard.Width, 48d / InstanceMorphCard.Height,
            targetY, 360, () => FinishInstanceReturn(version));
        AnimateMorph(InstanceShadowScale, InstanceShadowTranslate, 440d / InstanceMorphShadow.Width, 48d / InstanceMorphShadow.Height, targetY, 360);
        if (restoreDimmer) HideDimmer();
    }

    private void CloseInstanceToSection()
    {
        if (!_instanceOpen) return;
        _instanceOpen = false;
        var version = ++_instanceTransitionVersion;
        var fade = Number(0, 220);
        fade.Completed += (_, _) =>
        {
            if (version != _instanceTransitionVersion || _instanceOpen) return;
            InstanceMorphCard.Visibility = Visibility.Collapsed;
            InstanceMorphShadow.Visibility = Visibility.Collapsed;
            InstanceButton.BeginAnimation(OpacityProperty, null);
            InstanceButton.Opacity = 1;
            ShowGameLibrary(false);
        };
        InstanceMorphCard.BeginAnimation(OpacityProperty, fade);
        InstanceMorphShadow.BeginAnimation(OpacityProperty, Number(0, 220));
        HideDimmer(restoreHome: false);
    }

    private void FinishInstanceReturn(int version)
    {
        if (version != _instanceTransitionVersion || _instanceOpen) return;
        Fade(InstanceButton, 1, 110);
        Fade(InstanceMorphCard, 0, 110);
        Fade(InstanceMorphShadow, 0, 110);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (version != _instanceTransitionVersion || _instanceOpen) return;
            InstanceMorphCard.Visibility = Visibility.Collapsed;
            InstanceMorphShadow.Visibility = Visibility.Collapsed;
            InstanceButton.BeginAnimation(OpacityProperty, null);
            InstanceButton.Opacity = 1;
            ShowGameLibrary(false);
        };
        timer.Start();
    }

    private double InstanceMorphTargetY()
    {
        if (!InstanceButton.IsLoaded || ContentStage.ActualHeight <= 0) return 0;
        var center = InstanceButton.TranslatePoint(
            new Point(InstanceButton.ActualWidth / 2, InstanceButton.ActualHeight / 2), ContentStage);
        return center.Y - ContentStage.ActualHeight / 2;
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        await LaunchSelectedRealGameAsync();
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T result && result.Name == name) return result;
            var nested = FindVisualChild<T>(child, name);
            if (nested != null) return nested;
        }
        return null;
    }

    private void NavLaunch_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        MoveNavigationIndicator(NavLaunch);
        CloseAllOverlays();
        HideSection();
    }

    private void NavInstances_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        MoveNavigationIndicator(NavInstances);
        HideSection();
        OpenInstance();
    }

    private void NavDownload_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        MoveNavigationIndicator(NavDownload);
        CloseAllOverlays(toSection: true);
        _sectionMode = "download";
        ResetSectionView();
        ConfigureSection("想玩什么？", "安装原版，或导入现有的 PCL / HMCL 游戏目录。",
            "纯净 Minecraft", "获取正式版并安装", "导入游戏目录", "引用已有的 .minecraft", "整合包 ZIP", "尚未支持");
        ShowSection();
    }

    private void NavSettings_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        MoveNavigationIndicator(NavSettings);
        CloseAllOverlays(toSection: true);
        _sectionMode = "settings";
        ResetSectionView();
        ConfigureSection("设置", "普通玩家只看到需要决定的选项，高级参数默认收起。",
            "显示与动画", "跟随系统 · 平滑转场", "存储位置", "空间充足 · 自动管理", "诊断中心", "当前没有需要处理的问题");
        ShowSection();
    }

    private void ConfigureSection(string title, string subtitle, string t1, string i1, string t2, string i2, string t3, string i3)
    {
        SectionTitle.Text = title; SectionSubtitle.Text = subtitle;
        SectionCard1Title.Text = t1; SectionCard1Info.Text = i1;
        SectionCard2Title.Text = t2; SectionCard2Info.Text = i2;
        SectionCard3Title.Text = t3; SectionCard3Info.Text = i3;
        SectionCard1.Cursor = SectionCard2.Cursor = SectionCard3.Cursor = Cursors.Hand;
    }

    private void MoveNavigationIndicator(RadioButton target)
    {
        var destination = target.TranslatePoint(new Point(0, 0), NavHost).X;
        if (!_navigationMotion.IsInitialized || _appearance.ReducedMotion)
        {
            StopNavigationRendering();
            _navigationMotion.SetInstant(destination);
            NavIndicatorTranslate.X = destination;
            return;
        }

        var now = _navigationClock.Elapsed.TotalMilliseconds;
        _navigationMotion.Retarget(destination, now, 270);
        NavIndicatorTranslate.X = _navigationMotion.ValueAt(now);
        if (!_navigationRendering)
        {
            CompositionTarget.Rendering += UpdateNavigationIndicator;
            _navigationRendering = true;
        }
    }

    private void UpdateNavigationIndicator(object? sender, EventArgs e)
    {
        var now = _navigationClock.Elapsed.TotalMilliseconds;
        if (_appearance.ReducedMotion)
        {
            _navigationMotion.SetInstant(_navigationMotion.Target);
            StopNavigationRendering();
        }
        NavIndicatorTranslate.X = _navigationMotion.ValueAt(now);
        if (_navigationMotion.IsCompleteAt(now)) StopNavigationRendering();
    }

    private void StopNavigationRendering()
    {
        if (!_navigationRendering) return;
        CompositionTarget.Rendering -= UpdateNavigationIndicator;
        _navigationRendering = false;
    }

    private async void SectionCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border card || !int.TryParse(card.Tag?.ToString(), out var index)) return;
        if (_sectionMode == "settings")
        {
            OpenSettingsDetail(index);
            return;
        }
        if (index == 1)
        {
            ImportRealDirectory();
            return;
        }
        if (index == 2)
        {
            SectionSubtitle.Text = "整合包 ZIP 导入尚未接入；可先导入已有的 .minecraft 目录。";
            return;
        }
        _downloadKind = (DownloadTargetKind)Math.Clamp(index, 0, 2);
        (SectionTitle.Text, SectionSubtitle.Text, VersionDetailTitle.Text, VersionDetailDescription.Text,
            VersionDetailLoader.Text, VersionDetailJava.Text, VersionDetailSource.Text) = _downloadKind switch
        {
            DownloadTargetKind.Modpack => ("导入整合包", "先形成安装计划，再由下载与修复引擎执行。", "整合包与依赖修复", "选择兼容目标版本；接入文件选择器后可从压缩包自动识别。", "随整合包识别", "自动匹配", "Modrinth / CurseForge"),
            DownloadTargetKind.Recommended => ("推荐方案", "从经过兼容性验证的组合开始。", "推荐玩法方案", "将版本、加载器和 Java 作为一组方案交给下载引擎。", "NeoForge", "Java 21", "官方 + 社区源"),
            _ => ("纯净 Minecraft", "选择正式版，随后下载并检查游戏文件。", "纯净 Minecraft", "使用 Mojang 元数据与游戏资源，缺失的 Java 自动准备。", "原版", "自动匹配", "Mojang 官方")
        };
        VersionDetailSelected.Text = "选择版本";
        DownloadPlanActionText.Text = "安装所选版本";
        RealInstallButton.IsEnabled = false;
        SectionBackButton.Visibility = Visibility.Visible;
        SectionHeaderText.Margin = new Thickness(48, 0, 0, 0);
        SectionCardsPanel.Visibility = Visibility.Collapsed;
        DownloadDetailPanel.Visibility = Visibility.Visible;
        DownloadDetailPanel.Opacity = 0;
        Fade(DownloadDetailPanel, 1, 240);
        await LoadDownloadVersionsAsync();
    }

    private async Task LoadDownloadVersionsAsync()
    {
        var requestVersion = ++_versionCatalogVersion;
        VersionCountText.Text = "正在读取版本…";
        try
        {
            var releases = await _workspace.GetReleasesAsync(_workspacePreferences.GameDirectory, CancellationToken.None);
            var versions = releases.Select(item => new MinecraftVersionEntry(item.Id, "正式版", "Mojang 官方版本目录")).ToArray();
            if (requestVersion != _versionCatalogVersion || DownloadDetailPanel.Visibility != Visibility.Visible) return;
            var preferredVersion = _downloadVersion;
            VersionSearchBox.Text = string.Empty;
            ShowSnapshotsCheckBox.IsChecked = false;
            DownloadVersionList.ItemsSource = versions;
            DownloadVersionList.SelectedItem = versions.FirstOrDefault(item => item.Id == preferredVersion)
                ?? versions.FirstOrDefault();
            ApplyVersionFilter();
            RealInstallButton.IsEnabled = DownloadVersionList.SelectedItem is MinecraftVersionEntry;
        }
        catch (Exception exception)
        {
            VersionCountText.Text = $"版本读取失败：{exception.Message}";
            RealInstallButton.IsEnabled = false;
        }
    }

    private void VersionFilter_Changed(object sender, RoutedEventArgs e) => ApplyVersionFilter();

    private void ApplyVersionFilter()
    {
        if (!IsLoaded || DownloadVersionList is null || VersionSearchBox is null || ShowSnapshotsCheckBox is null) return;
        var view = CollectionViewSource.GetDefaultView(DownloadVersionList.ItemsSource);
        if (view is null) return;
        var search = VersionSearchBox.Text.Trim();
        var showSnapshots = ShowSnapshotsCheckBox.IsChecked == true;
        view.Filter = item => item is MinecraftVersionEntry version
            && (showSnapshots || !version.IsSnapshot)
            && (search.Length == 0 || version.Id.Contains(search, StringComparison.OrdinalIgnoreCase));
        view.Refresh();
        VersionCountText.Text = $"{view.Cast<object>().Count()} 个正式版 · 滚轮浏览";
        if (DownloadVersionList.SelectedItem is null && !view.IsEmpty)
            DownloadVersionList.SelectedItem = view.Cast<object>().First();
    }

    private void DownloadVersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DownloadVersionList.SelectedItem is not MinecraftVersionEntry version) return;
        _downloadVersion = version.Id;
        VersionDetailSelected.Text = version.Id;
        DownloadPlanActionText.Text = "安装所选版本";
        RealInstallButton.IsEnabled = _installCancellation is null;
    }

    private void SectionCard_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border card)
        {
            card.BorderBrush = (Brush)FindResource("BrandBlue");
            card.Opacity = 0.96;
        }
    }

    private void SectionCard_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border card)
        {
            card.BorderBrush = (Brush)FindResource("WindowBorder");
            card.Opacity = 1;
        }
    }

    private void BackToSectionCards_Click(object sender, RoutedEventArgs e)
    {
        ResetSectionView();
        if (_sectionMode == "settings")
            ConfigureSection("设置", "普通玩家只看到需要决定的选项，高级参数默认收起。",
                "显示与动画", "跟随系统 · 平滑转场", "存储位置", "空间充足 · 自动管理", "诊断中心", "当前没有需要处理的问题");
        else
            ConfigureSection("想玩什么？", "安装原版，或导入现有的 PCL / HMCL 游戏目录。",
                "纯净 Minecraft", "获取正式版并安装", "导入游戏目录", "引用已有的 .minecraft", "整合包 ZIP", "尚未支持");
        SectionCardsPanel.Opacity = 0;
        Fade(SectionCardsPanel, 1, 220);
    }

    private void ResetSectionView()
    {
        _versionCatalogVersion++;
        SectionBackButton.Visibility = Visibility.Collapsed;
        SectionHeaderText.Margin = new Thickness(0);
        DownloadDetailPanel.Visibility = Visibility.Collapsed;
        SettingsDetailPanel.Visibility = Visibility.Collapsed;
        SectionCardsPanel.Visibility = Visibility.Visible;
        SectionCardsPanel.Opacity = 1;
    }

    private void OpenSettingsDetail(int index)
    {
        _selectedSettingsCard = Math.Clamp(index, 0, 2);
        SettingsDisplayPanel.Visibility = _selectedSettingsCard == 0 ? Visibility.Visible : Visibility.Collapsed;
        SettingsStoragePanel.Visibility = _selectedSettingsCard == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsDiagnosticPanel.Visibility = _selectedSettingsCard == 2 ? Visibility.Visible : Visibility.Collapsed;
        (SectionTitle.Text, SectionSubtitle.Text, SettingsActionButton.Content) = _selectedSettingsCard switch
        {
            1 => ("存储位置", "选择游戏、运行环境与缓存的存放方式。", "选择其他位置（演示）"),
            2 => ("诊断中心", "用可解释的检查结果减少启动失败。", "开始全面检查"),
            _ => ("显示与动画", "主题与动作反馈立即预览。", "保存显示设置")
        };
        SectionBackButton.Visibility = Visibility.Visible;
        SectionHeaderText.Margin = new Thickness(48, 0, 0, 0);
        SectionCardsPanel.Visibility = Visibility.Collapsed;
        SettingsDetailPanel.Visibility = Visibility.Visible;
        SettingsDetailPanel.Opacity = 0;
        Fade(SettingsDetailPanel, 1, 240);
    }

    private void SelectTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button selected) return;
        var preference = selected.Tag?.ToString() ?? "System";
        if (Application.Current is App app) app.ApplyThemePreference(preference);
        _appearance.ThemePreference = preference;
        RefreshThemeButtons();
        ApplyAppearanceBackground();
        SettingsActionButton.Content = TrySaveAppearance() ? $"已保存：{selected.Content}" : "本次生效 · 无法保存设置";
    }

    private void RefreshThemeButtons()
    {
        foreach (var button in new[] { ThemeSystemButton, ThemeLightButton, ThemeDarkButton })
            button.Background = (Brush)FindResource(button.Tag?.ToString() == _appearance.ThemePreference
                ? "SelectionSurface" : "SoftSurface");
    }

    private bool TrySaveAppearance()
    {
        try { _appearance.Save(); return true; }
        catch (Exception) { return false; }
    }

    private void ApplyAppearanceBackground()
    {
        if (string.IsNullOrWhiteSpace(_appearance.BackgroundPath))
        {
            BackdropImage.Source = _defaultBackdropImage;
            BackdropImage.Opacity = (double)FindResource("BackdropImageOpacity");
            BackdropTint.Opacity = 1;
            SettingsBackgroundName.Text = "默认背景";
            SectionScene.SetResourceReference(Border.BackgroundProperty, "SectionSurface");
            return;
        }
        if (!TrySetBackgroundImage(_appearance.BackgroundPath))
        {
            BackdropImage.Source = _defaultBackdropImage;
            BackdropImage.Opacity = (double)FindResource("BackdropImageOpacity");
            BackdropTint.Opacity = 1;
            SettingsBackgroundName.Text = "背景文件不可读取 · 已回退默认底图";
            SectionScene.SetResourceReference(Border.BackgroundProperty, "SectionSurface");
        }
    }

    private bool TrySetBackgroundImage(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 2560;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            BackdropImage.Source = bitmap;
            BackdropImage.Opacity = 1;
            BackdropTint.Opacity = Math.Clamp(_appearance.BackgroundOverlayOpacity, 0.55, 0.95);
            SettingsBackgroundName.Text = Path.GetFileName(path);
            var dark = (Application.Current as App)?.IsDarkMode == true;
            SectionScene.Background = new SolidColorBrush(dark
                ? Color.FromArgb(200, 22, 38, 56)
                : Color.FromArgb(218, 255, 255, 255));
            return true;
        }
        catch (Exception) { return false; }
    }

    private void ChooseBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择启动器背景图",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!TrySetBackgroundImage(dialog.FileName))
        {
            SettingsBackgroundName.Text = "图片无法读取，保留当前背景";
            return;
        }
        _appearance.BackgroundPath = dialog.FileName;
        SettingsActionButton.Content = TrySaveAppearance() ? "背景图已保存" : "背景已预览 · 无法保存设置";
    }

    private void ResetBackground_Click(object sender, RoutedEventArgs e)
    {
        _appearance.BackgroundPath = null;
        ApplyAppearanceBackground();
        SettingsActionButton.Content = TrySaveAppearance() ? "已恢复默认背景" : "已恢复 · 无法保存设置";
    }

    private void BackgroundOverlaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _appearance is null) return;
        var opacity = Math.Clamp(e.NewValue / 100, 0.55, 0.95);
        _appearance.BackgroundOverlayOpacity = opacity;
        BackgroundOverlayValue.Text = $"{e.NewValue:0}%";
        if (!string.IsNullOrWhiteSpace(_appearance.BackgroundPath)) BackdropTint.Opacity = opacity;
        _appearanceSaveTimer.Stop();
        _appearanceSaveTimer.Start();
    }

    private async void SettingsAction_Click(object sender, RoutedEventArgs e)
    {
        switch (_selectedSettingsCard)
        {
            case 1:
                StoragePathText.Text = "路径选择接口已就绪 · 原型未改动目录";
                SettingsActionButton.Content = "未修改本机文件";
                break;
            case 2:
                DiagnosticStatusText.Text = "正在检查 Java、文件与下载源…";
                SettingsActionButton.IsEnabled = false;
                await Task.Delay(650);
                DiagnosticStatusText.Text = "检查完成 · 当前没有需要处理的问题";
                SettingsActionButton.Content = "重新检查";
                SettingsActionButton.IsEnabled = true;
                break;
            default:
                SettingsActionButton.Content = TrySaveAppearance() ? "背景、主题与动画设置已保存" : "无法保存外观设置";
                break;
        }
    }

    private async void CreateDownloadPlan_Click(object sender, RoutedEventArgs e)
    {
        await InstallSelectedRealGameAsync();
    }

    private void ShowSection()
    {
        _sectionTransitionVersion++;
        var resume = SectionScene.Visibility == Visibility.Visible;
        SectionScene.Visibility = Visibility.Visible;
        if (!resume)
        {
            SectionScene.Opacity = 0;
            SectionScale.ScaleX = SectionScale.ScaleY = 0.96;
            SectionTranslate.X = 48;
        }
        Fade(HomePanel, 0, 220);
        Fade(SectionScene, 1, 360);
        SectionScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, Number(1, 520));
        SectionScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, Number(1, 520));
        SectionTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, Number(0, 520));
    }

    private void HideSection()
    {
        var version = ++_sectionTransitionVersion;
        if (SectionScene.Visibility != Visibility.Visible) { Fade(HomePanel, 1, 260); return; }
        Fade(SectionScene, 0, 220);
        Fade(HomePanel, 1, 360, 80);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (version == _sectionTransitionVersion)
                SectionScene.Visibility = Visibility.Collapsed;
        };
        timer.Start();
    }

    private void CloseAllOverlays(bool toSection = false)
    {
        if (_accountOpen) CloseAccount();
        if (_instanceOpen)
        {
            if (toSection) CloseInstanceToSection();
            else CloseInstance();
        }
    }

    private void Dimmer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_accountOpen) CloseAccount();
        if (_instanceOpen) CloseInstance();
        NavLaunch.IsChecked = true;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
