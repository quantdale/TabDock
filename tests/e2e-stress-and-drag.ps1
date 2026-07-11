# Stress + drag gestures: 3 consoles, 20 rapid mouse tab switches, minimize/restore each,
# real-mouse drag-reorder, real-mouse drag-out (pop out), close one mid-way, group/ungroup x3.
# Guarded-spawn: hard cap 3 consoles + 1 TabDock, tracked kill in finally, bounded waits.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace W -Name U -MemberDefinition @'
public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, System.IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool IsIconic(System.IntPtr h);
[DllImport("user32.dll")] public static extern System.IntPtr GetParent(System.IntPtr h);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder sb, int n);
[DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint msg, System.IntPtr w, System.IntPtr l);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, System.UIntPtr extra);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
'@
$WM_CLOSE = 0x0010; $WM_SYSCOMMAND = 0x0112; $SC_MINIMIZE = 0xF020
$DOWN = 0x02; $UP = 0x04

$exe = 'D:\Documents\tryPython\TabDock\bin\Debug\net8.0-windows\win-x64\TabDock.exe'
$log = "$env:APPDATA\TabDock\logs\TabDock.log"
$tracked = @()
$failures = @()

function Say($m) { Write-Host "[stress] $m" }
function Get-Win32Window([string]$title, [uint32]$ownerPid = 0) {
    $found = New-Object System.Collections.Generic.List[IntPtr]
    $cb = [W.U+EnumProc]{
        param($h, $l)
        if (-not [W.U]::IsWindowVisible($h)) { return $true }
        [uint32]$wpid = 0
        [W.U]::GetWindowThreadProcessId($h, [ref]$wpid) | Out-Null
        if ($script:pidFilter -ne 0 -and $wpid -ne $script:pidFilter) { return $true }
        $sb = New-Object System.Text.StringBuilder 256
        [W.U]::GetWindowText($h, $sb, 256) | Out-Null
        if ($sb.ToString() -eq $script:titleFilter) { $found.Add($h) }
        return $true
    }
    $script:titleFilter = $title
    $script:pidFilter = $ownerPid
    [W.U]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    if ($found.Count -gt 0) { return $found[0] } else { return [IntPtr]::Zero }
}
function Wait-For([scriptblock]$probe, [string]$what, [int]$tries = 30) {
    for ($i = 0; $i -lt $tries; $i++) { $r = & $probe; if ($r) { return $r }; Start-Sleep -Milliseconds 400 }
    throw "Timed out waiting for: $what"
}
function New-LogMark { return (Get-Content $log -ErrorAction SilentlyContinue | Measure-Object -Line).Lines }
function Get-LogSince([int]$mark) { return @(Get-Content $log | Select-Object -Skip $mark) }
function Click-XY([int]$x, [int]$y) {
    [W.U]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 80
    [W.U]::mouse_event($DOWN, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50
    [W.U]::mouse_event($UP, 0, 0, 0, [UIntPtr]::Zero)
}
function Drag-Mouse([int]$x1, [int]$y1, [int]$x2, [int]$y2) {
    [W.U]::SetCursorPos($x1, $y1) | Out-Null; Start-Sleep -Milliseconds 120
    [W.U]::mouse_event($DOWN, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 120
    $steps = 12
    for ($s = 1; $s -le $steps; $s++) {
        $ix = $x1 + [int](($x2 - $x1) * $s / $steps)
        $iy = $y1 + [int](($y2 - $y1) * $s / $steps)
        [W.U]::SetCursorPos($ix, $iy) | Out-Null
        Start-Sleep -Milliseconds 40
    }
    Start-Sleep -Milliseconds 150
    [W.U]::mouse_event($UP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 500
}

function Setup-Group([uint32]$tabPid) {
    [System.Windows.Forms.SendKeys]::SendWait('^%g')
    $picker = [System.Windows.Automation.AutomationElement]::FromHandle((Wait-For { $x = Get-Win32Window 'Capture windows' $tabPid; if ($x -ne [IntPtr]::Zero) { $x } } 'capture picker'))
    $txtType = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $ticked = 0
    foreach ($txt in $picker.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtType)) {
        if ($txt.Current.Name -notmatch 'TabDockTest[ABC]') { continue }
        $node = $txt
        while ($null -ne $node -and $node.Current.ControlType -ne [System.Windows.Automation.ControlType]::CheckBox) { $node = $walker.GetParent($node) }
        if ($null -ne $node) {
            $tp = $node.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            if ($tp.Current.ToggleState -ne 'On') { $tp.Toggle() }
            $ticked++
        }
    }
    $picker.SetFocus(); Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    return $ticked
}

try {
    Set-Content "$env:APPDATA\TabDock\state.json" '{"Version":1,"Groups":[]}'
    Get-Process TabDock -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    foreach ($n in 'A','B','C') {
        Say "Spawning console $n"
        $c = Start-Process conhost.exe -ArgumentList "cmd.exe /k title TabDockTest$n" -PassThru
        $script:tracked += $c
    }
    foreach ($n in 'A','B','C') { Wait-For { (Get-Win32Window "TabDockTest$n") -ne [IntPtr]::Zero } "console $n" | Out-Null }

    $mark0 = New-LogMark
    $p = Start-Process $exe -PassThru
    $tracked += $p
    Wait-For { (Get-LogSince $mark0) -match 'TabDock startup complete' } 'startup' | Out-Null
    $tabPid = [uint32]$p.Id

    # ============ PHASE A: group/ungroup cycle x3 ============
    Say '=== PHASE A: group/ungroup cycle x3 ==='
    for ($cycle = 1; $cycle -le 3; $cycle++) {
        $t = Setup-Group $tabPid
        if ($t -ne 3) { $failures += "cycle ${cycle}: ticked $t/3" }
        $containerHwnd = Wait-For { $x = Get-Win32Window 'Group' $tabPid; if ($x -ne [IntPtr]::Zero) { $x } } "container (cycle $cycle)"
        Start-Sleep -Milliseconds 800
        # all three reparented?
        $stillTop = @('A','B','C') | Where-Object { (Get-Win32Window "TabDockTest$_") -ne [IntPtr]::Zero }
        if ($stillTop) { $failures += "cycle ${cycle}: not captured: $($stillTop -join ',')" }
        # ungroup via close dialog -> No
        [W.U]::PostMessage($containerHwnd, $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        $dlg = Wait-For { $x = Get-Win32Window 'Close group' $tabPid; if ($x -ne [IntPtr]::Zero) { $x } } "close dialog (cycle $cycle)" 15
        [W.U]::PostMessage($dlg, 0x0111, [IntPtr]7, [IntPtr]::Zero) | Out-Null
        Wait-For { @('A','B','C') | Where-Object { (Get-Win32Window "TabDockTest$_") -eq [IntPtr]::Zero } | Measure-Object | ForEach-Object { $_.Count -eq 0 } } "all standalone again (cycle $cycle)" | Out-Null
        $p.Refresh(); if ($p.HasExited) { throw "TabDock died in group/ungroup cycle $cycle" }
        Say "  cycle ${cycle}: capture + release OK"
    }

    # ============ PHASE B: 20 rapid mouse switches ============
    Say '=== PHASE B: capture 3, then 20 rapid real-mouse tab switches ==='
    Setup-Group $tabPid | Out-Null
    $containerHwnd = Wait-For { $x = Get-Win32Window 'Group' $tabPid; if ($x -ne [IntPtr]::Zero) { $x } } 'container'
    $container = [System.Windows.Automation.AutomationElement]::FromHandle($containerHwnd)
    $capLines = (Get-LogSince $mark0) | Select-String 'Captured 0x([0-9A-F]+) \(TabDockTest([ABC])\) into host' | Select-Object -Last 3
    $children = @{}
    foreach ($m in $capLines) { $g = $m.Matches[0].Groups; $children["TabDockTest$($g[2].Value)"] = [IntPtr][Convert]::ToInt64($g[1].Value, 16) }
    $tabsCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'TabsListBox')
    $tabsList = Wait-For { $container.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabsCond) } 'TabsListBox'
    $itemType = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    function Get-Tabs { return $tabsList.FindAll([System.Windows.Automation.TreeScope]::Children, $itemType) }
    Wait-For { (Get-Tabs).Count -eq 3 } '3 tabs' | Out-Null
    [W.U]::SetForegroundWindow($containerHwnd) | Out-Null
    Start-Sleep -Milliseconds 400

    $mark = New-LogMark
    $tabs = Get-Tabs
    $centers = @()
    foreach ($t in $tabs) { $r = $t.Current.BoundingRectangle; $centers += ,@([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2)) }
    for ($i = 0; $i -lt 20; $i++) {
        $c = $centers[$i % 3]
        Click-XY $c[0] $c[1]
        Start-Sleep -Milliseconds 120
        $p.Refresh(); if ($p.HasExited) { throw "TabDock died on rapid switch $i" }
    }
    Start-Sleep -Milliseconds 600
    $switchCount = ((Get-LogSince $mark) | Select-String 'Switched group').Count
    Say "  20 rapid clicks -> $switchCount switch log entries (expect ~13-20; clicks on already-active tab are no-ops)"
    if ($switchCount -lt 10) { $failures += "rapid switching: only $switchCount switches logged" }
    $visCount = ($children.Values | Where-Object { [W.U]::IsWindowVisible($_) }).Count
    if ($visCount -ne 1) { $failures += "after rapid switching: $visCount children visible (expect 1)" }
    Say "  exactly-one-visible check: $visCount visible"

    # ============ PHASE C: minimize/restore each captured window ============
    Say '=== PHASE C: minimize each captured window (auto-restore expected) ==='
    foreach ($name in ($children.Keys | Sort-Object)) {
        # activate its tab first by clicking each tab until this child is visible
        for ($k = 0; $k -lt 3; $k++) {
            Click-XY $centers[$k][0] $centers[$k][1]
            Start-Sleep -Milliseconds 250
            if ([W.U]::IsWindowVisible($children[$name])) { break }
        }
        if (-not [W.U]::IsWindowVisible($children[$name])) { $failures += "could not activate $name for minimize test"; continue }
        [W.U]::PostMessage($children[$name], $WM_SYSCOMMAND, [IntPtr]$SC_MINIMIZE, [IntPtr]::Zero) | Out-Null
        Start-Sleep -Milliseconds 800
        $ico = [W.U]::IsIconic($children[$name]); $vis = [W.U]::IsWindowVisible($children[$name])
        Say "  ${name}: after SC_MINIMIZE -> Iconic=$ico Visible=$vis (want Iconic=False: auto-restored)"
        if ($ico) { $failures += "$name stayed iconic after minimize" }
    }

    # ============ PHASE D: drag-reorder within the strip ============
    Say '=== PHASE D: real-mouse drag-reorder tab[0] -> tab[2] position ==='
    $mark = New-LogMark
    Drag-Mouse $centers[0][0] $centers[0][1] ($centers[2][0] + 30) $centers[2][1]
    $p.Refresh(); if ($p.HasExited) { throw 'TabDock died during drag-reorder' }
    $reordered = (Get-LogSince $mark) | Select-String 'Reordered tab'
    Say "  reorder log: $($reordered -join ' // ')"
    if (-not $reordered) { $failures += 'drag-reorder produced no Reordered log entry' }
    $visCount = ($children.Values | Where-Object { [W.U]::IsWindowVisible($_) }).Count
    if ($visCount -ne 1) { $failures += "after reorder: $visCount children visible (expect 1)" }

    # ============ PHASE E: close active guest mid-way, others must still work ============
    Say '=== PHASE E: close active guest; remaining tabs must still work ==='
    $activeName = $children.Keys | Where-Object { [W.U]::IsWindowVisible($children[$_]) } | Select-Object -First 1
    $mark = New-LogMark
    [W.U]::PostMessage($children[$activeName], $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 1500
    $since = Get-LogSince $mark
    if (-not ($since -match 'destroyed')) { $failures += 'destroy event not handled in stress phase E' }
    $tabs = Get-Tabs
    Say "  closed $activeName; tabs now: $($tabs.Count) (expect 2)"
    if ($tabs.Count -ne 2) { $failures += "tab count after close: $($tabs.Count)" }
    # click both remaining tabs; each remaining child must become visible when clicked
    $remaining = @($children.Keys | Where-Object { [W.U]::IsWindow($children[$_]) })
    $tabs = Get-Tabs
    $okSwitches = 0
    foreach ($idx in 0, 1, 0) {
        $r = $tabs[$idx].Current.BoundingRectangle
        Click-XY ([int]($r.X + $r.Width/2)) ([int]($r.Y + $r.Height/2))
        Start-Sleep -Milliseconds 400
        $visNow = @($remaining | Where-Object { [W.U]::IsWindowVisible($children[$_]) })
        if ($visNow.Count -eq 1) { $okSwitches++ }
    }
    Say "  post-close switches with exactly one visible child: $okSwitches/3"
    if ($okSwitches -lt 3) { $failures += "post-close switching broken ($okSwitches/3)" }

    # ============ PHASE F: drag-out to pop out (first-ever real test) ============
    Say '=== PHASE F: real-mouse DRAG-OUT of a tab (pop out) ==='
    $tabs = Get-Tabs
    $r = $tabs[0].Current.BoundingRectangle
    $contRect = $container.Current.BoundingRectangle
    $mark = New-LogMark
    # drag from tab center to 150px below the container's bottom edge
    Drag-Mouse ([int]($r.X + $r.Width/2)) ([int]($r.Y + $r.Height/2)) ([int]($r.X + $r.Width/2)) ([int]($contRect.Y + $contRect.Height + 150))
    $p.Refresh(); if ($p.HasExited) { throw 'TabDock died during drag-out' }
    Start-Sleep -Milliseconds 800
    $released = (Get-LogSince $mark) | Select-String 'Released 0x'
    Say "  drag-out log: $($released -join ' // ')"
    $poppedOut = @($remaining | Where-Object { ([W.U]::GetParent($children[$_])) -eq [IntPtr]::Zero -and [W.U]::IsWindow($children[$_]) })
    Say "  children now top-level: $($poppedOut -join ', ')"
    if (-not $released) { $failures += 'drag-out did not release any window' }
    $tabs = Get-Tabs
    Say "  tabs remaining after drag-out: $($tabs.Count) (expect 1)"
    if ($tabs.Count -ne 1) { $failures += "tab count after drag-out: $($tabs.Count)" }

    # ============ Shutdown ============
    Say 'Shutting down'
    $containerHwnd = Get-Win32Window 'Group' $tabPid
    if ($containerHwnd -ne [IntPtr]::Zero) {
        [W.U]::PostMessage($containerHwnd, $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        try {
            $dlg = Wait-For { $x = Get-Win32Window 'Close group' $tabPid; if ($x -ne [IntPtr]::Zero) { $x } } 'close dialog' 10
            [W.U]::PostMessage($dlg, 0x0111, [IntPtr]7, [IntPtr]::Zero) | Out-Null
        } catch { Say '  (no close dialog appeared — container may have been empty)' }
    }
    Start-Sleep -Milliseconds 800
    $mainHwnd = Get-Win32Window 'TabDock' $tabPid
    if ($mainHwnd -ne [IntPtr]::Zero) { [W.U]::PostMessage($mainHwnd, $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
    Wait-For { $p.Refresh(); $p.HasExited } 'exit' 20 | Out-Null

    $allNew = Get-LogSince $mark0
    $exc = $allNew | Select-String 'EXCEPTION|FATAL'
    if ($exc) { $failures += "exceptions in log: $($exc -join ' // ')" }

    if ($failures.Count -eq 0) { Write-Output 'RESULT: STRESS PASS' }
    else { Write-Output "RESULT: STRESS FAIL - $($failures -join ' | ')" }
}
catch {
    Write-Output "RESULT: STRESS ABORTED - $($_.Exception.Message)"
}
finally {
    Say 'Cleanup'
    foreach ($t in $tracked) { try { $t.Refresh(); if (-not $t.HasExited) { Stop-Process -Id $t.Id -Force -ErrorAction SilentlyContinue } } catch {} }
    Get-Process cmd -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match 'TabDockTest' } | Stop-Process -Force -ErrorAction SilentlyContinue
    Set-Content "$env:APPDATA\TabDock\state.json" '{"Version":1,"Groups":[]}'
    Say 'Cleanup done.'
}
