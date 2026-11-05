param([string]$Command = 'status')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media.Control,ContentType=WindowsRuntime]
function Await-Result($operation, $resultType) {
    $method = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
    $task = $method.MakeGenericMethod($resultType).Invoke($null,@($operation))
    if (-not $task.Wait(5000)) { throw '音乐客户端响应超时' }
    $task.Result
}
try {
    $manager = Await-Result ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
    $session = $manager.GetSessions() | Where-Object { $_.SourceAppUserModelId -match 'QQMusic' } | Select-Object -First 1
    if (-not $session) { throw '未检测到 QQ 音乐媒体会话，请先点播一首歌。' }
    if ($Command -ne 'status') {
        $operation = switch ($Command) {
            'pause' { $session.TryPauseAsync() }
            'play' { $session.TryPlayAsync() }
            'next' { $session.TrySkipNextAsync() }
            'previous' { $session.TrySkipPreviousAsync() }
            default { throw '未知音乐操作' }
        }
        if (-not (Await-Result $operation ([bool]))) { throw 'QQ 音乐未接受操作。' }
    }
    $media = Await-Result ($session.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
    ($media.Title + "`n" + $media.Artist + "`n" + $session.GetPlaybackInfo().PlaybackStatus)
} catch { Write-Output $_.Exception.Message; exit 1 }
