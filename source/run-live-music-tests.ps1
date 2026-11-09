param([switch]$RunLive, [switch]$PretenderOnly, [switch]$ClarifyFollowUpOnly, [ValidateSet('All', 'GPT', 'DeepSeek')][string]$Provider = 'All', [string]$Destination = (Join-Path $PSScriptRoot '..\build\live-context-tests'))
$ErrorActionPreference = 'Stop'
if (-not $RunLive) { Write-Output 'Not run. Add -RunLive to use existing configured API keys. Optional: -Provider DeepSeek -PretenderOnly for one targeted classification case.'; return }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$compiler = Join-Path $framework 'csc.exe'
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
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Where-Object { $_.Name -notlike '*Tests.cs' } | ForEach-Object { $_.FullName })
$sources += Join-Path $PSScriptRoot 'LiveMusicIntentTests.cs'
$overallExitCode = 0
$providers = if ($Provider -eq 'All') { @('GPT', 'DeepSeek') } else { @($Provider) }
foreach ($selectedProvider in $providers) {
    $exe = Join-Path $Destination ("LiveMusicIntentTests-$selectedProvider.exe")
    $defines = @()
    if ($selectedProvider -eq 'DeepSeek') { $defines += '/define:DEEPSEEK' }
    & $compiler /nologo /target:exe /platform:anycpu /langversion:5 /codepage:65001 /main:AnnaDesktopPet.LiveMusicIntentTests "/out:$exe" @defines @references @sources
    if ($LASTEXITCODE -ne 0) { throw "$selectedProvider live test compilation failed." }
    $testArguments = @('--live')
    if ($PretenderOnly) { $testArguments += '--pretender-only' }
    if ($ClarifyFollowUpOnly) { $testArguments += '--clarify-follow-up-only' }
    & $exe @testArguments
    $providerExitCode = $LASTEXITCODE
    Write-Output ("{0} live test exit code: {1}" -f $selectedProvider, $providerExitCode)
    if ($providerExitCode -eq 1) { $overallExitCode = 1 }
    elseif ($providerExitCode -ne 0 -and $overallExitCode -eq 0) { $overallExitCode = 2 }
}
exit $overallExitCode
