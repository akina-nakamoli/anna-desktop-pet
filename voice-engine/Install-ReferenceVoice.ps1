$ErrorActionPreference = 'Stop'
$folder = 'GPT-SoVITS-v2pro-20250604'
$archive = Join-Path $PSScriptRoot ($folder + '.7z')
$url = "https://huggingface.co/lj1995/GPT-SoVITS-windows-package/resolve/fb387b7a65a5441e5e3985f4ab9b721a9d455363/$folder.7z"
$engine = Join-Path $PSScriptRoot $folder
$ready = Join-Path $engine '.anna-runtime-ready'
if (!(Test-Path -LiteralPath $ready)) {
    Write-Host '首次安装杏菜配音：下载 8.19 GB，可关闭窗口并重新运行以续传。请预留引擎解压空间。'
    if (!(Test-Path -LiteralPath $archive) -or (Get-Item -LiteralPath $archive).Length -ne 8185086602) {
        & curl.exe --fail --location --continue-at - --retry 2 --output $archive $url
        if ($LASTEXITCODE -ne 0) { throw '下载未完成。请检查网络后重试。' }
    }
    if ((Get-FileHash -LiteralPath $archive).Hash -ne 'BD60D0796553FF05D8568136E199C13E0DC22EBE2ED24273134E34ED6F215CD6') { throw '下载校验失败，请移走损坏的压缩包并重试。' }
    & tar.exe -xf $archive -C $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { throw '解压失败，请检查磁盘空间。' }
    $python = Join-Path $engine 'runtime/python.exe'
    & $python -c "import torch,sys; sys.exit(0 if torch.cuda.is_available() else 42)"
    if ($LASTEXITCODE -eq 42) {
        $config = Join-Path $engine 'GPT_SoVITS/configs/tts_infer.yaml'
        & $python -c "import sys,yaml,pathlib; p=pathlib.Path(sys.argv[1]); d=yaml.safe_load(p.read_text(encoding='utf-8')); d['custom'].update(device='cpu',is_half=False); p.write_text(yaml.safe_dump(d,allow_unicode=True),encoding='utf-8')" $config
    }
    if ($LASTEXITCODE -ne 0) { throw '引擎运行环境检查失败。' }
    New-Item -ItemType File -Path $ready -Force | Out-Null
}
& (Join-Path $PSScriptRoot 'Register-VoiceEngine.ps1') -EnginePath $engine
Write-Host '杏菜参考音色安装成功。请重新打开桌宠，等待模型加载后聊天。'
