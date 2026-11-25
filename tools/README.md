# tools —— 做这两个音频用到的脚本

只依赖 numpy，全是命令行小工具。仓库里的 `bgm.m4a` 和 `loop.wav` 就是用它们做出来的。

## make_loop.py —— 裁无缝循环体

```bash
python make_loop.py in.wav body.wav --start 18 --end 347.59 --xfade 4
```

取 `[start, end)` 这一段，输出长度 = 区间长度 − xfade。前 xfade 秒是"开头淡入 + 结尾淡出"的等功率交叉，
其余原样。结果是：输出的最后一帧与第一帧在原始音频里正好相邻，**循环点是连续的**，
播放端只要一跳到底就行，不用再做交叉。

先找到曲子自己的循环周期（长曲子通常是一段几小节反复），把区间对齐到周期边界，效果最好。

## limit.py —— 增益 + 前视限幅

```bash
python limit.py body.wav loop.wav --gain 8 --ceiling -2
```

为什么不用 ffmpeg 的 `alimiter`：它默认 `level=true` 会自动补电平，加 5 ms attack，
遇到 120 BPM 这类密集打击会明显"抽气"、发沙。这个实现只做线性增益 + 越界处按前视峰值平滑下压，
恢复 150 ms，不做任何自动增益。

## denoise.py —— 稳态噪声门控

```bash
python denoise.py in.wav out.wav --start 0.9 --end 2.55 --fmin 1800 --alpha 2.5 --floor 0.03
```

用来去掉"一直铺着"的宽频底噪：先在 `[start, end)` 内按频点取功率均值做噪声谱（沿频率中值平滑），
再逐帧做维纳门控，只对 `fmin` 以上生效，窗口两端 feather 淡入淡出。

`--alpha` 1.5 温和 / 2.5 强；`--floor` 是保留下限，避免压出死寂。

判断该不该用它：把一段音频按 100 ms 扫各频段能量，如果某段 2–12 kHz 的电平**纹丝不动**、
而中频在剧烈起伏，那就是噪声床，不是音乐。
