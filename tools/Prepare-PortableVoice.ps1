param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Release-Validation.ps1')
$Destination = Assert-ReleaseChild $repo $Destination
$cache = Assert-ReleaseChild $repo (Join-Path $repo 'build/portable-tts')
New-Item -ItemType Directory -Path $cache -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

$dependencies = @(
    @{ Name='python.zip'; Hash='009d6bf7e3b2ddca3d784fa09f90fe54336d5b60f0e0f305c37f400bf83cfd3b'; Url='https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip' }
    @{ Name='numpy.whl'; Hash='cd25bcecc4974d09257ffcd1f098ee778f7834c3ad767fe5db785be9a4aa9cb2'; Url='https://files.pythonhosted.org/packages/3f/6b/5610004206cf7f8e7ad91c5a85a8c71b2f2f8051a0c0c4d5916b76d6cbb2/numpy-1.26.4-cp311-cp311-win_amd64.whl' }
    @{ Name='jtalk.whl'; Hash='21abfe4809b0913dd489818f87894976f995c769099c040f6537df87355e75fa'; Url='https://files.pythonhosted.org/packages/af/83/21b0a1623f1028c0aba6ca7c594d050f47652db3d4297f8f9f406fba5427/pyopenjtalk_plus-0.4.1.post8-cp311-cp311-win_amd64.whl' }
)
foreach ($dependency in $dependencies) {
    $file = Assert-ReleaseChild $repo (Join-Path $cache $dependency.Name)
    if (-not (Test-Path -LiteralPath $file)) {
        $download = Assert-ReleaseChild $repo ($file + '.' + [Guid]::NewGuid().ToString('N') + '.download')
        try {
            Invoke-WebRequest -UseBasicParsing $dependency.Url -OutFile $download
            if ((Get-FileHash -LiteralPath $download).Hash -ne $dependency.Hash) { throw "Dependency checksum mismatch: $($dependency.Name)" }
            Move-Item -LiteralPath $download -Destination $file
        } finally { if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force } }
    }
    if ((Get-FileHash -LiteralPath $file).Hash -ne $dependency.Hash) { throw "Dependency checksum mismatch: $file" }
}

Remove-ReleaseDirectory $repo $Destination
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $cache 'python.zip'), $Destination)
foreach ($name in @('numpy.whl','jtalk.whl')) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $cache $name))
    try {
        foreach ($entry in $archive.Entries) {
            if (-not $entry.Name) { continue }
            $relative = $entry.FullName
            if ($name -eq 'numpy.whl' -and ($relative -notmatch '^(numpy/|numpy\.libs/|numpy-[^/]+\.dist-info/)' -or $relative -match '/tests?/')) { continue }
            if ($name -eq 'jtalk.whl' -and $relative -notmatch '^pyopenjtalk/([^/]+\.pyd|dictionary/[^/]+|htsvoice/[^/]+)$|^pyopenjtalk_plus-[^/]+\.dist-info/licenses/') { continue }
            $target = Assert-ReleaseChild $Destination (Join-Path $Destination $relative)
            New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target)
        }
    } finally { $archive.Dispose() }
}
Copy-Item -LiteralPath (Join-Path $repo 'source/portable-tts.py') -Destination (Join-Path $Destination 'tts.py')
