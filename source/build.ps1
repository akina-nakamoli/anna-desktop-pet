param([string]$Destination = '', [switch]$DeepSeek, [switch]$PortableVoice)
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path (Split-Path $PSScriptRoot -Parent) ('build\' + @('GPT','DeepSeek')[[int][bool]$DeepSeek]) }
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
  <assemblyIdentity version="1.41.4.0" name="AnnaDesktopPet" />
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
$appSources += (Join-Path $PSScriptRoot 'PortableJapaneseVoice.cs')
$appSources += (Join-Path $PSScriptRoot 'AutonomousMotion.cs')
$appSources += (Join-Path $PSScriptRoot 'MusicRouter.cs')
$appSources += (Join-Path $PSScriptRoot 'MusicConversation.cs')
$appSources += (Join-Path $PSScriptRoot 'QqCatalog.cs')
$appSources += (Join-Path $PSScriptRoot 'MusicPanel.cs')
$appSources += (Join-Path $PSScriptRoot 'PetMemory.cs')
$appSources += (Join-Path $PSScriptRoot 'SemanticMemory.cs')
$appSources += (Join-Path $PSScriptRoot 'ConversationHistory.cs')
$appSources += (Join-Path $PSScriptRoot 'ConversationContext.cs')
$appSources += (Join-Path $PSScriptRoot 'GptStreaming.cs')
$appSources += (Join-Path $PSScriptRoot 'GptChatStreaming.cs')
$appSources += (Join-Path $PSScriptRoot 'SpeechStreamQueue.cs')
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
foreach ($animation in @('animation-wave.png','animation-blink.png','animation-speak.png','animation-happy.png')) {
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
if (-not (Test-Path -LiteralPath $dressingRoom)) { throw 'Missing dressing-room.png' }
Copy-Item -LiteralPath $dressingRoom -Destination (Join-Path $destAssets 'dressing-room.png') -Force
foreach ($animation in @('animation-wave.png','animation-blink.png','animation-speak.png','animation-happy.png')) {
    $asset = Join-Path $sourceAssets $animation
    if (Test-Path -LiteralPath $asset) { Copy-Item -LiteralPath $asset -Destination (Join-Path $destAssets $animation) -Force }
}
$referenceAsset = Join-Path (Split-Path $PSScriptRoot -Parent) 'reference\anna.wav'
if (Test-Path -LiteralPath $referenceAsset) {
    $referenceDestination = Join-Path $Destination 'reference'
    New-Item -ItemType Directory -Force -Path $referenceDestination | Out-Null
    Copy-Item -LiteralPath $referenceAsset -Destination (Join-Path $referenceDestination 'anna.wav') -Force
}
[System.IO.File]::WriteAllText((Join-Path $Destination 'AnnaDesktopPet.exe.config'), '<?xml version="1.0"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>', (New-Object System.Text.UTF8Encoding($false)))
Write-Output $exe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'local_asr.py') -Destination (Join-Path $Destination 'local_asr.py') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MusicSession.ps1') -Destination (Join-Path $Destination 'MusicSession.ps1') -Force
# Trial builds use the same runtime companions as the shared app, including outfit selection.
$runtimeRoot = Split-Path -Parent $PSScriptRoot
$runtimeIcon = Join-Path $runtimeRoot '杏菜Q版头像.ico'
if (Test-Path -LiteralPath $runtimeIcon) { Copy-Item -LiteralPath $runtimeIcon -Destination (Join-Path $Destination '杏菜Q版头像.ico') -Force }
foreach ($folder in @('outfits')) {
    $runtimeFolder = Join-Path $runtimeRoot $folder
    if (Test-Path -LiteralPath $runtimeFolder) {
        $targetFolder = Join-Path $Destination $folder
        New-Item -ItemType Directory -Force -Path $targetFolder | Out-Null
        Get-ChildItem -LiteralPath $runtimeFolder | Copy-Item -Destination $targetFolder -Recurse -Force
    }
}
$voiceTarget = Join-Path $Destination 'voice-engine'
New-Item -ItemType Directory -Force -Path $voiceTarget | Out-Null
foreach ($file in @('Start-VoiceEngine.ps1','Register-VoiceEngine.ps1','Get-OfficialPackage.ps1','check_engine.py','本地音色服务说明.md')) {
    $voiceSource = Join-Path $runtimeRoot ('voice-engine\' + $file)
    if (Test-Path -LiteralPath $voiceSource) { Copy-Item -LiteralPath $voiceSource -Destination (Join-Path $voiceTarget $file) -Force }
}

if ($PortableVoice) {
    $prepare = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools/Prepare-PortableVoice.ps1'
    & $prepare -Destination (Join-Path $Destination 'ja-tts')
}
