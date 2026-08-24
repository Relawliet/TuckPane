using TuckPane.Core;
using TuckPane.Models;
using TuckPane.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string logicRoot = Path.Combine(Path.GetTempPath(), $"TuckPane-logic-{Guid.NewGuid():N}");
Environment.SetEnvironmentVariable("TUCKPANE_TEST_ROOT", logicRoot);
Directory.CreateDirectory(logicRoot);
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    try { if (Directory.Exists(logicRoot)) Directory.Delete(logicRoot, recursive: true); }
    catch { }
};

Check(new AppStateV2().SchemaVersion == 3, "新状态版本不是 3。");
Check(new GlobalSettings().Language == AppLanguage.English, "新配置没有默认使用英文。");
Check(!new GlobalSettings().CollapseOnOutsideClick, "窗口外点击收缩没有默认关闭。");

string migrationRoot = Path.Combine(logicRoot, "Migration");
Directory.CreateDirectory(migrationRoot);
try
{
    string statePath = Path.Combine(migrationRoot, "state.json");
    await File.WriteAllTextAsync(statePath, """
        {
          "SchemaVersion": 2,
          "GlobalSettings": { "Theme": 0, "StartWithWindows": false, "Language": 2 },
          "Organizers": []
        }
        """);
    var migrationStore = new StateStore(statePath);
    AppStateV2 migrated = await migrationStore.LoadAsync();
    Check(migrated.SchemaVersion == 3 && migrated.GlobalSettings.Language == AppLanguage.English,
        "旧状态没有一次性迁移到英文。");

    migrated.GlobalSettings.Language = AppLanguage.Japanese;
    await migrationStore.SaveAsync(migrated);
    AppStateV2 reloaded = await migrationStore.LoadAsync();
    Check(reloaded.GlobalSettings.Language == AppLanguage.Japanese,
        "版本 3 没有保留用户重新选择的语言。");
}
finally
{
    Directory.Delete(migrationRoot, recursive: true);
}

string newStoragePath = AppPaths.CreateStorageRelativePath(
    "Storage",
    Guid.Parse("22222222-2222-2222-2222-222222222222"));
Check(!Path.GetFileName(newStoragePath).Equals("Items", StringComparison.OrdinalIgnoreCase),
    "新建默认目录仍包含末尾 Items 层。");

string customStorage = Path.Combine(logicRoot, "SelectedStorage");
Directory.CreateDirectory(customStorage);
await File.WriteAllTextAsync(Path.Combine(customStorage, "existing.txt"), "existing");
Check(AppPaths.ValidateCustomStoragePath(customStorage) == Path.GetFullPath(customStorage),
    "手选目录没有被直接作为最终存储目录。");
Check(new StorageService(customStorage, createIfMissing: false).ReadItems().Count == 1,
    "手选目录的已有顶层内容没有直接显示。");
Check(AppPaths.PathsOverlap(customStorage, Path.Combine(customStorage, "Child")),
    "父子收纳目录重叠没有被识别。");
Check(!AppPaths.PathsOverlap(customStorage, Path.Combine(logicRoot, "Sibling")),
    "无关目录被错误判定为重叠。");
bool rejectedProtectedPath = false;
try { _ = AppPaths.ValidateCustomStoragePath(logicRoot); }
catch (InvalidOperationException) { rejectedProtectedPath = true; }
Check(rejectedProtectedPath, "危险上级目录没有被拒绝。");

var oldStorageState = new AppStateV2
{
    Organizers = [new OrganizerDefinition { StorageRelativePath = Path.Combine("Windows", "Legacy-11111111", "Items") }]
};
StateStore.Normalize(oldStorageState);
Check(Path.GetFileName(oldStorageState.Organizers[0].StorageRelativePath).Equals("Items", StringComparison.OrdinalIgnoreCase),
    "旧版 Items 存储路径被意外迁移。");

