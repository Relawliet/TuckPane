namespace TuckPane.Services;

internal static class DesktopIconPlacementMath
{
    internal static NativeMethods.POINT SnapToGrid(
        NativeMethods.POINT dropClientPoint,
        NativeMethods.RECT monitorClientBounds,
        NativeMethods.POINT spacing)
    {
        if (spacing.X <= 0 || spacing.Y <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));

        int maxX = Math.Max(monitorClientBounds.Left, monitorClientBounds.Right - spacing.X);
        int maxY = Math.Max(monitorClientBounds.Top, monitorClientBounds.Bottom - spacing.Y);
        int desiredX = dropClientPoint.X - spacing.X / 2;
        int desiredY = dropClientPoint.Y - spacing.Y / 2;
        int snappedX = monitorClientBounds.Left +
            (int)Math.Round((desiredX - monitorClientBounds.Left) / (double)spacing.X) * spacing.X;
        int snappedY = monitorClientBounds.Top +
            (int)Math.Round((desiredY - monitorClientBounds.Top) / (double)spacing.Y) * spacing.Y;
        return new NativeMethods.POINT
        {
            X = Math.Clamp(snappedX, monitorClientBounds.Left, maxX),
            Y = Math.Clamp(snappedY, monitorClientBounds.Top, maxY)
        };
    }
}
