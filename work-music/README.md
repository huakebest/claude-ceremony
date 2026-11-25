# 工作音乐（ClaudeBgm）

托盘常驻的背景音乐播放器：把一段曲子做成**无缝循环**，按规则自动开关，手动操作优先。

## 编译 / 安装

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File out\install.ps1
```

`out\install.ps1` 装到 `%LOCALAPPDATA%\Programs\ClaudeWorkMusic\`，写开机自启（`HKCU\...\Run` → `ClaudeBgm.exe --startup`），然后启动。
参数：`-Dest 目录` 换位置、`-NoStart` 只装不启动、`-Force` 已装过另一份时强制装。
不想自己编就直接下 [Releases](https://github.com/huakebest/claude-ceremony/releases/latest) 里的成品包。

## 用法

- 托盘 ♪ **左键单击** = 打开设置窗口
- 右键菜单：暂停/播放、模式（自动跟随 / 常开 / 关闭）、音量、设置、开机自启、退出

## 规则（模式 = 自动跟随）

| 条件 | 结果 |
|---|---|
| Claude 没运行（进程 `claude.exe` 不存在） | 停 |
| Claude 刚启动 | 先不出声，等开屏动画和它的 BGM 走完（约 17.4 秒）再淡入；没有开屏动画时 20 秒兜底 |
| Claude 在前台 | 音量 = 设置里的「音量」（默认 55%） |
| Claude 不在前台 | 按「后台音量」降（默认 40%） |
| 键鼠静止 ≥ N 分钟（默认 10） | 淡出暂停，有输入自动恢复 |
| 屏幕锁定 | 暂停，解锁恢复 |
| 工作时段外（默认关闭此规则） | 暂停 |
| 开屏动画在放 BGM | 音量归零让位（命名事件 `Local\ClaudeBgm.BootIntro`） |

手动点过「暂停 / 播放」后 30 分钟内不受规则干扰，到期自动交回。

## 无缝循环怎么做

两个 `MediaPlayer` 交替：A 播到距结尾 0.35 秒时 B 从头起播，等功率交叉。
不用单播放器 `MediaEnded` 重开，那样每次循环都会有一声空隙。

音频本身是 WAV（PCM），解码零开销；循环体由 `tools/make_loop.py` 从长曲里裁。

## 配置

`_data\config.json`（跟 exe 同目录），日志 `_data\log.txt`。

| 键 | 默认 | 说明 |
|---|---|---|
| `mode` | `auto` | `auto` 跟随 Claude / `on` 常开 / `off` 关闭 |
| `volume` | `0.55` | 前台音量 |
| `duck` | `0.40` | 后台时乘这个比例 |
| `idleMinutes` | `10` | 键鼠静止多少分钟停，0 = 不停 |
| `hoursEnabled` | `false` | 只在工作时段播 |
| `startTime` / `endTime` | `09:00` / `23:00` | 工作时段 |
| `autoStart` | `true` | 开机自启 |

## 换自己的曲子

1. 把长音频转成 16-bit PCM WAV
2. `python ..\tools\make_loop.py in.wav body.wav --start 18 --end 347.59 --xfade 4`
3. 需要抬电平：`python ..\tools\limit.py body.wav loop.wav --gain 8 --ceiling -2`
4. 用 `loop.wav` 覆盖 `assets\loop.wav`，重启程序

## 卸载

```powershell
powershell -ExecutionPolicy Bypass -File uninstall.ps1
```

删进程、自启项、程序目录（`-KeepData` 保留 `_data` 和音源）。
