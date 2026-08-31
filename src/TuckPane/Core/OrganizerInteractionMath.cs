namespace TuckPane.Core;

using TuckPane.Models;
using Windows.ApplicationModel.DataTransfer;

[Flags]
internal enum CanvasResizeEdge
{
    None = 0,
    Left = 1 << 0,
    Top = 1 << 1,
    Right = 1 << 2,
    Bottom = 1 << 3
}

internal static class OrganizerInteractionMath
{
    internal const double WheelScaleStep = .05;

    internal static bool ShouldStartHoverExpand(
        bool enabled,
        bool station,
        bool expanded,
        bool animating,
        bool interactionActive) =>
        enabled && !station && !expanded && !animating && !interactionActive;

    internal static DataPackageOperation SelectDropOperation(DataPackageOperation allowed) =>
        allowed.HasFlag(DataPackageOperation.Move) ? DataPackageOperation.Move :
        allowed.HasFlag(DataPackageOperation.Copy) ? DataPackageOperation.Copy :
        DataPackageOperation.None;

    internal static string CreateCopyName(string sourceName, IEnumerable<string> existingNames, string suffix)
    {
        string stem = sourceName + suffix;
        var names = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(stem)) return stem;
        for (int number = 2; ; number++)
        {
            string candidate = $"{stem} ({number})";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    internal static OrganizerDefinition CopySettings(OrganizerDefinition source, string name) => new()
    {
        Name = name,
        ThemeOverride = source.ThemeOverride,
        PlacementMode = source.PlacementMode,
        DockEdge = source.DockEdge,
        Layout = new OrganizerLayout
        {
            Mode = source.Layout.Mode,
            Rows = source.Layout.Rows,
            Columns = source.Layout.Columns
        },
        CompactScale = source.CompactScale,
        CanvasScale = source.CanvasScale,
        ItemScale = source.ItemScale,
        NameScale = source.NameScale,
        ManualCanvasBaseWidthDip = source.ManualCanvasBaseWidthDip,
        ManualCanvasBaseHeightDip = source.ManualCanvasBaseHeightDip
    };

    internal static double CalculateResizeFactor(
        CanvasResizeEdge edge,
        double deltaX,
        double deltaY,
        double startWidth,
        double startHeight)
    {
        double vectorX = edge.HasFlag(CanvasResizeEdge.Left) ? -startWidth / 2 :
            edge.HasFlag(CanvasResizeEdge.Right) ? startWidth / 2 : 0;
        double vectorY = edge.HasFlag(CanvasResizeEdge.Top) ? -startHeight / 2 :
            edge.HasFlag(CanvasResizeEdge.Bottom) ? startHeight / 2 : 0;
        double lengthSquared = vectorX * vectorX + vectorY * vectorY;
        if (lengthSquared <= 0) return 1;
        return Math.Max(0, 1 + (deltaX * vectorX + deltaY * vectorY) / lengthSquared);
    }

    internal static double ApplyWheelSteps(double current, int steps, double minimum, double maximum)
    {
        if (minimum > maximum) minimum = maximum;
        double target = Math.Round((current + steps * WheelScaleStep) * 100, MidpointRounding.AwayFromZero) / 100;
        return Math.Clamp(target, minimum, maximum);
    }

    internal static (int Left, int Top, int Width, int Height) CreateCenteredBounds(
        int centerX,
        int centerY,
        double width,
        double height)
    {
        int roundedWidth = Math.Max(1, (int)Math.Round(width));
        int roundedHeight = Math.Max(1, (int)Math.Round(height));
        return (centerX - roundedWidth / 2, centerY - roundedHeight / 2, roundedWidth, roundedHeight);
    }
}
