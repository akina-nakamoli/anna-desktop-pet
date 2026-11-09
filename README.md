# 杏菜桌宠 v1.41.4

Windows 桌面宠物，提供 GPT 与 DeepSeek 两个版本。保留杏菜人物设定、三套服装、聊天背景与衣帽间，并支持文字/语音聊天、长期记忆、音乐控制及日常提醒。项目与原作及配音演员无官方关联，素材使用范围见 [ASSETS.md](ASSETS.md)。

## 下载并运行

在 GitHub **Releases** 下载一种版本：

- `anna-desktop-pet-GPT-v1.41.4.zip`
- `anna-desktop-pet-DeepSeek-v1.41.4.zip`

完整解压 ZIP，进入 `anna-desktop-pet-<版本>-v1.41.4/app`，双击 `AnnaDesktopPet.exe`。不要只取出 EXE；`assets`、`outfits`、`reference` 和 `voice-engine` 等目录需要与程序保存在一起。免编译包适用于 64 位 Windows，需 .NET Framework 4.8。GPT 与 DeepSeek 一次选择一个运行。

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

**杏菜日语参考音色**使用本机 GPT-SoVITS。发布包已包含 `app/reference/anna.wav` 和对应台词。首次配置：

1. 下载并完整解压 [GPT-SoVITS 官方 Windows 整合包](https://github.com/RVC-Boss/GPT-SoVITS#installation)，其中须有 `api_v2.py`、Python 运行环境和模型。
2. 在桌宠打开「日语配音与动作」，选择「参考音色 · 本地 GPT-SoVITS」，点击「配置杏菜参考音色」并选中整合包中的 `api_v2.py`。
3. 等桌宠检查环境、启动服务并播完试听。成功后自动保存配音，并开启聊天回复的日语配音。之后用已配置 API Key 的 GPT 或 DeepSeek 打字聊天。重新打开桌宠时会尝试启动登记过的本地服务。

默认地址为 `http://127.0.0.1:9880`。如需手动启动，可在 `app/voice-engine` 运行 `Register-VoiceEngine.ps1 -EnginePath <引擎目录>`，再运行 `Start-VoiceEngine.ps1`。如果没有兼容的 CUDA 环境，请按 GPT-SoVITS 官方说明准备 CPU 或适用的运行环境。发布包也提供可选的 Mei 离线声线和 Windows 日语声线。

**中文语音输入**是独立选项。侧键说话需要另行准备中文识别环境（Python 中的 FunASR、soundfile、NumPy 和 Paraformer 模型）。在「语音输入与麦克风」选择该引擎后重启。使用打字聊天和听日语回复不需要中文识别模型。

**QQ 音乐**需要电脑上安装客户端，正常登录并具备对应歌曲的播放权限。可先手动打开客户端。若要让桌宠在客户端关闭时自动打开它，进入「音乐控制」，粘贴或浏览选择本机的 `QQMusic.exe`，点击「保存路径」。旧版已保存的路径会自动显示。客户端版本变化可能影响控制协议。在线点歌不要求提前下载音乐文件；本地歌曲默认从当前 Windows 用户的“音乐”目录查找。

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

构建发布包时会校验并缓存 Python 嵌入式运行时、NumPy 和 PyOpenJTalk Plus 三个公开依赖。首次构建需要联网，下载 ZIP 的用户可直接离线合成。各依赖许可证与 Mei 声线授权说明随包放在 app/ja-tts 内。

这会检查公开文件、编译两版、运行离线回归、比对程序素材与源码素材的 SHA256，并生成、验证两个 ZIP。不会调用收费模型或录制麦克风。输出位于 `releases/`，包括两个英文命名 ZIP 和 `SHA256SUMS-v1.41.4.txt`。重复生成同一版本时需显式加 `-ReplaceExisting`；其他版本及仓库外历史包不会删除。仅重新打包已验证构建可运行 `Package-Release.ps1 -SkipBuild`，但日常发布应先通过完整检查。

只将源码提交到 Git；ZIP 作为 GitHub Release 附件上传。代码按 [MIT](LICENSE) 提供；人物、图片、音频等素材不随代码许可证授权。