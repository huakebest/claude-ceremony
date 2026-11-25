# Claude 开机仪式 · 工作音乐（Windows）

给 Claude 桌面端配的两件小东西，外加做它们用到的音频工具。

| 目录 | 是什么 |
|---|---|
| [`ceremony/`](ceremony/) | **开机仪式**：开屏动画的 Windows 改版 ——**独立 BGM 音轨**（开屏结束后继续放到曲末）、音效开头静音 + 降噪、任务栏自动隐藏修复 |
| [`work-music/`](work-music/) | **工作音乐**：托盘常驻的背景音乐播放器 ——5:25 无缝循环，跟随 Claude 进程 / 键鼠空闲 / 锁屏自动开关 |
| [`tools/`](tools/) | 做音频用的脚本：裁无缝循环体、前视限幅、稳态噪声门控 |

两个程序都是纯 C#，用 Windows 自带的 .NET Framework 编译器编译，**不需要装 SDK，没有任何第三方依赖**。
开屏动画**不修改 Claude 本体**，是个独立浮层；不想要了随时卸载。

## 下载（不用自己编译）

装好的成品在 [Releases](https://github.com/huakebest/claude-ceremony/releases/latest) 里，下载解压 → 双击 `安装.cmd` → 完事。
**需要 Windows 10 / 11**，不需要管理员权限，不联网。

| 包 | 大小 | 内容 |
|---|---|---|
| [`claude-ceremony-1.0.0-bundle.zip`](https://github.com/huakebest/claude-ceremony/releases/latest/download/claude-ceremony-1.0.0-bundle.zip) | 78 MB | **两个都装**，双击一个 `安装.cmd` 搞定（推荐） |
| [`ClaudeCeremony-1.0.0.zip`](https://github.com/huakebest/claude-ceremony/releases/latest/download/ClaudeCeremony-1.0.0.zip) | 51 MB | 只要开机仪式动画 |
| [`ClaudeWorkMusic-1.0.0.zip`](https://github.com/huakebest/claude-ceremony/releases/latest/download/ClaudeWorkMusic-1.0.0.zip) | 27 MB | 只要工作背景音乐 |

双击 `安装.cmd` 时如果弹出「Windows 已保护你的电脑」，点「更多信息」→「仍要运行」；那不是病毒提示，是没签名的新下载文件都会有的提示。
装到 `%LOCALAPPDATA%\Programs\`，自启项写在当前用户注册表，卸载运行包里的 `卸载.cmd`，一切都在用户目录内。

## 它们怎么配合

```
Claude 启动
  → 开屏动画铺满屏幕（12 秒后透出界面）
  → 开屏自带音效 + 外部 BGM（17.4 秒）
  → 后台工作音乐此时静音让位（命名事件 Local\ClaudeBgm.BootIntro）
  → 开屏 BGM 收尾，工作音乐淡入，一直循环
Claude 退出
  → 工作音乐 1 秒内淡出停止
```

## 快速开始

两个目录各自独立，可以只装一个。

```powershell
# 编 + 装一条龙（编到 out\，再运行 out\ 里的 install.ps1）
powershell -ExecutionPolicy Bypass -File ceremony\build.ps1
powershell -ExecutionPolicy Bypass -File ceremony\out\install.ps1

powershell -ExecutionPolicy Bypass -File work-music\build.ps1
powershell -ExecutionPolicy Bypass -File work-music\out\install.ps1
```

`install.ps1` 干的事：停旧进程 → 拷到 `%LOCALAPPDATA%\Programs\<名字>\` → 写 `HKCU\...\Run` 自启 → 启动。
参数：`-Dest 目录`（换安装位置）、`-NoStart`（只装不启动）、`-Force`（已经装过另一份时强制装）。

需要 Windows 10/11 和 .NET Framework 4.x（系统自带）。

## 音源

仓库里的 `ceremony/assets/bgm.m4a`（17.4 秒）和 `work-music/assets/loop.wav`（5:25.59）
是从一段公开直播里的音乐剪的，**只作个人折腾和学习分享**。
想换成自己的曲子，见 [`tools/`](tools/)：`make_loop.py` 裁无缝循环、`limit.py` 抬电平、`denoise.py` 去底噪。

## 许可

- `ceremony/`：基于 [wynneceles/ai-boot-animations](https://github.com/wynneceles/ai-boot-animations)（GPL-3.0），本目录同样是 **GPL-3.0**，见 [ceremony/LICENSE](ceremony/LICENSE)
- `work-music/` 与 `tools/`：**MIT**，见 [LICENSE](LICENSE)
- 动画素材版权归原作者，见 [ceremony/assets/LICENSE.md](ceremony/assets/LICENSE.md)
- 音乐版权归原权利人；本项目与 Anthropic、OpenAI 等公司无关，未使用其商标或图标

详见 [NOTICE.md](NOTICE.md)。

---

## English

Two small Windows utilities for the Claude desktop app:

- `ceremony/` — a patched build of [ai-boot-animations](https://github.com/wynneceles/ai-boot-animations) (GPL-3.0) with an external BGM track that keeps playing after the intro fades, plus audio cleanup and a taskbar fix.
- `work-music/` — a tray-resident background-music player (MIT) with a gapless 5:25 loop that follows the Claude process, goes quiet when idle or locked, and ducks out during the boot animation.
- `tools/` — small NumPy scripts for cutting seamless loops, look-ahead limiting, and stationary-noise gating.

Build with the C# compiler bundled in .NET Framework — no SDK, no third-party packages. Windows 10/11 only.
