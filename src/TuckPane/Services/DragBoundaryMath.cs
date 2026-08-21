namespace TuckPane.Services;

internal static class DragBoundaryMath
{
    internal static NativeMethods.RECT CreateDesktopExclusionBounds(
        NativeMethods.RECT bounds,
        double cellWidthDip,
        double cellHeightDip,
        double grabOffsetXDip,
        double grabOffsetYDip,
        uint dpi,
        int safetyPixels)
    {
        double scale = Math.Max(1, dpi / 96d);
        int width = Math.Max(1, (int)Math.Ceiling(Math.Max(1, cellWidthDip) * scale));
        int height = Math.Max(1, (int)Math.Ceiling(Math.Max(1, cellHeightDip) * scale));
        int grabX = Math.Clamp((int)Math.Round(grabOffsetXDip * scale, MidpointRounding.AwayFromZero), 0, width);
        int grabY = Math.Clamp((int)Math.Round(grabOffsetYDip * scale, MidpointRounding.AwayFromZero), 0, height);
        int safety = Math.Max(0, safetyPixels);
        return new NativeMethods.RECT
        {
            Left = bounds.Left - (width - grabX) - safety + 1,
            Top = bounds.Top - (height - grabY) - safety + 1,
            Right = bounds.Right + grabX + safety,
            Bottom = bounds.Bottom + grabY + safety
        };
    }

    internal static bool Contains(NativeMethods.RECT bounds, NativeMethods.POINT point) =>
        point.X >= bounds.Left && point.X < bounds.Right &&
        point.Y >= bounds.Top && point.Y < bounds.Bottom;
}

internal static class DragMessageRelay
{
    internal static bool TryReserve(ref int pending, ref long lastForwardedAt, long now, long minimumTicks)
    {
        if (Volatile.Read(ref pending) != 0) return false;
        long previous = Interlocked.Read(ref lastForwardedAt);
        if (previous != 0 && now - previous < minimumTicks) return false;
        Interlocked.Exchange(ref lastForwardedAt, now);
        return Interlocked.Exchange(ref pending, 1) == 0;
    }

    internal static void Complete(ref int pending) => Volatile.Write(ref pending, 0);

    internal static void Reset(ref int pending, ref long lastForwardedAt)
    {
        Volatile.Write(ref pending, 0);
        Interlocked.Exchange(ref lastForwardedAt, 0);
    }
}
