<#
.SYNOPSIS
    Starts or stops dictation in a Claude Desktop chat by invoking the app's own dictation button
    (Windows UI Automation). Optional SessionPulse feature, see docs/configuration.md.

.DESCRIPTION
    Rules (docs/LESSONS.md):
      * Only buttons inside the Claude window are considered, so Windows voice typing is never triggered.
      * A button whose name contains "cancel" or "abbrechen" is never invoked.
      * If another chat is still shown, wait for the configured one (at most -WaitMs) instead of
        dictating into the wrong chat.
    -ButtonNames lists the accepted button names separated by "|" (e.g. localized variants).
    Prints exactly one result line first ("OK ..." or "ERR ..."). Variable names are distinct because
    PowerShell variables are case-insensitive.
#>
param(
    [Parameter(Mandatory = $true)][string]$ChatId,
    [Parameter(Mandatory = $true)][string]$ButtonNames,
    [int]$WaitMs = 4000
)

$ErrorActionPreference = 'Stop'

try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes

    $acceptedNames = @($ButtonNames.Split('|') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    if ($acceptedNames.Count -eq 0) {
        Write-Output 'ERR no button names configured'
        exit 1
    }

    $claudeProcess = Get-Process -Name 'claude' -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne [System.IntPtr]::Zero } |
        Select-Object -First 1
    if ($null -eq $claudeProcess) {
        Write-Output 'ERR no Claude window'
        exit 1
    }

    $claudeWindow = [System.Windows.Automation.AutomationElement]::FromHandle($claudeProcess.MainWindowHandle)
    $descendantScope = [System.Windows.Automation.TreeScope]::Descendants

    # 1. Wait until the configured chat is shown.
    $documentCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Document)
    $waitUntil = [System.DateTime]::UtcNow.AddMilliseconds($WaitMs)
    $chatShown = $false
    while (-not $chatShown -and [System.DateTime]::UtcNow -lt $waitUntil) {
        foreach ($documentElement in $claudeWindow.FindAll($descendantScope, $documentCondition)) {
            $valuePatternObject = $null
            if ($documentElement.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePatternObject)) {
                if ($valuePatternObject.Current.Value -like "*$ChatId*") {
                    $chatShown = $true
                    break
                }
            }
        }
        if (-not $chatShown) { Start-Sleep -Milliseconds 200 }
    }
    if (-not $chatShown) {
        Write-Output 'ERR configured chat not shown, not dictating into another chat'
        exit 1
    }

    # 2. Find and invoke the dictation button of the Claude app.
    $buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    foreach ($buttonElement in $claudeWindow.FindAll($descendantScope, $buttonCondition)) {
        $buttonName = $buttonElement.Current.Name
        if ([string]::IsNullOrWhiteSpace($buttonName)) { continue }
        if ($buttonName -match '(?i)cancel|abbrechen') { continue }
        $nameMatches = $false
        foreach ($acceptedName in $acceptedNames) {
            if ($buttonName -ieq $acceptedName) { $nameMatches = $true; break }
        }
        if (-not $nameMatches) { continue }

        $invokePatternObject = $null
        if ($buttonElement.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invokePatternObject)) {
            $invokePatternObject.Invoke()
            Write-Output "OK invoked '$buttonName'"
            exit 0
        }
    }

    Write-Output ('ERR no button named ' + ($acceptedNames -join ' or '))
    exit 1
}
catch {
    Write-Output ('ERR ' + $_.Exception.Message)
    exit 1
}
