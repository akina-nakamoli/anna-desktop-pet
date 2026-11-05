$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repo
try {
    & (Join-Path $repo 'source\build.ps1') -Destination (Join-Path $repo 'build\GPT')
    if ($LASTEXITCODE -ne 0) { throw 'GPT build failed.' }
    & (Join-Path $repo 'source\build.ps1') -Destination (Join-Path $repo 'build\DeepSeek') -DeepSeek
    if ($LASTEXITCODE -ne 0) { throw 'DeepSeek build failed.' }

    $files = @(git ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Git file inventory failed.' }
    foreach ($file in $files) {
        if ($file -match '^(build|dist|private-assets)/' -or ($file -match '\.(exe|dll|wav|mp3|flac|mgg|bin|key)$' -and $file -ne 'reference/anna.wav')) {
            throw "Unexpected publish file: $file"
        }
    }
    $code = Get-ChildItem -LiteralPath (Join-Path $repo 'source') -File -Recurse |
        Where-Object { $_.Extension -in @('.cs','.ps1','.py','.md') }
    foreach ($file in $code) {
        $body = [IO.File]::ReadAllText($file.FullName)
        if ($body -match 'C:\\Users\\|D:\\桌宠|D:\\Tencent|sk-[A-Za-z0-9]{12,}') {
            throw "Private path or possible key in $($file.FullName)"
        }
    }
    foreach ($version in @('GPT','DeepSeek')) {
        foreach ($relative in @('AnnaDesktopPet.exe','MusicSession.ps1','assets\pet.png','assets\dressing-room.png','voice-engine\Start-VoiceEngine.ps1')) {
            if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $repo ('build\'+$version)) $relative))) {
                throw "Incomplete $version build: $relative"
            }
        }
    }
    Write-Output "PASS: both builds, clean publish inventory, and runtime file checks ($($files.Count) files)."
} finally { Pop-Location }
