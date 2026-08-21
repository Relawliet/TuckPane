using TuckPane.Models;

namespace TuckPane.Services;

internal sealed record DisplayInfo(string Device, NativeMethods.RECT Monitor, NativeMethods.RECT Work, double Scale);

internal static class DisplayPlacementService
{
    internal const double ExpandedSideInsetDip = 28;
    internal const double ExpandedTopInsetDip = 40.5;
    internal const double ExpandedBottomInsetDip = 28;
    internal const double ItemGapDip = 12;
    internal const double MaximumItemScale = 1.65;
    private const double IconCellFraction = .68;
    private const double NameCellFraction = .15;
    private const double PreviousIconCellFraction = .62;

    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref NativeMethods.RECT monitorRect, IntPtr data) =>
        {
            NativeMethods.MONITORINFOEX info = CreateMonitorInfo();
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                double scale = 1;
                if (NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
                {
                    scale = dpiX / 96d;
                }
                displays.Add(new(info.szDevice, info.rcMonitor, info.rcWork, scale));
            }
            return true;
        }, IntPtr.Zero);
        return displays;
    }

    public static NativeMethods.RECT Restore(WidgetPosition? saved, int widthPx, int heightPx)
    {
        IReadOnlyList<DisplayInfo> displays = GetDisplays();
        DisplayInfo display = displays.FirstOrDefault(d => string.Equals(d.Device, saved?.MonitorDevice, StringComparison.OrdinalIgnoreCase))
            ?? displays.FirstOrDefault(d => d.Monitor.Left == 0 && d.Monitor.Top == 0)
            ?? displays.First();

        return RestoreToDisplay(saved, display, widthPx, heightPx);
    }

    internal static NativeMethods.RECT RestoreToDisplay(WidgetPosition? saved, DisplayInfo display, int widthPx, int heightPx)
    {
        int x = saved is null || string.IsNullOrWhiteSpace(saved.MonitorDevice)
            ? display.Work.Left + (display.Work.Width - widthPx) / 2
            : display.Work.Left + (int)Math.Round(saved.XDip * display.Scale);
        int y = saved is null || string.IsNullOrWhiteSpace(saved.MonitorDevice)
            ? display.Work.Top + (int)Math.Round(96 * display.Scale)
            : display.Work.Top + (int)Math.Round(saved.YDip * display.Scale);
        return Clamp(new NativeMethods.RECT { Left = x, Top = y, Right = x + widthPx, Bottom = y + heightPx }, display.Work);
    }

    public static NativeMethods.RECT Clamp(NativeMethods.RECT bounds, NativeMethods.RECT work)
    {
        int width = Math.Min(bounds.Width, work.Width);
        int height = Math.Min(bounds.Height, work.Height);
        int x = Math.Clamp(bounds.Left, work.Left, work.Right - width);
        int y = Math.Clamp(bounds.Top, work.Top, work.Bottom - height);
        return new NativeMethods.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
    }

    internal static NativeMethods.RECT CalculateDraggedBounds(
        NativeMethods.RECT pressAnchor,
        NativeMethods.POINT pressCursor,
        NativeMethods.POINT currentCursor,
        NativeMethods.RECT work)
    {
        int x = pressAnchor.Left + currentCursor.X - pressCursor.X;
        int y = pressAnchor.Top + currentCursor.Y - pressCursor.Y;
        return Clamp(new NativeMethods.RECT
        {
            Left = x,
            Top = y,
            Right = x + pressAnchor.Width,
            Bottom = y + pressAnchor.Height
        }, work);
    }

    internal static double CalculateGridCellExtent(double availableExtent, int count, double gap)
    {
        int safeCount = Math.Max(1, count);
        double exactExtent = (availableExtent - gap * (safeCount - 1)) / safeCount;
        return Math.Max(1, exactExtent - .5);
    }

    internal static double CalculateMaximumItemScale(DisplayInfo display, OrganizerLayout layout, double canvasScale)
    {
        double cellDip = CalculateCanvasCell(display, layout, canvasScale) / display.Scale;
        if (CalculateRequiredCellDip(.5) >= cellDip) return .5;
        double low = .5;
        double high = MaximumItemScale;
        for (int iteration = 0; iteration < 24; iteration++)
        {
            double candidate = (low + high) / 2;
            if (CalculateRequiredCellDip(candidate) <= cellDip) low = candidate;
            else high = candidate;
        }
        return Math.Clamp(low, .5, MaximumItemScale);
    }

    internal static double CalculateMinimumCanvasScale(DisplayInfo display, OrganizerLayout layout)
    {
        double baseCell = CalculateBaseCell(display);
        double scale = display.Scale;
        int columns = Math.Clamp(layout.Columns, 1, OrganizerLimits.MaximumLayoutDimension);
        int rows = Math.Clamp(layout.Rows, 1, OrganizerLimits.MaximumLayoutDimension);
        double gap = ItemGapDip * scale;
        double horizontalChrome = ExpandedSideInsetDip * 2 * scale + gap * (columns - 1);
        double verticalChrome = (ExpandedTopInsetDip + ExpandedBottomInsetDip) * scale + gap * (rows - 1);
        double previousRequiredCellDip = Math.Max(88, Math.Max(72 / PreviousIconCellFraction, 13 / NameCellFraction));
        double legacyCell = Math.Min(
            CalculateMaximumCell(display, layout),
            Math.Max(baseCell * .4, previousRequiredCellDip * scale));
        double legacyLongest = Math.Max(
            legacyCell * columns + horizontalChrome,
            legacyCell * rows + verticalChrome);
        double targetLongest = legacyLongest * 2d / 3d;
        double targetCell = Math.Min(
            (targetLongest - horizontalChrome) / columns,
            (targetLongest - verticalChrome) / rows);
        targetCell = Math.Max(CalculateRequiredCellDip(.5) * scale, targetCell);
        return Math.Clamp(targetCell / baseCell, .1, 1.2);
    }

    internal static double CalculateRequiredCellDip(double itemScale)
    {
        double normalized = Math.Clamp(itemScale, .5, MaximumItemScale);
        double fontSize = Math.Max(8, 13 * normalized);
        double verticalContent = 72 * normalized + fontSize * 1.25 + Math.Max(2, 6 * normalized) + Math.Max(4, 10 * normalized);
        return Math.Max(
            Math.Max(72 * normalized / IconCellFraction, fontSize / NameCellFraction),
            verticalContent);
    }

    internal static NativeMethods.RECT CalculateExpandedBounds(NativeMethods.RECT compact, DisplayInfo display)
        => CalculateExpandedBounds(compact, display, new OrganizerLayout(), canvasScale: 1);

    internal static NativeMethods.RECT CalculateExpandedBounds(
        NativeMethods.RECT compact,
        DisplayInfo display,
        OrganizerLayout layout,
        double canvasScale)
    {
        int margin = (int)Math.Round(24 * display.Scale);
        double baseCell = CalculateBaseCell(display);
        int columns = Math.Clamp(layout.Columns, 1, OrganizerLimits.MaximumLayoutDimension);
        int rows = Math.Clamp(layout.Rows, 1, OrganizerLimits.MaximumLayoutDimension);
        double cell = CalculateCanvasCell(display, layout, canvasScale);
        double gap = ItemGapDip * display.Scale;
        int width = Math.Max(1, (int)Math.Round(
            cell * columns + gap * (columns - 1) + ExpandedSideInsetDip * 2 * display.Scale));
        int height = Math.Max(1, (int)Math.Round(
            cell * rows + gap * (rows - 1) + (ExpandedTopInsetDip + ExpandedBottomInsetDip) * display.Scale));
        int centerX = compact.Left + compact.Width / 2;
        int centerY = compact.Top + (int)Math.Round(19.5 * display.Scale);
        var desired = new NativeMethods.RECT
        {
            Left = centerX - width / 2,
            Top = centerY - height / 2,
            Right = centerX - width / 2 + width,
            Bottom = centerY - height / 2 + height
        };
        var insetWork = new NativeMethods.RECT
        {
            Left = display.Work.Left + margin,
            Top = display.Work.Top + margin,
            Right = display.Work.Right - margin,
            Bottom = display.Work.Bottom - margin
        };
        return Clamp(desired, insetWork);
    }

    private static double CalculateBaseCell(DisplayInfo display)
    {
        int margin = (int)Math.Round(24 * display.Scale);
        int legacyWidth = Math.Min((int)Math.Round(display.Work.Width * .70), display.Work.Width - margin * 2);
        return legacyWidth / 6d;
    }

    private static double CalculateCanvasCell(DisplayInfo display, OrganizerLayout layout, double canvasScale)
    {
        double minimumScale = CalculateMinimumCanvasScale(display, layout);
        double desiredCell = CalculateBaseCell(display) * Math.Clamp(canvasScale, minimumScale, 1.2);
        return Math.Min(CalculateMaximumCell(display, layout), desiredCell);
    }

    private static double CalculateMaximumCell(DisplayInfo display, OrganizerLayout layout)
    {
        int margin = (int)Math.Round(24 * display.Scale);
        int columns = Math.Clamp(layout.Columns, 1, OrganizerLimits.MaximumLayoutDimension);
        int rows = Math.Clamp(layout.Rows, 1, OrganizerLimits.MaximumLayoutDimension);
        double gap = ItemGapDip * display.Scale;
        double availableWidth = display.Work.Width - margin * 2 - ExpandedSideInsetDip * 2 * display.Scale - gap * (columns - 1);
        double availableHeight = display.Work.Height - margin * 2 - (ExpandedTopInsetDip + ExpandedBottomInsetDip) * display.Scale - gap * (rows - 1);
        return Math.Max(1, Math.Min(availableWidth / columns, availableHeight / rows));
    }

    public static NativeMethods.RECT FindAvailableOnPrimary(IReadOnlyList<NativeMethods.RECT> occupied, int widthPx, int heightPx)
    {
        DisplayInfo display = GetDisplays().FirstOrDefault(d => d.Monitor.Left == 0 && d.Monitor.Top == 0) ?? GetDisplays().First();
        int gap = (int)Math.Round(16 * display.Scale);
        for (int y = display.Work.Top + gap; y + heightPx <= display.Work.Bottom; y += heightPx + gap)
        {
            for (int x = display.Work.Left + gap; x + widthPx <= display.Work.Right; x += widthPx + gap)
            {
                var candidate = new NativeMethods.RECT { Left = x, Top = y, Right = x + widthPx, Bottom = y + heightPx };
                if (!occupied.Any(bounds => Intersects(candidate, bounds))) return candidate;
            }
        }
        return RestoreToDisplay(null, display, widthPx, heightPx);
    }

    public static WidgetPosition Capture(NativeMethods.RECT bounds, IntPtr window = default)
    {
        var point = new NativeMethods.POINT { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 };
        IntPtr monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        NativeMethods.MONITORINFOEX info = CreateMonitorInfo();
        NativeMethods.GetMonitorInfo(monitor, ref info);
        double scale = 1;
        if (NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
        {
            scale = dpiX / 96d;
        }
        if (window != IntPtr.Zero)
        {
            scale = Math.Max(1, NativeMethods.GetDpiForWindow(window) / 96d);
        }

        return new WidgetPosition
        {
            MonitorDevice = info.szDevice,
            XDip = (bounds.Left - info.rcWork.Left) / scale,
            YDip = (bounds.Top - info.rcWork.Top) / scale,
            SavedWorkAreaWidthDip = info.rcWork.Width / scale,
            SavedWorkAreaHeightDip = info.rcWork.Height / scale
        };
    }

    public static DisplayInfo ForBounds(NativeMethods.RECT bounds)
    {
        var point = new NativeMethods.POINT { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 };
        IntPtr monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        NativeMethods.MONITORINFOEX info = CreateMonitorInfo();
        NativeMethods.GetMonitorInfo(monitor, ref info);
        double scale = 1;
        if (NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
        {
            scale = dpiX / 96d;
        }
        return new(info.szDevice, info.rcMonitor, info.rcWork, scale);
    }

    private static NativeMethods.MONITORINFOEX CreateMonitorInfo() => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
        szDevice = string.Empty
    };

    private static bool Intersects(NativeMethods.RECT first, NativeMethods.RECT second) =>
        first.Left < second.Right && first.Right > second.Left && first.Top < second.Bottom && first.Bottom > second.Top;
}
