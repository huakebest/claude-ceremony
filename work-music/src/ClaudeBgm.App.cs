// ClaudeBgm 主程序：托盘、规则引擎、设置窗口
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace ClaudeBgm
{
    public class App : Application
    {
        Config config;
        LoopPlayer player;
        WF.NotifyIcon tray;
        WF.ToolStripMenuItem statusItem, playItem, startItem;
        WF.ToolStripMenuItem autoItem, onItem, offItem;
        System.Drawing.Icon iconOn, iconOff;
        DispatcherTimer rules;
        EventWaitHandle bootEv;
        bool locked;
        bool manual, manualPaused;
        DateTime manualUntil;
        string lastWhy = "";
        bool lastPlaying;
        SettingsWindow win;

        // Claude 进程探测（CLAUDEBGM_PROC_NAME 仅测试用）
        static readonly string ProcName = Environment.GetEnvironmentVariable("CLAUDEBGM_PROC_NAME") ?? "claude";
        bool claudeRunning, awaitIntro, sawBoot, procInit;
        DateTime awaitSince, lastProcCheck = DateTime.MinValue;

        public App()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            config = Config.Load();
            EnsureAutoStart();

            player = new LoopPlayer(BaseDir.Of(@"assets\loop.wav"));
            player.StateChanged += delegate (string s) { if (s != null) Log.W("播放器：" + s); };
            player.Start();

            BuildTray();
            SystemEvents.SessionSwitch += OnSessionSwitch;
            AppDomain.CurrentDomain.ProcessExit += delegate { Cleanup(); };

            rules = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            rules.Tick += delegate { Evaluate(); };
            rules.Start();
            Evaluate();
            Log.W("启动：mode=" + config.Mode + " vol=" + config.Volume.ToString("0.00", CultureInfo.InvariantCulture));
        }

        // ── 托盘 ──
        void BuildTray()
        {
            iconOn = IconFactory.Make(true);
            iconOff = IconFactory.Make(false);

            var menu = new WF.ContextMenuStrip();
            statusItem = new WF.ToolStripMenuItem("…"); statusItem.Enabled = false;
            playItem = new WF.ToolStripMenuItem("暂停"); playItem.Click += delegate { ToggleManual(); };
            startItem = new WF.ToolStripMenuItem("开机自启"); startItem.CheckOnClick = true;
            startItem.Checked = config.AutoStart;
            startItem.CheckedChanged += delegate
            {
                config.AutoStart = startItem.Checked; config.Save(); EnsureAutoStart();
                Log.W("开机自启 = " + startItem.Checked);
            };

            autoItem = new WF.ToolStripMenuItem("自动跟随 Claude"); autoItem.Click += delegate { SetMode("auto"); };
            onItem = new WF.ToolStripMenuItem("常开"); onItem.Click += delegate { SetMode("on"); };
            offItem = new WF.ToolStripMenuItem("关闭"); offItem.Click += delegate { SetMode("off"); };
            var modeMenu = new WF.ToolStripMenuItem("模式");
            modeMenu.DropDownItems.Add(autoItem);
            modeMenu.DropDownItems.Add(onItem);
            modeMenu.DropDownItems.Add(offItem);

            var volMenu = new WF.ToolStripMenuItem("音量");
            foreach (int pct in new int[] { 20, 35, 50, 65, 80, 100 })
            {
                int v = pct;
                var it = new WF.ToolStripMenuItem(pct + "%");
                it.Click += delegate { config.Volume = v / 100.0; config.Save(); Log.W("音量 " + v + "%"); };
                volMenu.DropDownItems.Add(it);
            }

            menu.Items.Add(statusItem);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(playItem);
            menu.Items.Add(modeMenu);
            menu.Items.Add(volMenu);
            menu.Items.Add(new WF.ToolStripMenuItem("设置…", null, delegate { OpenSettings(); }));
            menu.Items.Add(startItem);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(new WF.ToolStripMenuItem("退出", null, delegate { Shutdown(); }));

            tray = new WF.NotifyIcon { Icon = iconOff, Text = "Claude 背景音乐", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += delegate (object s, WF.MouseEventArgs ev) { if (ev.Button == WF.MouseButtons.Left) OpenSettings(); };
            RefreshMode();
        }

        void RefreshMode()
        {
            autoItem.Checked = config.Mode == "auto";
            onItem.Checked = config.Mode == "on";
            offItem.Checked = config.Mode == "off";
            playItem.Text = (manual && !manualPaused) ? "暂停（手动）" : "播放（手动）";
        }

        void SetMode(string m)
        {
            config.Mode = m; config.Save(); manual = false;
            RefreshMode(); Log.W("模式 = " + m); Evaluate();
        }

        void ToggleManual()
        {
            if (manual && !manualPaused) { manual = true; manualPaused = true; manualUntil = DateTime.Now.AddMinutes(30); Log.W("手动暂停 30 分钟"); }
            else { manual = true; manualPaused = false; manualUntil = DateTime.Now.AddMinutes(30); Log.W("手动播放 30 分钟"); }
            RefreshMode(); Evaluate();
        }

        void UpdateTray(bool playing, string why)
        {
            if (tray == null) return;
            string text = playing ? "播放中 · " + why : "已停 · " + why;
            if (tray.Text != "Claude 背景音乐 · " + text)
            {
                try { tray.Text = "Claude 背景音乐 · " + text; } catch { }
            }
            if (playing != lastPlaying)
            {
                lastPlaying = playing;
                tray.Icon = playing ? iconOn : iconOff;
            }
            if (why != lastWhy)
            {
                lastWhy = why; statusItem.Text = text;
                Log.W("状态：" + text);
            }
        }

        // ── 规则 ──
        void Evaluate()
        {
            var now = DateTime.Now;
            if (manual && now > manualUntil) { manual = false; RefreshMode(); Log.W("手动优先到期，交回规则"); }

            CheckClaude(now);

            bool claudeFront = Native.ForegroundProcess == ProcName;
            bool idle = config.IdleMinutes > 0 && Native.IdleSeconds >= config.IdleMinutes * 60;
            bool inHours = !config.HoursEnabled || InHours(now);
            bool boot = BootIntroActive();
            if (boot) sawBoot = true;

            // Claude 刚启动：等开屏动画（及其 BGM）走完再进来
            if (awaitIntro)
            {
                if (sawBoot && !boot) { awaitIntro = false; Log.W("开屏结束，背景音乐进来"); }
                else if (!sawBoot && (now - awaitSince).TotalSeconds > 20) { awaitIntro = false; Log.W("等开屏超时（没有开屏动画），直接播"); }
            }

            bool play; string why;
            if (config.Mode == "off") { play = false; why = "已关闭"; }
            else if (manual) { play = !manualPaused; why = manualPaused ? "手动暂停" : "手动播放"; }
            else if (config.Mode == "on") { play = true; why = "常开"; }
            else
            {
                if (!claudeRunning) { play = false; why = "Claude 未运行"; }
                else if (awaitIntro) { play = false; why = "等开屏结束"; }
                else if (locked) { play = false; why = "屏幕已锁"; }
                else if (!inHours) { play = false; why = "不在工作时段"; }
                else if (idle) { play = false; why = "空闲 " + config.IdleMinutes + " 分钟"; }
                else { play = true; why = claudeFront ? "跟随 Claude · 前台" : "跟随 Claude · 后台"; }
            }

            double vol = config.Volume;
            if (!claudeFront && config.Mode != "on") vol *= config.Duck;
            if (boot) { vol = 0; why += " · 开屏让位"; }

            player.Apply(play, vol);
            UpdateTray(play && !boot, why);
        }

        /// 每 1.5 秒查一次 Claude 进程；起停会触发「等开屏 → 自动接上」或「立即停」
        void CheckClaude(DateTime now)
        {
            if ((now - lastProcCheck).TotalSeconds < 1.5) return;
            lastProcCheck = now;
            bool running;
            try { running = System.Diagnostics.Process.GetProcessesByName(ProcName).Length > 0; }
            catch { running = claudeRunning; }
            if (!procInit) { procInit = true; claudeRunning = running; Log.W("初始：Claude " + (running ? "在运行" : "未运行")); return; }
            if (running == claudeRunning) return;
            claudeRunning = running;
            if (running)
            {
                awaitIntro = true; sawBoot = false; awaitSince = now;
                Log.W("Claude 启动了，等开屏结束再放");
            }
            else
            {
                awaitIntro = false;
                Log.W("Claude 退出了，背景音乐停");
            }
        }

        bool InHours(DateTime now)
        {
            TimeSpan s, e;
            if (!TimeSpan.TryParse(config.StartTime, out s)) s = new TimeSpan(9, 0, 0);
            if (!TimeSpan.TryParse(config.EndTime, out e)) e = new TimeSpan(23, 0, 0);
            var t = now.TimeOfDay;
            if (s <= e) return t >= s && t < e;
            return t >= s || t < e;   // 跨午夜
        }

        bool BootIntroActive()
        {
            if (bootEv == null)
            {
                try { bootEv = EventWaitHandle.OpenExisting(@"Local\ClaudeBgm.BootIntro"); Log.W("已连上开屏事件"); }
                catch { return false; }
            }
            try { return bootEv.WaitOne(0); }
            catch { bootEv = null; return false; }
        }

        void OnSessionSwitch(object s, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) { locked = true; Log.W("屏幕锁定"); }
            else if (e.Reason == SessionSwitchReason.SessionUnlock) { locked = false; Log.W("屏幕解锁"); }
        }

        // ── 自启 ──
        void EnsureAutoStart()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    string exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                    if (config.AutoStart) k.SetValue("ClaudeBgm", "\"" + exe + "\" --startup");
                    else k.DeleteValue("ClaudeBgm", false);
                }
            }
            catch (Exception e) { Log.W("自启设置失败：" + e.Message); }
        }

        public void OpenSettings()
        {
            if (win != null) { win.Activate(); return; }
            win = new SettingsWindow(config, delegate (Config c)
            {
                config = c; config.Save(); EnsureAutoStart(); RefreshMode(); Evaluate();
                Log.W("设置已保存");
            }, delegate { win = null; });
            win.Show();
        }

        void Cleanup()
        {
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
            try { player.Shutdown(); } catch { }
            try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
        }
    }

    // ───────────────────────── 设置窗口 ─────────────────────────

    public class SettingsWindow : Window
    {
        readonly Config c;
        readonly Action<Config> onSave;

        public SettingsWindow(Config src, Action<Config> save, Action closed)
        {
            c = new Config();
            c.Mode = src.Mode; c.Volume = src.Volume; c.Duck = src.Duck;
            c.IdleMinutes = src.IdleMinutes; c.HoursEnabled = src.HoursEnabled;
            c.StartTime = src.StartTime; c.EndTime = src.EndTime; c.AutoStart = src.AutoStart;
            onSave = save;

            Title = "Claude 背景音乐 · 设置";
            Width = 430; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            Foreground = new SolidColorBrush(Color.FromRgb(230, 230, 230));
            Closed += delegate { closed(); };

            var root = new StackPanel { Margin = new Thickness(20) };
            Content = root;

            // 模式
            root.Children.Add(LabelText("模式"));
            var mode = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
            mode.Items.Add("自动跟随 Claude");
            mode.Items.Add("常开");
            mode.Items.Add("关闭");
            mode.SelectedIndex = c.Mode == "on" ? 1 : (c.Mode == "off" ? 2 : 0);
            root.Children.Add(mode);

            // 音量
            root.Children.Add(LabelText("音量"));
            var vol = MakeSlider(0, 100, c.Volume * 100);
            root.Children.Add(vol);
            var volTxt = Value(c.Volume * 100, "%");
            root.Children.Add(volTxt);
            vol.ValueChanged += delegate { volTxt.Text = ((int)vol.Value) + "%"; };

            // 后台音量
            root.Children.Add(LabelText("Claude 不在前台时降到"));
            var duck = MakeSlider(0, 100, c.Duck * 100);
            root.Children.Add(duck);
            var duckTxt = Value(c.Duck * 100, "%");
            root.Children.Add(duckTxt);
            duck.ValueChanged += delegate { duckTxt.Text = ((int)duck.Value) + "%"; };

            // 空闲
            root.Children.Add(LabelText("键鼠静止多久自动停（分钟，0 = 不停）"));
            var idle = new TextBox { Text = c.IdleMinutes.ToString(), Margin = new Thickness(0, 4, 0, 12), Padding = new Thickness(6, 4, 6, 4) };
            root.Children.Add(idle);

            // 工作时段
            var hours = new CheckBox { Content = "只在工作时段播放", IsChecked = c.HoursEnabled, Margin = new Thickness(0, 4, 0, 6) };
            root.Children.Add(hours);
            var hp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var st = new TextBox { Text = c.StartTime, Width = 70, Padding = new Thickness(6, 4, 6, 4) };
            var et = new TextBox { Text = c.EndTime, Width = 70, Padding = new Thickness(6, 4, 6, 4) };
            hp.Children.Add(st);
            hp.Children.Add(new TextBlock { Text = "  →  ", VerticalAlignment = VerticalAlignment.Center });
            hp.Children.Add(et);
            root.Children.Add(hp);

            // 自启
            var auto = new CheckBox { Content = "开机自启", IsChecked = c.AutoStart, Margin = new Thickness(0, 4, 0, 16) };
            root.Children.Add(auto);

            // 按钮
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", Width = 80, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(0, 5, 0, 5) };
            var ok = new Button { Content = "保存", Width = 80, Padding = new Thickness(0, 5, 0, 5), IsDefault = true };
            cancel.Click += delegate { Close(); };
            ok.Click += delegate
            {
                c.Mode = mode.SelectedIndex == 1 ? "on" : (mode.SelectedIndex == 2 ? "off" : "auto");
                c.Volume = Math.Round(vol.Value) / 100.0;
                c.Duck = Math.Round(duck.Value) / 100.0;
                int im; c.IdleMinutes = int.TryParse(idle.Text.Trim(), out im) ? Math.Max(0, im) : 10;
                c.HoursEnabled = hours.IsChecked == true;
                c.StartTime = st.Text.Trim(); c.EndTime = et.Text.Trim();
                c.AutoStart = auto.IsChecked == true;
                onSave(c);
                Close();
            };
            btns.Children.Add(cancel);
            btns.Children.Add(ok);
            root.Children.Add(btns);
        }

        static TextBlock LabelText(string t)
        {
            return new TextBlock { Text = t, Margin = new Thickness(0, 8, 0, 0), FontSize = 13, Opacity = 0.85 };
        }

        static TextBlock Value(double v, string unit)
        {
            return new TextBlock { Text = ((int)v) + unit, HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0) };
        }

        static Slider MakeSlider(double min, double max, double val)
        {
            return new Slider { Minimum = min, Maximum = max, Value = val, Margin = new Thickness(0, 6, 0, 0), IsSnapToTickEnabled = false };
        }
    }

    // ───────────────────────── 入口 ─────────────────────────

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            bool created;
            Mutex mutex = new Mutex(true, @"Local\ClaudeBgm", out created);
            EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ClaudeBgm.Show");
            if (!created)
            {
                showEvent.Set();   // 已经在跑：再开一次 = 弹设置
                return 0;
            }

            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log.W("崩溃：" + e.ExceptionObject); };
            var app = new App();
            app.DispatcherUnhandledException += delegate (object s, DispatcherUnhandledExceptionEventArgs e) { Log.W("出错：" + e.Exception); e.Handled = true; };

            if (Array.IndexOf(args, "--settings") >= 0) app.Dispatcher.BeginInvoke(new Action(delegate { app.OpenSettings(); }));

            var th = new Thread(delegate ()
            {
                while (true) { showEvent.WaitOne(); app.Dispatcher.BeginInvoke(new Action(delegate { app.OpenSettings(); })); }
            }) { IsBackground = true };
            th.Start();

            int code = app.Run();
            GC.KeepAlive(mutex);
            return code;
        }
    }
}
