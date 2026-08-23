using System.Diagnostics;
using System.Numerics;
using TuckPane.Controls;
using TuckPane.Models;
using TuckPane.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.Graphics;
using WinRT.Interop;

namespace TuckPane;

public sealed partial class ConsoleWindow : Window
{
    private readonly AppHost _host;
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _placementTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _stateSaveTimer;
    private AppWindow? _appWindow;
    private NativeWindowChromeController? _chrome;
    private bool _closingPermanently;
    private bool _componentReady;
    private bool _initialized;
    private bool _loadingEditor;
    private bool _loadingStartup;
    private bool _loadingLanguage;
    private bool _loadingDefaultName;
    private bool _addNameWasEdited;
    private bool _adjustingAddControls;
    private bool _adjustingManageControls;
    private bool _suppressSelection;
    private bool _runtimeApplyScheduled;
    private Guid? _selectedId;
    private OrganizerDefinition? _editing;
    private OrganizerVisualChange _pendingVisualChanges;
    private CancellationTokenSource? _pageTransition;
    private string _defaultAddName = string.Empty;
    private string? _addStorageParentPath;

    public ConsoleWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        RemoveTextBoxUnderline(AddNameBox, ManageNameBox, ManagePathBox);
        _componentReady = true;
        _defaultAddName = AppStrings.DefaultOrganizerName;
        _loadingDefaultName = true;
        AddNameBox.Text = _defaultAddName;
        _loadingDefaultName = false;
        UpdateAddStoragePath();
        ApplyLanguage();
        ConsoleRoot.RequestedTheme = ElementTheme.Light;
        ApplyTheme();
        _placementTimer = DispatcherQueue.CreateTimer();
        _placementTimer.Interval = TimeSpan.FromMilliseconds(450);
        _placementTimer.IsRepeating = false;
        _placementTimer.Tick += async (_, _) => await SavePlacementAsync();
        _stateSaveTimer = DispatcherQueue.CreateTimer();
        _stateSaveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _stateSaveTimer.IsRepeating = false;
        _stateSaveTimer.Tick += StateSaveTimer_Tick;
        RootNavigation.SelectedItem = ManageNavItem;
        AddThemeCombo.SelectedIndex = 0;
    }

    public IntPtr Hwnd { get; private set; }

    public void InitializeHostWindow()
    {
        if (_initialized) return;
        _initialized = true;
        Hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(Hwnd));
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
        }
        _chrome = new NativeWindowChromeController(Hwnd, DispatcherQueue);
        Activated += ConsoleWindow_Activated;
        Closed += ConsoleWindow_Closed;
        ApplyNativeWindowChrome();
        RestorePlacement();
        _appWindow.Changed += AppWindow_Changed;
        _appWindow.Closing += AppWindow_Closing;
    }

    public void ApplyTheme()
    {
        GlassTheme theme = _host.State.GlobalSettings.Theme;
        ConsoleRoot.RequestedTheme = GlassThemePalette.IsDark(theme) ? ElementTheme.Dark : ElementTheme.Light;
        ApplyConsoleSurfacePalette(theme);
        if (GlassThemePalette.IsSolid(theme))
        {
            SystemBackdrop = null;
            ConsoleRoot.Background = new SolidColorBrush(GlassThemePalette.SurfaceColor(theme));
        }
        else
        {
            ConsoleRoot.Background = new SolidColorBrush(ColorHelper.FromArgb(1, 255, 255, 255));
            SystemBackdrop = new NeutralAcrylicBackdrop(theme);
        }
        ApplyNativeWindowChrome(refreshFrame: true);
        UpdateThemeCards(theme);
    }

    public void RefreshAll(Guid? selectId = null)
    {
        UpdateThemeCards(_host.State.GlobalSettings.Theme);
        UpdateStartupToggle();
        CreateOrganizerButton.IsEnabled = _host.State.Organizers.Count < OrganizerLimits.MaximumOrganizers;
        CreateLimitText.Visibility = _host.State.Organizers.Count >= OrganizerLimits.MaximumOrganizers ? Visibility.Visible : Visibility.Collapsed;
        PopulateManageList(selectId ?? _selectedId);
        UpdateTransferState();
        UpdateAddControls();
    }

    public void ApplyLanguage()
    {
        if (!_componentReady) return;
        bool replaceDefaultName = !_addNameWasEdited;
        _defaultAddName = AppStrings.DefaultOrganizerName;
        if (replaceDefaultName)
        {
            _loadingDefaultName = true;
            AddNameBox.Text = _defaultAddName;
            _loadingDefaultName = false;
        }

        Title = AppStrings.Get("AppTitle");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ConsoleMinimizeButton, AppStrings.Get("WindowMinimize"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ConsoleCloseButton, AppStrings.Get("WindowClose"));
        GeneralNavItem.Content = AppStrings.Get("NavGeneral");
        ThemeNavItem.Content = AppStrings.Get("NavTheme");
        AddNavItem.Content = AppStrings.Get("NavAdd");
        ManageNavItem.Content = AppStrings.Get("NavManage");
        MissingStorageInfo.Title = AppStrings.Get("MissingStorage");
        ApplyLocalizedTree(ConsoleRoot);
        ApplyTypography(ConsoleRoot);
        foreach (Control control in new Control[] { GeneralNavItem, ThemeNavItem, AddNavItem, ManageNavItem })
        {
            control.FontFamily = new FontFamily(AppStrings.FontFamily);
            control.CharacterSpacing = AppStrings.CharacterSpacing;
        }
        _loadingLanguage = true;
        LanguageCombo.SelectedIndex = (int)_host.State.GlobalSettings.Language;
        _loadingLanguage = false;
        ConsoleInfoBar.IsOpen = false;
        PopulateManageList(_selectedId);
    }

    public void UpdateTransferState()
    {
        if (DeleteOrganizerButton is not null) DeleteOrganizerButton.IsEnabled = _selectedId is not null && !_host.TransferQueue.IsActive;
    }

    public void ShowTransparencyNotice()
    {
        ConsoleInfoBar.Title = AppStrings.Get("TransparencyTitle");
        ConsoleInfoBar.Message = AppStrings.Get("TransparencyMessage");
        ConsoleInfoBar.Severity = InfoBarSeverity.Informational;
        ConsoleInfoBar.IsOpen = true;
    }

    public void HideToTray()
    {
        FlushPendingManageChanges();
        _appWindow?.Hide();
    }

    private void ConsoleMinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter) presenter.Minimize();
    }

    private void ConsoleCloseButton_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void ConsoleCloseButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ConsoleCloseButton.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 196, 43, 28));
        ConsoleCloseButton.Foreground = new SolidColorBrush(Colors.White);
    }

    private void ConsoleCloseButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        ConsoleCloseButton.Background = new SolidColorBrush(Colors.Transparent);
        ConsoleCloseButton.Foreground = (Brush)ConsoleRoot.Resources["ConsolePrimaryTextBrush"];
    }

    public void ShowAndActivate(Guid? organizerId = null)
    {
        _appWindow?.Show();
        Activate();
        _ = NativeMethods.SetForegroundWindow(Hwnd);
        RootNavigation.SelectedItem = ManageNavItem;
        ShowPage(ManagePage);
        PopulateManageList(organizerId ?? _selectedId);
    }

    public void ClosePermanently()
    {
        _closingPermanently = true;
        Close();
    }

    public async Task<bool> ConfirmCancelTransferAndExitAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = ConsoleRoot.XamlRoot,
            Title = AppStrings.Get("FilesMovingTitle"),
            Content = AppStrings.Get("FilesMovingMessage"),
            PrimaryButtonText = AppStrings.Get("CancelTransferAndExit"),
            CloseButtonText = AppStrings.Get("ContinueWaiting"),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closingPermanently) return;
        args.Cancel = true;
        HideToTray();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            DisplayInfo display = DisplayPlacementService.ForBounds(new NativeMethods.RECT
            {
                Left = sender.Position.X,
                Top = sender.Position.Y,
                Right = sender.Position.X + sender.Size.Width,
                Bottom = sender.Position.Y + sender.Size.Height
            });
            int minimumWidth = (int)Math.Round(860 * display.Scale);
            int minimumHeight = (int)Math.Round(600 * display.Scale);
            if (sender.Size.Width < minimumWidth || sender.Size.Height < minimumHeight)
            {
                sender.Resize(new SizeInt32(Math.Max(minimumWidth, sender.Size.Width), Math.Max(minimumHeight, sender.Size.Height)));
            }
        }
        if (args.DidPositionChange || args.DidSizeChange)
        {
            _placementTimer.Stop();
            _placementTimer.Start();
        }
        ApplyNativeWindowChrome();
    }

    private void ConsoleWindow_Activated(object sender, WindowActivatedEventArgs args) => ApplyNativeWindowChrome();

    private void ConsoleWindow_Closed(object sender, WindowEventArgs args)
    {
        Activated -= ConsoleWindow_Activated;
        Closed -= ConsoleWindow_Closed;
        _chrome?.Dispose();
        _chrome = null;
    }

    private void ApplyNativeWindowChrome(bool refreshFrame = false)
    {
        if (Hwnd == IntPtr.Zero) return;
        _chrome?.Apply(refreshFrame);
        if (_appWindow is null) return;
        _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private static void RemoveTextBoxUnderline(params TextBox[] textBoxes)
    {
        foreach (TextBox textBox in textBoxes)
        {
            textBox.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
            textBox.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
            textBox.Resources["TextControlBorderBrush"] = new SolidColorBrush(Colors.Transparent);
            textBox.Resources["TextControlBorderBrushPointerOver"] = new SolidColorBrush(Colors.Transparent);
            textBox.Resources["TextControlBorderBrushFocused"] = new SolidColorBrush(Colors.Transparent);
        }
    }

    private void ApplyConsoleSurfacePalette(GlassTheme theme)
    {
        Windows.UI.Color pane;
        Windows.UI.Color page;
        Windows.UI.Color card;
        Windows.UI.Color title;
        Windows.UI.Color manageRow;
        Windows.UI.Color manageBorder;
        Windows.UI.Color listItem;
        Windows.UI.Color selectedListItem;
        Windows.UI.Color primaryText;
        Windows.UI.Color secondaryText;
        Windows.UI.Color input;
        Windows.UI.Color sliderThumb;
        Windows.UI.Color sliderActive;
        Windows.UI.Color sliderInactive;
        Windows.UI.Color sliderThumbBorder;
        Windows.UI.Color sliderFocusPrimary;
        Windows.UI.Color sliderFocusSecondary;
        switch (theme)
        {
            case GlassTheme.SolidLight:
                pane = ColorHelper.FromArgb(255, 229, 226, 226);
                page = ColorHelper.FromArgb(255, 222, 220, 220);
                card = ColorHelper.FromArgb(255, 245, 243, 238);
                title = ColorHelper.FromArgb(255, 225, 222, 222);
                manageRow = ColorHelper.FromArgb(255, 245, 243, 238);
                manageBorder = ColorHelper.FromArgb(255, 184, 178, 168);
                listItem = ColorHelper.FromArgb(255, 245, 243, 238);
                selectedListItem = ColorHelper.FromArgb(255, 232, 228, 218);
                primaryText = ColorHelper.FromArgb(255, 31, 31, 31);
                secondaryText = ColorHelper.FromArgb(255, 101, 96, 96);
                input = ColorHelper.FromArgb(255, 250, 248, 242);
                sliderThumb = ColorHelper.FromArgb(255, 250, 248, 242);
                sliderActive = ColorHelper.FromArgb(255, 137, 132, 123);
                sliderInactive = ColorHelper.FromArgb(255, 196, 190, 179);
                sliderThumbBorder = ColorHelper.FromArgb(255, 184, 178, 168);
                sliderFocusPrimary = ColorHelper.FromArgb(255, 97, 95, 91);
                sliderFocusSecondary = ColorHelper.FromArgb(255, 250, 248, 242);
                break;
            case GlassTheme.SolidDark:
                pane = ColorHelper.FromArgb(255, 41, 39, 39);
                page = ColorHelper.FromArgb(255, 36, 35, 35);
                card = ColorHelper.FromArgb(255, 59, 57, 57);
                title = ColorHelper.FromArgb(255, 43, 41, 41);
                manageRow = ColorHelper.FromArgb(255, 71, 68, 68);
                manageBorder = ColorHelper.FromArgb(255, 119, 112, 112);
                listItem = ColorHelper.FromArgb(255, 71, 68, 68);
                selectedListItem = ColorHelper.FromArgb(255, 85, 81, 81);
                primaryText = ColorHelper.FromArgb(255, 245, 245, 245);
                secondaryText = ColorHelper.FromArgb(255, 201, 196, 196);
                input = ColorHelper.FromArgb(255, 79, 76, 76);
                sliderThumb = ColorHelper.FromArgb(255, 242, 240, 236);
                sliderActive = ColorHelper.FromArgb(255, 113, 110, 108);
                sliderInactive = ColorHelper.FromArgb(255, 157, 153, 150);
                sliderThumbBorder = ColorHelper.FromArgb(255, 205, 202, 198);
                sliderFocusPrimary = ColorHelper.FromArgb(255, 242, 240, 236);
                sliderFocusSecondary = ColorHelper.FromArgb(255, 87, 84, 82);
                break;
            case GlassTheme.Gray:
                pane = ColorHelper.FromArgb(36, 255, 255, 255);
                page = ColorHelper.FromArgb(16, 255, 255, 255);
                card = ColorHelper.FromArgb(42, 255, 255, 255);
                title = ColorHelper.FromArgb(22, 255, 255, 255);
                manageRow = card;
                manageBorder = Colors.Transparent;
                listItem = ColorHelper.FromArgb(18, 255, 255, 255);
                selectedListItem = ColorHelper.FromArgb(52, 255, 255, 255);
                primaryText = ColorHelper.FromArgb(255, 245, 245, 245);
                secondaryText = ColorHelper.FromArgb(255, 201, 196, 196);
                input = ColorHelper.FromArgb(48, 255, 255, 255);
                sliderThumb = ColorHelper.FromArgb(255, 244, 243, 241);
                sliderActive = ColorHelper.FromArgb(255, 115, 118, 121);
                sliderInactive = ColorHelper.FromArgb(255, 158, 161, 163);
                sliderThumbBorder = ColorHelper.FromArgb(255, 210, 208, 204);
                sliderFocusPrimary = ColorHelper.FromArgb(255, 244, 243, 241);
                sliderFocusSecondary = ColorHelper.FromArgb(255, 87, 84, 82);
                break;
            default:
                pane = ColorHelper.FromArgb(24, 255, 255, 255);
                page = ColorHelper.FromArgb(10, 255, 255, 255);
                card = ColorHelper.FromArgb(52, 255, 255, 255);
                title = ColorHelper.FromArgb(18, 255, 255, 255);
                manageRow = card;
                manageBorder = Colors.Transparent;
                listItem = ColorHelper.FromArgb(12, 255, 255, 255);
                selectedListItem = ColorHelper.FromArgb(52, 255, 255, 255);
                primaryText = ColorHelper.FromArgb(255, 31, 31, 31);
                secondaryText = ColorHelper.FromArgb(255, 101, 96, 96);
                input = ColorHelper.FromArgb(52, 255, 255, 255);
                sliderThumb = ColorHelper.FromArgb(255, 250, 249, 246);
                sliderActive = ColorHelper.FromArgb(255, 136, 139, 142);
                sliderInactive = ColorHelper.FromArgb(255, 193, 196, 198);
                sliderThumbBorder = ColorHelper.FromArgb(255, 184, 183, 179);
                sliderFocusPrimary = ColorHelper.FromArgb(255, 97, 95, 91);
                sliderFocusSecondary = ColorHelper.FromArgb(255, 250, 249, 246);
                break;
        }

        SetSurfaceBrush("ConsolePaneSurfaceBrush", pane);
        SetSurfaceBrush("NavigationViewDefaultPaneBackground", pane);
        SetSurfaceBrush("ConsolePageSurfaceBrush", page);
        SetSurfaceBrush("ConsoleCardSurfaceBrush", card);
        SetSurfaceBrush("ConsoleTitleBarSurfaceBrush", title);
        SetSurfaceBrush("ConsoleManageRowSurfaceBrush", manageRow);
        SetSurfaceBrush("ConsoleManageRowBorderBrush", manageBorder);
        SetSurfaceBrush("ConsoleListItemSurfaceBrush", listItem);
        SetSurfaceBrush("ConsoleListItemSelectedSurfaceBrush", selectedListItem);
        SetSurfaceBrush("ConsolePrimaryTextBrush", primaryText);
        SetSurfaceBrush("ConsoleSecondaryTextBrush", secondaryText);
        SetSurfaceBrush("ConsoleInputSurfaceBrush", input);
        SetSurfaceBrush("ConsoleSliderActiveBrush", sliderActive);
        SetSurfaceBrush("ConsoleSliderInactiveBrush", sliderInactive);
        SetSurfaceBrush("ConsoleSliderThumbBorderBrush", sliderThumbBorder);
        SetSurfaceBrush("ConsoleSliderFocusPrimaryBrush", sliderFocusPrimary);
        SetSurfaceBrush("ConsoleSliderFocusSecondaryBrush", sliderFocusSecondary);
        SetSurfaceBrush("SliderThumbBackground", sliderThumb);
        SetSurfaceBrush("SliderThumbBackgroundPointerOver", sliderThumb);
        SetSurfaceBrush("SliderThumbBackgroundPressed", sliderThumb);
        SetSurfaceBrush("SliderThumbBackgroundDisabled", ColorHelper.FromArgb(115, sliderThumb.R, sliderThumb.G, sliderThumb.B));
        SetSurfaceBrush("SliderThumbBorderBrush", sliderThumbBorder);
        SetSurfaceBrush("SliderTrackFill", sliderInactive);
        SetSurfaceBrush("SliderTrackFillPointerOver", sliderInactive);
        SetSurfaceBrush("SliderTrackFillPressed", sliderInactive);
        SetSurfaceBrush("SliderTrackFillDisabled", ColorHelper.FromArgb(115, sliderInactive.R, sliderInactive.G, sliderInactive.B));
        SetSurfaceBrush("SliderTrackValueFill", sliderActive);
        SetSurfaceBrush("SliderTrackValueFillPointerOver", sliderActive);
        SetSurfaceBrush("SliderTrackValueFillPressed", sliderActive);
        SetSurfaceBrush("SliderTrackValueFillDisabled", ColorHelper.FromArgb(115, sliderActive.R, sliderActive.G, sliderActive.B));
        if (_componentReady)
        {
            VisitTree(ConsoleRoot, element =>
            {
                if (element is ConsoleSlider slider)
                {
                    slider.Background = GetSurfaceBrush("ConsoleSliderInactiveBrush");
                    slider.Foreground = GetSurfaceBrush("ConsoleSliderActiveBrush");
                    slider.SetThumbPalette(sliderThumb, sliderThumbBorder);
                }
            });
        }
    }

    private void SetSurfaceBrush(string key, Windows.UI.Color color)
    {
        if (ConsoleRoot.Resources[key] is SolidColorBrush brush) brush.Color = color;
    }

    private SolidColorBrush GetSurfaceBrush(string key) => (SolidColorBrush)ConsoleRoot.Resources[key];

    private void RestorePlacement()
    {
        if (_appWindow is null) return;
        ConsolePlacement? saved = _host.State.ConsolePlacement;
        IReadOnlyList<DisplayInfo> displays = DisplayPlacementService.GetDisplays();
        DisplayInfo display = displays.FirstOrDefault(item => string.Equals(item.Device, saved?.MonitorDevice, StringComparison.OrdinalIgnoreCase))
            ?? displays.FirstOrDefault(item => item.Monitor.Left == 0 && item.Monitor.Top == 0)
            ?? displays.First();
        int width = (int)Math.Round(Math.Max(860, saved?.WidthDip ?? 960) * display.Scale);
        int height = (int)Math.Round(Math.Max(600, saved?.HeightDip ?? 680) * display.Scale);
        int x = saved is null ? display.Work.Left + (display.Work.Width - width) / 2 : display.Work.Left + (int)Math.Round(saved.XDip * display.Scale);
        int y = saved is null ? display.Work.Top + (display.Work.Height - height) / 2 : display.Work.Top + (int)Math.Round(saved.YDip * display.Scale);
        NativeMethods.RECT bounds = DisplayPlacementService.Clamp(new NativeMethods.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height }, display.Work);
        _appWindow.MoveAndResize(new RectInt32(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
    }

    private async Task SavePlacementAsync()
    {
        if (_appWindow is null || _closingPermanently) return;
        DisplayInfo display = DisplayPlacementService.ForBounds(new NativeMethods.RECT
        {
            Left = _appWindow.Position.X,
            Top = _appWindow.Position.Y,
            Right = _appWindow.Position.X + _appWindow.Size.Width,
            Bottom = _appWindow.Position.Y + _appWindow.Size.Height
        });
        _host.State.ConsolePlacement = new ConsolePlacement
        {
            MonitorDevice = display.Device,
            XDip = (_appWindow.Position.X - display.Work.Left) / display.Scale,
            YDip = (_appWindow.Position.Y - display.Work.Top) / display.Scale,
            WidthDip = _appWindow.Size.Width / display.Scale,
            HeightDip = _appWindow.Size.Height / display.Scale
        };
        await _host.SaveStateAsync();
    }

    private async void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;
        FrameworkElement page = tag switch
        {
            "general" => GeneralPage,
            "theme" => ThemePage,
            "add" => AddPage,
            _ => ManagePage
        };
        await ShowPageAsync(page);
        if (ReferenceEquals(page, ManagePage)) PopulateManageList(_selectedId);
    }

    private void ShowPage(FrameworkElement page)
    {
        foreach (FrameworkElement candidate in new FrameworkElement[] { GeneralPage, ThemePage, AddPage, ManagePage }) candidate.Visibility = ReferenceEquals(candidate, page) ? Visibility.Visible : Visibility.Collapsed;
        page.Opacity = 1;
        page.Translation = Vector3.Zero;
    }

    private void UpdateStartupToggle()
    {
        if (StartupToggle is null) return;
        _loadingStartup = true;
        StartupToggle.IsOn = _host.State.GlobalSettings.StartWithWindows;
        _loadingStartup = false;
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_componentReady || _loadingStartup) return;
        try
        {
            await _host.SetStartupAsync(StartupToggle.IsOn);
        }
        catch (Exception ex)
        {
            AppLogger.Error("无法更新开机启动设置。", ex);
            UpdateStartupToggle();
            ShowError(AppStrings.Get("StartupErrorTitle"), ex.Message);
        }
    }

    private async void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_componentReady || _loadingLanguage || LanguageCombo.SelectedIndex < 0) return;
        try
        {
            await _host.SetLanguageAsync((AppLanguage)LanguageCombo.SelectedIndex);
        }
        catch (Exception ex)
        {
            _loadingLanguage = true;
            LanguageCombo.SelectedIndex = (int)_host.State.GlobalSettings.Language;
            _loadingLanguage = false;
            ShowError(AppStrings.Get("LanguageErrorTitle"), ex.Message);
        }
    }

    private static void ApplyLocalizedTree(DependencyObject root)
    {
        VisitTree(root, element =>
        {
            if (element is not FrameworkElement { Tag: string tag } || !tag.StartsWith("loc:", StringComparison.Ordinal)) return;
            string value = AppStrings.Get(tag[4..]);
            if (element is TextBlock textBlock) textBlock.Text = value;
            else if (element is ContentControl contentControl) contentControl.Content = value;
        });
    }

    private static void ApplyTypography(DependencyObject root)
    {
        FontFamily family = new(AppStrings.FontFamily);
        VisitTree(root, element =>
        {
            bool localized = element is FrameworkElement { Tag: string tag } && tag.StartsWith("loc:", StringComparison.Ordinal);
            if (localized && element is TextBlock text)
            {
                text.FontFamily = family;
                text.CharacterSpacing = AppStrings.CharacterSpacing;
            }
            else if ((localized || element is ComboBox) && element is Control control and not TextBox)
            {
                control.FontFamily = family;
                control.CharacterSpacing = AppStrings.CharacterSpacing;
            }
        });
    }

    private static void VisitTree(DependencyObject root, Action<DependencyObject> visitor)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        Visit(root);
        void Visit(DependencyObject current)
        {
            if (!visited.Add(current)) return;
            visitor(current);
            if (current is ItemsControl items)
            {
                foreach (object item in items.Items)
                    if (item is DependencyObject dependencyObject) Visit(dependencyObject);
            }
            if (current is ContentControl { Content: DependencyObject content }) Visit(content);
            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int index = 0; index < count; index++) Visit(VisualTreeHelper.GetChild(current, index));
        }
    }

    private async Task ShowPageAsync(FrameworkElement page)
    {
        FlushPendingManageChanges();
        _pageTransition?.Cancel();
        _pageTransition?.Dispose();
        _pageTransition = new CancellationTokenSource();
        CancellationToken token = _pageTransition.Token;
        ShowPage(page);
        if (!_uiSettings.AnimationsEnabled) return;
        page.Opacity = .01;
        page.Translation = new Vector3(0, 6, 0);
        long started = Stopwatch.GetTimestamp();
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                double raw = Math.Clamp(Stopwatch.GetElapsedTime(started).TotalMilliseconds / 180, 0, 1);
                double eased = 1 - Math.Pow(1 - raw, 4);
                page.Opacity = Math.Max(.01, eased);
                page.Translation = new Vector3(0, (float)(6 * (1 - eased)), 0);
                if (raw >= 1) break;
                await Task.Delay(16, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async void LightThemeCard_Click(object sender, RoutedEventArgs e) => await _host.SetGlobalThemeAsync(GlassTheme.Light);
    private async void GrayThemeCard_Click(object sender, RoutedEventArgs e) => await _host.SetGlobalThemeAsync(GlassTheme.Gray);
    private async void SolidLightThemeCard_Click(object sender, RoutedEventArgs e) => await _host.SetGlobalThemeAsync(GlassTheme.SolidLight);
    private async void SolidDarkThemeCard_Click(object sender, RoutedEventArgs e) => await _host.SetGlobalThemeAsync(GlassTheme.SolidDark);

    private void UpdateThemeCards(GlassTheme theme)
    {
        LightThemeCard.IsChecked = theme == GlassTheme.Light;
        GrayThemeCard.IsChecked = theme == GlassTheme.Gray;
        SolidLightThemeCard.IsChecked = theme == GlassTheme.SolidLight;
        SolidDarkThemeCard.IsChecked = theme == GlassTheme.SolidDark;
    }

    private void AddControl_Changed(object sender, object e)
    {
        if (_componentReady) UpdateAddControls();
    }

    private void AddNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_componentReady && !_loadingDefaultName) _addNameWasEdited = true;
        AddControl_Changed(sender, e);
    }

    private async void ChooseAddStorageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_appWindow is null) return;
        try
        {
            var picker = new FolderPicker(_appWindow.Id)
            {
                Title = AppStrings.Get("SelectStorageFolderTitle"),
                CommitButtonText = AppStrings.Get("SelectStorageFolderCommit"),
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            string suggested = _addStorageParentPath ?? AppPaths.WindowsRoot;
            if (Directory.Exists(suggested)) picker.SuggestedStartFolder = suggested;
            PickFolderResult? result = await picker.PickSingleFolderAsync();
            if (result is null || string.IsNullOrWhiteSpace(result.Path)) return;
            _addStorageParentPath = Path.GetFullPath(result.Path);
            UpdateAddStoragePath();
        }
        catch (Exception ex)
        {
            AppLogger.Error("无法选择收纳窗保存位置。", ex);
            ShowError(AppStrings.Get("StorageFolderPickerError"), ex.Message);
        }
    }

    private void ResetAddStorageButton_Click(object sender, RoutedEventArgs e)
    {
        _addStorageParentPath = null;
        UpdateAddStoragePath();
    }

    private void UpdateAddStoragePath()
    {
        if (AddStoragePathBox is not null) AddStoragePathBox.Text = _addStorageParentPath ?? AppPaths.WindowsRoot;
    }

    private void UpdateAddControls()
    {
        if (!_componentReady || AddRowsCard is null || _adjustingAddControls) return;
        _adjustingAddControls = true;
        bool positioned = AddPlacementModeCombo.SelectedIndex == (int)OrganizerPlacementMode.Positioned;
        AddCompactScaleCard.Visibility = positioned ? Visibility.Collapsed : Visibility.Visible;
        if (positioned) AddCompactScaleSlider.Value = OrganizerLimits.PositionedCompactScale;
        (int rows, int columns) = ReadGridDimensions(AddRowsSlider, AddColumnsSlider);
        var layout = new OrganizerLayout
        {
            Mode = OrganizerLayoutMode.Grid,
            Rows = rows,
            Columns = columns
        };
        DisplayInfo display = GetPrimaryDisplay();
        AddCanvasScaleSlider.Minimum = DisplayPlacementService.CalculateMinimumCanvasScale(display, layout);
        if (AddCanvasScaleSlider.Value < AddCanvasScaleSlider.Minimum) AddCanvasScaleSlider.Value = AddCanvasScaleSlider.Minimum;
        AddItemScaleSlider.Maximum = DisplayPlacementService.CalculateMaximumItemScale(display, layout, AddCanvasScaleSlider.Value);
        if (AddItemScaleSlider.Value > AddItemScaleSlider.Maximum) AddItemScaleSlider.Value = AddItemScaleSlider.Maximum;
        SetPercent(AddCompactPercent, AddCompactScaleSlider.Value);
        SetPercent(AddCanvasPercent, AddCanvasScaleSlider.Value);
        SetPercent(AddItemPercent, AddItemScaleSlider.Value);
        SetPercent(AddNamePercent, AddNameScaleSlider.Value);
        _adjustingAddControls = false;
    }

    private async void CreateOrganizerButton_Click(object sender, RoutedEventArgs e)
    {
        (int rows, int columns) = ReadGridDimensions(AddRowsSlider, AddColumnsSlider);
        var definition = new OrganizerDefinition
        {
            Name = string.IsNullOrWhiteSpace(AddNameBox.Text) ? AppStrings.DefaultOrganizerName : AddNameBox.Text.Trim(),
            ThemeOverride = ThemeFromCombo(AddThemeCombo.SelectedIndex),
            PlacementMode = AddPlacementModeCombo.SelectedIndex == (int)OrganizerPlacementMode.Positioned
                ? OrganizerPlacementMode.Positioned
                : OrganizerPlacementMode.Floating,
            Layout = new OrganizerLayout { Mode = OrganizerLayoutMode.Grid, Rows = rows, Columns = columns },
            CompactScale = AddPlacementModeCombo.SelectedIndex == (int)OrganizerPlacementMode.Positioned
                ? OrganizerLimits.PositionedCompactScale
                : AddCompactScaleSlider.Value,
            CanvasScale = AddCanvasScaleSlider.Value,
            ItemScale = AddItemScaleSlider.Value,
            NameScale = AddNameScaleSlider.Value
        };
        try
        {
            OrganizerDefinition created = await _host.CreateOrganizerAsync(definition, _addStorageParentPath);
            RootNavigation.SelectedItem = ManageNavItem;
            ShowAndActivate(created.Id);
        }
        catch (Exception ex)
        {
            ShowError(AppStrings.Get("CreateErrorTitle"), ex.Message);
        }
    }

    private void PopulateManageList(Guid? selectId)
    {
        if (ManageList is null) return;
        _suppressSelection = true;
        IEnumerable<OrganizerDefinition> definitions = _host.State.Organizers
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id);
        ManageList.Items.Clear();
        foreach (OrganizerDefinition definition in definitions)
        {
            MainWindow? window = _host.Windows.FirstOrDefault(item => item.OrganizerId == definition.Id);
            string layout = AppStrings.Format("GridLayoutFormat", definition.Layout.Columns, definition.Layout.Rows);
            var panel = new StackPanel { Spacing = 3 };
            panel.Children.Add(new TextBlock { Text = definition.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = GetSurfaceBrush("ConsolePrimaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(new TextBlock { Text = AppStrings.Format("ManageItemSummaryFormat", layout, AppStrings.FormatItemCount(window?.FileCount ?? 0), AppStrings.FormatDate(definition.CreatedAtUtc)), FontFamily = new FontFamily(AppStrings.FontFamily), CharacterSpacing = AppStrings.CharacterSpacing, FontSize = 12, Foreground = GetSurfaceBrush("ConsoleSecondaryTextBrush") });
            var content = new Grid { ColumnSpacing = 7 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var indicator = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = GetSurfaceBrush("ConsoleSelectionAccentBrush"), Visibility = Visibility.Collapsed };
            Grid.SetColumn(panel, 1);
            content.Children.Add(indicator);
            content.Children.Add(panel);
            ApplyTypography(content);
            var item = new ListViewItem { Tag = definition.Id, Content = content, Padding = new Thickness(7, 8, 10, 8), Background = GetSurfaceBrush("ConsoleListItemSurfaceBrush") };
            ManageList.Items.Add(item);
            if (definition.Id == selectId) ManageList.SelectedItem = item;
        }
        ManageEmptyState.Visibility = ManageList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ManageEditor.Visibility = ManageList.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ManageDetailCard.Visibility = ManageList.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (ManageList.SelectedItem is null && ManageList.Items.Count > 0) ManageList.SelectedIndex = 0;
        UpdateManageListItemSurfaces();
        _suppressSelection = false;
        if (ManageList.SelectedItem is ListViewItem { Tag: Guid id }) LoadManageEditor(id);
    }

    private void ManageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateManageListItemSurfaces();
        if (_suppressSelection || ManageList.SelectedItem is not ListViewItem { Tag: Guid nextId } || nextId == _selectedId) return;
        FlushPendingManageChanges();
        LoadManageEditor(nextId);
    }

    private void UpdateManageListItemSurfaces()
    {
        if (ManageList is null) return;
        SolidColorBrush normal = GetSurfaceBrush("ConsoleListItemSurfaceBrush");
        SolidColorBrush selected = GetSurfaceBrush("ConsoleListItemSelectedSurfaceBrush");
        foreach (ListViewItem item in ManageList.Items.OfType<ListViewItem>())
        {
            bool isSelected = ReferenceEquals(item, ManageList.SelectedItem);
            item.Background = isSelected ? selected : normal;
            if (item.Content is Grid content && content.Children.OfType<Border>().FirstOrDefault() is { } indicator)
            {
                indicator.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void LoadManageEditor(Guid id)
    {
        OrganizerDefinition source = _host.State.Organizers.First(item => item.Id == id);
        _selectedId = id;
        _editing = Clone(source);
        _loadingEditor = true;
        ManageNameBox.Text = source.Name;
        ManagePlacementModeCombo.SelectedIndex = (int)source.PlacementMode;
        ManagePositionLockToggle.IsOn = source.PositionLocked;
        ManageRowsSlider.Value = source.Layout.Rows;
        ManageColumnsSlider.Value = source.Layout.Columns;
        ManageThemeCombo.SelectedIndex = ComboFromTheme(source.ThemeOverride);
        ManageCompactScaleSlider.Value = source.CompactScale;
        ManageCanvasScaleSlider.Value = source.CanvasScale;
        ManageItemScaleSlider.Value = source.ItemScale;
        ManageNameScaleSlider.Value = source.NameScale;
        string path = AppPaths.ResolveStoragePath(source);
        ManagePathBox.Text = path;
        bool missing = !Directory.Exists(path);
        MissingStorageInfo.IsOpen = missing;
        RecreateStorageButton.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        UpdateManageControls();
        _loadingEditor = false;
        UpdateTransferState();
    }

    private void ManageEditor_Changed(object sender, object e)
    {
        if (_loadingEditor || _adjustingManageControls || _editing is null) return;
        UpdateManageControls();
        ManageNameError.Visibility = string.IsNullOrWhiteSpace(ManageNameBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ScheduleRuntimeApply(GetVisualChange(sender));
        _stateSaveTimer.Stop();
        _stateSaveTimer.Start();
    }

    private void UpdateManageControls()
    {
        if (ManageRowsCard is null || _adjustingManageControls) return;
        _adjustingManageControls = true;
        bool positioned = ManagePlacementModeCombo.SelectedIndex == (int)OrganizerPlacementMode.Positioned;
        ManagePositionLockCard.Visibility = positioned ? Visibility.Visible : Visibility.Collapsed;
        ManageCompactScaleCard.Visibility = positioned ? Visibility.Collapsed : Visibility.Visible;
        if (positioned) ManageCompactScaleSlider.Value = OrganizerLimits.PositionedCompactScale;
        (int rows, int columns) = ReadGridDimensions(ManageRowsSlider, ManageColumnsSlider);
        var layout = new OrganizerLayout { Mode = OrganizerLayoutMode.Grid, Rows = rows, Columns = columns };
        MainWindow? window = _selectedId is Guid id ? _host.Windows.FirstOrDefault(item => item.OrganizerId == id) : null;
        DisplayInfo display = window is null ? GetPrimaryDisplay() : DisplayPlacementService.ForBounds(window.CompactBounds);
        ManageCanvasScaleSlider.Minimum = DisplayPlacementService.CalculateMinimumCanvasScale(display, layout);
        if (ManageCanvasScaleSlider.Value < ManageCanvasScaleSlider.Minimum) ManageCanvasScaleSlider.Value = ManageCanvasScaleSlider.Minimum;
        ManageItemScaleSlider.Maximum = DisplayPlacementService.CalculateMaximumItemScale(display, layout, ManageCanvasScaleSlider.Value);
        if (ManageItemScaleSlider.Value > ManageItemScaleSlider.Maximum) ManageItemScaleSlider.Value = ManageItemScaleSlider.Maximum;
        SetPercent(ManageCompactPercent, ManageCompactScaleSlider.Value);
        SetPercent(ManageCanvasPercent, ManageCanvasScaleSlider.Value);
        SetPercent(ManageItemPercent, ManageItemScaleSlider.Value);
        SetPercent(ManageNamePercent, ManageNameScaleSlider.Value);
        _adjustingManageControls = false;
    }

    private OrganizerDefinition? CaptureManageDraft()
    {
        if (_editing is null) return null;
        if (!string.IsNullOrWhiteSpace(ManageNameBox.Text)) _editing.Name = ManageNameBox.Text.Trim();
        _editing.PlacementMode = ManagePlacementModeCombo.SelectedIndex == (int)OrganizerPlacementMode.Positioned
            ? OrganizerPlacementMode.Positioned
            : OrganizerPlacementMode.Floating;
        _editing.PositionLocked = ManagePositionLockToggle.IsOn;
        _editing.Layout.Mode = OrganizerLayoutMode.Grid;
        (_editing.Layout.Rows, _editing.Layout.Columns) = ReadGridDimensions(ManageRowsSlider, ManageColumnsSlider);
        _editing.ThemeOverride = ThemeFromCombo(ManageThemeCombo.SelectedIndex);
        _editing.CompactScale = ManageCompactScaleSlider.Value;
        _editing.CanvasScale = ManageCanvasScaleSlider.Value;
        _editing.ItemScale = ManageItemScaleSlider.Value;
        _editing.NameScale = ManageNameScaleSlider.Value;
        return Clone(_editing);
    }

    private void ScheduleRuntimeApply(OrganizerVisualChange changes)
    {
        _pendingVisualChanges |= changes;
        if (_runtimeApplyScheduled) return;
        _runtimeApplyScheduled = true;
        CompositionTarget.Rendering += ApplyPendingRuntimeChanges;
    }

    private void ApplyPendingRuntimeChanges(object? sender, object args)
    {
        if (_runtimeApplyScheduled) CompositionTarget.Rendering -= ApplyPendingRuntimeChanges;
        _runtimeApplyScheduled = false;
        OrganizerVisualChange changes = _pendingVisualChanges;
        _pendingVisualChanges = OrganizerVisualChange.None;
        if (changes != OrganizerVisualChange.None && CaptureManageDraft() is { } draft)
        {
            string? error = _host.ApplyOrganizerRuntime(draft, changes);
            if (error is not null)
            {
                ShowError(AppStrings.Get("PositionedModeErrorTitle"), error);
                LoadManageEditor(draft.Id);
            }
        }
    }

    private async void StateSaveTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_runtimeApplyScheduled) ApplyPendingRuntimeChanges(null, args);
        await _host.SaveStateAsync();
    }

    private void FlushPendingManageChanges()
    {
        bool shouldSave = _runtimeApplyScheduled || _stateSaveTimer.IsRunning;
        _stateSaveTimer.Stop();
        if (_runtimeApplyScheduled) ApplyPendingRuntimeChanges(null, EventArgs.Empty);
        if (shouldSave) _ = _host.SaveStateAsync();
    }

    private async void DeleteOrganizerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId is not Guid id) return;
        OrganizerDefinition definition = _host.State.Organizers.First(item => item.Id == id);
        MainWindow? window = _host.Windows.FirstOrDefault(item => item.OrganizerId == id);
        var dialog = new ContentDialog
        {
            XamlRoot = ConsoleRoot.XamlRoot,
            Title = AppStrings.Format("DeleteTitleFormat", definition.Name),
            Content = window?.FileCount > 0
                ? AppStrings.Format("DeleteNonEmptyFormat", AppStrings.FormatItemCount(window.FileCount), definition.Name)
                : AppStrings.Get("DeleteEmpty"),
            PrimaryButtonText = AppStrings.Get("ExportDelete"),
            CloseButtonText = AppStrings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        TransferOutcome outcome = await _host.DeleteOrganizerAsync(id);
        if (outcome.Status != TransferStatus.Moved) ShowError(AppStrings.Get("DeleteErrorTitle"), outcome.Message);
    }

    private void RecreateStorageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId is not Guid id) return;
        try { _host.RecreateStorage(id); }
        catch (Exception ex)
        {
            AppLogger.Error("无法重建收纳目录。", ex);
            ShowError(AppStrings.Get("RecreateStorageErrorTitle"), ex.Message);
        }
    }

    private void OpenManagedFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ManagePathBox.Text)) return;
        if (!Directory.Exists(ManagePathBox.Text))
        {
            ShowError(AppStrings.Get("OpenFolderErrorTitle"), AppStrings.Get("MissingStorageMessage"));
            return;
        }
        Process.Start(new ProcessStartInfo(ManagePathBox.Text) { UseShellExecute = true });
    }

    private void EmptyAddButton_Click(object sender, RoutedEventArgs e) => RootNavigation.SelectedItem = AddNavItem;

    private void ShowError(string title, string message)
    {
        ConsoleInfoBar.Title = title;
        ConsoleInfoBar.Message = message;
        ConsoleInfoBar.Severity = InfoBarSeverity.Error;
        ConsoleInfoBar.IsOpen = true;
    }

    private OrganizerVisualChange GetVisualChange(object sender)
    {
        if (ReferenceEquals(sender, ManageNameBox)) return OrganizerVisualChange.Name;
        if (ReferenceEquals(sender, ManagePlacementModeCombo)) return OrganizerVisualChange.PlacementMode | OrganizerVisualChange.CompactScale;
        if (ReferenceEquals(sender, ManagePositionLockToggle)) return OrganizerVisualChange.PositionLock;
        if (ReferenceEquals(sender, ManageThemeCombo)) return OrganizerVisualChange.Theme;
        if (ReferenceEquals(sender, ManageCompactScaleSlider)) return OrganizerVisualChange.CompactScale;
        if (ReferenceEquals(sender, ManageCanvasScaleSlider)) return OrganizerVisualChange.CanvasScale | OrganizerVisualChange.ItemScale;
        if (ReferenceEquals(sender, ManageItemScaleSlider)) return OrganizerVisualChange.ItemScale;
        if (ReferenceEquals(sender, ManageNameScaleSlider)) return OrganizerVisualChange.NameScale | OrganizerVisualChange.CompactScale;
        return OrganizerVisualChange.Layout | OrganizerVisualChange.ItemScale | OrganizerVisualChange.CanvasScale;
    }

    private static DisplayInfo GetPrimaryDisplay() => DisplayPlacementService.GetDisplays()
        .FirstOrDefault(display => display.Monitor.Left == 0 && display.Monitor.Top == 0)
        ?? DisplayPlacementService.GetDisplays().First();

    private static void SetPercent(TextBlock target, double value) => target.Text = $"{Math.Round(value * 100):0}%";

    private static (int Rows, int Columns) ReadGridDimensions(Slider rows, Slider columns) => (
        Math.Clamp((int)Math.Round(rows.Value), OrganizerLimits.MinimumGridDimension, OrganizerLimits.MaximumLayoutDimension),
        Math.Clamp((int)Math.Round(columns.Value), OrganizerLimits.MinimumGridDimension, OrganizerLimits.MaximumLayoutDimension));

    private static GlassTheme? ThemeFromCombo(int selectedIndex) => selectedIndex switch
    {
        1 => GlassTheme.Light,
        2 => GlassTheme.Gray,
        3 => GlassTheme.SolidLight,
        4 => GlassTheme.SolidDark,
        _ => null
    };

    private static int ComboFromTheme(GlassTheme? theme) => theme switch
    {
        GlassTheme.Light => 1,
        GlassTheme.Gray => 2,
        GlassTheme.SolidLight => 3,
        GlassTheme.SolidDark => 4,
        _ => 0
    };

    private static OrganizerDefinition Clone(OrganizerDefinition source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        CreatedAtUtc = source.CreatedAtUtc,
        ThemeOverride = source.ThemeOverride,
        PlacementMode = source.PlacementMode,
        PositionLocked = source.PositionLocked,
        Layout = new OrganizerLayout { Mode = source.Layout.Mode, Rows = source.Layout.Rows, Columns = source.Layout.Columns },
        CompactScale = source.CompactScale,
        CanvasScale = source.CanvasScale,
        ItemScale = source.ItemScale,
        NameScale = source.NameScale,
        Position = source.Position,
        StorageRelativePath = source.StorageRelativePath,
        StorageAbsolutePath = source.StorageAbsolutePath,
        ItemOrder = source.ItemOrder.ToList()
    };
}
