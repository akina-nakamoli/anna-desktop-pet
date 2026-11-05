param([string]$Destination = (Join-Path $PSScriptRoot 'build'), [switch]$DeepSeek)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    $compiler = Join-Path $framework 'csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4 compiler is unavailable.' }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$manifestBody = @'
<?xml version="1.0" encoding="utf-8"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity version="1.37.0.0" name="AnnaDesktopPet" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3"><security><requestedPrivileges><requestedExecutionLevel level="asInvoker" uiAccess="false" /></requestedPrivileges></security></trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1"><application><supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" /></application></compatibility>
</assembly>
'@
[System.IO.File]::WriteAllText($manifest, $manifestBody, (New-Object System.Text.UTF8Encoding($false)))
$exe = Join-Path $Destination 'AnnaDesktopPet.exe'
$petAsset = Join-Path $PSScriptRoot 'assets\pet.png'
if (-not (Test-Path -LiteralPath $petAsset)) { throw 'Missing character image: assets\pet.png' }
$refs = @(
    "/reference:$framework\WPF\PresentationFramework.dll",
    "/reference:$framework\WPF\PresentationCore.dll",
    "/reference:$framework\WPF\WindowsBase.dll",
    "/reference:$framework\System.Xaml.dll",
    "/reference:$framework\System.Web.Extensions.dll",
    "/reference:$framework\System.Security.dll",
    "/reference:$framework\System.Net.Http.dll",
    "/reference:$framework\WPF\System.Speech.dll",
    "/reference:$framework\System.Windows.Forms.dll",
    "/reference:$framework\System.Drawing.dll"
)
$appSources = @((Join-Path $PSScriptRoot 'Pet.cs'), (Join-Path $PSScriptRoot 'GptUi.cs'), (Join-Path $PSScriptRoot 'GptClient.cs'), (Join-Path $PSScriptRoot 'MotionController.cs'), (Join-Path $PSScriptRoot 'SpeechSettings.cs'), (Join-Path $PSScriptRoot 'PetMediaUi.cs'), (Join-Path $PSScriptRoot 'SpeechService.cs'))
$appSources += (Join-Path $PSScriptRoot 'GptSovitsClient.cs')
$appSources += (Join-Path $PSScriptRoot 'AutonomousMotion.cs')
$appSources += (Join-Path $PSScriptRoot 'MusicRouter.cs')
$appSources += (Join-Path $PSScriptRoot 'QqCatalog.cs')
$appSources += (Join-Path $PSScriptRoot 'MusicPanel.cs')
$appSources += (Join-Path $PSScriptRoot 'PetMemory.cs')
$appSources += (Join-Path $PSScriptRoot 'Routine.cs')
$appSources += (Join-Path $PSScriptRoot 'VoiceAutoStart.cs')
$appSources += (Join-Path $PSScriptRoot 'Appearance.cs')
$appSources += (Join-Path $PSScriptRoot 'LifeActivity.cs')
$appSources += (Join-Path $PSScriptRoot 'PanelTheme.cs')
$appSources += (Join-Path $PSScriptRoot 'MusicFavorites.cs')
$appSources += (Join-Path $PSScriptRoot 'DesktopShell.cs')
$appSources += (Join-Path $PSScriptRoot 'VoiceChat.cs')
$appSources += (Join-Path $PSScriptRoot 'LoginStartup.cs')
$appSources += (Join-Path $PSScriptRoot 'PetQuickMenu.cs')
$appSources += (Join-Path $PSScriptRoot 'LocalAsr.cs')
$appSources += (Join-Path $PSScriptRoot 'AsrWorker.cs')
$appSources += (Join-Path $PSScriptRoot 'Microphone.cs')
$appSources += (Join-Path $PSScriptRoot 'VoiceInputSettings.cs')
$appSources += (Join-Path $PSScriptRoot 'LayeredWaveRig.cs')
$appSources += (Join-Path $PSScriptRoot 'RestPoseLayers.cs')
$appSources += (Join-Path $PSScriptRoot 'SkeletalWave.cs')
$appSources += (Join-Path $PSScriptRoot 'PngSequence.cs')
$appSources += (Join-Path $PSScriptRoot 'AnnaCharacter.cs')
foreach ($appSource in $appSources) { if (-not (Test-Path -LiteralPath $appSource)) { throw "Missing source file: $appSource" } }
$petResources = @("/resource:$petAsset,AnnaDesktopPet.pet.png")
$poseAsset = Join-Path $PSScriptRoot 'assets\poses.png'
if (Test-Path -LiteralPath $poseAsset) { $petResources += "/resource:$poseAsset,AnnaDesktopPet.poses.png" }
$chatBackground = Join-Path $PSScriptRoot 'assets\chat-background.png'
if (Test-Path -LiteralPath $chatBackground) { $petResources += "/resource:$chatBackground,AnnaDesktopPet.chat-background.png" }
foreach ($animation in @('animation-wave.png','animation-blink.png','animation-speak.png','animation-happy.png','animation-walk.png','animation-walk8.png')) {
    $asset = Join-Path $PSScriptRoot ('assets\' + $animation)
    if (Test-Path -LiteralPath $asset) { $petResources += "/resource:$asset,AnnaDesktopPet.$animation" }
}
if ($DeepSeek) {
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /langversion:5 /codepage:65001 /define:DEEPSEEK "/win32manifest:$manifest" "/out:$exe" @petResources @refs @appSources
} else {
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /langversion:5 /codepage:65001 "/win32manifest:$manifest" "/out:$exe" @petResources @refs @appSources
}
if ($LASTEXITCODE -ne 0) { throw "Compiler failed: $LASTEXITCODE" }
$sourceAssets = Join-Path $PSScriptRoot 'assets'
$destAssets = Join-Path $Destination 'assets'
New-Item -ItemType Directory -Force -Path $destAssets | Out-Null
if (Test-Path -LiteralPath (Join-Path $sourceAssets 'pet.png')) { Copy-Item -LiteralPath (Join-Path $sourceAssets 'pet.png') -Destination (Join-Path $destAssets 'pet.png') -Force }
if (Test-Path -LiteralPath $poseAsset) { Copy-Item -LiteralPath $poseAsset -Destination (Join-Path $destAssets 'poses.png') -Force }
if (Test-Path -LiteralPath $chatBackground) { Copy-Item -LiteralPath $chatBackground -Destination (Join-Path $destAssets 'chat-background.png') -Force }
$dressingRoom = Join-Path $sourceAssets 'dressing-room.png'
if (Test-Path -LiteralPath $dressingRoom) { Copy-Item -LiteralPath $dressingRoom -Destination (Join-Path $destAssets 'dressing-room.png') -Force }
foreach ($animation in @('animation-wave.png','animation-blink.png','animation-speak.png','animation-happy.png','animation-walk.png','animation-walk8.png')) {
    $asset = Join-Path $sourceAssets $animation
    if (Test-Path -LiteralPath $asset) { Copy-Item -LiteralPath $asset -Destination (Join-Path $destAssets $animation) -Force }
}
$referenceAsset = Join-Path $PSScriptRoot 'reference\anna.wav'
if (Test-Path -LiteralPath $referenceAsset) {
    $referenceDestination = Join-Path $Destination 'reference'
    New-Item -ItemType Directory -Force -Path $referenceDestination | Out-Null
    Copy-Item -LiteralPath $referenceAsset -Destination (Join-Path $referenceDestination 'anna.wav') -Force
}
[System.IO.File]::WriteAllText((Join-Path $Destination 'AnnaDesktopPet.exe.config'), '<?xml version="1.0"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>', (New-Object System.Text.UTF8Encoding($false)))
Write-Output $exe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'local_asr.py') -Destination (Join-Path $Destination 'local_asr.py') -Force
$musicScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'MusicSession.ps1'
if (Test-Path -LiteralPath $musicScript) { Copy-Item -LiteralPath $musicScript -Destination (Join-Path $Destination 'MusicSession.ps1') -Force }
$engineScripts = Join-Path (Split-Path $PSScriptRoot -Parent) 'voice-engine'
if (Test-Path -LiteralPath $engineScripts) {
    $engineOut = Join-Path $Destination 'voice-engine'
    New-Item -ItemType Directory -Path $engineOut -Force | Out-Null
    foreach ($script in @('Start-VoiceEngine.ps1','Register-VoiceEngine.ps1','check_engine.py')) {
        $source = Join-Path $engineScripts $script
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $engineOut $script) -Force }
    }
}
