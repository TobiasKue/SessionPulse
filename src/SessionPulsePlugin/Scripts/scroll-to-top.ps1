<#
.SYNOPSIS
    Scrolls the Claude Desktop conversation of a session to its beginning (Windows UI Automation).

.DESCRIPTION
    Used by the SessionPulse keypad plugin for scheduled runs, which Claude Desktop opens at the end.
    Waits until the Claude window shows the session (its document URL contains the session id, at most
    -WaitMs), then scrolls the largest vertically scrollable element to the top. Content loads lazily
    while scrolling up, so it retries up to -Retries times.

    Prints exactly one result line first ("OK ..." or "ERR ..."); the plugin reads only that line
    (docs/LESSONS.md). PowerShell variables are case-insensitive, so all names here are distinct and
    type names are written out in full.
#>
param(
    [Parameter(Mandatory = $true)][string]$SessionId,
    [int]$WaitMs = 4000,
    [int]$Retries = 5
)

$ErrorActionPreference = 'Stop'

try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes

    $claudeProcess = Get-Process -Name 'claude' -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne [System.IntPtr]::Zero } |
        Select-Object -First 1
    if ($null -eq $claudeProcess) {
        Write-Output 'ERR no Claude window'
        exit 1
    }

    $claudeWindow = [System.Windows.Automation.AutomationElement]::FromHandle($claudeProcess.MainWindowHandle)
    $descendantScope = [System.Windows.Automation.TreeScope]::Descendants

    # 1. Wait until the session is shown (never scroll another conversation).
    $documentCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Document)
    $waitUntil = [System.DateTime]::UtcNow.AddMilliseconds($WaitMs)
    $sessionShown = $false
    while (-not $sessionShown -and [System.DateTime]::UtcNow -lt $waitUntil) {
        foreach ($documentElement in $claudeWindow.FindAll($descendantScope, $documentCondition)) {
            $valuePatternObject = $null
            if ($documentElement.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePatternObject)) {
                if ($valuePatternObject.Current.Value -like "*$SessionId*") {
                    $sessionShown = $true
                    break
                }
            }
        }
        if (-not $sessionShown) { Start-Sleep -Milliseconds 200 }
    }
    if (-not $sessionShown) {
        Write-Output 'ERR session not shown'
        exit 1
    }

    # 2. Scroll the largest vertically scrollable element to the top, retrying while content loads.
    $scrollableCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty, $true)
    for ($attemptNumber = 1; $attemptNumber -le $Retries; $attemptNumber++) {
        $largestScrollPattern = $null
        $largestArea = 0.0
        foreach ($scrollCandidate in $claudeWindow.FindAll($descendantScope, $scrollableCondition)) {
            $scrollPatternObject = $null
            if ($scrollCandidate.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$scrollPatternObject)) {
                if ($scrollPatternObject.Current.VerticallyScrollable) {
                    $candidateBounds = $scrollCandidate.Current.BoundingRectangle
                    $candidateArea = $candidateBounds.Width * $candidateBounds.Height
                    if ($candidateArea -gt $largestArea) {
                        $largestArea = $candidateArea
                        $largestScrollPattern = $scrollPatternObject
                    }
                }
            }
        }
        if ($null -eq $largestScrollPattern) {
            Write-Output 'ERR nothing scrollable'
            exit 1
        }

        $largestScrollPattern.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 0)
        Start-Sleep -Milliseconds 400
        if ($largestScrollPattern.Current.VerticalScrollPercent -le 0.5) {
            Write-Output "OK top after $attemptNumber attempt(s)"
            exit 0
        }
    }

    Write-Output 'OK scrolled, more content may still load'
    exit 0
}
catch {
    Write-Output ('ERR ' + $_.Exception.Message)
    exit 1
}
