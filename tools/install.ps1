<#
.SYNOPSIS
Builds KeyStats from this repository and installs it for the current user.

.DESCRIPTION
Publishes the agent (NativeAOT) and the dashboard, copies both to the install folder, registers the
agent in the per-user Run key, creates a "KeyStats" Start-menu shortcut to the dashboard, then starts
the agent and opens the dashboard, which runs the first-run wizard while encryption is not set up.

No administrator rights are needed. Running it again upgrades in place: the running agent is asked to
quit first, so that it writes what it holds. The data folder (%LOCALAPPDATA%\KeyStats) is never
written or deleted by this script.

.PARAMETER InstallDir
Folder of the binaries. Default: %LOCALAPPDATA%\Programs\KeyStats.

.PARAMETER DataDir
Folder of the database the installed agent and dashboard use. Default: %LOCALAPPDATA%\KeyStats.
Another folder is passed to both with --db; meant for tests.

.PARAMETER StartMenuDir
Folder of the shortcut. Default: the current user's Start-menu Programs folder.

.PARAMETER RunKey
Registry key of the start-up entry. Default: HKCU:\Software\Microsoft\Windows\CurrentVersion\Run.

.PARAMETER NoStart
Installs without starting the agent or opening the dashboard.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\install.ps1
#>
[CmdletBinding()]
param(
    [string] $InstallDir = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\KeyStats'),
    [string] $DataDir = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KeyStats'),
    [string] $StartMenuDir = ([Environment]::GetFolderPath('Programs')),
    [string] $RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
    [switch] $NoStart
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install-common.ps1')

# A trailing backslash before the closing quote would escape it in robocopy's command line.
$InstallDir = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')

$repo = Split-Path -Parent $PSScriptRoot
$dbPath = Get-KeyStatsDbPath $DataDir
$dbArgument = Get-KeyStatsDbArgument $dbPath

# Windows 10 2004 (build 19041) is the dashboard's minimum (Windows Hello through WinRT).
if ([Environment]::OSVersion.Version.Build -lt 19041) {
    throw 'KeyStats needs Windows 10 version 2004 (build 19041) or later.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK is needed to build KeyStats: https://dotnet.microsoft.com/download'
}
# NativeAOT finds the MSVC linker through vswhere.exe, which the Visual Studio installer does not put on PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $installer 'vswhere.exe'))) {
    $env:PATH = "$env:PATH;$installer"
}
if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
    throw 'NativeAOT needs the Visual Studio C++ build tools ("Desktop development with C++"): https://visualstudio.microsoft.com/downloads/'
}
# Evergreen WebView2 Runtime registration, per machine (32- or 64-bit view) or per user.
$webView2 = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$webView2Keys = "HKLM:\SOFTWARE\WOW6432Node\$webView2", "HKLM:\SOFTWARE\$webView2", "HKCU:\SOFTWARE\$webView2"
if (-not ($webView2Keys | Where-Object { Test-Path $_ })) {
    Write-Warning 'WebView2 Runtime not found; the dashboard needs it: https://developer.microsoft.com/microsoft-edge/webview2/'
}
Assert-KeyStatsInstallDir $InstallDir $DataDir

# Build into a staging folder first: a failed build leaves the current install untouched.
$stage = Join-Path ([IO.Path]::GetTempPath()) "KeyStats-install-$PID"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
try {
    Write-Host 'Publishing the agent (NativeAOT)...'
    & dotnet publish (Join-Path $repo 'src\KeyStats.Agent') -c Release -o (Join-Path $stage 'agent') --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Agent publish failed (exit code $LASTEXITCODE)." }
    Write-Host 'Publishing the dashboard...'
    & dotnet publish (Join-Path $repo 'src\KeyStats.Dashboard') -c Release -o (Join-Path $stage 'app') --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Dashboard publish failed (exit code $LASTEXITCODE)." }
    # The dashboard looks for the agent next to itself (AgentControl), so both share one folder.
    Copy-Item (Join-Path $stage 'agent\*') (Join-Path $stage 'app') -Recurse -Force
    Get-ChildItem (Join-Path $stage 'app') -Filter *.pdb -Recurse | Remove-Item

    Stop-KeyStats $InstallDir $dbPath

    Write-Host "Copying to $InstallDir..."
    [void] (New-Item -ItemType Directory -Force -Path $InstallDir)
    & robocopy (Join-Path $stage 'app') $InstallDir /MIR /R:3 /W:1 /NJH /NJS /NFL /NDL /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "Copy to '$InstallDir' failed (robocopy exit code $LASTEXITCODE). The agent is stopped and the install may be partial: run install.ps1 again."
    }
}
finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}

$agent = Join-Path $InstallDir $script:AgentExe
$dashboard = Join-Path $InstallDir $script:DashboardExe

if (-not (Test-Path -LiteralPath $RunKey)) { [void] (New-Item -Path $RunKey -Force) }
Set-ItemProperty -LiteralPath $RunKey -Name $script:RunValueName -Value ("`"$agent`" $dbArgument").Trim()
Write-Host "Start-up entry set in $RunKey."

[void] (New-Item -ItemType Directory -Force -Path $StartMenuDir)
$shell = New-Object -ComObject WScript.Shell
try {
    $link = $shell.CreateShortcut((Join-Path $StartMenuDir $script:ShortcutName))
    $link.TargetPath = $dashboard
    $link.Arguments = $dbArgument
    $link.WorkingDirectory = $InstallDir
    $link.IconLocation = "$dashboard,0"
    $link.Description = 'KeyStats statistics'
    $link.Save()
}
finally { [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
Write-Host "Start-menu shortcut created in $StartMenuDir."

if ($NoStart) {
    Write-Host 'Installed. The agent starts at the next sign-in.'
    return
}
$agentArgs = @{ FilePath = $agent; WorkingDirectory = $InstallDir }
$dashboardArgs = @{ FilePath = $dashboard; WorkingDirectory = $InstallDir }
if ($dbArgument) { $agentArgs.ArgumentList = $dbArgument; $dashboardArgs.ArgumentList = $dbArgument }
Start-Process @agentArgs
Start-Process @dashboardArgs
Write-Host 'Installed. The agent is running; the dashboard opens on the first-run wizard if encryption is not set up yet.'
