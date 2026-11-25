#!/usr/bin/env python3
"""增益 + 前视限幅（自己写的，比 ffmpeg alimiter 温和）。

为什么不用 ffmpeg 的 alimiter：它默认 `level=true` 会自动补电平，
配上 5ms attack，遇到 120 BPM 打击乐会明显"抽气"、发沙。
这个实现只做两件事：线性增益 + 越界处按前视峰值平滑下压，
恢复时间 150ms，不加任何自动增益。

用法：
  python limit.py in.wav out.wav --gain 8 --ceiling -2

--gain   线性增益（dB），默认 0
--ceiling 限幅天花板（dBFS），默认 -1.5
"""
import argparse
import wave

import numpy as np


def main():
    ap = argparse.ArgumentParser(description='增益 + 前视限幅')
    ap.add_argument('src')
    ap.add_argument('dst')
    ap.add_argument('--gain', type=float, default=0.0, help='增益 dB')
    ap.add_argument('--ceiling', type=float, default=-1.5, help='天花板 dBFS')
    ap.add_argument('--lookahead', type=float, default=8.0, help='前视 ms')
    ap.add_argument('--release', type=float, default=150.0, help='恢复 ms')
    args = ap.parse_args()

    w = wave.open(args.src, 'rb')
    sr, ch, n = w.getframerate(), w.getnchannels(), w.getnframes()
    x = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float32) / 32768.0
    w.close()
    if ch > 1:
        x = x.reshape(-1, ch).mean(axis=1)

    y = (x * (10 ** (args.gain / 20))).astype(np.float32)

    blk = max(1, int(sr / 1000))                       # 1 ms 一块
    nb = len(y) // blk
    peaks = np.abs(y[:nb * blk]).reshape(nb, blk).max(axis=1)
    k = max(1, int(args.lookahead))
    env = np.max(np.stack([np.pad(peaks, (0, k), mode='edge')[i:i + nb] for i in range(k)]), axis=0)

    ceil = 10 ** (args.ceiling / 20)
    g = np.minimum(1.0, ceil / np.maximum(env, 1e-9))

    out_g = np.empty(nb)
    cur = 1.0
    att = 1 - np.exp(-1.0 / max(args.lookahead, 0.5))
    rel = 1 - np.exp(-1000.0 / max(args.release, 1.0))
    for i in range(nb):
        tgt = g[i]
        cur += (tgt - cur) * (att if tgt < cur else rel)
        out_g[i] = cur

    gc = np.concatenate([np.repeat(out_g, blk),
                         np.full(len(y) - nb * blk, out_g[-1] if nb else 1.0, dtype=np.float32)])
    z = y * gc

    print('peak %.2f -> %.2f dBFS, max reduction %.2f dB' % (
        20 * np.log10(max(np.abs(y).max(), 1e-9)),
        20 * np.log10(max(np.abs(z).max(), 1e-9)),
        20 * np.log10(1.0 / max(gc.min(), 1e-9))))

    w = wave.open(args.dst, 'wb')
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(sr)
    w.writeframes((np.clip(z, -1, 1) * 32767).astype(np.int16).tobytes())
    w.close()


if __name__ == '__main__':
    main()
