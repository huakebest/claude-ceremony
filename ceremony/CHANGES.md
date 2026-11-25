# 相对上游的改动

上游：[wynneceles/ai-boot-animations](https://github.com/wynneceles/ai-boot-animations) 的 `claude/windows`。
改动都在 `src/ClaudeBootAnimation.cs` 里，共四处（前两处是上游之后加的，第三处是 BGM，第四处是联动）。

## 1. 任务栏自动隐藏失效

上游把浮层窗口铺满整块显示器，系统会判定为「全屏应用」（`SHQueryUserNotificationState` = 2），
任务栏自动隐藏因此被抑制，鼠标探到底也不弹。

改动：`Follow()` 里 `Native.InsetFromMonitor(..., 2)`，把浮层裁进显示器范围再往里收 2 像素。
实测浮层矩形 2,2,2047,1279，状态回到 5，任务栏恢复正常。副作用是画面小 2 像素，肉眼不可见。

## 2. 独立 BGM 音轨

上游只有开屏视频自带的音效。本机在开屏视频之外，用**另一个 `MediaPlayer`** 播 `assets\bgm.m4a`，
生命周期独立于开屏播放器：开屏 12 秒进入循环背景后，BGM 继续响到曲末（约 17.4 秒）。

- 新增 `Config.Bgm`，默认 `true`；设置窗口、托盘都能开关
- 起播时机跟 `intro.MediaOpened`（第一帧出来才响），和画面同步
- 跳过开屏 / 关掉动画 / Claude 关闭 → 0.3–0.6 秒淡出或立即停
- 音量跟「开屏音量」滑块联动

## 3. 音轨降"沙"

`assets/intro.mp4` 的原音效在开头有很响的宽频噪声，听着像"沙"。两处处理（都在音轨上，**视频流逐位复制**）：

- 前 0.9 秒静音，0.9 秒处 50 ms 淡入接回
- 0.9–2.55 秒做稳态噪声门控（1.8 kHz 以上，降 15–20 dB，钟声主体不动）

处理脚本在仓库的 `tools/` 里（`denoise.py` 等）。

## 4. 与工作音乐的联动

开屏 BGM 起播时把命名事件 `Local\ClaudeBgm.BootIntro` 置位，收尾/关闭时复位：

```csharp
static EventWaitHandle bootSignal;
static void SignalBoot(bool on) { ... new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\ClaudeBgm.BootIntro") ... }
```

`work-music/` 每秒读一次，置位期间把音量归零，避免两层钟声打架。
没装 `work-music` 时这个事件没人读，无任何副作用。
