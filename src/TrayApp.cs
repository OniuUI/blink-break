using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BlinkBreak
{
    class TrayApp : ApplicationContext
    {
        public const string StatsHotkeyText = "Ctrl+Alt+Shift+B";
        const int HotkeyStats = 1;
        const int HotkeySkip = 2;
        const double IdleThresholdSeconds = 300;

        static readonly int[] IntervalChoices = { 10, 15, 20, 30, 45, 60 };
        static readonly int[] LengthChoices = { 10, 15, 20, 30, 60 };

        readonly Settings settings;
        readonly Stats stats;
        readonly NotifyIcon tray;
        readonly MessageWindow messages;
        readonly System.Windows.Forms.Timer ticker;
        readonly Stopwatch tickClock = Stopwatch.StartNew();
        readonly ToolStripMenuItem statusItem, pauseItem, autoStartItem, intervalMenu, lengthMenu;

        DateTime nextBreak;
        DateTime pausedUntil = DateTime.MinValue;
        List<BreakForm> overlays;
        Stopwatch breakClock;
        int breakLength;
        double stretchSeconds;            // active screen time since the last real break
        StatsForm statsForm;
        volatile bool locked, resetRequested;

        public TrayApp(bool breakNow, bool showStats)
        {
            settings = Settings.Load();
            stats = Stats.Load();
            // Start with Windows by default; refresh the path if the exe moved.
            if (settings.FirstRun || AutoStart.IsEnabled()) AutoStart.Set(true);

            messages = new MessageWindow();
            messages.HotkeyPressed += OnHotkey;
            messages.ShowStatsRequested += ShowStats;
            messages.BreakNowRequested += StartBreak;
            bool hotkeyOk = messages.Register(HotkeyStats, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT, Keys.B);
            if (!hotkeyOk) Log.Write("Could not register " + StatsHotkeyText + " (another app is using it)");

            var menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem("Blink Break") { Enabled = false };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());

            var statsItem = new ToolStripMenuItem("Eye health stats...", null, delegate { ShowStats(); });
            statsItem.Font = new Font(statsItem.Font, FontStyle.Bold);
            if (hotkeyOk) statsItem.ShortcutKeyDisplayString = StatsHotkeyText;
            menu.Items.Add(statsItem);
            menu.Items.Add(new ToolStripMenuItem("Take a blink break now", null, delegate { StartBreak(); }));
            pauseItem = new ToolStripMenuItem("Pause for 1 hour", null, delegate { TogglePause(); });
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());

            intervalMenu = new ToolStripMenuItem("Break every");
            foreach (int m in IntervalChoices)
            {
                int minutes = m;
                var item = new ToolStripMenuItem(minutes + " minutes" + (minutes == 20 ? "  (20-20-20 rule)" : ""), null, delegate
                {
                    settings.IntervalMinutes = minutes;
                    settings.Save();
                    ResetCountdown();
                });
                item.Tag = minutes;
                intervalMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(intervalMenu);

            lengthMenu = new ToolStripMenuItem("Break length");
            foreach (int s in LengthChoices)
            {
                int seconds = s;
                var item = new ToolStripMenuItem(seconds + " seconds" + (seconds == 20 ? "  (recommended)" : ""), null, delegate
                {
                    settings.BreakSeconds = seconds;
                    settings.Save();
                });
                item.Tag = seconds;
                lengthMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(lengthMenu);

            autoStartItem = new ToolStripMenuItem("Start with Windows", null, delegate { AutoStart.Set(!AutoStart.IsEnabled()); });
            menu.Items.Add(autoStartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitThread(); }));
            menu.Opening += delegate { RefreshMenu(); };

            int iconSize = Math.Max(16, SystemInformation.SmallIconSize.Width);
            Icon icon;
            using (var bmp = IconArt.Render(iconSize)) icon = Icon.FromHandle(bmp.GetHicon());

            tray = new NotifyIcon();
            tray.Icon = icon;
            tray.Text = "Blink Break";
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowStats();
            };
            tray.Visible = true;

            ResetCountdown();
            if (breakNow) nextBreak = DateTime.Now;

            if (settings.FirstRun)
            {
                settings.Save();
                tray.ShowBalloonTip(8000, "Blink Break is running",
                    "You'll get a " + settings.BreakSeconds + "-second blink break every " + settings.IntervalMinutes +
                    " minutes. Click the eye icon for your stats, right-click for options.", ToolTipIcon.Info);
            }

            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            ticker = new System.Windows.Forms.Timer();
            ticker.Interval = 1000;
            ticker.Tick += delegate { Tick(); };
            ticker.Start();

            Log.Write("Started v" + Program.Version + " (every " + settings.IntervalMinutes + " min, " + settings.BreakSeconds + " s)");
            if (showStats) ShowStats();
        }

        bool IsPaused { get { return pausedUntil != DateTime.MinValue; } }

        void ResetCountdown()
        {
            nextBreak = DateTime.Now.AddMinutes(settings.IntervalMinutes);
            UpdateTooltip();
        }

        void Tick()
        {
            Log.Heartbeat();
            double dt = tickClock.Elapsed.TotalSeconds;
            tickClock.Restart();
            if (dt > 5) dt = 0;                 // sleep / long stall: don't count it as screen time

            if (overlays != null) return;

            if (resetRequested) { resetRequested = false; stretchSeconds = 0; ResetCountdown(); }

            // Active screen time: unlocked and either recent input or something full-screen (e.g. a video).
            if (!locked && (Native.IdleSeconds() < IdleThresholdSeconds || Native.IsFullscreenAnything()))
            {
                stretchSeconds += dt;
                stats.AddScreenTime(dt, stretchSeconds);
            }

            if (IsPaused)
            {
                if (DateTime.Now >= pausedUntil) { pausedUntil = DateTime.MinValue; ResetCountdown(); }
                UpdateTooltip();
                return;
            }

            // While the PC is locked you're away from the screen: keep the timer full.
            if (locked) { ResetCountdown(); return; }

            if (DateTime.Now >= nextBreak)
            {
                // Exclusive full-screen games / presentation mode: try again in a minute.
                if (Native.IsFullscreenExclusive()) nextBreak = DateTime.Now.AddMinutes(1);
                else StartBreak();
            }
            UpdateTooltip();
        }

        void StartBreak()
        {
            if (overlays != null) return;
            Log.Write("Break started (foreground app: " + Native.ForegroundProcessName() + ")");

            // Esc works as a global hotkey during the break, so skipping never depends on
            // the overlay having keyboard focus (full-screen apps can keep focus).
            messages.Register(HotkeySkip, 0, Keys.Escape);

            overlays = new List<BreakForm>();
            breakClock = Stopwatch.StartNew();
            breakLength = settings.BreakSeconds;
            BreakForm primary = null;

            foreach (Screen screen in Screen.AllScreens)
            {
                var form = new BreakForm(screen, breakClock, breakLength);
                form.Finished += EndBreak;
                overlays.Add(form);
                if (screen.Primary) primary = form;
            }
            foreach (var f in overlays) f.Show();
            if (primary == null && overlays.Count > 0) primary = overlays[0];
            if (primary != null) primary.Activate();
            UpdateTooltip();
        }

        void EndBreak(bool completed)
        {
            if (overlays == null) return;
            double rest = Math.Min(breakClock.Elapsed.TotalSeconds, breakLength);
            messages.Unregister(HotkeySkip);

            var list = overlays;
            overlays = null;
            foreach (var f in list) { f.AllowClose = true; f.Close(); }

            stats.RecordBreak(completed, rest);
            if (completed || rest >= Math.Min(10, breakLength)) stretchSeconds = 0;
            Log.Write("Break " + (completed ? "completed" : "skipped") + " after " + rest.ToString("0.0") + " s");
            ResetCountdown();
            if (statsForm != null) statsForm.Invalidate();
        }

        void OnHotkey(int id)
        {
            if (id == HotkeySkip) EndBreak(false);
            else if (id == HotkeyStats && overlays == null) ShowStats();
        }

        void ShowStats()
        {
            if (overlays != null) return;
            if (statsForm == null || statsForm.IsDisposed)
            {
                statsForm = new StatsForm(stats, settings, StatusText);
                statsForm.FormClosed += delegate { statsForm = null; };
                statsForm.Show();
            }
            if (statsForm.WindowState == FormWindowState.Minimized) statsForm.WindowState = FormWindowState.Normal;
            statsForm.Activate();
        }

        void TogglePause()
        {
            if (IsPaused) { pausedUntil = DateTime.MinValue; ResetCountdown(); }
            else pausedUntil = DateTime.Now.AddHours(1);
            UpdateTooltip();
        }

        string StatusText()
        {
            if (overlays != null) return "Blink break in progress";
            if (IsPaused) return "Paused until " + pausedUntil.ToString("t");
            double left = (nextBreak - DateTime.Now).TotalSeconds;
            if (left < 60) return "Next blink break in " + Math.Max(0, (int)Math.Ceiling(left)) + " s";
            return "Next blink break in " + (int)Math.Ceiling(left / 60.0) + " min";
        }

        void UpdateTooltip()
        {
            if (tray == null) return;
            string text = "Blink Break\n" + StatusText();
            if (text.Length > 63) text = text.Substring(0, 63);
            tray.Text = text;
        }

        void RefreshMenu()
        {
            statusItem.Text = StatusText();
            pauseItem.Text = IsPaused ? "Resume" : "Pause for 1 hour";
            foreach (ToolStripMenuItem item in intervalMenu.DropDownItems)
                item.Checked = (int)item.Tag == settings.IntervalMinutes;
            foreach (ToolStripMenuItem item in lengthMenu.DropDownItems)
                item.Checked = (int)item.Tag == settings.BreakSeconds;
            autoStartItem.Checked = AutoStart.IsEnabled();
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) locked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock) { locked = false; resetRequested = true; }
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) resetRequested = true;
        }

        protected override void ExitThreadCore()
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            ticker.Stop();
            EndBreak(false);
            stats.SaveIfDirty();
            if (statsForm != null) statsForm.Close();
            messages.Dispose();
            tray.Visible = false;
            tray.Dispose();
            Log.Write("Exited");
            base.ExitThreadCore();
        }
    }
}
