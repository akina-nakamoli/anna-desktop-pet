param([Parameter(Mandatory=$true)][string]$EnginePath, [string]$PythonPath = '')
$ErrorActionPreference = 'Stop'
$resolvedEngine = (Resolve-Path -LiteralPath $EnginePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $resolvedEngine 'api_v2.py'))) { throw 'Choose the extracted GPT-SoVITS folder containing api_v2.py.' }
$startScript = Join-Path $PSScriptRoot 'Start-VoiceEngine.ps1'
# Use a child shell so its CheckOnly exit cannot end this registration script.
$shellPath = (Get-Process -Id $PID).Path
& $shellPath -NoProfile -File $startScript -EnginePath $resolvedEngine -PythonPath $PythonPath -CheckOnly
if ($LASTEXITCODE -ne 0) { throw 'Registration stopped because the runtime/model preflight did not pass.' }
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'engine-path.txt'), $resolvedEngine, (New-Object System.Text.UTF8Encoding($false)))
if (-not [string]::IsNullOrWhiteSpace($PythonPath)) {
    $resolvedPython = (Resolve-Path -LiteralPath $PythonPath).Path
    [System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'python-path.txt'), $resolvedPython, (New-Object System.Text.UTF8Encoding($false)))
}
Write-Output 'Registered this engine path. Run Start-VoiceEngine.ps1 to load the local TTS service.'
