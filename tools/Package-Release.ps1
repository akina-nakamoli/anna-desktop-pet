param(
    [string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'releases'),
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.41.0',
    [switch]$SkipBuild,
    [switch]$ReplaceExisting
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Split-Path $PSScriptRoot -Parent)).Path
. (Join-Path $PSScriptRoot 'Release-Validation.ps1')
$Output = Assert-ReleaseChild $repo $Output
$build = Join-Path $repo 'build'
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$checksums = @()
# Refuse to replace even one package until both versioned names are checked.
foreach ($name in @('GPT','DeepSeek')) {
    $stem = 'anna-desktop-pet-' + $name + '-v' + $Version
    foreach ($path in @((Join-Path $Output $stem), (Join-Path $Output ($stem + '.zip')))) {
        $null = Assert-ReleaseChild $repo $path
        if ((Test-Path -LiteralPath $path) -and -not $ReplaceExisting) { throw "Package already exists: $path. Use -ReplaceExisting only to rebuild this version." }
    }
}
foreach ($variant in @(@{Name='GPT'; Args=@{}}, @{Name='DeepSeek'; Args=@{DeepSeek=$true}})) {
    $destination = Join-Path $build $variant.Name
    if (-not $SkipBuild) {
        Remove-ReleaseDirectory $repo $destination
        if ($variant.Args.DeepSeek) {
            & (Join-Path $repo 'source/build.ps1') -Destination $destination -DeepSeek
        } else {
            & (Join-Path $repo 'source/build.ps1') -Destination $destination
        }
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $($variant.Name)" }
    }
    Assert-ReleaseBuild $repo $destination $Version
    $packageName = 'anna-desktop-pet-' + $variant.Name + '-v' + $Version
    $package = Join-Path $Output $packageName
    Remove-ReleaseDirectory $repo $package
    New-Item -ItemType Directory -Force -Path $package | Out-Null
    Copy-Item -LiteralPath $destination -Destination (Join-Path $package 'app') -Recurse
    Copy-Item -LiteralPath (Join-Path $repo 'README.md'), (Join-Path $repo 'ASSETS.md'), (Join-Path $repo 'LICENSE') -Destination $package
    $zip = Join-Path $Output ($packageName + '.zip')
    $null = Assert-ReleaseChild $repo $zip
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -LiteralPath $package -DestinationPath $zip
    Assert-ReleaseZip $repo $zip $packageName $destination
    $checksums += (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)
    Write-Output "PASS: $packageName ZIP contents match the verified build."
}
[IO.File]::WriteAllLines((Join-Path $Output ('SHA256SUMS-v' + $Version + '.txt')), $checksums, [Text.Encoding]::ASCII)
Write-Output "Release packages: $Output"