# 开机仪式（Windows 改版，含独立 BGM）

Claude 桌面端启动时先铺一层开屏动画（约 12 秒后透出界面），之后保留一层半透明循环背景。
独立浮层程序，**不修改 Claude 本体**。

基于 [wynneceles/ai-boot-animations](https://github.com/wynneceles/ai-boot-animations)（GPL-3.0），
本目录的改动见 [CHANGES.md](CHANGES.md)。

## 编译 / 安装

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

编译产物在 `out\`（exe + assets + install.ps1），装好并登记开机自启：

```powershell
powershell -ExecutionPolicy Bypass -File out\install.ps1
```

装到 `%LOCALAPPDATA%\Programs\ClaudeBootAnimation\`。参数：`-Dest 目录` 换位置、`-NoStart` 只装不启动、`-Force` 已装过另一份时强制装。
不想自己编就直接下 [Releases](https://github.com/huakebest/claude-ceremony/releases/latest) 里的成品包。

托盘图标（✨）右键可以开关开屏 / BGM / 循环背景、调不透明度和音量、播放一次。

## 配置

`%APPDATA%\ClaudeBootAnimation\config.json`，日志在 `%LOCALAPPDATA%\ClaudeBootAnimation\log.txt`。

| 键 | 默认 | 说明 |
|---|---|---|
| `playIntro` | `true` | 打开 Claude 时播开屏 |
| `bgm` | `true` | 开屏时播 `assets\bgm.m4a`，开屏结束后继续放到曲末 |
| `loopBackground` | `true` | 开屏后保留半透明循环背景 |
| `residualAlpha` | `0.25` | 循环背景不透明度 |
| `introVolume` | `1.0` | 开屏音量（再乘系统音量） |
| `fadeStartSeconds` | `10.767` | 成片里进入循环帧的时间点，从这开始透出界面 |
| `fadeDurationSeconds` | `1.2` | 从不透明淡到半透明用多久 |

## 性能

- 只常驻不显示：CPU 约 0.4%，内存约 42 MB
- 循环背景播放中：CPU 约 170%（WPF 半透明窗口走软件渲染，1080p30 全靠 CPU 合成），内存约 630 MB
- 电池下建议托盘里关掉循环背景

## 卸载

```powershell
powershell -ExecutionPolicy Bypass -File uninstall.ps1
```

删除程序、自启项、GPU 偏好、设置与日志。Claude 本体从未被修改。
