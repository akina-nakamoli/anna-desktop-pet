param([string]$Destination = '', [switch]$DeepSeek, [string]$Live2DModel)
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
  <assemblyIdentity version="1.42.2.0" name="AnnaDesktopPet" />
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
$appSources += (Join-Path $PSScriptRoot 'MusicConversation.cs')
$appSources += (Join-Path $PSScriptRoot 'QqCatalog.cs')
$appSources += (Join-Path $PSScriptRoot 'QqArtwork.cs')
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
$appSources += (Join-Path $PSScriptRoot 'VoicePerformance.cs')
$appSources += (Join-Path $PSScriptRoot 'EmotionVoiceWindow.cs')
$appSources += (Join-Path $PSScriptRoot 'CallAudio.cs')
$appSources += (Join-Path $PSScriptRoot 'CallWindow.cs')
$appSources += (Join-Path $PSScriptRoot 'LoginStartup.cs')
$appSources += (Join-Path $PSScriptRoot 'PetQuickMenu.cs')
$appSources += (Join-Path $PSScriptRoot 'PetActionRail.cs')
$appSources += (Join-Path $PSScriptRoot 'CompanionPanel.cs')
$appSources += (Join-Path $PSScriptRoot 'CompanionSettings.cs')
$appSources += (Join-Path $PSScriptRoot 'LocalAsr.cs')
$appSources += (Join-Path $PSScriptRoot 'AsrWorker.cs')
$appSources += (Join-Path $PSScriptRoot 'Microphone.cs')
$appSources += (Join-Path $PSScriptRoot 'VoiceInputSettings.cs')
$appSources += (Join-Path $PSScriptRoot 'AnnaCharacter.cs')
$appSources += (Join-Path $PSScriptRoot 'Live2DPetModel.cs')
$appSources += (Join-Path $PSScriptRoot 'IdleGestures.cs')
$appSources += (Join-Path $PSScriptRoot 'GhostGestures.cs')
$appSources += (Join-Path $PSScriptRoot 'Uniform2Gestures.cs')
$appSources += (Join-Path $PSScriptRoot 'Uniform2GestureDirector.cs')
$appSources += (Join-Path $PSScriptRoot 'PortraitConversationMotion.cs')
$appSources += (Join-Path $PSScriptRoot 'PortraitConversationSmoke.cs')
$appSources += (Join-Path $PSScriptRoot 'GhostPortraitSmoke.cs')
$appSources += (Join-Path $PSScriptRoot 'Uniform2PortraitSmoke.cs')
$appSources += (Join-Path $PSScriptRoot 'PortableJapaneseVoice.cs')
foreach ($appSource in $appSources) { if (-not (Test-Path -LiteralPath $appSource)) { throw "Missing source file: $appSource" } }
$petResources = @("/resource:$petAsset,AnnaDesktopPet.pet.png")
if ($DeepSeek) {
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /langversion:5 /codepage:65001 /define:DEEPSEEK "/win32manifest:$manifest" "/out:$exe" @petResources @refs @appSources
} else {
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /langversion:5 /codepage:65001 "/win32manifest:$manifest" "/out:$exe" @petResources @refs @appSources
}
if ($LASTEXITCODE -ne 0) { throw "Compiler failed: $LASTEXITCODE" }
$sourceAssets = Join-Path $PSScriptRoot 'assets'
$destAssets = Join-Path $Destination 'assets'
$modelAssets = Join-Path $sourceAssets 'live2d'
$modelTarget = Join-Path $Destination 'Live2D'
New-Item -ItemType Directory -Path $modelTarget -Force | Out-Null
$manifestFile = if ($Live2DModel) { Get-Item -LiteralPath $Live2DModel } else { Get-Item -LiteralPath (Join-Path $modelAssets 'Anna.model3.json') }
$modelAssets = $manifestFile.Directory.FullName
$modelFiles = (Get-Content -LiteralPath $manifestFile.FullName -Encoding UTF8 -Raw | ConvertFrom-Json).FileReferences
$resources = @($manifestFile.Name, $modelFiles.Moc, $modelFiles.DisplayInfo) + @($modelFiles.Textures)
foreach ($group in $modelFiles.Motions.PSObject.Properties) {
    foreach ($motion in $group.Value) { $resources += $motion.File }
}
foreach ($resource in $resources) {
    $file = Get-Item -LiteralPath (Join-Path $modelAssets $resource)
    if (!$file.FullName.StartsWith($manifestFile.Directory.FullName + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Model file outside asset folder: $resource" }
    $target = Join-Path $modelTarget $resource
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
foreach ($file in 'PurismCore.dll', 'PurismCore.LICENSE.txt') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "live2d-runtime/$file") -Destination $Destination -Force
}
New-Item -ItemType Directory -Force -Path $destAssets | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceAssets 'dressing-room.png') -Destination $destAssets -Force
if (Test-Path -LiteralPath (Join-Path $sourceAssets 'pet.png')) { Copy-Item -LiteralPath (Join-Path $sourceAssets 'pet.png') -Destination (Join-Path $destAssets 'pet.png') -Force }
Copy-Item -LiteralPath (Join-Path $sourceAssets 'chat-background.png') -Destination $destAssets -Force
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
$portrait = Join-Path $runtimeRoot '杏菜Q版头像.png'
if (-not (Test-Path -LiteralPath $portrait)) { throw 'High-resolution call portrait is missing.' }
Copy-Item -LiteralPath $portrait -Destination (Join-Path $Destination '杏菜Q版头像.png') -Force
$runtimeIcon = Join-Path $runtimeRoot '杏菜Q版头像.ico'
if (Test-Path -LiteralPath $runtimeIcon) { Copy-Item -LiteralPath $runtimeIcon -Destination (Join-Path $Destination '杏菜Q版头像.ico') -Force }
$targetOutfits = Join-Path $Destination 'outfits'
New-Item -ItemType Directory -Force -Path $targetOutfits | Out-Null
foreach ($outfitName in '校服2立绘','幽灵娘立绘','海獭服杏菜') {
    $outfit = Join-Path (Join-Path $runtimeRoot 'outfits') $outfitName
    Copy-Item -LiteralPath $outfit -Destination $targetOutfits -Recurse -Force
}
$voiceTarget = Join-Path $Destination 'voice-engine'
New-Item -ItemType Directory -Force -Path $voiceTarget | Out-Null
foreach ($file in @('Start-VoiceEngine.ps1','Register-VoiceEngine.ps1','Install-ReferenceVoice.ps1','check_engine.py','本地音色服务说明.md')) {
    $voiceSource = Join-Path $runtimeRoot ('voice-engine\' + $file)
    if (Test-Path -LiteralPath $voiceSource) { Copy-Item -LiteralPath $voiceSource -Destination (Join-Path $voiceTarget $file) -Force }
}

