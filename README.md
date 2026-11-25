# Claude 工作音乐（Windows）

给 Claude 桌面端配的两件小东西，外加做它们用到的音频工具。

| 目录 | 是什么 |
|---|---|
| [`boot-animation/`](boot-animation/) | 开屏动画的 Windows 改版：**独立 BGM 音轨**（开屏结束后继续放到曲末）、音效开头静音 + 降噪、任务栏自动隐藏修复 |
| [`work-music/`](work-music/) | 托盘常驻的**工作背景音乐**播放器：5:25 无缝循环，跟随 Claude 进程 / 键鼠空闲 / 锁屏自动开关 |
| [`tools/`](tools/) | 做音频用的脚本：裁无缝循环体、前视限幅、稳态噪声门控 |

两个程序都是纯 C#，用 Windows 自带的 .NET Framework 编译器编译，**不需要装 SDK，没有任何第三方依赖**。
开屏动画**不修改 Claude 本体**，是个独立浮层；不想要了随时卸载。

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
# 开屏动画
powershell -ExecutionPolicy Bypass -File boot-animation\build.ps1
# 编出来在 boot-animation\out\，把 ClaudeBootAnimation 加进
# HKCU\Software\Microsoft\Windows\CurrentVersion\Run 即可开机自启

# 工作音乐
powershell -ExecutionPolicy Bypass -File work-music\build.ps1
# 编出来在 work-music\out\，运行一次就会自己写自启项
```

需要 Windows 10/11 和 .NET Framework 4.x（系统自带）。

## 音源

仓库里的 `boot-animation/assets/bgm.m4a`（17.4 秒）和 `work-music/assets/loop.wav`（5:25.59）
是从一段公开直播里的音乐剪的，**只作个人折腾和学习分享**。
想换成自己的曲子，见 [`tools/`](tools/)：`make_loop.py` 裁无缝循环、`limit.py` 抬电平、`denoise.py` 去底噪。

## 许可

- `boot-animation/`：基于 [wynneceles/ai-boot-animations](https://github.com/wynneceles/ai-boot-animations)（GPL-3.0），本目录同样是 **GPL-3.0**，见 [boot-animation/LICENSE](boot-animation/LICENSE)
- `work-music/` 与 `tools/`：**MIT**，见 [LICENSE](LICENSE)
- 动画素材版权归原作者，见 [boot-animation/assets/LICENSE.md](boot-animation/assets/LICENSE.md)
- 音乐版权归原权利人；本项目与 Anthropic、OpenAI 等公司无关，未使用其商标或图标

详见 [NOTICE.md](NOTICE.md)。

---

## English

Two small Windows utilities for the Claude desktop app:

- `boot-animation/` — a patched build of [ai-boot-animations](https://github.com/wynneceles/ai-boot-animations) (GPL-3.0) with an external BGM track that keeps playing after the intro fades, plus audio cleanup and a taskbar fix.
- `work-music/` — a tray-resident background-music player (MIT) with a gapless 5:25 loop that follows the Claude process, goes quiet when idle or locked, and ducks out during the boot animation.
- `tools/` — small NumPy scripts for cutting seamless loops, look-ahead limiting, and stationary-noise gating.

Build with the C# compiler bundled in .NET Framework — no SDK, no third-party packages. Windows 10/11 only.
