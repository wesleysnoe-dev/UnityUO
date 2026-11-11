# Unity UO launcher: installs, updates and starts the Unity client for the UO Smallz shard.
#
#   Setup (UnityUO-Setup.cmd, once):  downloads the game into %LOCALAPPDATA%\UnityUO, puts this launcher beside it,
#                                      makes the desktop shortcut, and makes sure the game data is on the PC.
#   Every start (the shortcut):        checks the latest release; downloads only the parts that changed; starts the game.
#                                      With no connection it starts the game as installed.
#
# A release (github.com/wesleysnoe-dev/UnityUO/releases) holds manifest.json and one zip per part. Each part is
# checked against its SHA-256 before anything is replaced. The player's own folders (UserSettings, Logs, Library) are
# never touched.
#
# The client needs nothing from the UO Smallz (TazUO) client. Its game data lives in its own folder:
#   UOData\        Ultima Online 7.0.23.1, then the shard's map files over it (the release's "maps" part)
#   Shard\Client\  the shard's custom art and item tables (the "shard" part)
#   Shard\HDArt\   the 2x HD art pack
# On a PC that has the UO Smallz client, the game data and the HD pack are copied from its folder (which is only ever
# read); otherwise they are downloaded from the addresses in the manifest and checked against their SHA-256.
param(
    [string]$InstallDir,
    [switch]$Setup,
    [switch]$NoLaunch,
    # Where the release files are: the latest GitHub release, or a folder (for testing a release before it is published).
    [string]$Source = 'https://github.com/wesleysnoe-dev/UnityUO/releases/latest/download',
    [string]$ShortcutName = 'Unity UO',
    # The UO Smallz client's folder, if this PC has one: game data and HD art are copied from it, saving the downloads.
    [string]$OldClientDir = (Join-Path $env:LOCALAPPDATA 'UOSmallz')
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
    # A release file by its name, or anything else by its full address.
    $absolute = $name -match '^https?://'
    if (-not $absolute -and -not $IsUrl) { Copy-Item (Join-Path $Source $name) $to -Force; return }
    $address = if ($absolute) { $name } else { "$Source/$name" }
    $req = [Net.HttpWebRequest]::Create($address); $req.Timeout = 30000; $req.ReadWriteTimeout = 60000; $req.UserAgent = 'UnityUO-launcher'
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
                if ($Setup -and $percent % 10 -eq 0) { Write-Host "    $label $percent%" }
            }
        }
    } finally { $out.Close(); $in.Close(); $res.Close() }
}
function Get-Sha([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLower() }

# ---- the install's record: which version of each part is here, and the files it put down ----
function Read-State {
    $p = Join-Path $InstallDir $StateFile
    if (Test-Path $p) { try { return Get-Content $p -Raw | ConvertFrom-Json } catch { } }
    return [pscustomobject]@{ version = 0; parts = [pscustomobject]@{}; data = [pscustomobject]@{} }
}
function Set-Data($state, [string]$name, [string]$value) {
    if (-not $state.PSObject.Properties['data']) { $state | Add-Member -NotePropertyName data -NotePropertyValue ([pscustomobject]@{}) }
    $state.data | Add-Member -NotePropertyName $name -NotePropertyValue $value -Force
}
function Get-Data($state, [string]$name) {
    if (-not $state.PSObject.Properties['data']) { return $null }
    $p = $state.data.PSObject.Properties[$name]
    if ($p) { return $p.Value } else { return $null }
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

# ---- the game data: the client's own copy ----
$OwnData = Join-Path $InstallDir 'UOData'
function Test-OwnData { Test-Path (Join-Path $OwnData 'tiledata.mul') }
function Find-Data([string]$root) {
    # The folder holding tiledata.mul and art.mul under a root, or null.
    if (-not $root -or -not (Test-Path -LiteralPath $root)) { return $null }
    if ((Test-Path (Join-Path $root 'tiledata.mul')) -and (Test-Path (Join-Path $root 'art.mul'))) { return $root }
    $hit = Get-ChildItem -LiteralPath $root -Recurse -Filter 'tiledata.mul' -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -notmatch '\\_' } | Select-Object -First 1
    if ($hit -and (Test-Path (Join-Path $hit.DirectoryName 'art.mul'))) { return $hit.DirectoryName }
    return $null
}
function Test-OldData { [bool](Find-Data (Join-Path $OldClientDir 'UOData')) }
function Copy-Tree([string]$from, [string]$to, [string[]]$skipDirs = @()) {
    $arguments = @($from, $to, '/E', '/R:1', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
    if ($skipDirs.Count -gt 0) { $arguments += '/XD'; $arguments += $skipDirs }
    & robocopy @arguments | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying $from failed (robocopy exit $LASTEXITCODE)." }
    $global:LASTEXITCODE = 0
}

# The Ultima Online 7.0.23.1 files, once: from the UO Smallz client's folder when this PC has it, else the download.
function Install-BaseData($info, $state) {
    if (Test-OwnData) { return $false }
    $from = Find-Data (Join-Path $OldClientDir 'UOData')
    if ($from) {
        Show-Status 'Copying the game data from your UO Smallz client (about 2 GB)...'
        # Its backup folders (_backup-..., _magincia-source) stay behind.
        $skip = @(Get-ChildItem -LiteralPath $from -Directory | Where-Object { $_.Name.StartsWith('_') } | ForEach-Object { $_.FullName })
        Copy-Tree $from $OwnData $skip
    }
    else {
        if (-not $info -or -not $info.url) { throw 'This release does not say where the game data comes from.' }
        $work = Join-Path $InstallDir 'UOData.download'
        [IO.Directory]::CreateDirectory($work) | Out-Null
        $sfx = Join-Path $work "$($info.version).exe"
        if ((Test-Path -LiteralPath $sfx) -and (Get-Sha $sfx) -ne $info.sha256) { [IO.File]::Delete($sfx) }
        if (-not (Test-Path -LiteralPath $sfx)) { Get-File $info.url $sfx 'Downloading the Ultima Online game data' ([long]$info.size) }
        if ((Get-Sha $sfx) -ne $info.sha256) { [IO.File]::Delete($sfx); throw 'The game data download is damaged (its checksum does not match). Nothing was run. Try again.' }
        Show-Status 'Unpacking the game data (a few minutes)...' 100
        # The same unpacking the UO Smallz installer does: the archive's own silent extract into a folder.
        $target = Join-Path $work 'extract'
        [IO.Directory]::CreateDirectory($target) | Out-Null
        Start-Process -FilePath $sfx -ArgumentList "-s2 -y `"-d$target`"" -Wait
        $found = Find-Data $target
        if (-not $found) { throw 'The game data could not be unpacked (tiledata.mul was not found).' }
        Copy-Tree $found $OwnData
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not (Test-OwnData)) { throw 'The game data did not arrive in the install folder.' }
    return $true
}

# The 2x HD art pack (optional: without it the game draws the original art).
function Install-HdArt($info, $state) {
    if (-not $info -or -not $info.sha256) { return }
    $dir = Join-Path $InstallDir 'Shard\HDArt'
    if ((Get-Data $state 'hdart') -eq $info.sha256 -and (Test-Path (Join-Path $dir 'art'))) { return }
    $oldPack = Join-Path $OldClientDir 'HDArt'
    $oldStamp = Join-Path $OldClientDir 'hdart-version.json'
    $oldSha = $null
    if (Test-Path -LiteralPath $oldStamp) { try { $oldSha = (Get-Content $oldStamp -Raw | ConvertFrom-Json).sha256 } catch { } }
    if ($oldSha -eq $info.sha256 -and (Test-Path (Join-Path $oldPack 'art'))) {
        Show-Status 'Copying the HD art from your UO Smallz client (about 700 MB)...'
        foreach ($sub in 'art', 'land', 'gumps', 'texmaps') {
            if (Test-Path (Join-Path $oldPack $sub)) { Copy-Tree (Join-Path $oldPack $sub) (Join-Path $dir $sub) }
        }
        if (Test-Path (Join-Path $oldPack 'hd-art.json')) { Copy-Item (Join-Path $oldPack 'hd-art.json') $dir -Force }
    }
    else {
        $zip = Join-Path $InstallDir 'HDArt.download.zip'
        if ((Test-Path -LiteralPath $zip) -and (Get-Sha $zip) -ne $info.sha256) { [IO.File]::Delete($zip) }
        if (-not (Test-Path -LiteralPath $zip)) { Get-File $info.url $zip 'Downloading the HD art' ([long]$info.size) }
        if ((Get-Sha $zip) -ne $info.sha256) { [IO.File]::Delete($zip); throw 'The HD art download is damaged (its checksum does not match).' }
        Show-Status 'Unpacking the HD art...' 100
        $new = "$dir.new"
        if (Test-Path -LiteralPath $new) { Remove-Item -LiteralPath $new -Recurse -Force }
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $new)
        # The pack's folders may sit one level down in the zip.
        $root = $new
        if (-not (Test-Path (Join-Path $root 'art'))) {
            $inner = Get-ChildItem -LiteralPath $new -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'art') } | Select-Object -First 1
            if (-not $inner) { Remove-Item -LiteralPath $new -Recurse -Force; throw 'The HD art pack has no art folder.' }
            $root = $inner.FullName
        }
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        [IO.Directory]::CreateDirectory((Split-Path -Parent $dir)) | Out-Null
        Move-Item -LiteralPath $root -Destination $dir
        if (Test-Path -LiteralPath $new) { Remove-Item -LiteralPath $new -Recurse -Force }
        [IO.File]::Delete($zip)
    }
    Set-Data $state 'hdart' $info.sha256
}

# The map files must be the server's: the manifest names their SHA-256.
function Test-Maps($manifest) {
    $off = @()
    if ($manifest.PSObject.Properties['maps']) {
        foreach ($m in $manifest.maps.PSObject.Properties) {
            $file = Join-Path $OwnData $m.Name
            if (-not (Test-Path -LiteralPath $file) -or (Get-Sha $file) -ne $m.Value) { $off += $m.Name }
        }
    }
    return $off
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
        $dataInfo = if ($manifest.PSObject.Properties['data']) { $manifest.data } else { $null }
        # The game data first: the shard's map files (a part) go over it.
        if ($dataInfo -and -not (Test-OwnData)) {
            if ($Setup) { Write-Host ''; Write-Host '== Game data ==' -ForegroundColor Cyan }
            try {
                if (Install-BaseData $dataInfo.uodata $state) {
                    if ($state.parts.PSObject.Properties['maps']) { $state.parts.PSObject.Properties.Remove('maps') }
                    Write-State $state
                    if ($Setup) { Write-Host '[ok] Game data installed.' -ForegroundColor Green }
                }
            }
            catch {
                # With the UO Smallz client's data to fall back on, the game still runs.
                if (-not (Test-OldData)) { throw }
                if ($Setup) { Write-Host "[warn] $($_.Exception.Message) Using the UO Smallz client's data for now." -ForegroundColor Yellow }
            }
        }
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

        if ($dataInfo) {
            try { Install-HdArt $dataInfo.hdart $state; Write-State $state }
            catch { if ($Setup) { Write-Host "[warn] HD art: $($_.Exception.Message) The game runs without it; setup will try again next time." -ForegroundColor Yellow } }
            if ($Setup -and (Test-OwnData)) {
                $off = @(Test-Maps $manifest)
                if ($off.Count -eq 0) { Write-Host "[ok] Your map files match the UO Smallz server's." -ForegroundColor Green }
                else { Write-Host "[warn] $($off -join ', ') differ from the server's: some places may look different in game." -ForegroundColor Yellow }
            }
        }

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

        if (-not (Test-OwnData) -and -not (Test-OldData)) {
            Write-Host '[warn] The game data is not installed yet. Run setup again when you are online.' -ForegroundColor Yellow
        }
        Write-Host ''
        Write-Host "Done. Start the game with the $ShortcutName shortcut on your desktop." -ForegroundColor Green
        if (-not $NoLaunch) { Read-Host 'Press Enter to close' | Out-Null }
        exit 0
    }

    if (-not (Test-OwnData) -and -not (Test-OldData)) {
        Close-Status
        [Windows.Forms.MessageBox]::Show("The Ultima Online game data is not installed yet.`n`nStart the game again while online, or run UnityUO-Setup.cmd.", 'Unity UO') | Out-Null
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
