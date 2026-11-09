param([string]$Destination = (Join-Path $PSScriptRoot '..\build\qq-path-tests'))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$exe = Join-Path $Destination 'QqPathTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /langversion:5 /codepage:65001 /define:QQ_PATH_TEST_STANDALONE /main:AnnaDesktopPet.QqPathTests "/out:$exe" "/reference:$framework\System.Web.Extensions.dll" (Join-Path $PSScriptRoot 'QqCatalog.cs') (Join-Path $PSScriptRoot 'QqPathTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'QQ path test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'QQ portable configuration tests failed.' }
