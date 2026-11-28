<#
.SYNOPSIS
Removes KeyStats for the current user. Keeps the recorded data unless -RemoveData is passed.

.DESCRIPTION
Stops the dashboard and the agent (the agent is asked to quit first, so that it writes what it holds),
removes the start-up entry from the per-user Run key, the "KeyStats" Start-menu shortcut and the install
folder. No administrator rights are needed.

The data folder (%LOCALAPPDATA%\KeyStats: database, settings.json, backup\) is kept, so that a later
install finds the history again. -RemoveData deletes it as well, after a confirmation prompt that
-Force skips. A backup folder chosen outside the data folder is never deleted.

.PARAMETER InstallDir
Folder of the binaries. Default: %LOCALAPPDATA%\Programs\KeyStats.

.PARAMETER DataDir
Folder of the data. Default: %LOCALAPPDATA%\KeyStats.

.PARAMETER StartMenuDir
Folder of the shortcut. Default: the current user's Start-menu Programs folder.

.PARAMETER RunKey
Registry key of the start-up entry. Default: HKCU:\Software\Microsoft\Windows\CurrentVersion\Run.

.PARAMETER RemoveData
Also deletes the data folder. Cannot be undone.

.PARAMETER Force
With -RemoveData, deletes the data folder without the confirmation prompt.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\uninstall.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\uninstall.ps1 -RemoveData
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string] $InstallDir = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\KeyStats'),
    [string] $DataDir = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KeyStats'),
    [string] $StartMenuDir = ([Environment]::GetFolderPath('Programs')),
    [string] $RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
    [switch] $RemoveData,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install-common.ps1')

$dbPath = Get-KeyStatsDbPath $DataDir
Assert-KeyStatsInstallDir $InstallDir $DataDir

if ($PSCmdlet.ShouldProcess('KeyStats agent and dashboard', 'Stop')) {
    Stop-KeyStats $InstallDir $dbPath
}

if ((Test-Path -LiteralPath $RunKey) -and (Get-ItemProperty -LiteralPath $RunKey -Name $script:RunValueName -ErrorAction SilentlyContinue)) {
    if ($PSCmdlet.ShouldProcess("$RunKey\$($script:RunValueName)", 'Remove start-up entry')) {
        Remove-ItemProperty -LiteralPath $RunKey -Name $script:RunValueName
        Write-Host 'Start-up entry removed.'
    }
}

$shortcut = Join-Path $StartMenuDir $script:ShortcutName
if ((Test-Path -LiteralPath $shortcut) -and $PSCmdlet.ShouldProcess($shortcut, 'Remove shortcut')) {
    Remove-Item -LiteralPath $shortcut -Force
    Write-Host 'Start-menu shortcut removed.'
}

if ((Test-Path -LiteralPath $InstallDir) -and $PSCmdlet.ShouldProcess($InstallDir, 'Remove install folder')) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
    Write-Host "Install folder $InstallDir removed."
}

if (-not $RemoveData) {
    Write-Host "Data kept in $DataDir. Run uninstall.ps1 -RemoveData to delete it as well."
    return
}
if (-not (Test-Path -LiteralPath $DataDir)) {
    Write-Host "No data folder at $DataDir."
    return
}
if (-not $PSCmdlet.ShouldProcess($DataDir, 'Delete data folder')) { return }
# Deleting the history always asks, unless -Force says so beforehand.
if ($Force -or $PSCmdlet.ShouldContinue("Delete $DataDir with the whole typing history, settings and backups? This cannot be undone.", 'Remove KeyStats data')) {
    Remove-Item -LiteralPath $DataDir -Recurse -Force
    Write-Host "Data folder $DataDir deleted. A backup folder chosen outside it is left in place."
}
else {
    Write-Host "Data kept in $DataDir."
}
