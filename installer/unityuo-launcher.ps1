# Unity UO launcher: installs, updates and starts the Unity client for the UO Smallz shard.
#
#   Setup (UnityUO-Setup.cmd, once):  downloads the game into %LOCALAPPDATA%\UnityUO, puts this launcher beside it,
#                                      makes the desktop shortcut, and makes sure the game data is on the PC.
#   Every start (the shortcut):        checks the latest release; downloads only the parts that changed; starts the game.
#                                      With no connection it starts the game as installed.
#
# A release (github.com/wesleysnoe-dev/UnityUO/releases) holds manifest.json and one zip per part. Each part is
# checked against its SHA-256 before anything is replaced. The player's own folders (UserSettings, Logs, Library) are
# never touched. The UO Smallz client's folder (%LOCALAPPDATA%\UOSmallz) is only read: the game data there is what this
# client draws, and when it is missing the UO Smallz installer is fetched and run to put it there.
param(
    [string]$InstallDir,
    [switch]$Setup,
    [switch]$NoLaunch,
    # Where the release files are: the latest GitHub release, or a folder (for testing a release before it is published).
    [string]$Source = 'https://github.com/wesleysnoe-dev/UnityUO/releases/latest/download',
    [string]$ShortcutName = 'Unity UO',
    [string]$DataInstallerUrl = 'https://github.com/wesleysnoe-dev/uosmallz-client/releases/latest/download/UOSmallz-Installer-win.zip'
)
$ErrorActionPreference = 'Stop'
$LauncherFile = 'unityuo-launcher.ps1'
$StateFile = 'unityuo-install.json'
$Keep = @('UserSettings', 'Logs', 'Library')   # the player's own: never removed or replaced

if (-not $InstallDir) {
    $InstallDir = if ($Setup) { Join-Path $env:LOCALAPPDATA 'UnityUO' } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
}
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Windows.Forms

# ---- what the player sees: the console during setup, a small window during an update at launch ----
$script:Form = $null
function Show-Status([string]$text, [int]$percent = -1) {
    if ($Setup) {
        if ($percent -lt 0) { Write-Host $text }
        return
    }
    if (-not $script:Form) {
        $f = New-Object Windows.Forms.Form
        $f.Text = 'Unity UO'; $f.Width = 440; $f.Height = 130; $f.StartPosition = 'CenterScreen'
        $f.FormBorderStyle = 'FixedDialog'; $f.MaximizeBox = $false; $f.MinimizeBox = $false
        $l = New-Object Windows.Forms.Label; $l.Left = 14; $l.Top = 14; $l.Width = 400; $l.Height = 20; $l.Name = 'text'
        $b = New-Object Windows.Forms.ProgressBar; $b.Left = 14; $b.Top = 44; $b.Width = 400; $b.Height = 22; $b.Name = 'bar'
        $f.Controls.Add($l); $f.Controls.Add($b); $f.Show()
        $script:Form = $f
    }
    $script:Form.Controls['text'].Text = $text
    if ($percent -ge 0) { $script:Form.Controls['bar'].Value = [Math]::Min(100, $percent) }
    [Windows.Forms.Application]::DoEvents()
}
function Close-Status { if ($script:Form) { $script:Form.Close(); $script:Form = $null } }
function Fail([string]$text) {
    Close-Status
    if ($Setup) { Write-Host ''; Write-Host "[error] $text" -ForegroundColor Red; Write-Host ''; Read-Host 'Press Enter to close' | Out-Null }
    else { [Windows.Forms.MessageBox]::Show($text, 'Unity UO') | Out-Null }
    exit 1
}

