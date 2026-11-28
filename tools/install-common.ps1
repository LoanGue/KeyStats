# Helpers shared by install.ps1 and uninstall.ps1. Dot-sourced, never run on its own.
# Windows PowerShell 5.1 compatible. Never reads or writes the data folder: it only derives
# the agent's pipe name from the database path, as KeyStats.Core.Control.ControlProtocol does.

Set-StrictMode -Version Latest

$script:RunValueName = 'KeyStats'
$script:ShortcutName = 'KeyStats.lnk'
$script:AgentExe = 'KeyStats.Agent.exe'
$script:DashboardExe = 'KeyStats.exe'

function Get-KeyStatsDefaultDataDir {
    Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KeyStats'
}

function Get-KeyStatsDbPath([string] $DataDir) {
    Join-Path ([IO.Path]::GetFullPath($DataDir)) 'keystats.db'
}

function Test-KeyStatsDefaultDb([string] $DbPath) {
    $default = Get-KeyStatsDbPath (Get-KeyStatsDefaultDataDir)
    [string]::Equals($DbPath, $default, [StringComparison]::OrdinalIgnoreCase)
}

# Arguments that make the agent and the dashboard use this database: none for the default one,
# as ControlProtocol.StartArguments does.
function Get-KeyStatsDbArgument([string] $DbPath) {
    if (Test-KeyStatsDefaultDb $DbPath) { return '' }
    return "--db `"$DbPath`""
}

# Same derivation as ControlProtocol.InstanceKey: SHA-256 of the upper-cased full path, first 8 bytes in hex.
function Get-KeyStatsInstanceKey([string] $DbPath) {
    $bytes = [Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($DbPath).ToUpperInvariant())
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($bytes) } finally { $sha.Dispose() }
    -join ($hash[0..7] | ForEach-Object { $_.ToString('x2') })
}

# Sends 'quit' on the agent's pipe. Returns $true when the agent answered 'ok' within 5 seconds.
function Send-KeyStatsQuit([string] $DbPath) {
    $session = (Get-Process -Id $PID).SessionId
    $pipe = New-Object IO.Pipes.NamedPipeClientStream('.', "KeyStats.Agent.$session.$(Get-KeyStatsInstanceKey $DbPath)",
        [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect(1000)
        $line = [Text.Encoding]::ASCII.GetBytes("quit`n")
        $pipe.Write($line, 0, $line.Length)
        $pipe.Flush()
        $reader = New-Object IO.StreamReader($pipe, [Text.Encoding]::ASCII)
        $reply = $reader.ReadLineAsync()
        return ($reply.Wait(5000) -and $reply.Result -eq 'ok')
    }
    catch [TimeoutException] { return $false }
    catch [IO.IOException] { return $false }
    catch [AggregateException] { return $false }
    finally { $pipe.Dispose() }
}

# True while an agent holds the single-instance mutex of this database (ControlProtocol.MutexName).
function Test-KeyStatsAgentRunning([string] $DbPath) {
    $mutex = $null
    if (-not [Threading.Mutex]::TryOpenExisting("Local\KeyStats.Agent.$(Get-KeyStatsInstanceKey $DbPath)", [ref] $mutex)) { return $false }
    $mutex.Dispose()
    return $true
}

# Retries a removal for up to 60 seconds: WebView2 processes of a stopped dashboard can take that
# long to release its profile, even once stopped.
function Remove-KeyStatsFolder([string] $Path) {
    for ($i = 1; ; $i++) {
        try { Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop; return }
        catch {
            if ($i -ge 60) { throw "'$Path' is still in use and was only partly removed; run the script again." }
            if ($i -eq 1) { Write-Host "Waiting for '$Path' to be released..." }
            Start-Sleep -Seconds 1
        }
    }
}

function Get-KeyStatsProcess([string] $InstallDir, [string] $Exe) {
    $prefix = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\') + '\'
    Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($Exe)) -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq (Get-Process -Id $PID).SessionId -and $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) }
}

function Wait-KeyStatsExit($Processes, [int] $Seconds) {
    foreach ($p in @($Processes)) {
        if (-not $p.WaitForExit($Seconds * 1000)) { return $false }
    }
    return $true
}

# WebView2 processes of the dashboard's profile outlive a dashboard that was stopped and keep the
# profile locked: they get 3 seconds to exit on their own, then are stopped. Matched by the profile
# folder in their command line, so no other WebView2 host is touched.
function Stop-KeyStatsWebView([string] $WebViewDir) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt 10) {
        $engines = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" |
            Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($WebViewDir, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
        if ($engines.Count -eq 0) { return }
        if ($clock.Elapsed.TotalSeconds -ge 3) {
            $engines | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        }
        Start-Sleep -Milliseconds 500
    }
}

# Stops the dashboard windows and the agent that run from InstallDir, and the agent on DbPath
# wherever it runs from (a second agent on the same database would exit at once).
# The agent is asked to quit first so that it writes what it holds; it is killed only if it does not.
function Stop-KeyStats([string] $InstallDir, [string] $DbPath) {
    $dashboards = @(Get-KeyStatsProcess $InstallDir $script:DashboardExe)
    foreach ($d in $dashboards) { [void] $d.CloseMainWindow() }
    if (-not (Wait-KeyStatsExit $dashboards 10)) {
        $dashboards | Where-Object { -not $_.HasExited } | Stop-Process -Force
        Write-Warning 'A dashboard window did not close and was stopped.'
    }
    Stop-KeyStatsWebView (Join-Path (Split-Path -Parent $DbPath) 'webview')

    $agents = @(Get-KeyStatsProcess $InstallDir $script:AgentExe)
    $asked = Send-KeyStatsQuit $DbPath
    if ($asked) { Write-Host 'Running agent asked to quit.' }
    # Waits for every agent on this database, including one started from another folder.
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Test-KeyStatsAgentRunning $DbPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (-not (Wait-KeyStatsExit $agents ([Math]::Max(0, ($deadline - [DateTime]::UtcNow).TotalSeconds)))) {
        $agents | Where-Object { -not $_.HasExited } | Stop-Process -Force
        Write-Warning 'The agent did not quit in time and was stopped; events not yet written are lost.'
    }
    if (Test-KeyStatsAgentRunning $DbPath) {
        Write-Warning 'An agent started from another folder still runs on this database; quit it from its tray icon.'
    }
}

# Refuses a folder that is not an install folder of KeyStats, so that a mistyped path never
# mirrors over or deletes unrelated files, and refuses any overlap with the data folder.
function Assert-KeyStatsInstallDir([string] $InstallDir, [string] $DataDir) {
    $install = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\') + '\'
    $data = [IO.Path]::GetFullPath($DataDir).TrimEnd('\') + '\'
    if ($install.StartsWith($data, [StringComparison]::OrdinalIgnoreCase) -or $data.StartsWith($install, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The install folder '$InstallDir' overlaps the data folder '$DataDir'."
    }
    if (-not (Test-Path -LiteralPath $InstallDir)) { return }
    $entries = @(Get-ChildItem -LiteralPath $InstallDir -Force)
    if ($entries.Count -eq 0) { return }
    if (-not (Test-Path -LiteralPath (Join-Path $InstallDir $script:AgentExe)) -and -not (Test-Path -LiteralPath (Join-Path $InstallDir $script:DashboardExe))) {
        throw "'$InstallDir' is not empty and holds no KeyStats install; nothing was changed."
    }
}
