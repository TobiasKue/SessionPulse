<#
.SYNOPSIS
    Finds the terminal window, and the Windows Terminal tab, of a Claude Code CLI session.

.DESCRIPTION
    Used by the SessionPulse keypad plugin when a terminal session's key is pressed.

    1. Window: with -ProcessId (the session's Claude Code process), attaches to its console and asks for
       the console window. A classic console window is the answer itself; the hidden pseudo console window
       of a terminal is owned by the terminal window (Windows Terminal, also after a console handover from
       cmd). Attaching affects the whole process, which is why this runs here and not in the plugin
       (docs/LESSONS.md). Nothing is read from the console.
    2. Tab: with -Title, looks at the tabs of every Windows Terminal window (the window from step 1
       first) and selects the first whose name, without Claude Code's status glyph, equals the title, else
       the first that starts with it. Same rule as TerminalTabs.Find in SessionPulse.Core; keep the
       pattern and the minimum length in sync (a test checks). Claude Code doesn't set the tab title in
       every terminal; then step 1 alone decides.
    3. Fallback with -Wsl, when step 2 found no tab: a WSL session has no Windows process id, and a tab
       shows only the title of its active pane, so a session in another pane has no name anywhere. Takes
       the first visible window showing the console of a wsl.exe. Only the window: it can be the wrong one
       with several WSL windows, and nothing tells which pane is the session's (docs/LESSONS.md).

    Selecting a tab needs no focus. The plugin brings the window to the front itself, because a helper
    process may not take the focus. Prints exactly one result line: "OK <window handle> tab" (tab
    selected), "OK <window handle> window" (window only) or "ERR ...". PowerShell variables are
    case-insensitive, so all names here are distinct and type names are written out in full.
#>
param(
    [string]$Title = '',
    [int]$ProcessId = 0,
    [switch]$Wsl
)

$statusPrefixPattern = '^[\s\p{So}\p{Sm}\p{Sk}*\u00B7]+'
$minPrefixLength = 4

# Saved before the console is detached: the result line goes to the redirected stdout pipe.
$resultWriter = [System.Console]::Out

function Write-Result([string]$resultLine) {
    $resultWriter.WriteLine($resultLine)
    $resultWriter.Flush()
}

function Get-PlainTitle([string]$rawTitle) {
    return ($rawTitle -replace $statusPrefixPattern, '').Trim()
}

# The window showing the console of a process, or 0.
function Get-ConsoleRootWindow([uint32]$consoleProcessId) {
    [void][SessionPulse.ConsoleWindowNative]::FreeConsole()
    if (-not [SessionPulse.ConsoleWindowNative]::AttachConsole($consoleProcessId)) {
        return [long]0
    }

    $consoleWindow = [SessionPulse.ConsoleWindowNative]::GetConsoleWindow()
    [void][SessionPulse.ConsoleWindowNative]::FreeConsole()
    if ($consoleWindow -eq [System.IntPtr]::Zero) {
        return [long]0
    }

    # GA_ROOTOWNER: the terminal window that owns a pseudo console window, or the window itself.
    return [SessionPulse.ConsoleWindowNative]::GetAncestor($consoleWindow, 3).ToInt64()
}

try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes

    # --- 1. the window that shows the session's console ---
    if ($ProcessId -gt 0 -or $Wsl) {
        Add-Type -Namespace SessionPulse -Name ConsoleWindowNative -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern bool FreeConsole();
[DllImport("kernel32.dll")] public static extern bool AttachConsole(uint processId);
[DllImport("kernel32.dll")] public static extern System.IntPtr GetConsoleWindow();
[DllImport("user32.dll")] public static extern System.IntPtr GetAncestor(System.IntPtr window, uint flags);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr window);
'@
    }

    $consoleRootWindow = [long]0
    if ($ProcessId -gt 0) {
        $consoleRootWindow = Get-ConsoleRootWindow $ProcessId
    }

    # --- 2. the tab, by title ---
    $wantedTitle = Get-PlainTitle $Title
    if ($wantedTitle.Length -gt 0) {
        $windowCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'CASCADIA_HOSTING_WINDOW_CLASS')
        $terminalWindows = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children, $windowCondition))
        $terminalWindows = @($terminalWindows | Sort-Object { if ($_.Current.NativeWindowHandle -eq $consoleRootWindow) { 0 } else { 1 } })

        $tabCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)
        # An exact match anywhere wins over a prefix match found earlier.
        $chosenTab = $null
        $chosenWindow = $null
        $foundExact = $false
        foreach ($terminalWindow in $terminalWindows) {
            foreach ($tabElement in $terminalWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCondition)) {
                $tabTitle = Get-PlainTitle $tabElement.Current.Name
                if ([System.String]::Equals($tabTitle, $wantedTitle, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $chosenTab = $tabElement
                    $chosenWindow = $terminalWindow
                    $foundExact = $true
                    break
                }
                if ($null -eq $chosenTab -and $wantedTitle.Length -ge $minPrefixLength -and
                    $tabTitle.StartsWith($wantedTitle, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $chosenTab = $tabElement
                    $chosenWindow = $terminalWindow
                }
            }
            if ($foundExact) { break }
        }

        $selectionPatternObject = $null
        if ($null -ne $chosenTab -and
            $chosenTab.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selectionPatternObject)) {
            # Selecting the tab already selected would move the keyboard focus from the terminal to the tab.
            if (-not $selectionPatternObject.Current.IsSelected) {
                $selectionPatternObject.Select()
            }
            Write-Result ('OK ' + $chosenWindow.Current.NativeWindowHandle + ' tab')
            exit 0
        }
    }

    if ($consoleRootWindow -ne 0) {
        Write-Result ('OK ' + $consoleRootWindow + ' window')
        exit 0
    }

    # --- 3. any window with a WSL console ---
    if ($Wsl) {
        foreach ($wslProcess in @(Get-Process -Name wsl -ErrorAction SilentlyContinue)) {
            # wsl.exe of background services (Docker, Claude Desktop) has only a hidden console window.
            $wslWindow = Get-ConsoleRootWindow $wslProcess.Id
            if ($wslWindow -ne 0 -and [SessionPulse.ConsoleWindowNative]::IsWindowVisible([System.IntPtr]$wslWindow)) {
                Write-Result ('OK ' + $wslWindow + ' window')
                exit 0
            }
        }
    }

    Write-Result 'ERR no window or tab found'
    exit 1
}
catch {
    Write-Result ('ERR ' + $_.Exception.Message)
    exit 1
}
