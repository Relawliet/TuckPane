using TuckPane.Core;
using TuckPane.Models;
using TuckPane.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

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

Console.WriteLine("TuckPane logic checks: PASS");
