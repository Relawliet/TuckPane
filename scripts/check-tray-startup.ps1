[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExecutablePath,

    [switch]$CheckExpandedOrganizer
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "TuckPane executable was not found: $resolvedExecutable"
}

if (Get-Process -Name 'TuckPane' -ErrorAction SilentlyContinue) {
    throw 'TuckPane is already running. Exit it from the tray before running this check.'
}

Add-Type -AssemblyName UIAutomationClient

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
    public double WidthDip { get; set; }
    public double HeightDip { get; set; }
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
    private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

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
            GetWindowRect(window, out Rect bounds);
            double dpi = Math.Max(96u, GetDpiForWindow(window));
            result.Add(new TuckPaneWindowRecord
            {
                Handle = window.ToInt64(),
                Visible = IsWindowVisible(window),
                Owner = GetWindow(window, GW_OWNER).ToInt64(),
                ExtendedStyle = GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64(),
                WidthDip = (bounds.Right - bounds.Left) * 96d / dpi,
                HeightDip = (bounds.Bottom - bounds.Top) * 96d / dpi,
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

function Start-TuckPane([string]$Path, [string]$TestRoot, [bool]$ExpandOrganizer) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new($Path)
    $startInfo.UseShellExecute = $false
    $startInfo.Environment['TUCKPANE_TEST_ROOT'] = $TestRoot
    if ($ExpandOrganizer) {
        $startInfo.Environment['GLASSFOLDER_TEST_EXPANDED'] = '1'
    }
    return [Diagnostics.Process]::Start($startInfo)
}

function Get-TuckPaneWindows([Diagnostics.Process]$Process) {
    return @([TuckPaneTaskbarProbe]::ForProcess([uint32]$Process.Id))
}

function Get-TaskbarCandidates([Diagnostics.Process]$Process) {
    return @(Get-TuckPaneWindows $Process | Where-Object { [TuckPaneTaskbarProbe]::IsTaskbarCandidate($_) })
}

function Format-Window([TuckPaneWindowRecord]$Window) {
    return "HWND=0x$('{0:X}' -f $Window.Handle) Visible=$($Window.Visible) Owner=0x$('{0:X}' -f $Window.Owner) ExStyle=0x$('{0:X}' -f $Window.ExtendedStyle) Size=$([Math]::Round($Window.WidthDip, 1))x$([Math]::Round($Window.HeightDip, 1))dip Title=$($Window.Title)"
}

function Get-TuckPaneTaskbarButtons {
    $classCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ClassNameProperty,
        'Shell_TrayWnd')
    $taskbar = $null
    foreach ($attempt in 1..10) {
        $taskbar = [Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [Windows.Automation.TreeScope]::Children,
            $classCondition)
        if ($null -ne $taskbar) { break }
        Start-Sleep -Milliseconds 50
    }
    if ($null -eq $taskbar) {
        throw 'Windows taskbar could not be found through UI Automation.'
    }

    $buttons = $taskbar.FindAll(
        [Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.Condition]::TrueCondition)
    return @(0..($buttons.Count - 1) | ForEach-Object {
        $button = $buttons.Item($_)
        if ($button.Current.ClassName -eq 'Taskbar.TaskListButtonAutomationPeer' -and
            ($button.Current.AutomationId -match 'TuckPane' -or $button.Current.Name -match 'TuckPane')) {
            $button.Current.Name
        }
    })
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$testRoot = [IO.Path]::GetFullPath((Join-Path $tempBase ("TuckPane-tray-check-{0}" -f [Guid]::NewGuid().ToString('N'))))
if (-not $testRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe test root: $testRoot"
}

[IO.Directory]::CreateDirectory($testRoot) | Out-Null

if ($CheckExpandedOrganizer) {
    $localRoot = Join-Path $testRoot 'LocalAppData\TuckPane'
    $userRoot = Join-Path $testRoot 'UserProfile\TuckPane'
    [IO.Directory]::CreateDirectory($localRoot) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $userRoot 'Items')) | Out-Null

    $state = @{
        SchemaVersion = 2
        GlobalSettings = @{
            Theme = 0
            StartWithWindows = $false
            Language = 0
        }
        Organizers = @(
            @{
                Id = [Guid]::NewGuid()
                Name = 'Expansion regression'
                CreatedAtUtc = [DateTimeOffset]::UtcNow
                ThemeOverride = $null
                PlacementMode = 0
                Layout = @{
                    Mode = 0
                    Rows = 3
                    Columns = 3
                }
                CompactScale = 1.56
                CanvasScale = 1.0
                ItemScale = 1.0
                NameScale = 1.0
                Position = $null
                StorageRelativePath = 'Items'
                StorageAbsolutePath = $null
                ItemOrder = @()
            }
        )
    }
    $stateJson = $state | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText((Join-Path $localRoot 'state.json'), $stateJson, [Text.UTF8Encoding]::new($false))
}

$primary = $null
$secondary = $null

