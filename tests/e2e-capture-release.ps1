# E2E: capture two console windows, switch tabs, release via close dialog.
# Follows docs/internal/guarded-spawn-pattern.md: hard cap (2 console spawns + 1 TabDock,
# no loops around Start-Process), tracked kill in finally, bounded waits, immediate logging.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace W -Name U -MemberDefinition @'
public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, System.IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder sb, int n);
[DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint msg, System.IntPtr w, System.IntPtr l);
'@
$WM_CLOSE = 0x0010

$exe = 'D:\Documents\tryPython\TabDock\bin\Release\net8.0-windows\win-x64\publish\TabDock.exe'
$log = "$env:APPDATA\TabDock\logs\TabDock.log"
$tracked = @()
$failures = @()

function Say($m) { Write-Host "[e2e] $m" }

# Top-level Win32 window lookup: title match, optional PID, visible only.
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
    for ($i = 0; $i -lt $tries; $i++) {
        $r = & $probe
        if ($r) { return $r }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for: $what"
}

function Get-UiaFromTitle([string]$title, [uint32]$ownerPid, [string]$what) {
    $h = Wait-For { $x = Get-Win32Window $title $ownerPid; if ($x -ne [IntPtr]::Zero) { $x } } $what
    return [System.Windows.Automation.AutomationElement]::FromHandle($h)
}

