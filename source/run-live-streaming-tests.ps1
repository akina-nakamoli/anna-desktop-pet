param(
    [switch]$RunLive,
    
    [ValidateSet('All', 'GPT', 'DeepSeek')][string]$Provider = 'DeepSeek',
    [string]$Destination = (Join-Path $PSScriptRoot '..\build\stream-live-tests')
)
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
$sources += Join-Path $PSScriptRoot 'LiveStreamingTests.cs'
$providers = @($Provider)
if ($Provider -eq 'All') { $providers = @('GPT', 'DeepSeek') }
foreach ($current in $providers) {
    $exe = Join-Path $Destination ("LiveStreamingTests-$current.exe")
    $defines = @()
    if ($current -eq 'DeepSeek') { $defines += '/define:DEEPSEEK' }
    & $compiler /nologo /target:exe /platform:anycpu /langversion:5 /codepage:65001 /main:AnnaDesktopPet.LiveStreamingTests "/out:$exe" @defines @references @sources
    if ($LASTEXITCODE -ne 0) { throw "$current live test compilation failed." }
    if ($RunLive) {
        $arguments = @('--live')

        & $exe @arguments
        if ($LASTEXITCODE -ne 0) { throw "$current live semantic probes failed or were blocked (exit $LASTEXITCODE)." }
    } else {
        Write-Output "$current live harness compiled only. Use -RunLive -Provider $current for three synthetic streaming probes."
    }
}
