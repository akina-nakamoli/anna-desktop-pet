param([string]$Destination = (Join-Path $PSScriptRoot '..\build\memory-tests'))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$sources = @((Join-Path $PSScriptRoot 'PetMemory.cs'), (Join-Path $PSScriptRoot 'SemanticMemoryTests.cs'))
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'SemanticMemory.cs')) { $sources += Join-Path $PSScriptRoot 'SemanticMemory.cs' }
$exe = Join-Path $Destination 'SemanticMemoryTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /langversion:5 /codepage:65001 /define:MEMORY_TEST_STANDALONE /main:AnnaDesktopPet.SemanticMemoryTests "/out:$exe" "/reference:$framework\System.Security.dll" "/reference:$framework\System.Web.Extensions.dll" @sources
if ($LASTEXITCODE -ne 0) { throw 'Memory test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Memory regression tests failed.' }
