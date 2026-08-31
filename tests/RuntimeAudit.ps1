param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [Parameter(Mandatory = $true)]
    [string]$LogPath
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class TuckPaneWindowAudit
{
    public delegate bool EnumProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
}
'@

$windows = [Collections.Generic.List[object]]::new()
[TuckPaneWindowAudit]::EnumWindows({
    param($window, $parameter)

    $owner = 0
    [TuckPaneWindowAudit]::GetWindowThreadProcessId($window, [ref]$owner) | Out-Null
    if ($owner -eq $ProcessId) {
        $title = [Text.StringBuilder]::new(256)
        $class = [Text.StringBuilder]::new(256)
        [TuckPaneWindowAudit]::GetWindowText($window, $title, $title.Capacity) | Out-Null
        [TuckPaneWindowAudit]::GetClassName($window, $class, $class.Capacity) | Out-Null
        $windows.Add([pscustomobject]@{
            Handle = '0x{0:X}' -f $window.ToInt64()
            Visible = [TuckPaneWindowAudit]::IsWindowVisible($window)
            Title = $title.ToString()
            Class = $class.ToString()
        })
    }
    return $true
}, [IntPtr]::Zero) | Out-Null

$visible = @($windows | Where-Object Visible)
if ($visible.Count -ne 3) {
    throw "Expected 3 visible organizer windows, found $($visible.Count)."
}

$processStart = (Get-Process -Id $ProcessId).StartTime
$recentErrors = @(
    Get-Content -LiteralPath $LogPath -Encoding UTF8 -ErrorAction SilentlyContinue |
        Where-Object {
            if ($_ -notmatch '\[ERROR\]' -or $_.Length -lt 33) { return $false }
            $timestamp = [DateTimeOffset]::MinValue
            return [DateTimeOffset]::TryParse($_.Substring(0, 33), [ref]$timestamp) -and
                $timestamp.LocalDateTime -ge $processStart.AddSeconds(-1)
        }
)
if ($recentErrors.Count -gt 0) {
    throw "Recent application errors: $($recentErrors -join [Environment]::NewLine)"
}

[pscustomobject]@{
    TopLevelWindows = $windows.Count
    VisibleOrganizerWindows = $visible.Count
    HiddenTopLevelWindows = @($windows | Where-Object { -not $_.Visible }).Count
    RecentErrors = $recentErrors.Count
    ProcessStart = $processStart
} | Format-List
$windows | Format-Table -AutoSize
