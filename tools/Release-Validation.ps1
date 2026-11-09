$ErrorActionPreference = 'Stop'

# Deletes are allowed only under this repository, never through a directory link.
function Assert-ReleaseChild([string]$Repo, [string]$Path) {
    $root = [IO.Path]::GetFullPath($Repo).TrimEnd('\','/')
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\','/')
    if (-not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release path must stay inside the repository: $full"
    }
    $cursor = $full
    while ($cursor.Length -ge $root.Length) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked release path is not allowed: $cursor" }
        }
        if ($cursor -eq $root) { break }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $full
}

function Remove-ReleaseDirectory([string]$Repo, [string]$Path) {
    $full = Assert-ReleaseChild $Repo $Path
    if (Test-Path -LiteralPath $full) {
        $links = @(Get-ChildItem -LiteralPath $full -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
        if ($links.Count) { throw "Release directory contains links: $full" }
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}

function Get-ReleaseAssetMap([string]$Repo) {
    $map = [ordered]@{}
    foreach ($name in @('pet.png','poses.png','chat-background.png','dressing-room.png','animation-blink.png','animation-speak.png','animation-wave.png','animation-happy.png')) {
        $source = Join-Path $Repo ('source\assets\' + $name)
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required artwork missing: $source" }
        $map['assets/' + $name] = $source
    }
    $map['reference/anna.wav'] = Join-Path $Repo 'reference\anna.wav'
    $map['杏菜Q版头像.ico'] = Join-Path $Repo '杏菜Q版头像.ico'
    foreach ($outfit in @('校服1','校服2','幽灵服')) {
        $folder = Join-Path $Repo ('outfits\' + $outfit)
        foreach ($required in @('pet.png','poses.png')) {
            if (-not (Test-Path -LiteralPath (Join-Path $folder $required) -PathType Leaf)) { throw "Incomplete outfit: $outfit/$required" }
        }
        foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File) {
            $relative = $file.FullName.Substring($folder.Length).TrimStart('\','/').Replace('\','/')
            $map['outfits/' + $outfit + '/' + $relative] = $file.FullName
        }
    }
    foreach ($source in $map.Values) {
        if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $source).Length -eq 0) { throw "Missing or empty release asset: $source" }
    }
    return $map
}

function Assert-NoPrivateReleaseFile([string]$Relative) {
    if ($Relative -cmatch '^ja-tts/pyopenjtalk/dictionary/(char|matrix)\.bin$') { return }
    if ($Relative -match '(^|/)(logs|models|\.git)(/|$)|(^|/)(engine-path|python-path|qqmusic-path|asr-engine-path)\.txt$|(^|/)engine-process\.json$|(^|/)([^/]*key[^/]*|[^/]*settings[^/]*|memory[^/]*|chat-history[^/]*|conversation[^/]*|appearance|favorites|routine)\.(json|ini|txt|dat|bin)$|\.(log|key|tmp|bin|dat)$') {
        throw "Private runtime file in release: $Relative"
    }
}

function Assert-ReleaseBuild([string]$Repo, [string]$Directory, [string]$Version) {
    $Directory = Assert-ReleaseChild $Repo $Directory
    foreach ($required in @('AnnaDesktopPet.exe','AnnaDesktopPet.exe.config','local_asr.py','MusicSession.ps1','voice-engine\Start-VoiceEngine.ps1','voice-engine\Register-VoiceEngine.ps1','voice-engine\check_engine.py')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $required) -PathType Leaf)) { throw "Incomplete build: $Directory/$required" }
    }
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Directory 'AnnaDesktopPet.exe')).FileVersion
    foreach ($required in @('tts.py','python.exe','LICENSE.txt','pyopenjtalk/dictionary/sys.dic','pyopenjtalk/dictionary/COPYING','pyopenjtalk/htsvoice/mei_normal.htsvoice','pyopenjtalk/htsvoice/LICENSE_mei_normal.htsvoice')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory ('ja-tts/' + $required)) -PathType Leaf)) { throw "Missing portable Japanese voice file: $required" }
    }
    if ($fileVersion -ne ($Version + '.0')) { throw "Wrong program version: $fileVersion; expected $Version.0" }
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse) {
        Assert-NoPrivateReleaseFile ($file.FullName.Substring($Directory.Length).TrimStart('\','/').Replace('\','/'))
    }
    $map = Get-ReleaseAssetMap $Repo
    foreach ($relative in $map.Keys) {
        $target = Join-Path $Directory $relative
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Asset missing from build: $relative" }
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $map[$relative] -Algorithm SHA256).Hash) {
            throw "Artwork/reference differs from the source: $relative"
        }
    }
}

function Assert-ReleaseZip([string]$Repo, [string]$ZipPath, [string]$PackageName, [string]$BuildDirectory) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entries = @{}
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\','/')
            if (-not $name.StartsWith($PackageName + '/', [StringComparison]::Ordinal) -or $name -match '(^|/)\.\.(/|$)') { throw "Unexpected archive path: $name" }
            if ($entries.ContainsKey($name)) { throw "Duplicate archive entry: $name" }
            $entries[$name] = $entry
            $runtimePrefix = $PackageName + '/app/'
            if ($name.StartsWith($runtimePrefix, [StringComparison]::Ordinal)) {
                Assert-NoPrivateReleaseFile ($name.Substring($runtimePrefix.Length))
            } else {
                Assert-NoPrivateReleaseFile $name
            }
        }
        foreach ($doc in @('README.md','ASSETS.md','LICENSE')) {
            if (-not $entries.ContainsKey($PackageName + '/' + $doc)) { throw "Archive document missing: $doc" }
        }
        # Every runtime file must survive packing, byte for byte, including both
        # the executable and the original character/outfit/reference assets.
        foreach ($file in Get-ChildItem -LiteralPath $BuildDirectory -File -Recurse) {
            $relative = $file.FullName.Substring($BuildDirectory.Length).TrimStart('\','/').Replace('\','/')
            $key = $PackageName + '/app/' + $relative
            if (-not $entries.ContainsKey($key)) { throw "Archive runtime missing: $relative" }
            $stream = $entries[$key].Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $sha.Dispose(); $stream.Dispose() }
            if ($actual -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "Archive byte mismatch: $relative" }
        }
    } finally { $archive.Dispose() }
}
