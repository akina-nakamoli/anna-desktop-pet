param([string]$Destination = (Join-Path $PSScriptRoot '..\build\stream-ui-tests'), [string]$ExtractedReleaseRoot, [string]$PackageVersion)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler is unavailable.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$references = @(
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
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File |
    Where-Object { $_.Name -notlike '*Tests.cs' } | ForEach-Object { $_.FullName })
$sources += Join-Path $PSScriptRoot 'StreamUiTests.cs'
foreach ($provider in @('GPT', 'DeepSeek')) {
    $testFolder = $Destination
    $scenario = @()
    if ($ExtractedReleaseRoot) {
        if ($PackageVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'PackageVersion is required for an extracted release test.' }
        $testFolder = Join-Path $ExtractedReleaseRoot "anna-desktop-pet-$provider-v$PackageVersion/app"
        if (-not (Test-Path -LiteralPath (Join-Path $testFolder 'ja-tts/tts.py'))) { throw "Missing extracted $provider voice bundle." }
        $scenario = @('--packaged-voice')
    }
    $testName = "StreamUiTests-$provider"
    if ($ExtractedReleaseRoot) { $testName += '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) }
    $exe = Join-Path $testFolder ($testName + '.exe')
    $defines = @()
    if ($provider -eq 'DeepSeek') { $defines += '/define:DEEPSEEK' }
    & $compiler /nologo /target:exe /platform:anycpu /langversion:5 /codepage:65001 /main:AnnaDesktopPet.StreamUiTests "/out:$exe" @defines @references @sources
    if ($LASTEXITCODE -ne 0) { throw "$provider stream UI test compilation failed." }
    try {
        & $exe @scenario
        if ($LASTEXITCODE -ne 0) { throw "$provider stream UI regression failed." }
    } finally {
        if ($ExtractedReleaseRoot) { Remove-Item -LiteralPath $exe -Force }
    }
}