TransferOutcome exportOutcome = await new StorageService(customStorage, createIfMissing: false)
    .ExportToDesktopAsync("Direct storage", null, CancellationToken.None);
Check(exportOutcome.Status == TransferStatus.Moved && !Directory.Exists(customStorage) &&
      exportOutcome.DestinationPath is not null && File.Exists(Path.Combine(exportOutcome.DestinationPath, "existing.txt")),
    "手选目录没有作为整个目录导出并删除原目录。");

string emptyCustomStorage = Path.Combine(logicRoot, "EmptySelectedStorage");
Directory.CreateDirectory(emptyCustomStorage);
TransferOutcome emptyExportOutcome = await new StorageService(
        emptyCustomStorage,
        createIfMissing: false,
        exportEmptyDirectory: true)
    .ExportToDesktopAsync("Empty direct storage", null, CancellationToken.None);
Check(emptyExportOutcome.Status == TransferStatus.Moved && !Directory.Exists(emptyCustomStorage) &&
      emptyExportOutcome.DestinationPath is not null && Directory.Exists(emptyExportOutcome.DestinationPath),
    "空的手选目录没有整体导出到桌面。");

string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "TuckPane.ico");
string shortcutRoot = Path.Combine(logicRoot, "Shortcut");
Directory.CreateDirectory(shortcutRoot);
string shortcutIconPath = Path.Combine(shortcutRoot, "TuckPane.ico");
File.Copy(iconPath, shortcutIconPath);
string internetShortcutPath = Path.Combine(shortcutRoot, "Steam.url");
await File.WriteAllTextAsync(internetShortcutPath, """
    [InternetShortcut]
    URL=steam://rungameid/431960
    IconFile=TuckPane.ico
    IconIndex=0
    """);
IconCacheService.IconSnapshot expectedIcon = IconCacheService.ExtractShellIconPixels(shortcutIconPath);
IconCacheService.IconSnapshot shortcutIcon = IconCacheService.ExtractShellIconPixels(internetShortcutPath);
long iconDifference = 0;
long iconRange = 0;
for (int index = 0; index < expectedIcon.Pixels.Length; index += 4)
{
    if (expectedIcon.Pixels[index + 3] == 0 && shortcutIcon.Pixels[index + 3] == 0) continue;
    for (int channel = 0; channel < 4; channel++)
    {
        iconDifference += Math.Abs(expectedIcon.Pixels[index + channel] - shortcutIcon.Pixels[index + channel]);
        iconRange += byte.MaxValue;
    }
}
double iconSimilarity = iconRange == 0 ? 0 : 1d - (double)iconDifference / iconRange;
Check(expectedIcon.Size == shortcutIcon.Size && iconSimilarity >= .95,
    $"Steam .url 没有使用声明图标，相似度仅 {iconSimilarity:F4}。");

string copyName = OrganizerInteractionMath.CreateCopyName(
    "学习",
    ["学习", "学习 - 副本", "学习 - 副本 (2)"],
    " - 副本");
Check(copyName == "学习 - 副本 (3)", "副本名称编号错误。");

var source = new OrganizerDefinition
{
    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
    Name = "学习",
    ThemeOverride = GlassTheme.SolidDark,
    PlacementMode = OrganizerPlacementMode.Positioned,
    Layout = new OrganizerLayout { Rows = 4, Columns = 5 },
    CompactScale = 1.8,
    CanvasScale = .72,
    ItemScale = 1.15,
    NameScale = .9,
    ManualCanvasBaseWidthDip = 800,
    ManualCanvasBaseHeightDip = 600,
    Position = new WidgetPosition { MonitorDevice = "test" },
    StorageAbsolutePath = @"D:\source\Items",
    ItemOrder = ["one.txt"]
};
OrganizerDefinition copy = OrganizerInteractionMath.CopySettings(source, copyName);
Check(copy.Id != source.Id && copy.Name == copyName, "副本身份未重建。");
Check(copy.Layout.Rows == 4 && copy.Layout.Columns == 5 && copy.ThemeOverride == source.ThemeOverride,
    "外观设置未完整复制。");
