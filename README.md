# 杏菜桌宠（源码预览）

Windows 桌面宠物实验项目，提供 GPT 与 DeepSeek 两种对话构建、可选的本地日语配音、中文语音输入，以及本地音乐和 QQ 音乐控制。仓库包含作者使用的动漫人物素材和参考音频；项目与原作及配音演员无官方关联。素材使用边界见 [ASSETS.md](ASSETS.md)。

## 直接运行（推荐）

从 GitHub 的 **Releases** 下载对应的免编译 ZIP，解压后双击其中的 `桌宠/AnnaDesktopPet.exe`。GPT 版和 DeepSeek 版请选择一个运行，不要同时启动。首次使用时在设置中填写自己的 API Key，切勿把 API Key 上传到 GitHub。

## 构建

需要 Windows、.NET Framework 4.8 及其 C# 编译器。于 PowerShell 运行：

```powershell
./tools/GenerateDemoAssets.ps1
./source/build.ps1 -Destination ./build/GPT
./source/build.ps1 -Destination ./build/DeepSeek -DeepSeek
```

分别运行 `build/GPT/AnnaDesktopPet.exe` 或 `build/DeepSeek/AnnaDesktopPet.exe`。一次只运行一个版本。两个版本共用当前 Windows 用户的桌宠设置与记忆，API Key 分开保存；首次对话需在各自设置中填写对应服务的 API Key。API 调用可能产生费用。

## 可选功能

- **日语配音**：另行安装 GPT-SoVITS，按其许可证使用。在 `build/<版本>/voice-engine/engine-path.txt` 中写入引擎目录绝对路径；配音设置中填入本地服务地址并提供有权使用的 WAV 参考音频。配音服务只连接本机。没有音色服务时仍可文字聊天。
- **中文语音识别**：使用已安装 GPT-SoVITS 包中的 Python 与识别模型。可以在当前用户的 `%LOCALAPPDATA%\AnnaDesktopPet\asr-engine-path.txt` 写入引擎目录绝对路径，也可设置 `ANNA_ASR_ENGINE` 环境变量。未配置时语音输入不可用，不影响文字聊天。
- **QQ 音乐**：先运行 QQ 音乐客户端；若希望桌宠在客户端未运行时启动它，可在 `%LOCALAPPDATA%\AnnaDesktopPet\qqmusic-path.txt` 写入 `QQMusic.exe` 的完整路径。在线搜索和点播依赖 QQ 音乐的网络、账号、播放权限及客户端行为，可能因客户端更新失效。
- **本地音乐**：默认搜索当前用户的“音乐”文件夹。

## 隐私与数据

API Key 选择“记住”后使用 Windows 当前用户的数据保护机制保存在 `%LOCALAPPDATA%\AnnaDesktopPet`；未选择时只在进程内使用。对话历史默认只在当前运行内存中保留。已保存的偏好记忆可在记忆面板查看和删除，发送聊天请求时会随提示发送给所选模型服务。中文语音识别在本机进行，临时录音在处理后删除。QQ 音乐搜索词发送到其曲库接口。

## 素材与许可

代码按 [MIT](LICENSE) 许可证提供；人物、图片、音频等素材不随代码许可证授权。素材边界见 [ASSETS.md](ASSETS.md)。维护者可运行 `./tools/Package-Release.ps1` 生成两个免编译 ZIP。
