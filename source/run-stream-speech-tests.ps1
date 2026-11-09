param([string]$Destination = (Join-Path $PSScriptRoot '..\build\stream-speech-tests'))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$exe = Join-Path $Destination 'SpeechStreamQueueTests.exe'
$sources = @((Join-Path $PSScriptRoot 'SpeechStreamQueue.cs'), (Join-Path $PSScriptRoot 'SpeechStreamQueueTests.cs'))
& (Join-Path $framework 'csc.exe') /nologo /target:exe /langversion:5 /codepage:65001 /main:AnnaDesktopPet.SpeechStreamQueueTests "/out:$exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Speech stream queue test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Speech stream queue tests failed.' }
$serviceExe = Join-Path $Destination 'SpeechStreamServiceTests.exe'
$serviceSources = @('SpeechStreamQueue.cs','SpeechService.cs','PortableJapaneseVoice.cs','GptSovitsClient.cs','SpeechStreamServiceTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$references = @("/reference:$framework\WPF\WindowsBase.dll", "/reference:$framework\WPF\PresentationCore.dll", "/reference:$framework\WPF\System.Speech.dll", "/reference:$framework\System.Net.Http.dll", "/reference:$framework\System.Web.Extensions.dll")
& (Join-Path $framework 'csc.exe') /nologo /target:exe /langversion:5 /codepage:65001 /main:AnnaDesktopPet.SpeechStreamServiceTests "/out:$serviceExe" @references @serviceSources
if ($LASTEXITCODE -ne 0) { throw 'Speech stream service test compilation failed.' }
& $serviceExe
if ($LASTEXITCODE -ne 0) { throw 'Speech stream service tests failed.' }
