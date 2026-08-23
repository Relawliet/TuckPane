using TuckPane.Models;
using TuckPane.Services;
using TuckPane.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace TuckPane;

public sealed class AppHost : IDisposable
{
    private readonly StateStore _stateStore = new();
    private readonly DesktopGridService _desktopGrid = new();
    private readonly Dictionary<Guid, MainWindow> _windows = [];
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private TrayIconService? _tray;
    private MainWindow? _expandedWindow;
    private bool _transparencyNoticeShown;
    private bool _gridFallbackNoticeShown;
    private bool _exiting;

    public AppStateV2 State { get; private set; } = new();
    public TransferQueue TransferQueue { get; } = new();
    public ConsoleWindow Console { get; private set; } = null!;
    public IReadOnlyCollection<MainWindow> Windows => _windows.Values;

    public async Task InitializeAsync(bool showConsole)
    {
        AppPaths.EnsureCreated();
        State = await _stateStore.LoadAsync();
        AppStrings.SetLanguage(State.GlobalSettings.Language);
        StartupService.Apply(State.GlobalSettings.StartWithWindows);

        Console = new ConsoleWindow(this);
        Console.Activate();
        Console.InitializeHostWindow();
        _tray = new TrayIconService(Console.Hwnd, () => State.GlobalSettings.StartWithWindows, () => TransferQueue.IsActive, HandleTrayCommand);
        TransferQueue.StateChanged += (_, _) => Console.UpdateTransferState();

        if (NormalizePositionedPlacementsOnStartup()) await SaveStateAsync();
        foreach (OrganizerDefinition organizer in State.Organizers) CreateWindow(organizer);
        Console.RefreshAll();
        if (!showConsole) Console.HideToTray();
    }

    public GlassTheme GetTheme(OrganizerDefinition organizer) => organizer.ThemeOverride ?? State.GlobalSettings.Theme;

    public async Task<OrganizerDefinition> CreateOrganizerAsync(OrganizerDefinition draft, string? storageParentPath = null)
    {
        if (State.Organizers.Count >= 12) throw new InvalidOperationException(AppStrings.Get("MaximumOrganizersError"));
        Guid id = Guid.NewGuid();
        draft.Id = id;
        draft.Name = string.IsNullOrWhiteSpace(draft.Name) ? AppStrings.DefaultOrganizerName : draft.Name.Trim();
        draft.CreatedAtUtc = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(storageParentPath))
        {
            draft.StorageRelativePath = AppPaths.CreateStorageRelativePath(draft.Name, id);
            draft.StorageAbsolutePath = null;
        }
        else
        {
            draft.StorageRelativePath = string.Empty;
            draft.StorageAbsolutePath = AppPaths.CreateStorageAbsolutePath(storageParentPath, draft.Name, id);
        }

        DisplayInfo primary = DisplayPlacementService.GetDisplays().FirstOrDefault(display => display.Monitor.Left == 0 && display.Monitor.Top == 0)
            ?? DisplayPlacementService.GetDisplays().First();
        NativeMethods.RECT bounds;
        if (draft.PlacementMode == OrganizerPlacementMode.Positioned)
        {
            draft.CompactScale = OrganizerLimits.PositionedCompactScale;
            DesktopGridPlacement? placement = FindPositionedPlacement(primary, desiredCenter: null, excludeId: id);
            if (placement is null) throw new InvalidOperationException(AppStrings.Get("NoPrimaryGridError"));
            bounds = placement.Bounds;
        }
        else
        {
            int width = Math.Max(1, (int)Math.Round(OrganizerLimits.CompactWindowWidthDip * draft.CompactScale * primary.Scale));
            int height = Math.Max(1, (int)Math.Round(OrganizerLimits.CompactWindowHeightDip * draft.CompactScale * primary.Scale));
            bounds = DisplayPlacementService.FindAvailableOnPrimary(_windows.Values.Select(window => window.CompactBounds).ToArray(), width, height);
        }
        draft.Position = DisplayPlacementService.Capture(bounds);