try {
    Set-Content "$env:APPDATA\TabDock\state.json" '{"Version":1,"Groups":[]}'
    Get-Process TabDock -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Say 'Spawning console window A (conhost/cmd)'
    $c1 = Start-Process conhost.exe -ArgumentList 'cmd.exe /k title TabDockTestA' -PassThru
    $tracked += $c1
    Say 'Spawning console window B (conhost/cmd)'
    $c2 = Start-Process conhost.exe -ArgumentList 'cmd.exe /k title TabDockTestB' -PassThru
    $tracked += $c2
    Wait-For { (Get-Win32Window 'TabDockTestA') -ne [IntPtr]::Zero } 'console window A' | Out-Null
    Wait-For { (Get-Win32Window 'TabDockTestB') -ne [IntPtr]::Zero } 'console window B' | Out-Null
    Say 'Both console windows are up.'

    $before = (Get-Content $log | Measure-Object -Line).Lines
    Say 'Launching TabDock'
    $p = Start-Process $exe -PassThru
    $tracked += $p
    Wait-For { (Get-Content $log | Select-Object -Skip $before) -match 'TabDock startup complete' } 'TabDock startup' | Out-Null
    $tabPid = [uint32]$p.Id

    Say 'Opening capture picker via global hotkey Ctrl+Alt+G'
    [System.Windows.Forms.SendKeys]::SendWait('^%g')

    $picker = Get-UiaFromTitle 'Capture windows' $tabPid 'capture picker window'
    Say 'Picker open; ticking checkboxes for the two consoles'
    # CheckBox content is a StackPanel, so the title sits on the inner text element;
    # find those and walk up to the owning CheckBox.
    $txtType = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $ticked = 0
    foreach ($txt in $picker.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtType)) {
        if ($txt.Current.Name -notmatch 'TabDockTest[AB]') { continue }
        $node = $txt
        while ($null -ne $node -and $node.Current.ControlType -ne [System.Windows.Automation.ControlType]::CheckBox) {
            $node = $walker.GetParent($node)
        }
        if ($null -ne $node) {
            $tp = $node.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            if ($tp.Current.ToggleState -ne 'On') { $tp.Toggle() }
            $ticked++
        }
    }
    if ($ticked -ne 2) { throw "Expected to tick 2 console entries in picker, ticked $ticked." }
    Say 'Confirming with Enter (Group these is the default button)'
    $picker.SetFocus()
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')

    $container = Get-UiaFromTitle 'Group' $tabPid 'container window'
    Say 'Container window open; checking tab strip'
    $tabsCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'TabsListBox')
    $tabsList = Wait-For { $container.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabsCond) } 'TabsListBox'
    $itemType = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $tabs = Wait-For { $items = $tabsList.FindAll([System.Windows.Automation.TreeScope]::Children, $itemType); if ($items.Count -eq 2) { $items } } 'two tabs in strip'
    Say "Tabs present: $(($tabs | ForEach-Object { $_.Current.Name }) -join ' | ')"

    # Shepherd never reparents: the ACTIVE tab's console stays a real, visible
    # top-level window (docked over the content area); only the INACTIVE one
    # is hidden. Exactly one of the two must be findable as a visible
    # top-level window, not zero and not both.
    $visibleCount = @('TabDockTestA', 'TabDockTestB') | Where-Object { (Get-Win32Window $_) -ne [IntPtr]::Zero } | Measure-Object | ForEach-Object { $_.Count }
    if ($visibleCount -ne 1) { $failures += "expected exactly 1 of the 2 captured consoles visible (docked active tab), found $visibleCount" }
    if ($failures.Count -eq 0) { Say 'Confirmed: exactly one console is docked+visible (the active tab), the other is hidden.' }

    Say 'Switching tabs: select tab 2, then tab 1'
    foreach ($idx in 1, 0) {
        $sel = $tabs[$idx].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $sel.Select()
        Start-Sleep -Milliseconds 700
        $p.Refresh()
        if ($p.HasExited) { throw "TabDock died switching to tab index $idx." }
        $isSel = $tabs[$idx].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
        Say "Tab index $idx selected=$isSel, process alive"
        if (-not $isSel) { $failures += "Tab $idx did not become selected" }
    }

    Say 'Releasing: closing container via WM_CLOSE, answering No (release to standalone)'
    $containerHwnd = Get-Win32Window 'Group' $tabPid
    [W.U]::PostMessage($containerHwnd, $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    $dlgHwnd = Wait-For { $x = Get-Win32Window 'Close group' $tabPid; if ($x -ne [IntPtr]::Zero) { $x } } 'close-group dialog'
    # WM_COMMAND with IDNO (7) presses the No button without needing focus.
    [W.U]::PostMessage($dlgHwnd, 0x0111, [IntPtr]7, [IntPtr]::Zero) | Out-Null

    Wait-For { ((Get-Win32Window 'TabDockTestA') -ne [IntPtr]::Zero) -and ((Get-Win32Window 'TabDockTestB') -ne [IntPtr]::Zero) } 'both consoles back as standalone top-level windows' | Out-Null
    Say 'Both consoles are top-level standalone windows again.'

    $p.Refresh()
    if ($p.HasExited) { throw 'TabDock exited unexpectedly after release.' }

    Say 'Exiting TabDock gracefully via main window WM_CLOSE'
    $mainHwnd = Get-Win32Window 'TabDock' $tabPid
    [W.U]::PostMessage($mainHwnd, $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Wait-For { $p.Refresh(); $p.HasExited } 'TabDock graceful exit' | Out-Null
    Say 'TabDock exited.'

    $newLines = Get-Content $log | Select-Object -Skip $before
    if ($newLines -match 'EXCEPTION|FATAL') { $failures += 'Exception found in TabDock log during e2e' }
    Say '--- relevant log lines ---'
    $newLines | Select-String 'Captur|[Rr]eleas|Opened container|EXCEPTION|health|exiting' | ForEach-Object { Write-Host "  $_" }

    if ($failures.Count -eq 0) { Write-Output 'RESULT: PASS - capture, reparent check, tab switching, and release-to-standalone all verified' }
    else { Write-Output "RESULT: FAIL - $($failures -join '; ')" }
}
catch {
    Write-Output "RESULT: FAIL - $($_.Exception.Message)"
}
finally {
    Say 'Cleanup: killing tracked processes'
    foreach ($t in $tracked) {
        try { $t.Refresh(); if (-not $t.HasExited) { Stop-Process -Id $t.Id -Force -ErrorAction SilentlyContinue } } catch {}
    }
    Get-Process cmd -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match 'TabDockTest' } | Stop-Process -Force -ErrorAction SilentlyContinue
    Set-Content "$env:APPDATA\TabDock\state.json" '{"Version":1,"Groups":[]}'
    Say 'Cleanup done.'
}
