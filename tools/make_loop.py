#!/usr/bin/env python3
"""把一段长音频裁成**无缝循环体**（等功率交叉）。

原理：取 [start, end) 这一段 S，长度 L。输出长度 L-xfade：
  前 xfade 秒 = S 的开头淡入 + S 的结尾淡出（等功率），
  其余原样。
这样输出的最后一帧与第一帧在原始音频里正好相邻，循环点是连续的，
不需要播放器做交叉淡化。

用法：
  python make_loop.py in.wav out.wav --start 18 --end 347.59 --xfade 4

只依赖 numpy。输入支持 16-bit PCM WAV（单/多声道，多声道会下混）。
"""
import argparse
import wave

import numpy as np


def load(path):
    w = wave.open(path, 'rb')
    sr, ch, n = w.getframerate(), w.getnchannels(), w.getnframes()
    a = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float32) / 32768.0
    w.close()
    if ch > 1:
        a = a.reshape(-1, ch).mean(axis=1)
    return a, sr


def save(path, a, sr):
    w = wave.open(path, 'wb')
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(sr)
    w.writeframes((np.clip(a, -1, 1) * 32767).astype(np.int16).tobytes())
    w.close()


def main():
    ap = argparse.ArgumentParser(description='裁无缝循环体（等功率交叉）')
    ap.add_argument('src')
    ap.add_argument('dst')
    ap.add_argument('--start', type=float, required=True, help='循环区间起点（秒）')
    ap.add_argument('--end', type=float, required=True, help='循环区间终点（秒）')
    ap.add_argument('--xfade', type=float, default=4.0, help='交叉长度（秒），默认 4')
    args = ap.parse_args()

    x, sr = load(args.src)
    seg = x[int(args.start * sr):int(args.end * sr)]
    n = int(args.xfade * sr)
    if n * 2 >= len(seg):
        raise SystemExit('交叉长度太长，比区间的一半还长')

    t = np.arange(n) / float(n)
    fade_in = np.sin(t * np.pi / 2).astype(np.float32)   # 等功率
    fade_out = np.cos(t * np.pi / 2).astype(np.float32)

    out = seg[:len(seg) - n].copy()
    out[:n] = seg[:n] * fade_in + seg[len(seg) - n:] * fade_out

    save(args.dst, out, sr)
    print('output %.2f s (%.2f min), peak %.1f dBFS' % (
        len(out) / sr, len(out) / sr / 60,
        20 * np.log10(max(np.abs(out).max(), 1e-9))))


if __name__ == '__main__':
    main()
