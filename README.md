# 杏菜桌宠 v1.41.0

Windows 桌面宠物，提供 GPT 与 DeepSeek 两个版本。保留杏菜人物设定、三套服装、聊天背景与衣帽间，并支持文字/语音聊天、长期记忆、音乐控制及日常提醒。项目与原作及配音演员无官方关联，素材使用范围见 [ASSETS.md](ASSETS.md)。

## 下载并运行

在 GitHub **Releases** 下载一种版本：

- `anna-desktop-pet-GPT-v1.41.0.zip`
- `anna-desktop-pet-DeepSeek-v1.41.0.zip`

完整解压 ZIP，进入 `anna-desktop-pet-<版本>-v1.41.0/app`，双击 `AnnaDesktopPet.exe`。不要只取出 EXE；`assets`、`outfits`、`reference` 和 `voice-engine` 等目录需要与程序保存在一起。Windows 需要 .NET Framework 4.8。GPT 与 DeepSeek 一次选择一个运行。

首次使用在设置中填写自己的 API Key。两种服务分别使用自己的 Key 和账户额度，API 调用可能产生费用；没有 Key 时仍可打开桌宠及本地功能。

## 本版功能

- 中文聊天与日语配音，真实流式文字回复；新消息或鼠标后退侧键可打断当前生成和配音。
- 保存本机加密聊天记录，重启续聊；长对话摘要、相关旧片段与长期偏好记忆辅助承接上下文。
- 人物设定 V4.0：沿用既有语言、关系和饮食设定，补充场景、知情范围、事件状态及记忆纠正规则；原作事实与角色推演分开。
- QQ 音乐上下文点歌、候选选择、收藏/歌单与正在播放控制；只有真实执行结果才能确认已播放。
- 校服1、校服2、幽灵服及衣帽间换装；聊天背景、昼夜主题、专注计时、作息问候、托盘、快捷键与可选开机启动。
- 保留思考时的动作反馈。之前停用的挥手、走路、吃面包等自主动作不会因更新重新开放。

模型理解、记忆推断及外部音乐客户端仍可能出错；记忆可在界面中查看和删除。不能恢复旧版从未保存的聊天。

## 可选语音和音乐配置

**本地日语配音和中文语音输入**需要另行安装完整 GPT-SoVITS Windows 整合包（包含 Python 与所需模型），本项目的 ZIP 不包含这些大型依赖。未配置时仍可文字聊天。

在 `app/voice-engine` 打开 PowerShell，按自己的安装位置登记：

```powershell
.\Register-VoiceEngine.ps1 -EnginePath 'D:\Apps\GPT-SoVITS'
```

登记目录必须包含 `api_v2.py`；本地中文识别使用整合包的 `runtime/python.exe`。然后在桌宠配音设置中选择本地 GPT-SoVITS；默认服务地址为 `http://127.0.0.1:9880`，配套参考音频在 `app/reference/anna.wav`。首次模型加载需要等待，桌宠会尝试启动已登记的音色服务。也可以运行 `Start-VoiceEngine.ps1` 手动检查/启动。每位用户需登记自己的路径，发布包不携带作者电脑上的登记文件。 中文识别也兼容旧版 `ANNA_ASR_ENGINE` 环境变量及 `%LOCALAPPDATA%\AnnaDesktopPet\asr-engine-path.txt` 配置（优先于程序目录登记）；旧路径若已失效，需要更新或移除该旧配置。

**QQ 音乐**需要电脑上安装客户端，正常登录并具备对应歌曲的播放权限。可先手动打开客户端；若要让桌宠启动它，在 `%LOCALAPPDATA%\AnnaDesktopPet\qqmusic-path.txt` 写入自己电脑 `QQMusic.exe` 的完整路径。客户端版本变化可能影响控制协议。在线点歌不要求提前下载音乐文件；本地歌曲默认从当前 Windows 用户的“音乐”目录查找。

## 隐私与数据

选择“记住”后，API Key 使用 Windows 当前用户的数据保护机制保存到 `%LOCALAPPDATA%\AnnaDesktopPet`；未选择则仅在当前进程内使用。聊天记录在本机加密保存，GPT 与 DeepSeek 当前会话分别保存，长期记忆共用。聊天、新对话和记忆清理入口可用于管理这些内容。

最近对话、摘要、相关历史片段、人物设定及需要的长期记忆会发送给选定的模型服务。后台摘要与记忆整理也会使用 API 用量。中文语音识别在本机进行，临时录音处理后删除；日语合成连接本地音色服务；QQ 音乐搜索词会发送到 QQ 曲库。发布源码和 ZIP 不应包含用户的密钥、私人聊天、个人记忆、引擎登记或运行日志。

## 从源码构建

在仓库根目录打开 PowerShell。需要 Windows、.NET Framework 4.8 和其 C# 编译器。

```powershell
.\source\build.ps1 -Destination .\build\GPT
.\source\build.ps1 -Destination .\build\DeepSeek -DeepSeek
```

仓库已经包含实际人物素材，**不要运行旧的占位素材生成流程**。`GenerateDemoAssets.ps1` 已停用，不会覆盖人物、背景或衣帽间。

维护者发布前运行：

```powershell
.\tools\Check-Release.ps1
```

这会检查公开文件、编译两版、运行离线回归、比对程序素材与源码素材的 SHA256，并生成、验证两个 ZIP。不会调用收费模型或录制麦克风。输出位于 `releases/`，包括两个英文命名 ZIP 和 `SHA256SUMS-v1.41.0.txt`。重复生成同一版本时需显式加 `-ReplaceExisting`；其他版本及仓库外历史包不会删除。仅重新打包已验证构建可运行 `Package-Release.ps1 -SkipBuild`，但日常发布应先通过完整检查。

只将源码提交到 Git；ZIP 作为 GitHub Release 附件上传。代码按 [MIT](LICENSE) 提供；人物、图片、音频等素材不随代码许可证授权。