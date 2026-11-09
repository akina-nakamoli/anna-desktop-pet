param([switch]$SkipTests, [switch]$ReplaceExisting)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Release-Validation.ps1')
Push-Location $repo
try {
    $files = @(git -c core.quotePath=false ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Git file inventory failed.' }
    foreach ($file in $files) {
        if ($file -match '^(build|dist|private-assets|releases[^/]*)/' -or ($file -match '\.(exe|dll|wav|mp3|flac|mgg|bin|key)$' -and $file -ne 'reference/anna.wav')) { throw "Unexpected publish file: $file" }
        Assert-NoPrivateReleaseFile $file
    }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repo 'source') -File -Recurse | Where-Object { $_.Extension -in @('.cs','.ps1','.py','.md') }) {
        $body = [IO.File]::ReadAllText($file.FullName)
        if ($body -match 'C:\\Users\\|D:\\桌宠|D:\\Tencent|sk-[A-Za-z0-9]{12,}') { throw "Private path or possible key in $($file.FullName)" }
    }
    $null = Get-ReleaseAssetMap $repo
    foreach ($provider in @('GPT','DeepSeek')) {
        $destination = Join-Path $repo ('build\' + $provider)
        Remove-ReleaseDirectory $repo $destination
        & (Join-Path $repo 'source\build.ps1') -Destination $destination -DeepSeek:($provider -eq 'DeepSeek')
        if ($LASTEXITCODE -ne 0) { throw "$provider build failed." }
        Assert-ReleaseBuild $repo $destination '1.41.0'
    }
    if (-not $SkipTests) {
        foreach ($test in @('run-engine-path-tests.ps1','run-qq-path-tests.ps1','run-context-tests.ps1','run-general-context-tests.ps1','run-conversation-store-tests.ps1','run-semantic-memory-tests.ps1','run-character-v4-tests.ps1','run-stream-client-tests.ps1','run-stream-speech-tests.ps1','run-stream-ui-tests.ps1')) {
            $testPath = Join-Path $repo ('source\' + $test)
            if (-not (Test-Path -LiteralPath $testPath)) { throw "Missing offline regression: $test" }
            & $testPath
            if ($LASTEXITCODE -ne 0) { throw "Offline regression failed: $test" }
        }
    }
    & (Join-Path $PSScriptRoot 'Package-Release.ps1') -SkipBuild -ReplaceExisting:$ReplaceExisting
    Write-Output "PASS: both providers, publish inventory, original artwork hashes, and ZIP integrity ($($files.Count) source files)."
    if ($SkipTests) { Write-Output 'Offline regressions were explicitly skipped.' }
} finally { Pop-Location }