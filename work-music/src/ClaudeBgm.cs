// ClaudeBgm —— 工作背景音乐小工具（托盘常驻）
// 播放 assets\loop.wav（5:25.6 无缝循环），规则自动开关，手动优先。
// 编译：C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:ClaudeBgm.exe
//        /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll
//        /r:PresentationCore.dll /r:PresentationFramework.dll /r:WindowsBase.dll
//        /r:System.Windows.Forms.dll /r:System.Drawing.dll ClaudeBgm.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace ClaudeBgm
{
    // ───────────────────────── 日志 ─────────────────────────

    public static class Log
    {
        public static string FilePath = Path.Combine(BaseDir.Path, "_data", "log.txt");
        static readonly object Lock = new object();
        public static void W(string msg)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + msg + "\r\n", Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    public static class BaseDir
    {
        public static string Path { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); } }
        public static string Of(string rel) { return System.IO.Path.Combine(Path, rel); }
    }

    // ───────────────────────── 设置 ─────────────────────────

    public class Config
    {
        public string Mode = "auto";      // auto | on | off
        public double Volume = 0.55;      // 前台/常开音量
        public double Duck = 0.40;        // 后台（Claude 不在前台）时按此比例降
        public int IdleMinutes = 10;      // 键鼠静止多久自动停
        public bool HoursEnabled = false; // 只在工作时段播
        public string StartTime = "09:00";
        public string EndTime = "23:00";
        public bool AutoStart = true;

        public static string FilePath { get { return BaseDir.Of(@"_data\config.json"); } }

        static double D(Dictionary<string, object> d, string k, double def)
        {
            object v; if (d.TryGetValue(k, out v) && v != null) { try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { } }
            return def;
        }
        static bool B(Dictionary<string, object> d, string k, bool def)
        {
            object v; if (d.TryGetValue(k, out v) && v is bool) return (bool)v;
            return def;
        }
        static string S(Dictionary<string, object> d, string k, string def)
        {
            object v; if (d.TryGetValue(k, out v) && v != null) { var s = Convert.ToString(v); if (s.Length > 0) return s; }
            return def;
        }

        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
                    if (d != null)
                    {
                        c.Mode = S(d, "mode", c.Mode);
                        c.Volume = D(d, "volume", c.Volume);
                        c.Duck = D(d, "duck", c.Duck);
                        c.IdleMinutes = (int)D(d, "idleMinutes", c.IdleMinutes);
                        c.HoursEnabled = B(d, "hoursEnabled", c.HoursEnabled);
                        c.StartTime = S(d, "startTime", c.StartTime);
                        c.EndTime = S(d, "endTime", c.EndTime);
                        c.AutoStart = B(d, "autoStart", c.AutoStart);
                    }
                }
            }
            catch (Exception e) { Log.W("设置文件读不了，用默认值：" + e.Message); }
            c.Save();
            return c;
        }

        static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        static string L(bool v) { return v ? "true" : "false"; }
        static string Q(string v) { return new JavaScriptSerializer().Serialize(v); }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"mode\" : " + Q(Mode) + ",\n");
                sb.Append("  \"volume\" : " + N(Volume) + ",\n");
                sb.Append("  \"duck\" : " + N(Duck) + ",\n");
                sb.Append("  \"idleMinutes\" : " + IdleMinutes + ",\n");
                sb.Append("  \"hoursEnabled\" : " + L(HoursEnabled) + ",\n");
                sb.Append("  \"startTime\" : " + Q(StartTime) + ",\n");
                sb.Append("  \"endTime\" : " + Q(EndTime) + ",\n");
                sb.Append("  \"autoStart\" : " + L(AutoStart) + "\n}\n");
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Log.W("设置文件写不了：" + e.Message); }
        }
    }

    // ───────────────────────── Win32 ─────────────────────────

    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

        public static double IdleSeconds
        {
            get
            {
                var li = new LASTINPUTINFO();
                li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
                if (!GetLastInputInfo(ref li)) return 0;
                return (Environment.TickCount - (int)li.dwTime) / 1000.0;
            }
        }

        public static string ForegroundProcess
        {
            get
            {
                try
                {
                    IntPtr h = GetForegroundWindow();
                    if (h == IntPtr.Zero) return "";
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid == 0) return "";
                    using (var p = Process.GetProcessById((int)pid)) return p.ProcessName.ToLowerInvariant();
                }
                catch { return ""; }
            }
        }
    }

    // ───────────────────────── 双播放器无缝循环 ─────────────────────────

    /// 两个 MediaPlayer 交替：A 快到尾时 B 从头起播，0.35 秒等功率交叉，避免单播放器 restart 的空隙。
    public class LoopPlayer
    {
        System.Windows.Media.MediaPlayer[] p = new System.Windows.Media.MediaPlayer[2];
        double[] env = new double[2];      // 交叉包络 0..1
        int act;
        double length;                     // 曲长（秒）
        double target;                     // 主音量 0..1（来自规则）
        double volNow;                     // 主音量当前值
        bool wantPlay;
        bool started;
        bool crossfading;
        double zeroSince = -1;
        string path;
        DispatcherTimer tick;

        public const double Xfade = 0.35;

        public LoopPlayer(string path) { this.path = path; }

        public event Action<string> StateChanged;

        public void Start()
        {
            for (int i = 0; i < 2; i++)
            {
                var m = new System.Windows.Media.MediaPlayer();
                m.MediaOpened += OnOpened;
                m.MediaFailed += OnFailed;
                m.Volume = 0;
                m.Open(new Uri(path, UriKind.Absolute));
                p[i] = m;
            }
            tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            tick.Tick += OnTick;
            tick.Start();
            Log.W("播放器已装载：" + path);
        }

        void OnOpened(object s, EventArgs e)
        {
            var m = (System.Windows.Media.MediaPlayer)s;
            if (m.NaturalDuration.HasTimeSpan && m.NaturalDuration.TimeSpan.TotalSeconds > 1)
            {
                length = m.NaturalDuration.TimeSpan.TotalSeconds;
                Log.W("曲长 " + length.ToString("0.00", CultureInfo.InvariantCulture) + " 秒");
            }
        }

        void OnFailed(object s, System.Windows.Media.ExceptionEventArgs e)
        {
            Log.W("播放失败：" + e.ErrorException.Message);
            if (StateChanged != null) StateChanged("文件播不了");
        }

        /// shouldPlay：是否该出声；vol：目标音量 0..1（0 = 淡出后停）
        public void Apply(bool shouldPlay, double vol)
        {
            bool changed = (shouldPlay != wantPlay) || Math.Abs(vol - target) > 0.001;
            wantPlay = shouldPlay;
            target = shouldPlay ? Math.Max(0, Math.Min(1, vol)) : 0;
            if (changed && StateChanged != null) StateChanged(null);
        }

        void OnTick(object s, EventArgs e)
        {
            if (length <= 0) return;
            double dt = 0.05;

            // 主音量斜坡：0.8 秒
            double step = dt / 0.8;
            if (volNow < target) volNow = Math.Min(target, volNow + step);
            else if (volNow > target) volNow = Math.Max(target, volNow - step);

            if (wantPlay)
            {
                if (volNow > 0.0001 && !started)
                {
                    try { p[act].Play(); started = true; Log.W("开始出声"); } catch (Exception ex) { Log.W("Play 失败：" + ex.Message); }
                }
                // 到尾部 → 切换
                if (!crossfading)
                {
                    double pos = SafePos(act);
                    if (pos >= length - Xfade - 0.05) BeginCrossfade();
                }
            }
            else
            {
                // 淡完了就真正暂停，省电
                if (volNow <= 0.0001 && crossfading == false && AllSilent())
                {
                    if (zeroSince < 0) zeroSince = Now;
                    if (Now - zeroSince > 1.2)
                    {
                        for (int i = 0; i < 2; i++) { try { p[i].Pause(); } catch { } }
                        started = false;
                        env[0] = env[1] = 0; volNow = 0;
                        zeroSince = -1;
                    }
                }
                else zeroSince = -1;
            }
            if (wantPlay) zeroSince = -1;

            // 交叉包络推进
            if (crossfading)
            {
                double k = dt / Xfade;
                int other = 1 - act;
                env[act] = Math.Max(0, env[act] - k);
                env[other] = Math.Min(1, env[other] + k);
                if (env[act] <= 0 && env[other] >= 1)
                {
                    crossfading = false;
                    try { p[act].Pause(); p[act].Position = TimeSpan.Zero; } catch { }
                    act = other;
                }
            }
            else
            {
                env[act] = 1; env[1 - act] = 0;
            }

            for (int i = 0; i < 2; i++)
            {
                double v = env[i] * volNow;
                try { p[i].Volume = Math.Max(0, Math.Min(1, v)); } catch { }
            }
        }

        void BeginCrossfade()
        {
            int other = 1 - act;
            try
            {
                p[other].Position = TimeSpan.Zero;
                p[other].Play();
                crossfading = true;
                Log.W("循环切换 → 播放器 " + other);
            }
            catch (Exception e) { Log.W("切换失败：" + e.Message); }
        }

        bool AllSilent()
        {
            for (int i = 0; i < 2; i++) { try { if (p[i].Volume > 0.0001) return false; } catch { } }
            return true;
        }

        bool IsPlaying(int i)
        {
            try { return p[i] != null; } catch { return false; }
        }

        double SafePos(int i)
        {
            try { return p[i].Position.TotalSeconds; } catch { return 0; }
        }

        static double Now { get { return Environment.TickCount / 1000.0; } }

        public void Shutdown()
        {
            if (tick != null) tick.Stop();
            for (int i = 0; i < 2; i++) { try { p[i].Stop(); p[i].Close(); } catch { } }
        }
    }

    // ───────────────────────── 托盘图标 ─────────────────────────

    public static class IconFactory
    {
        public static Icon Make(bool playing)
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var br = new SolidBrush(playing ? Color.FromArgb(217, 119, 87) : Color.FromArgb(128, 128, 128)))
                    g.FillEllipse(br, 1, 1, 30, 30);
                using (var f = new Font("Segoe UI Symbol", 15, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("♪", f, Brushes.White, new RectangleF(0, 0, 32, 33), sf);
            }
            IntPtr h = bmp.GetHicon();
            var ic = (Icon)Icon.FromHandle(h).Clone();
            Native.DestroyIcon(h);
            bmp.Dispose();
            return ic;
        }
    }
}