        string itemsPath = AppPaths.ResolveStoragePath(draft);
        string? ownedContainer = AppPaths.GetOwnedStorageContainer(draft);
        bool createdContainer = ownedContainer is not null && !Directory.Exists(ownedContainer);
        try
        {
            Directory.CreateDirectory(itemsPath);
            State.Organizers.Add(draft);
            await SaveStateAsync();
            CreateWindow(draft);
            Console.RefreshAll();
            return draft;
        }
        catch
        {
            State.Organizers.RemoveAll(item => item.Id == draft.Id);
            try { await SaveStateAsync(); }
            catch (Exception rollbackError) { AppLogger.Error("无法回滚创建收纳窗的状态。", rollbackError); }
            if (createdContainer && ownedContainer is not null) TryDeleteEmptyCreatedContainer(ownedContainer);
            throw;
        }
    }

    public Task<OrganizerDefinition> DuplicateOrganizerAsync(Guid id)
    {
        OrganizerDefinition source = State.Organizers.First(item => item.Id == id);
        string name = OrganizerInteractionMath.CreateCopyName(
            source.Name,
            State.Organizers.Select(item => item.Name),
            AppStrings.Get("CopyNameSuffix"));
        var draft = OrganizerInteractionMath.CopySettings(source, name);
        string itemsPath = AppPaths.ResolveStoragePath(source);
        string? container = AppPaths.GetOwnedStorageContainer(source);
        string? storageParent = Path.GetDirectoryName(container ?? itemsPath);
        return CreateOrganizerAsync(draft, storageParent);
    }

    public async Task<string?> ToggleOrganizerModeAsync(Guid id)
    {
        OrganizerDefinition current = State.Organizers.First(item => item.Id == id);
        if (_windows.TryGetValue(id, out MainWindow? window) && window.IsExpanded)
        {
            await window.CollapseForPeerAsync();
        }

        OrganizerDefinition edited = OrganizerInteractionMath.CopySettings(current, current.Name);
        edited.Id = current.Id;
        edited.PlacementMode = current.PlacementMode == OrganizerPlacementMode.Floating
            ? OrganizerPlacementMode.Positioned
            : OrganizerPlacementMode.Floating;
        if (edited.PlacementMode == OrganizerPlacementMode.Positioned)
        {
            edited.CompactScale = OrganizerLimits.PositionedCompactScale;
        }

        string? error = ApplyOrganizerRuntime(
            edited,
            OrganizerVisualChange.PlacementMode | OrganizerVisualChange.CompactScale);
        if (error is not null) return error;
        await SaveStateAsync();
        Console.RefreshAll(id);
        return null;
    }

    internal string? ApplyOrganizerRuntime(OrganizerDefinition edited, OrganizerVisualChange changes)
    {
        OrganizerDefinition current = State.Organizers.First(item => item.Id == edited.Id);
        bool layoutChanged = current.Layout.Mode != edited.Layout.Mode ||
            current.Layout.Rows != edited.Layout.Rows ||
            current.Layout.Columns != edited.Layout.Columns;
        OrganizerPlacementMode previousMode = current.PlacementMode;
        double previousCompactScale = current.CompactScale;
        WidgetPosition? previousPosition = current.Position;
        current.Name = string.IsNullOrWhiteSpace(edited.Name) ? current.Name : edited.Name.Trim();
        current.ThemeOverride = edited.ThemeOverride;
        current.PlacementMode = edited.PlacementMode;
        current.Layout = new OrganizerLayout { Mode = edited.Layout.Mode, Rows = edited.Layout.Rows, Columns = edited.Layout.Columns };
        current.CompactScale = edited.CompactScale;
        current.CanvasScale = edited.CanvasScale;
        current.ItemScale = edited.ItemScale;
        current.NameScale = edited.NameScale;
        if (layoutChanged)
        {
            current.ManualCanvasBaseWidthDip = null;
            current.ManualCanvasBaseHeightDip = null;
        }
        StateStore.Normalize(State);
        if (_windows.TryGetValue(current.Id, out MainWindow? window))
        {
            if (previousMode != OrganizerPlacementMode.Positioned && current.PlacementMode == OrganizerPlacementMode.Positioned)
            {
                NativeMethods.RECT currentBounds = window.CompactBounds;
                var center = new NativeMethods.POINT
                {
                    X = currentBounds.Left + currentBounds.Width / 2,
                    Y = currentBounds.Top + currentBounds.Height / 2
                };
                DisplayInfo display = DisplayPlacementService.ForBounds(currentBounds);
                DesktopGridPlacement? placement = FindPositionedPlacement(display, center, current.Id);
                if (placement is null)
                {
                    current.PlacementMode = previousMode;
                    current.CompactScale = previousCompactScale;
                    current.Position = previousPosition;
                    return AppStrings.Get("PositionedRollbackError");
                }
                current.Position = DisplayPlacementService.Capture(placement.Bounds);
                window.ApplyDefinition(changes & ~(OrganizerVisualChange.CompactScale | OrganizerVisualChange.PlacementMode));
                window.MoveToPositionedPlacement(placement.Bounds, placement.CompactScale);
                return null;
            }

            window.ApplyDefinition(changes);
            if (previousMode == OrganizerPlacementMode.Positioned && current.PlacementMode == OrganizerPlacementMode.Floating)
            {
                current.Position = window.AdoptExpandedCenterForFloating() ?? current.Position;
            }
        }
        return null;
    }

    internal DesktopGridPlacement? FindNearestPositionedPlacement(Guid organizerId, NativeMethods.RECT desiredBounds)
    {
        DisplayInfo display = DisplayPlacementService.ForBounds(desiredBounds);
        var center = new NativeMethods.POINT
        {
            X = desiredBounds.Left + desiredBounds.Width / 2,
            Y = desiredBounds.Top + desiredBounds.Height / 2
        };
        return FindPositionedPlacement(display, center, organizerId);
    }

    internal DesktopGridPlacement? FindCurrentPositionedPlacement(Guid organizerId, NativeMethods.RECT currentBounds)
    {
        DisplayInfo display = DisplayPlacementService.ForBounds(currentBounds);
        var center = new NativeMethods.POINT
        {
            X = currentBounds.Left + currentBounds.Width / 2,
            Y = currentBounds.Top + currentBounds.Height / 2
        };
        return FindPositionedPlacement(display, center, organizerId);
    }

    public async Task<TransferOutcome> DeleteOrganizerAsync(Guid id)
    {
        if (TransferQueue.IsActive) return new(string.Empty, null, TransferStatus.Failed, AppStrings.Get("TransferBeforeDelete"));
        OrganizerDefinition definition = State.Organizers.First(item => item.Id == id);
        var storage = new StorageService(
            AppPaths.ResolveStoragePath(definition),
            createIfMissing: false,
            ownedContainerPath: AppPaths.GetOwnedStorageContainer(definition));
        TransferOutcome outcome = await TransferQueue.RunAsync(token => storage.ExportToDesktopAsync(definition.Name, null, token));
        if (outcome.Status != TransferStatus.Moved) return outcome;

        if (_windows.Remove(id, out MainWindow? window))
        {
            if (ReferenceEquals(_expandedWindow, window)) _expandedWindow = null;
            window.ClosePermanently();
        }
        State.Organizers.RemoveAll(item => item.Id == id);
        await SaveStateAsync();
        Console.RefreshAll();
        return outcome;
    }

    public void RecreateStorage(Guid id)
    {
        if (_windows.TryGetValue(id, out MainWindow? window)) window.RecreateStorage();
        Console.RefreshAll(id);
    }

    public async Task SetGlobalThemeAsync(GlassTheme theme)
    {
        if (State.GlobalSettings.Theme == theme)
        {
            Console.RefreshAll();
            return;
        }
        State.GlobalSettings.Theme = theme;
        Console.ApplyTheme();
        await SaveStateAsync();
        foreach ((Guid id, MainWindow window) in _windows)
        {
            if (State.Organizers.First(item => item.Id == id).ThemeOverride is null) window.ApplyInheritedTheme();
        }
        Console.RefreshAll();
    }

    public async Task SetStartupAsync(bool enabled)
    {
        if (State.GlobalSettings.StartWithWindows == enabled) return;
        bool previous = State.GlobalSettings.StartWithWindows;
        StartupService.Apply(enabled);
        State.GlobalSettings.StartWithWindows = enabled;
        try
        {
            await SaveStateAsync();
        }
        catch
        {
            State.GlobalSettings.StartWithWindows = previous;
            StartupService.Apply(previous);
            throw;
        }
    }

    public async Task SetLanguageAsync(AppLanguage language)
    {
        if (!Enum.IsDefined(language)) language = AppLanguage.ChineseSimplified;
        if (State.GlobalSettings.Language == language)
        {
            Console.ApplyLanguage();
            return;
        }
        AppLanguage previous = State.GlobalSettings.Language;
        State.GlobalSettings.Language = language;
        AppStrings.SetLanguage(language);
        try
        {
            await SaveStateAsync();
        }
        catch
        {
            State.GlobalSettings.Language = previous;
            AppStrings.SetLanguage(previous);
            throw;
        }
        Console.ApplyLanguage();
        foreach (MainWindow window in _windows.Values) window.ApplyLanguage();
        _tray?.ApplyLanguage();
        Console.RefreshAll();
    }

    public async Task PrepareToExpandAsync(MainWindow source)
    {
        if (_expandedWindow is not null && !ReferenceEquals(_expandedWindow, source)) await _expandedWindow.CollapseForPeerAsync();
        _expandedWindow = source;
    }

    public void NotifyCollapsed(MainWindow source)
    {
        if (ReferenceEquals(_expandedWindow, source)) _expandedWindow = null;
    }

    public void OpenConsole(Guid? organizerId = null)
    {
        _ = _dispatcher.TryEnqueue(() => Console.ShowAndActivate(organizerId));
    }

    public void Notify(string title, string message, bool warning = false)
    {
        AppLogger.Info($"{title}: {message}");
        _tray?.ShowNotification(title, message, warning);
    }

    public void NotifyTransparencyFallback()
    {
        if (_transparencyNoticeShown) return;
        _transparencyNoticeShown = true;
        Notify("TuckPane", AppStrings.Get("TransparencyNotification"));
        Console.ShowTransparencyNotice();
    }

    private DesktopGridPlacement? FindPositionedPlacement(DisplayInfo display, NativeMethods.POINT? desiredCenter, Guid excludeId)
    {
        DesktopGridSnapshot snapshot = ReadGridSnapshot(display);
        NativeMethods.RECT[] occupied = _windows.Values
            .Where(window => window.OrganizerId != excludeId)
            .Where(window => State.Organizers.First(item => item.Id == window.OrganizerId).PlacementMode == OrganizerPlacementMode.Positioned)
            .Select(window => window.CompactBounds)
            .ToArray();
        return DesktopGridService.Find(snapshot, occupied, desiredCenter);
    }

    private bool NormalizePositionedPlacementsOnStartup()
    {
        IReadOnlyList<DisplayInfo> displays = DisplayPlacementService.GetDisplays();
        DisplayInfo primary = displays.FirstOrDefault(display => display.Monitor.Left == 0 && display.Monitor.Top == 0) ?? displays.First();
        var occupied = new List<NativeMethods.RECT>();
        bool changed = false;
        foreach (OrganizerDefinition organizer in State.Organizers.Where(item => item.PlacementMode == OrganizerPlacementMode.Positioned))
        {
            DisplayInfo display = displays.FirstOrDefault(item => string.Equals(item.Device, organizer.Position?.MonitorDevice, StringComparison.OrdinalIgnoreCase)) ?? primary;
            DesktopGridSnapshot snapshot = ReadGridSnapshot(display);
            double scale = DesktopGridService.CalculatePositionedCompactScale(snapshot);
            (int width, int height, _) = DesktopGridService.CalculatePositionedWindowSize(snapshot, scale);
            NativeMethods.RECT desired = DisplayPlacementService.RestoreToDisplay(organizer.Position, display, width, height);
            var center = new NativeMethods.POINT
            {
                X = desired.Left + desired.Width / 2,
                Y = desired.Top + desired.Height / 2
            };
            DesktopGridPlacement? placement = DesktopGridService.Find(snapshot, occupied, center);
            if (placement is null)
            {
                occupied.Add(desired);
                continue;
            }
            occupied.Add(placement.Bounds);
            if (!RectsEqual(desired, placement.Bounds)) changed = true;
            organizer.Position = DisplayPlacementService.Capture(placement.Bounds);
        }
        return changed;
    }

    private DesktopGridSnapshot ReadGridSnapshot(DisplayInfo display)
    {
        DesktopGridSnapshot snapshot = _desktopGrid.ReadSnapshot(display);
        if (!snapshot.ExplorerPositionsAvailable && !_gridFallbackNoticeShown)
        {
            _gridFallbackNoticeShown = true;
            Notify("TuckPane", AppStrings.Get("GridFallbackMessage"));
        }
        return snapshot;
    }

    private static bool RectsEqual(NativeMethods.RECT first, NativeMethods.RECT second) =>
        first.Left == second.Left && first.Top == second.Top && first.Right == second.Right && first.Bottom == second.Bottom;

    private static void TryDeleteEmptyCreatedContainer(string container)
    {
        try
        {
            string items = Path.Combine(container, "Items");
            if (Directory.Exists(items) && !Directory.EnumerateFileSystemEntries(items).Any()) Directory.Delete(items);
            if (Directory.Exists(container) && !Directory.EnumerateFileSystemEntries(container).Any()) Directory.Delete(container);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"无法回滚空的收纳目录：{container}", ex);
        }
    }

    public Task SaveStateAsync() => _stateStore.SaveAsync(State);

    public async Task ExitAsync()
    {
        if (_exiting) return;
        if (TransferQueue.IsActive && !await Console.ConfirmCancelTransferAndExitAsync()) return;
        _exiting = true;
        TransferQueue.CancelAll();
        await TransferQueue.WaitForIdleAsync();
        foreach (MainWindow window in _windows.Values.ToArray()) window.ClosePermanently();
        _windows.Clear();
        _tray?.Dispose();
        Console.ClosePermanently();
        Application.Current.Exit();
    }

    private void CreateWindow(OrganizerDefinition organizer)
    {
        var window = new MainWindow(this, organizer);
        _windows.Add(organizer.Id, window);
        window.Activate();
    }

    private void HandleTrayCommand(TrayCommand command)
    {
        _ = _dispatcher.TryEnqueue(async () =>
        {
            switch (command)
            {
                case TrayCommand.OpenConsole:
                    OpenConsole();
                    break;
                case TrayCommand.ShowAll:
                    foreach (MainWindow window in _windows.Values) window.SetVisible(true);
                    break;
                case TrayCommand.HideAll:
                    foreach (MainWindow window in _windows.Values) window.SetVisible(false);
                    break;
                case TrayCommand.ToggleStartup:
                    await SetStartupAsync(!State.GlobalSettings.StartWithWindows);
                    Console.RefreshAll();
                    break;
                case TrayCommand.CancelTransfer:
                    TransferQueue.CancelCurrent();
                    break;
                case TrayCommand.Exit:
                    await ExitAsync();
                    break;
            }
        });
    }

    public void Dispose()
    {
        _tray?.Dispose();
        foreach (MainWindow window in _windows.Values) window.ClosePermanently();
    }
}