Check(copy.ManualCanvasBaseWidthDip == 800 && copy.ManualCanvasBaseHeightDip == 600,
    "手动画布形状未复制。");
Check(copy.Position is null && copy.StorageAbsolutePath is null && copy.StorageRelativePath.Length == 0 && copy.ItemOrder.Count == 0,
    "副本错误复制了位置、目录或文件顺序。");

CanvasResizeEdge[] edges =
[
    CanvasResizeEdge.Left,
    CanvasResizeEdge.Top,
    CanvasResizeEdge.Right,
    CanvasResizeEdge.Bottom,
    CanvasResizeEdge.Left | CanvasResizeEdge.Top,
    CanvasResizeEdge.Right | CanvasResizeEdge.Top,
    CanvasResizeEdge.Left | CanvasResizeEdge.Bottom,
    CanvasResizeEdge.Right | CanvasResizeEdge.Bottom
];
foreach (CanvasResizeEdge edge in edges)
{
    double deltaX = edge.HasFlag(CanvasResizeEdge.Left) ? -40 : edge.HasFlag(CanvasResizeEdge.Right) ? 40 : 0;
    double deltaY = edge.HasFlag(CanvasResizeEdge.Top) ? -30 : edge.HasFlag(CanvasResizeEdge.Bottom) ? 30 : 0;
    double factor = OrganizerInteractionMath.CalculateResizeFactor(edge, deltaX, deltaY, 400, 300);
    Check(Math.Abs(factor - 1.2) < .0001, $"{edge} 缩放倍率错误：{factor}");
    double width = 400 * factor;
    double height = 300 * factor;
    Check(Math.Abs(width / height - 4d / 3d) < .0001, $"{edge} 未保持宽高比。");
}

(int left, int top, int roundedWidth, int roundedHeight) =
    OrganizerInteractionMath.CreateCenteredBounds(1000, 600, 487.3, 365.475);
Check(Math.Abs((left + roundedWidth / 2d) - 1000) <= .5 &&
      Math.Abs((top + roundedHeight / 2d) - 600) <= .5,
    "整数像素取整后的缩放中心误差超过 1 像素。");
Check(Math.Abs(roundedWidth - roundedHeight * 4d / 3d) <= 1,
    "整数像素取整后的宽高比误差超过 1 像素。");

Check(OrganizerInteractionMath.ApplyWheelSteps(1, 1, .5, 1.65) == 1.05, "滚轮放大步长错误。");
Check(OrganizerInteractionMath.ApplyWheelSteps(1, -1, .5, 1.65) == .95, "滚轮缩小步长错误。");
Check(OrganizerInteractionMath.ApplyWheelSteps(1.64, 1, .5, 1.65) == 1.65, "滚轮上限错误。");
Check(OrganizerInteractionMath.ApplyWheelSteps(.51, -1, .5, 1.65) == .5, "滚轮下限错误。");

var layout = new OrganizerLayout { Rows = 3, Columns = 3 };
(double minimumWidth, double minimumHeight) = DisplayPlacementService.CalculateMinimumExpandedSizeDip(layout, .5);
Check(DisplayPlacementService.CalculateMaximumItemScaleForExpandedSize(layout, minimumWidth, minimumHeight) == .5,
    "最小画布没有把内容比例限制为 50%。");
Check(DisplayPlacementService.CalculateMaximumItemScaleForExpandedSize(layout, minimumWidth * 2, minimumHeight * 2) > .5,
    "放大画布后内容比例上限没有提高。");

var invalidPair = new AppStateV2
{
    Organizers = [new OrganizerDefinition { ManualCanvasBaseWidthDip = 800 }]
};
StateStore.Normalize(invalidPair);
Check(invalidPair.Organizers[0].ManualCanvasBaseWidthDip is null &&
      invalidPair.Organizers[0].ManualCanvasBaseHeightDip is null,
    "不完整的手动画布尺寸未被清理。");