try {
    $primary = Start-TuckPane $resolvedExecutable $testRoot $CheckExpandedOrganizer.IsPresent
    $startupClock = [Diagnostics.Stopwatch]::StartNew()
    $firstWindowSeenAt = $null
    $expandedWindowSeenAt = $null
    $startupTaskbarWindow = $null
    $startupTaskbarButton = $null
    $observedWindowStates = [Collections.Generic.List[string]]::new()
    $lastWindowState = $null

    while ($startupClock.Elapsed -lt [TimeSpan]::FromSeconds(15)) {
        if ($primary.HasExited) {
            throw "TuckPane exited during startup with code $($primary.ExitCode)."
        }

        $windows = @(Get-TuckPaneWindows $primary)
        $visibleWindow = @($windows | Where-Object Visible) | Select-Object -First 1
        if ($null -ne $visibleWindow) {
            $windowState = Format-Window $visibleWindow
            if ($windowState -ne $lastWindowState) {
                $observedWindowStates.Add($windowState)
                $lastWindowState = $windowState
            }
        }
        if ($windows.Count -gt 0 -and $null -eq $firstWindowSeenAt) {
            $firstWindowSeenAt = $startupClock.Elapsed
        }
        if ($CheckExpandedOrganizer -and $null -eq $expandedWindowSeenAt) {
            $expandedWindow = @($windows | Where-Object {
                $_.Visible -and $_.WidthDip -gt 180 -and $_.HeightDip -gt 160
            }) | Select-Object -First 1
            if ($null -ne $expandedWindow) {
                $expandedWindowSeenAt = $startupClock.Elapsed
            }
        }

        $startupTaskbarWindow = @(Get-TaskbarCandidates $primary) | Select-Object -First 1
        $startupTaskbarButton = @(Get-TuckPaneTaskbarButtons) | Select-Object -First 1
        if ($CheckExpandedOrganizer) {
            if ($null -ne $expandedWindowSeenAt -and ($startupClock.Elapsed - $expandedWindowSeenAt) -ge [TimeSpan]::FromSeconds(1)) { break }
        }
        elseif ($null -ne $firstWindowSeenAt -and ($startupClock.Elapsed - $firstWindowSeenAt) -ge [TimeSpan]::FromSeconds(2)) {
            break
        }
        Start-Sleep -Milliseconds 10
    }

    if ($null -eq $firstWindowSeenAt) {
        throw 'TuckPane did not create a top-level window within 15 seconds.'
    }
    if ($CheckExpandedOrganizer -and $null -eq $expandedWindowSeenAt -and $null -eq $startupTaskbarButton) {
        throw 'TuckPane did not expand the isolated organizer within 15 seconds.'
    }
    $startupTaskbarButton = @(Get-TuckPaneTaskbarButtons) | Select-Object -First 1
    $startupTaskbarWindow = @(Get-TaskbarCandidates $primary) | Select-Object -First 1
    if ($null -ne $startupTaskbarButton) {
        $phase = if ($CheckExpandedOrganizer) { 'Expanding an organizer' } else { 'First launch' }
        $windowDetails = if ($null -ne $startupTaskbarWindow) { Format-Window $startupTaskbarWindow } else { 'no taskbar-eligible HWND found' }
        throw "$phase exposed taskbar button '$startupTaskbarButton': $windowDetails. Observed states: $($observedWindowStates -join ' -> ')"
    }
    if (-not [TuckPaneTaskbarProbe]::HasTrayIcon([uint32]$primary.Id, 1)) {
        throw 'TuckPane did not register its system tray icon.'
    }

    $startupWindowHandles = @(Get-TuckPaneWindows $primary | Where-Object Visible | ForEach-Object Handle)

    $secondary = Start-TuckPane $resolvedExecutable $testRoot $CheckExpandedOrganizer.IsPresent
    if (-not $secondary.WaitForExit(5000)) {
        throw 'The secondary launch did not exit after signaling the primary instance.'
    }

    $openClock = [Diagnostics.Stopwatch]::StartNew()
    $consoleWindow = $null
    $consoleTaskbarButton = $null
    while ($openClock.Elapsed -lt [TimeSpan]::FromSeconds(5)) {
        $consoleWindow = @(Get-TuckPaneWindows $primary | Where-Object {
            $_.Visible -and $_.Handle -notin $startupWindowHandles
        }) | Select-Object -First 1
        $consoleTaskbarButton = @(Get-TuckPaneTaskbarButtons) | Select-Object -First 1
        if ($null -ne $consoleWindow -and $null -ne $consoleTaskbarButton) { break }
        Start-Sleep -Milliseconds 25
    }
    if ($null -eq $consoleWindow -or $null -eq $consoleTaskbarButton) {
        throw 'The secondary launch did not open the existing console window.'
    }

    if (-not [TuckPaneTaskbarProbe]::Close($consoleWindow.Handle)) {
        throw 'WM_CLOSE could not be posted to the console window.'
    }

    $closeClock = [Diagnostics.Stopwatch]::StartNew()
    while ($closeClock.Elapsed -lt [TimeSpan]::FromSeconds(5) -and (Get-TuckPaneTaskbarButtons).Count -gt 0) {
        Start-Sleep -Milliseconds 25
    }
    if ($primary.HasExited) {
        throw 'Closing the console terminated TuckPane instead of hiding it to the tray.'
    }
    if ((Get-TuckPaneTaskbarButtons).Count -gt 0) {
        throw 'Closing the console left a taskbar window visible.'
    }

    $scope = if ($CheckExpandedOrganizer) { 'startup and organizer expansion' } else { 'tray startup' }
    Write-Output "TuckPane $scope check: PASS"
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
