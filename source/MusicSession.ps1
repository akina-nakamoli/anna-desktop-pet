param([string]$Command = 'status', [ValidatePattern('^[a-f0-9]{0,64}$')][string]$KnownTrackKey = '')
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
function Select-QqSession($manager) {
    $current = $manager.GetCurrentSession()
    $best = $null
    $bestRank = -1
    foreach ($candidate in $manager.GetSessions()) {
        if ($candidate.SourceAppUserModelId -notmatch 'QQMusic|QQ\s*音乐') { continue }
        try { $state = $candidate.GetPlaybackInfo().PlaybackStatus.ToString() } catch { continue }
        $rank = switch ($state) { 'Playing' { 4 } 'Paused' { 2 } default { 0 } }
        if ($current -and $candidate.SourceAppUserModelId -eq $current.SourceAppUserModelId) { $rank++ }
        if ($rank -gt $bestRank) { $best = $candidate; $bestRank = $rank }
    }
    return $best
}
function Get-CoverBase64($thumbnail) {
    # Windows PowerShell returns this WinRT interface as System.__ComObject. Its
    # Size and GetInputStreamAt members are only exposed through the interface.
    $raw = Await-Result ($thumbnail.OpenReadAsync()) ([Windows.Storage.Streams.IRandomAccessStreamWithContentType,Windows.Storage.Streams,ContentType=WindowsRuntime])
    $randomStream = [Windows.Storage.Streams.IRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
    $stream = $null
    $memory = $null
    try {
        $size = $randomStream.GetProperty('Size').GetValue($raw, $null)
        if ($size -le 0 -or $size -gt 16MB) { return }
        $winrtInput = $randomStream.GetMethod('GetInputStreamAt').Invoke($raw, @([uint64]0))
        $reader = [IO.WindowsRuntimeStreamExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsStreamForRead' -and $_.GetParameters().Count -eq 1 } | Select-Object -First 1
        $stream = $reader.Invoke($null, @($winrtInput))
        $memory = New-Object IO.MemoryStream
        $stream.CopyTo($memory)
        if ($memory.Length -ne $size) { return }
        if ($size -le 1536KB) { return [Convert]::ToBase64String($memory.ToArray()) }

        # Bound the JSON response while retaining artwork from large thumbnails.
        Add-Type -AssemblyName PresentationCore
        $memory.Position = 0
        $image = New-Object Windows.Media.Imaging.BitmapImage
        $image.BeginInit()
        $image.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
        $image.DecodePixelWidth = 360
        $image.StreamSource = $memory
        $image.EndInit()
        $jpeg = New-Object Windows.Media.Imaging.JpegBitmapEncoder
        $jpeg.QualityLevel = 88
        $jpeg.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
        $encoded = New-Object IO.MemoryStream
        try {
            $jpeg.Save($encoded)
            if ($encoded.Length -le 1536KB) { return [Convert]::ToBase64String($encoded.ToArray()) }
        } finally { $encoded.Dispose() }
    } finally {
        if ($stream) { $stream.Dispose() }
        if ($memory) { $memory.Dispose() }
        if ($raw -and [Runtime.InteropServices.Marshal]::IsComObject($raw)) {
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($raw)
        }
    }
}
try {
    $manager = Await-Result ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
    $session = Select-QqSession $manager
    if (-not $session) { throw '未检测到 QQ 音乐媒体会话，请先点播一首歌。' }
    if ($Command -eq 'details') {
        $media = Await-Result ($session.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
        $playback = $session.GetPlaybackInfo()
        $identity = [Text.Encoding]::UTF8.GetBytes(([String]::Join([char]31, @($media.Title, $media.Artist, $media.AlbumTitle))))
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $key = [BitConverter]::ToString($sha.ComputeHash($identity)).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() }
        $data = [ordered]@{
            title = $media.Title; artist = $media.Artist; album = $media.AlbumTitle; status = $playback.PlaybackStatus.ToString()
            previous = $playback.Controls.IsPreviousEnabled; next = $playback.Controls.IsNextEnabled
            play = $playback.Controls.IsPlayEnabled; pause = $playback.Controls.IsPauseEnabled
            duration = 0; position = 0; key = $key; art = $null
        }
        try {
            $time = $session.GetTimelineProperties()
            $data.duration = [Math]::Max(0, ($time.EndTime - $time.StartTime).TotalSeconds)
            $offset = ($time.Position - $time.StartTime).TotalSeconds
            if ($data.status -eq 'Playing' -and $time.LastUpdatedTime.Year -gt 2000) {
                $offset += [Math]::Max(0, ([DateTimeOffset]::Now - $time.LastUpdatedTime).TotalSeconds) * $playback.PlaybackRate
            }
            $data.position = [Math]::Max(0, [Math]::Min($data.duration, $offset))
        } catch { }
        if ($media.Thumbnail -and $key -ne $KnownTrackKey) {
            try { $data.art = Get-CoverBase64 $media.Thumbnail } catch { $data.art = $null }
        }
        ConvertTo-Json -InputObject $data -Compress
        exit 0
    }
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