var compactScaleLimits = new AppStateV2
{
    Organizers =
    [
        new OrganizerDefinition { Name = "悬浮下限", PlacementMode = OrganizerPlacementMode.Floating, CompactScale = .5 },
        new OrganizerDefinition { Name = "悬浮上限", PlacementMode = OrganizerPlacementMode.Floating, CompactScale = 4 },
        new OrganizerDefinition { Name = "定位下限", PlacementMode = OrganizerPlacementMode.Positioned, CompactScale = .5 },
        new OrganizerDefinition { Name = "定位上限", PlacementMode = OrganizerPlacementMode.Positioned, CompactScale = 4 },
        new OrganizerDefinition { Name = "定位旧值", PlacementMode = OrganizerPlacementMode.Positioned, CompactScale = 1.8 }
    ]
};
StateStore.Normalize(compactScaleLimits);
Check(compactScaleLimits.Organizers[0].CompactScale == 1.2, "悬浮入口下限不是 120%。");
Check(compactScaleLimits.Organizers[1].CompactScale == 3, "悬浮入口上限不是 300%。");
Check(compactScaleLimits.Organizers[2].CompactScale == 1.2, "定位入口下限不是 120%。");
Check(compactScaleLimits.Organizers[3].CompactScale == 1.8, "定位入口上限不是 180%。");
Check(compactScaleLimits.Organizers[4].CompactScale == 1.8, "旧定位入口 180% 没有保持不变。");

var gridDisplay = new DisplayInfo(
    "test-grid",
    new NativeMethods.RECT { Left = 0, Top = 0, Right = 960, Bottom = 960 },
    new NativeMethods.RECT { Left = 0, Top = 0, Right = 960, Bottom = 960 },
    1);
var gridSnapshot = new DesktopGridSnapshot(gridDisplay, 96, 96, [], true);
DesktopGridPlacement smallPlacement = DesktopGridService.Find(gridSnapshot, [], null, 1.2)!;
DesktopGridPlacement defaultPlacement = DesktopGridService.Find(gridSnapshot, [], null, 1.56)!;
DesktopGridPlacement maximumPlacement = DesktopGridService.Find(gridSnapshot, [], null, 1.8)!;
Check(smallPlacement.CompactScale == 1.2 && defaultPlacement.CompactScale == 1.56 && maximumPlacement.CompactScale == 1.8,
    "定位网格没有使用请求的入口比例。");
Check(smallPlacement.Bounds.Width < defaultPlacement.Bounds.Width &&
      defaultPlacement.Bounds.Width < maximumPlacement.Bounds.Width &&
      smallPlacement.Bounds.Height < defaultPlacement.Bounds.Height &&
      defaultPlacement.Bounds.Height < maximumPlacement.Bounds.Height,
    "定位入口尺寸没有随比例递增。");
Check(maximumPlacement.Bounds.Width <= gridSnapshot.CellWidthPx &&
      maximumPlacement.Bounds.Height <= gridSnapshot.CellHeightPx,
    "定位入口超过了一个桌面网格。");
var tightGridSnapshot = new DesktopGridSnapshot(gridDisplay, 64, 64, [], true);
DesktopGridPlacement tightPlacement = DesktopGridService.Find(tightGridSnapshot, [], null, 1.2)!;
Check(tightPlacement.CompactScale < 1.2 &&
      tightPlacement.Bounds.Width <= tightGridSnapshot.CellWidthPx &&
      tightPlacement.Bounds.Height <= tightGridSnapshot.CellHeightPx,
    "极端桌面网格没有优先保持单格占用。");

Directory.Delete(logicRoot, recursive: true);
Console.WriteLine("TuckPane logic checks: PASS");
