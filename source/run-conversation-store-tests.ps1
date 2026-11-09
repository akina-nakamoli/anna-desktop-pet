param([string]$Destination = (Join-Path $PSScriptRoot '..\build\conversation-store-tests'))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$references = @('System.Web.Extensions.dll','System.Security.dll') | ForEach-Object { "/reference:$framework\$_" }
$sources = @((Join-Path $PSScriptRoot 'ConversationHistory.cs'), (Join-Path $PSScriptRoot 'ConversationStoreTests.cs'))
foreach ($provider in @('GPT','DeepSeek')) {
    $exe = Join-Path $Destination ("ConversationStoreTests-$provider.exe")
    $defines = @('/define:CONVERSATION_STORE_TEST')
    if ($provider -eq 'DeepSeek') { $defines += '/define:DEEPSEEK' }
    & $compiler /nologo /target:exe /platform:anycpu /langversion:5 /codepage:65001 /main:AnnaDesktopPet.ConversationStoreTests "/out:$exe" @defines @references @sources
    if ($LASTEXITCODE -ne 0) { throw "$provider store test compilation failed." }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "$provider store regression failed." }
}
