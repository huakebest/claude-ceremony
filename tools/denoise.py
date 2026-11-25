#!/usr/bin/env python3
"""稳态噪声门控：去掉一段里"一直铺着"的宽频底噪（俗称"沙"），保留音乐主体。

适用场景：某段音频里有个电平恒定、不随内容起伏的噪声床
（比如素材自带的底噪、灌录噪声），而且它只在某个时间窗里明显。

做法：
  1. 在 [start, end) 窗口内，按频点取功率的时间均值，再沿频率做 7 点中值平滑 → 噪声谱
  2. 逐帧做维纳门控 gain = sqrt(max(0, 1 - alpha*N/P))，P 先做时/频平滑，避免"音乐噪声"
  3. 只对 fmin 以上生效（下方留 600Hz 过渡），窗口两端 feather 秒淡入淡出

用法：
  python denoise.py in.wav out.wav --start 0.9 --end 2.55 --fmin 1800 --alpha 2.5 --floor 0.03

--alpha 越大压得越狠（1.5 温和 / 2.5 强），--floor 是保留下限（避免死寂）。
只依赖 numpy。
"""
import argparse
import wave

import numpy as np
from numpy.lib.stride_tricks import sliding_window_view


def main():
    ap = argparse.ArgumentParser(description='稳态噪声门控降噪')
    ap.add_argument('src')
    ap.add_argument('dst')
    ap.add_argument('--start', type=float, required=True)
    ap.add_argument('--end', type=float, required=True)
    ap.add_argument('--fmin', type=float, default=1800.0, help='生效频段下限 Hz')
    ap.add_argument('--alpha', type=float, default=2.5)
    ap.add_argument('--floor', type=float, default=0.03)
    ap.add_argument('--feather', type=float, default=0.06)
    args = ap.parse_args()

    w = wave.open(args.src, 'rb')
    sr, ch, n = w.getframerate(), w.getnchannels(), w.getnframes()
    x = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float32) / 32768.0
    w.close()
    x = x.reshape(-1, ch)

    N, hop = 2048, 512
    win = np.hanning(N).astype(np.float32)
    nframes = (len(x) - N) // hop + 1
    idx = np.arange(N)[None, :] + hop * np.arange(nframes)[:, None]

    FL = np.fft.rfft(x[idx, 0] * win[None, :], axis=1)
    FR = np.fft.rfft(x[idx, 1] * win[None, :], axis=1) if ch > 1 else FL
    freqs = np.fft.rfftfreq(N, 1.0 / sr)
    times = (np.arange(nframes) * hop + N / 2) / sr
    sel = (times >= args.start) & (times <= args.end)

    npow = ((np.abs(FL) ** 2 + np.abs(FR) ** 2) / 2.0)[sel].mean(axis=0)
    npow = np.median(sliding_window_view(np.pad(npow, 3, mode='edge'), 7), axis=1)

    def smooth_time(a, k=3):
        p = np.pad(a, ((k // 2, k // 2), (0, 0)), mode='edge')
        return sliding_window_view(p, k, axis=0).mean(axis=-1)

    def gate(F):
        p = smooth_time(np.abs(F) ** 2, 3)
        ker = np.array([0.25, 0.5, 0.25])
        p = np.apply_along_axis(lambda r: np.convolve(r, ker, mode='same'), 1, p)
        g = np.sqrt(np.clip(1.0 - args.alpha * npow[None, :] / (p + 1e-15), args.floor ** 2, 1.0))
        g = np.maximum(g, args.floor)
        ramp = np.clip((freqs - (args.fmin - 600)) / 600.0, 0, 1)
        g = g * ramp[None, :] + (1 - ramp)[None, :]
        tw = (np.clip((times - args.start) / args.feather, 0, 1) *
              np.clip((args.end + args.feather - times) / args.feather, 0, 1))
        g = g * tw[:, None] + (1 - tw[:, None])
        return F * g

    def istft(F):
        seg = np.fft.irfft(F, n=N, axis=1) * win[None, :]
        out = np.zeros(len(x))
        norm = np.zeros(len(x))
        for i in range(nframes):
            out[i * hop:i * hop + N] += seg[i]
            norm[i * hop:i * hop + N] += win ** 2
        return out / np.maximum(norm, 1e-8)

    yl = istft(gate(FL))
    yr = istft(gate(FR)) if ch > 1 else yl
    y = np.stack([yl, yr], axis=1) if ch > 1 else yl[:, None]
    y = np.clip(y, -1, 1)

    w = wave.open(args.dst, 'wb')
    w.setnchannels(ch)
    w.setsampwidth(2)
    w.setframerate(sr)
    w.writeframes((y * 32767).astype(np.int16).tobytes())
    w.close()
    print('done: alpha=%.1f floor=%.2f fmin=%.0f' % (args.alpha, args.floor, args.fmin))


if __name__ == '__main__':
    main()
