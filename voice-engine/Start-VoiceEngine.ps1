param(
    [string]$EnginePath = '',
    [string]$PythonPath = '',
    [ValidateRange(1024,65535)][int]$Port = 9880,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($EnginePath)) {
    $savedPath = Join-Path $PSScriptRoot 'engine-path.txt'
    if (Test-Path -LiteralPath $savedPath) { $EnginePath = (Get-Content -LiteralPath $savedPath -Raw -Encoding UTF8).Trim() }
}
if ([string]::IsNullOrWhiteSpace($EnginePath)) {
    $localCandidate = Join-Path $PSScriptRoot 'GPT-SoVITS'
    if (Test-Path -LiteralPath (Join-Path $localCandidate 'api_v2.py')) { $EnginePath = $localCandidate }
}
if ([string]::IsNullOrWhiteSpace($EnginePath)) {
    throw 'No GPT-SoVITS engine has been configured. Extract the official Windows package, then run Register-VoiceEngine.ps1 -EnginePath <folder containing api_v2.py>.'
}
$EnginePath = (Resolve-Path -LiteralPath $EnginePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $EnginePath 'api_v2.py'))) { throw 'The selected folder does not contain api_v2.py.' }
if ([string]::IsNullOrWhiteSpace($PythonPath)) {
    $savedPythonPath = Join-Path $PSScriptRoot 'python-path.txt'
    if (Test-Path -LiteralPath $savedPythonPath) { $PythonPath = (Get-Content -LiteralPath $savedPythonPath -Raw -Encoding UTF8).Trim() }
}
if ([string]::IsNullOrWhiteSpace($PythonPath)) {
    foreach ($relativePython in @('runtime\python.exe', '.venv\Scripts\python.exe', 'venv\Scripts\python.exe')) {
        $candidatePython = Join-Path $EnginePath $relativePython
        if (Test-Path -LiteralPath $candidatePython) { $PythonPath = $candidatePython; break }
    }
}
if ([string]::IsNullOrWhiteSpace($PythonPath)) { throw 'The selected engine has no portable Python runtime. Use the official Windows integrated package or provide -PythonPath for a prepared environment.' }
$PythonPath = (Resolve-Path -LiteralPath $PythonPath).Path
$configPath = Join-Path $EnginePath 'GPT_SoVITS\configs\tts_infer.yaml'
if (-not (Test-Path -LiteralPath $configPath)) { throw 'Missing GPT_SoVITS/configs/tts_infer.yaml. Complete the official engine installation first.' }

$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
$checkScript = Join-Path $PSScriptRoot 'check_engine.py'
Push-Location -LiteralPath $EnginePath
try {
    & $PythonPath $checkScript $EnginePath $configPath
    if ($LASTEXITCODE -ne 0) { throw 'Engine preflight failed. The engine has not been started; see the missing runtime/model items above.' }
} finally { Pop-Location }
if ($CheckOnly) { Write-Output 'Preflight passed. This checks prerequisites only; it does not synthesize speech.'; exit 0 }

$client = New-Object System.Net.Sockets.TcpClient
try {
    $connect = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
    $inUse = $connect.AsyncWaitHandle.WaitOne(600) -and $client.Connected
} catch { $inUse = $false } finally { $client.Close() }
if ($inUse) { throw "Port $Port is already in use. No existing process was stopped. If GPT-SoVITS is already running, use its address in the pet settings." }

$logDir = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stdout = Join-Path $logDir ("engine-$stamp.stdout.log")
$stderr = Join-Path $logDir ("engine-$stamp.stderr.log")
$arguments = @('-u', 'api_v2.py', '-a', '127.0.0.1', '-p', $Port.ToString(), '-c', ('"' + $configPath + '"'))
$process = Start-Process -FilePath $PythonPath -ArgumentList $arguments -WorkingDirectory $EnginePath -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
$state = [ordered]@{ pid = $process.Id; engine_path = $EnginePath; port = $Port; started_utc = [DateTime]::UtcNow.ToString('o'); stdout = $stdout; stderr = $stderr }
$state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'engine-process.json') -Encoding UTF8
Write-Output "Started Python process $($process.Id), loading models. It will listen only on http://127.0.0.1:$Port ."
Write-Output "Startup logs: $stderr"
Write-Output 'Model startup can take time. Process creation is not proof that TTS is ready; use the pet voice test after startup.'
