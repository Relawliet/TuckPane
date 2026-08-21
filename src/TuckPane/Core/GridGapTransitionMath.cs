using System.Numerics;

namespace TuckPane.Core;

internal static class GridGapTransitionMath
{
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(90);

    internal static double GetProgress(TimeSpan elapsed)
    {
        double linear = Math.Clamp(elapsed.TotalMilliseconds / Duration.TotalMilliseconds, 0, 1);
        double remaining = 1 - linear;
        return 1 - remaining * remaining * remaining;
    }

    internal static Vector2 GetTranslation(
        int originalIndex,
        int fromVisualIndex,
        int toVisualIndex,
        int columns,
        double cellWidth,
        double cellHeight,
        double gap,
        double progress)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(fromVisualIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(toVisualIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        double stepX = cellWidth + gap;
        double stepY = cellHeight + gap;
        double fromX = (fromVisualIndex % columns - originalIndex % columns) * stepX;
        double fromY = (fromVisualIndex / columns - originalIndex / columns) * stepY;
        double toX = (toVisualIndex % columns - originalIndex % columns) * stepX;
        double toY = (toVisualIndex / columns - originalIndex / columns) * stepY;
        float amount = (float)Math.Clamp(progress, 0, 1);
        return Vector2.Lerp(new Vector2((float)fromX, (float)fromY), new Vector2((float)toX, (float)toY), amount);
    }
}