# ---- getting files: a URL, or a folder when testing ----
$IsUrl = $Source -match '^https?://'
function Get-Text([string]$name) {
    if ($IsUrl) {
        $req = [Net.HttpWebRequest]::Create("$Source/$name"); $req.Timeout = 15000; $req.UserAgent = 'UnityUO-launcher'
        $res = $req.GetResponse()
        try { return (New-Object IO.StreamReader($res.GetResponseStream())).ReadToEnd() } finally { $res.Close() }
    }
    return [IO.File]::ReadAllText((Join-Path $Source $name))
}
function Get-File([string]$name, [string]$to, [string]$label, [long]$size) {
    if (-not $IsUrl) { Copy-Item (Join-Path $Source $name) $to -Force; return }
    $req = [Net.HttpWebRequest]::Create("$Source/$name"); $req.Timeout = 30000; $req.ReadWriteTimeout = 60000; $req.UserAgent = 'UnityUO-launcher'
    $res = $req.GetResponse()
    $in = $res.GetResponseStream(); $out = [IO.File]::Create($to)
    try {
        $buffer = New-Object byte[] 262144; $done = 0L; $shown = -1
        while (($n = $in.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $out.Write($buffer, 0, $n); $done += $n
            $percent = if ($size -gt 0) { [int](100 * $done / $size) } else { 0 }
            if ($percent -ne $shown) {
                $shown = $percent
                Show-Status "$label ($([Math]::Round($done / 1MB)) of $([Math]::Round($size / 1MB)) MB)" $percent
                if ($Setup -and $percent % 10 -eq 0) { Write-Host "    $percent%" }
            }
        }
    } finally { $out.Close(); $in.Close(); $res.Close() }
}
function Get-Sha([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLower() }

# ---- the install's record: which version of each part is here, and the files it put down ----
function Read-State {
    $p = Join-Path $InstallDir $StateFile
    if (Test-Path $p) { try { return Get-Content $p -Raw | ConvertFrom-Json } catch { } }
    return [pscustomobject]@{ version = 0; parts = [pscustomobject]@{} }
}
function Write-State($state) { $state | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $InstallDir $StateFile) -Encoding UTF8 }
function Test-Kept([string]$relative) {
    $first = ($relative -split '[\\/]')[0]
    return ($Keep -contains $first) -or $relative -eq $StateFile -or $relative -eq $LauncherFile -or $relative -eq 'launch.vbs'
}

function Install-Part($part, $state) {
    $temp = Join-Path ([IO.Path]::GetTempPath()) ("unityuo-" + $part.file)
    Get-File $part.file $temp "Downloading $($part.title)" ([long]$part.size)
    if ((Get-Sha $temp) -ne $part.sha256) { Remove-Item -LiteralPath $temp -Force; throw "The download of $($part.title) is damaged (its checksum does not match). Try again." }
    Show-Status "Installing $($part.title)..." 100
    # Out with the files the old version of this part put down, in with the new ones.
    $old = $state.parts.PSObject.Properties[$part.name]
    if ($old -and $old.Value.files) {
        foreach ($f in $old.Value.files) {
            if (Test-Kept $f) { continue }
            $path = Join-Path $InstallDir $f
            if (Test-Path -LiteralPath $path -PathType Leaf) { [IO.File]::Delete($path) }
        }
    }
    $files = New-Object Collections.Generic.List[string]
    $zip = [IO.Compression.ZipFile]::OpenRead($temp)
    try {
        foreach ($entry in $zip.Entries) {
            $relative = $entry.FullName.Replace('/', '\')
            if ($relative.EndsWith('\') -or $relative.Contains('..') -or (Test-Kept $relative)) { continue }
            $target = Join-Path $InstallDir $relative
            [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            $files.Add($relative)
        }
    } finally { $zip.Dispose() }
    Remove-Item -LiteralPath $temp -Force
    $state.parts | Add-Member -NotePropertyName $part.name -NotePropertyValue ([pscustomobject]@{ sha256 = $part.sha256; files = $files.ToArray() }) -Force
}

function Start-Game {
    if ($NoLaunch) { return }
    $exe = Join-Path $InstallDir 'Unity UO.exe'
    if (Test-Path $exe) { Start-Process $exe -WorkingDirectory $InstallDir }
}
function Test-Installed { Test-Path (Join-Path $InstallDir 'Unity UO.exe') }

# ---- the game data this client draws: the UO Smallz client's (never written from here) ----
$DataDir = Join-Path $env:LOCALAPPDATA 'UOSmallz\UOData\7.0.23.1\7.0.23.1'
function Test-GameData { Test-Path (Join-Path $DataDir 'tiledata.mul') }
function Install-GameData {
    Write-Host ''
    Write-Host '== Game data ==' -ForegroundColor Cyan
    Write-Host 'Unity UO draws the Ultima Online game data that the UO Smallz client installs, and this PC'
    Write-Host 'does not have it yet. The UO Smallz installer opens next: press Enter at its questions to'
    Write-Host 'take the defaults, and come back here when it has finished.'
    $zip = Join-Path ([IO.Path]::GetTempPath()) 'UOSmallz-Installer-win.zip'
    $dir = Join-Path ([IO.Path]::GetTempPath()) 'UOSmallz-Installer'
    $wc = New-Object Net.WebClient; $wc.Headers['User-Agent'] = 'UnityUO-launcher'; $wc.DownloadFile($DataInstallerUrl, $zip)
    if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $dir)
    $bat = Get-ChildItem $dir -Recurse -Filter 'install.bat' | Select-Object -First 1
    if (-not $bat) { throw 'The UO Smallz installer did not contain install.bat.' }
    Start-Process cmd.exe -ArgumentList '/c', "`"$($bat.FullName)`"" -Wait
}

# =====================================================================================================================
try {
    if ($Setup) {
        Write-Host ''
        Write-Host 'Unity UO for the UO Smallz shard' -ForegroundColor Cyan
        Write-Host "Installing to $InstallDir"
        $running = Get-Process 'Unity UO' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) }
        if ($running) { Fail 'Unity UO is running from that folder. Close the game and run setup again.' }
    }
    [IO.Directory]::CreateDirectory($InstallDir) | Out-Null

    # The latest release; without it, play what is installed.
    $manifest = $null
    try { $manifest = Get-Text 'manifest.json' | ConvertFrom-Json }
    catch {
        if (-not (Test-Installed)) { Fail "Could not reach the download server ($($_.Exception.Message)). Check your connection and try again." }
        if ($Setup) { Write-Host '[warn] Could not check for a newer version; keeping what is installed.' -ForegroundColor Yellow }
    }

    if ($manifest) {
        $state = Read-State
        $changed = @($manifest.parts | Where-Object {
            $have = $state.parts.PSObject.Properties[$_.name]
            -not $have -or $have.Value.sha256 -ne $_.sha256
        })
        if (-not (Test-Installed)) { $changed = @($manifest.parts) }
        if ($changed.Count -gt 0) {
            if (-not $Setup) {
                # Updating at launch: the game must not be running from here.
                $running = Get-Process 'Unity UO' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) }
                if ($running) { exit 0 }  # already playing: the update waits for the next start
            }
            if ($Setup) { Write-Host ''; Write-Host "== Version $($manifest.version) ==" -ForegroundColor Cyan }
            foreach ($part in $changed) { Install-Part $part $state }
            $state.version = $manifest.version
            Write-State $state
        }
        elseif ($state.version -ne $manifest.version) { $state.version = $manifest.version; Write-State $state }

        # The launcher itself: a newer one takes over at the next start.
        $mine = Join-Path $InstallDir $LauncherFile
        if (-not (Test-Path $mine) -or (Get-Sha $mine) -ne $manifest.launcher.sha256) {
            $temp = "$mine.new"
            Get-File $LauncherFile $temp 'Updating the launcher' ([long]$manifest.launcher.size)
            if ((Get-Sha $temp) -eq $manifest.launcher.sha256) { Move-Item -LiteralPath $temp -Destination $mine -Force } else { Remove-Item -LiteralPath $temp -Force }
        }
    }

    # The hidden starter the shortcut runs (no console window).
    $vbs = Join-Path $InstallDir 'launch.vbs'
    if (-not (Test-Path $vbs)) {
        @'
' Unity UO: runs the launcher (update check, then the game) with no console window.
Dim fso, sh, dir, ps
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
ps = sh.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
sh.Run """" & ps & """ -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & dir & "\unityuo-launcher.ps1""", 0, False
'@ | Set-Content $vbs -Encoding ASCII
    }

    if ($Setup) {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) "$ShortcutName.lnk"))
        $link.TargetPath = Join-Path $env:SystemRoot 'System32\wscript.exe'
        $link.Arguments = "`"$vbs`""
        $link.WorkingDirectory = $InstallDir
        $link.IconLocation = (Join-Path $InstallDir 'Unity UO.exe') + ',0'
        $link.Description = 'UO Smallz (Unity client)'
        $link.Save()
        Write-Host "[ok] Installed. Shortcut on the desktop: $ShortcutName" -ForegroundColor Green

        if (-not (Test-GameData)) {
            Install-GameData
            if (-not (Test-GameData)) {
                Write-Host '[warn] The game data is still not in the usual place. If you installed UO Smallz to another' -ForegroundColor Yellow
                Write-Host '       folder, set "uoDataPath" in UserSettings\unity-uo-settings.json to its UOData folder.' -ForegroundColor Yellow
            }
        }
        Write-Host ''
        Write-Host "Done. Start the game with the $ShortcutName shortcut on your desktop." -ForegroundColor Green
        if (-not $NoLaunch) { Read-Host 'Press Enter to close' | Out-Null }
        exit 0
    }

    if (-not (Test-GameData)) {
        Close-Status
        [Windows.Forms.MessageBox]::Show("The Ultima Online game data was not found in $DataDir.`n`nRun UnityUO-Setup.cmd again to install it.", 'Unity UO') | Out-Null
        exit 1
    }
    Close-Status
    Start-Game
}
catch {
    $message = $_.Exception.Message
    # An update that failed at launch must not keep the player out: play what is installed.
    if (-not $Setup -and (Test-Installed)) {
        Close-Status
        [Windows.Forms.MessageBox]::Show("The update could not be installed:`n$message`n`nStarting the version you have.", 'Unity UO') | Out-Null
        Start-Game
        exit 0
    }
    Fail $message
}
