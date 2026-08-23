[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "TuckPane executable was not found: $resolvedExecutable"
}

if (Get-Process -Name 'TuckPane' -ErrorAction SilentlyContinue) {
    throw 'TuckPane is already running. Exit it from the tray before running this check.'
}

if (-not ('TuckPaneTaskbarProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public sealed class TuckPaneWindowRecord
{
    public long Handle { get; set; }
    public bool Visible { get; set; }
    public long Owner { get; set; }
    public long ExtendedStyle { get; set; }
    public string Title { get; set; } = "";
}

public static class TuckPaneTaskbarProbe
{
    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const uint WM_CLOSE = 0x0010;
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public IntPtr Window;
        public uint IconId;
        public Guid Guid;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect iconLocation);

    public static List<TuckPaneWindowRecord> ForProcess(uint targetProcessId)
    {
        var result = new List<TuckPaneWindowRecord>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint processId);
            if (processId != targetProcessId) return true;

            var title = new StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            result.Add(new TuckPaneWindowRecord
            {
                Handle = window.ToInt64(),
                Visible = IsWindowVisible(window),
                Owner = GetWindow(window, GW_OWNER).ToInt64(),
                ExtendedStyle = GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64(),
                Title = title.ToString()
            });
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static bool IsTaskbarCandidate(TuckPaneWindowRecord window) =>
        window.Visible && window.Owner == 0 && (window.ExtendedStyle & WS_EX_TOOLWINDOW) == 0;

    public static bool HasTrayIcon(uint targetProcessId, uint iconId)
    {
        foreach (TuckPaneWindowRecord window in ForProcess(targetProcessId))
        {
            var identifier = new NotifyIconIdentifier
            {
                Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
                Window = new IntPtr(window.Handle),
                IconId = iconId
            };
            if (Shell_NotifyIconGetRect(ref identifier, out _) == 0) return true;
        }
        return false;
    }

    public static bool Close(long handle) =>
        PostMessage(new IntPtr(handle), WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
}
'@
}

function Start-TuckPane([string]$Path, [string]$TestRoot) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new($Path)
    $startInfo.UseShellExecute = $false
    $startInfo.Environment['TUCKPANE_TEST_ROOT'] = $TestRoot
    return [Diagnostics.Process]::Start($startInfo)
}

function Get-TuckPaneWindows([Diagnostics.Process]$Process) {
    return @([TuckPaneTaskbarProbe]::ForProcess([uint32]$Process.Id))
}

function Get-TaskbarCandidates([Diagnostics.Process]$Process) {
    return @(Get-TuckPaneWindows $Process | Where-Object { [TuckPaneTaskbarProbe]::IsTaskbarCandidate($_) })
}

function Format-Window([TuckPaneWindowRecord]$Window) {
    return "HWND=0x$('{0:X}' -f $Window.Handle) Visible=$($Window.Visible) Owner=0x$('{0:X}' -f $Window.Owner) ExStyle=0x$('{0:X}' -f $Window.ExtendedStyle) Title=$($Window.Title)"
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$testRoot = [IO.Path]::GetFullPath((Join-Path $tempBase ("TuckPane-tray-check-{0}" -f [Guid]::NewGuid().ToString('N'))))
if (-not $testRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe test root: $testRoot"
}

[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$primary = $null
$secondary = $null

try {
    $primary = Start-TuckPane $resolvedExecutable $testRoot
    $startupClock = [Diagnostics.Stopwatch]::StartNew()
    $firstWindowSeenAt = $null
    $startupTaskbarWindow = $null

    while ($startupClock.Elapsed -lt [TimeSpan]::FromSeconds(15)) {
        if ($primary.HasExited) {
            throw "TuckPane exited during startup with code $($primary.ExitCode)."
        }

        $windows = @(Get-TuckPaneWindows $primary)
        if ($windows.Count -gt 0 -and $null -eq $firstWindowSeenAt) {
            $firstWindowSeenAt = $startupClock.Elapsed
        }

        $startupTaskbarWindow = @(Get-TaskbarCandidates $primary) | Select-Object -First 1
        if ($null -ne $startupTaskbarWindow) { break }
        if ($null -ne $firstWindowSeenAt -and ($startupClock.Elapsed - $firstWindowSeenAt) -ge [TimeSpan]::FromSeconds(2)) { break }
        Start-Sleep -Milliseconds 10
    }

    if ($null -eq $firstWindowSeenAt) {
        throw 'TuckPane did not create a top-level window within 15 seconds.'
    }
    if ($null -ne $startupTaskbarWindow) {
        throw "First launch exposed a taskbar window: $(Format-Window $startupTaskbarWindow)"
    }
    if (-not [TuckPaneTaskbarProbe]::HasTrayIcon([uint32]$primary.Id, 1)) {
        throw 'TuckPane did not register its system tray icon.'
    }

    $secondary = Start-TuckPane $resolvedExecutable $testRoot
    if (-not $secondary.WaitForExit(5000)) {
        throw 'The secondary launch did not exit after signaling the primary instance.'
    }

    $openClock = [Diagnostics.Stopwatch]::StartNew()
    $consoleWindow = $null
    while ($openClock.Elapsed -lt [TimeSpan]::FromSeconds(5)) {
        $consoleWindow = @(Get-TaskbarCandidates $primary) | Select-Object -First 1
        if ($null -ne $consoleWindow) { break }
        Start-Sleep -Milliseconds 25
    }
    if ($null -eq $consoleWindow) {
        throw 'The secondary launch did not open the existing console window.'
    }

    if (-not [TuckPaneTaskbarProbe]::Close($consoleWindow.Handle)) {
        throw 'WM_CLOSE could not be posted to the console window.'
    }

    $closeClock = [Diagnostics.Stopwatch]::StartNew()
    while ($closeClock.Elapsed -lt [TimeSpan]::FromSeconds(5) -and (Get-TaskbarCandidates $primary).Count -gt 0) {
        Start-Sleep -Milliseconds 25
    }
    if ($primary.HasExited) {
        throw 'Closing the console terminated TuckPane instead of hiding it to the tray.'
    }
    if ((Get-TaskbarCandidates $primary).Count -gt 0) {
        throw 'Closing the console left a taskbar window visible.'
    }

    Write-Output 'TuckPane tray startup check: PASS'
}
finally {
    if ($secondary -and -not $secondary.HasExited) {
        $secondary.Kill($true)
        $secondary.WaitForExit(5000) | Out-Null
    }
    if ($primary -and -not $primary.HasExited) {
        $primary.Kill($true)
        $primary.WaitForExit(5000) | Out-Null
    }
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
        if (-not $resolvedTestRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unsafe test root: $resolvedTestRoot"
        }
        [IO.Directory]::Delete($resolvedTestRoot, $true)
    }
}
