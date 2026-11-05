param(
    [string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'releases')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$build = Join-Path $repo 'build'
New-Item -ItemType Directory -Force -Path $Output | Out-Null

foreach ($variant in @(@{Name='GPT'; Args=@{}}, @{Name='DeepSeek'; Args=@{DeepSeek=$true}})) {
    $destination = Join-Path $build $variant.Name
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
    if ($variant.Args.DeepSeek) {
        & (Join-Path $repo 'source/build.ps1') -Destination $destination -DeepSeek
    } else {
        & (Join-Path $repo 'source/build.ps1') -Destination $destination
    }
    if ($LASTEXITCODE -ne 0) { throw "构建 $($variant.Name) 版失败。" }

    $package = Join-Path $Output ("杏菜桌宠-$($variant.Name)-免编译版")
    if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $package | Out-Null
    Copy-Item -LiteralPath $destination -Destination (Join-Path $package '桌宠') -Recurse
    Copy-Item -LiteralPath (Join-Path $repo 'README.md'), (Join-Path $repo 'ASSETS.md'), (Join-Path $repo 'LICENSE') -Destination $package
    Compress-Archive -Path $package -DestinationPath (Join-Path $Output ("杏菜桌宠-$($variant.Name)-免编译版.zip")) -Force
}

Write-Output "发布包已生成：$Output"
