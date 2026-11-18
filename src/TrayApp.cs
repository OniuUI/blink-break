using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BlinkBreak
{
    class TrayApp : ApplicationContext, IHealthHost
    {
        public const string StatsHotkeyText = "Ctrl+Alt+Shift+B";
        public const string WaterHotkeyText = "Ctrl+Alt+Shift+W";
        const int HotkeyStats = 1;
        const int HotkeySkip = 2;
        const int HotkeyWater = 3;
        const double IdleThresholdSeconds = 300;
        const int SnoozeMinutes = 15;

        static readonly int[] IntervalChoices = { 10, 15, 20, 30, 45, 60 };
        static readonly int[] LengthChoices = { 10, 15, 20, 30, 60 };
        static readonly int[] GoalChoices = { 1600, 2000, 2200, 2500, 3000, 3500 };
        static readonly string[] GoalNotes = { "EFSA, women", "EFSA, men", "US, women", "", "US, men", "very active or hot weather" };
        static readonly int[] GlassChoices = { 150, 200, 250, 330, 400, 500 };
        static readonly int[] WaterReminderChoices = { 0, 30, 45, 60, 90, 120 };

        readonly Settings settings;
        readonly Stats stats;
        readonly WaterLog water;
        readonly NotifyIcon tray;
        readonly MessageWindow messages;
        readonly System.Windows.Forms.Timer ticker;
        readonly Stopwatch tickClock = Stopwatch.StartNew();
        readonly ToolStripMenuItem statusItem, waterStatusItem, pauseItem, autoStartItem, intervalMenu, lengthMenu;
        readonly ToolStripMenuItem logWaterItem, undoWaterItem, goalMenu, glassMenu, waterReminderMenu;

        DateTime nextBreak;
        DateTime pausedUntil = DateTime.MinValue;
        List<BreakForm> overlays;
        Stopwatch breakClock;
        int breakLength;
        double stretchSeconds;            // active screen time since the last real break
        double waterSinceSeconds;         // active screen time since the last drink or water reminder
        DateTime waterSnoozeUntil = DateTime.MinValue;
        StatsForm statsForm;
        WaterToast toast;
        volatile bool locked, resetRequested;

        public TrayApp(bool breakNow, bool showStats, bool showWater)
        {
            settings = Settings.Load();
            stats = Stats.Load();
            water = WaterLog.Load();
            // Start with Windows by default; refresh the path if the exe moved.
            if (settings.FirstRun || AutoStart.IsEnabled()) AutoStart.Set(true);

            messages = new MessageWindow();
            messages.HotkeyPressed += OnHotkey;
            messages.ShowStatsRequested += () => ShowStats(HealthTab.Eyes);
            messages.BreakNowRequested += StartBreak;
            messages.ShowWaterRequested += () => ShowStats(HealthTab.Water);
            const uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT;
            bool statsKeyOk = messages.Register(HotkeyStats, mods, Keys.B);
            if (!statsKeyOk) Log.Write("Could not register " + StatsHotkeyText + " (another app is using it)");
            bool waterKeyOk = messages.Register(HotkeyWater, mods, Keys.W);
            if (!waterKeyOk) Log.Write("Could not register " + WaterHotkeyText + " (another app is using it)");

            // ---- tray menu
            var menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem("Blink Break") { Enabled = false };
            waterStatusItem = new ToolStripMenuItem("Water") { Enabled = false };
            menu.Items.Add(statusItem);
            menu.Items.Add(waterStatusItem);
            menu.Items.Add(new ToolStripSeparator());

            var statsItem = new ToolStripMenuItem("Eye health stats...", null, delegate { ShowStats(HealthTab.Eyes); });
            statsItem.Font = new Font(statsItem.Font, FontStyle.Bold);
            if (statsKeyOk) statsItem.ShortcutKeyDisplayString = StatsHotkeyText;
            menu.Items.Add(statsItem);
            menu.Items.Add(new ToolStripMenuItem("Water && hydration...", null, delegate { ShowStats(HealthTab.Water); }));
            menu.Items.Add(new ToolStripSeparator());

            logWaterItem = new ToolStripMenuItem("Log a glass of water", null, delegate { LogWater(settings.GlassMl, true); });
            if (waterKeyOk) logWaterItem.ShortcutKeyDisplayString = WaterHotkeyText;
            menu.Items.Add(logWaterItem);
            undoWaterItem = new ToolStripMenuItem("Undo last drink", null, delegate { UndoWater(); });
            menu.Items.Add(undoWaterItem);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem("Take a blink break now", null, delegate { StartBreak(); }));
            pauseItem = new ToolStripMenuItem("Pause reminders for 1 hour", null, delegate { TogglePause(); });
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());

            intervalMenu = Choices("Break every", IntervalChoices,
                m => m + " minutes" + (m == 20 ? "  (20-20-20 rule)" : ""),
                m => { settings.IntervalMinutes = m; ResetCountdown(); });
            menu.Items.Add(intervalMenu);
            lengthMenu = Choices("Break length", LengthChoices,
                v => v + " seconds" + (v == 20 ? "  (recommended)" : ""),
                v => settings.BreakSeconds = v);
            menu.Items.Add(lengthMenu);

            var waterMenu = new ToolStripMenuItem("Water settings");
            goalMenu = Choices("Daily goal", GoalChoices,
                ml => { int i = Array.IndexOf(GoalChoices, ml); string note = GoalNotes[i]; return WaterLog.Litres(ml) + (note.Length > 0 ? "  (" + note + ")" : ""); },
                ml => settings.WaterGoalMl = ml);
            glassMenu = Choices("Glass size", GlassChoices, ml => ml + " ml", ml => settings.GlassMl = ml);
            waterReminderMenu = Choices("Remind me", WaterReminderChoices,
                m => m == 0 ? "Off" : "Every " + m + " min of screen time",
                m => settings.WaterReminderMinutes = m);
            waterMenu.DropDownItems.Add(goalMenu);
            waterMenu.DropDownItems.Add(glassMenu);
            waterMenu.DropDownItems.Add(waterReminderMenu);
            menu.Items.Add(waterMenu);

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
                if (e.Button == MouseButtons.Left) ShowStats(HealthTab.Eyes);
            };
            tray.Visible = true;

            ResetCountdown();
            if (breakNow) nextBreak = DateTime.Now;

            // Pick up the water timer from the last drink logged today.
            DateTime? last = water.LastDrink;
            if (last.HasValue && last.Value.Date == DateTime.Today)
                waterSinceSeconds = Math.Max(0, (DateTime.Now - last.Value).TotalSeconds);

            if (settings.FirstRun)
            {
                settings.WaterIntroShown = 1;    // the welcome message already covers water
                settings.Save();
                tray.ShowBalloonTip(8000, "Blink Break is running",
                    "You'll get a " + settings.BreakSeconds + "-second blink break every " + settings.IntervalMinutes +
                    " minutes, plus water reminders. Click the eye icon for your stats, right-click for options.", ToolTipIcon.Info);
            }

            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            ticker = new System.Windows.Forms.Timer();
            ticker.Interval = 1000;
            ticker.Tick += delegate { Tick(); };
            ticker.Start();

            Log.Write("Started v" + Program.Version + " (every " + settings.IntervalMinutes + " min, " + settings.BreakSeconds +
                      " s; water goal " + settings.WaterGoalMl + " ml, reminder " + settings.WaterReminderMinutes + " min)");
            if (showWater) ShowStats(HealthTab.Water);
            else if (showStats) ShowStats(HealthTab.Eyes);
            else if (!settings.FirstRun && settings.WaterIntroShown == 0) ShowWaterIntro();
        }

        // A submenu of radio-style choices that saves the setting when picked.
        ToolStripMenuItem Choices(string title, int[] values, Func<int, string> label, Action<int> apply)
        {
            var parent = new ToolStripMenuItem(title);
            foreach (int v in values)
            {
                int value = v;
                var item = new ToolStripMenuItem(label(value), null, delegate
                {
                    apply(value);
                    settings.Save();
                    UpdateTooltip();
                    if (statsForm != null) statsForm.Invalidate();
                });
                item.Tag = value;
                parent.DropDownItems.Add(item);
            }
            return parent;
        }

        static void Check(ToolStripMenuItem parent, int current)
        {
            foreach (ToolStripMenuItem item in parent.DropDownItems) item.Checked = (int)item.Tag == current;
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
                waterSinceSeconds += dt;
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
            else CheckWaterReminder();
            UpdateTooltip();
        }

        // ------------------------------------------------------------------ blink breaks

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

        // ------------------------------------------------------------------ water

        void CheckWaterReminder()
        {
            int interval = settings.WaterReminderMinutes;
            if (interval <= 0 || toast != null || overlays != null) return;
            if (water.TodayMl >= settings.WaterGoalMl) return;
            if (waterSinceSeconds < interval * 60) return;
            if (DateTime.Now < waterSnoozeUntil) return;
            if ((nextBreak - DateTime.Now).TotalSeconds < 60) return;   // let the blink break go first
            if (Native.IsFullscreenExclusive()) return;
            ShowWaterReminder();
        }

        void ShowWaterReminder()
        {
            waterSinceSeconds = 0;
            int glass = settings.GlassMl;
            Log.Write("Water reminder shown (" + water.TodayMl + " of " + settings.WaterGoalMl + " ml)");
            ShowToast(new WaterToast(
                "Time for some water",
                WaterProgressLine(),
                Progress(),
                "I drank " + glass + " ml", () => LogWater(glass, true),
                "Snooze " + SnoozeMinutes + " min", Snooze,
                () => ShowStats(HealthTab.Water),
                90000));
        }

        void ShowWaterIntro()
        {
            settings.WaterIntroShown = 1;
            settings.Save();
            ShowToast(new WaterToast(
                "New: water reminders",
                "Goal " + WaterLog.Litres(settings.WaterGoalMl) + " a day. Log a glass with " + WaterHotkeyText + ".",
                Progress(),
                "Open water tab", () => ShowStats(HealthTab.Water),
                "Got it", null,
                () => ShowStats(HealthTab.Water),
                30000));
        }

        void Snooze()
        {
            waterSnoozeUntil = DateTime.Now.AddMinutes(SnoozeMinutes);
            waterSinceSeconds = Math.Max(0, settings.WaterReminderMinutes * 60 - SnoozeMinutes * 60);
            Log.Write("Water reminder snoozed");
        }

        public void LogWater(int ml, bool showToast)
        {
            bool wasReached = water.TodayMl >= settings.WaterGoalMl;
            water.Add(ml);
            waterSinceSeconds = 0;
            waterSnoozeUntil = DateTime.MinValue;
            bool reachedNow = !wasReached && water.TodayMl >= settings.WaterGoalMl;
            Log.Write("Drank " + ml + " ml (" + water.TodayMl + " ml today)");
            UpdateTooltip();
            if (statsForm != null) statsForm.Invalidate();

            if (showToast)
                ShowToast(new WaterToast(
                    reachedNow ? "Daily goal reached!" : "+" + ml + " ml logged",
                    WaterProgressLine(),
                    Progress(),
                    "View today", () => ShowStats(HealthTab.Water),
                    "Undo", UndoWater,
                    () => ShowStats(HealthTab.Water),
                    reachedNow ? 8000 : 5000));
        }

        public void UndoWater()
        {
            var removed = water.UndoLast();
            if (removed == null) return;
            Log.Write("Undid " + removed.Ml + " ml (" + water.TodayMl + " ml today)");
            UpdateTooltip();
            if (statsForm != null) statsForm.Invalidate();
        }

        float Progress() { return Math.Min(1f, water.TodayMl / (float)settings.WaterGoalMl); }

        string WaterProgressLine()
        {
            int left = settings.WaterGoalMl - water.TodayMl;
            string line = WaterLog.Litres(water.TodayMl) + " of " + WaterLog.Litres(settings.WaterGoalMl) + " today";
            if (left > 0)
            {
                int glasses = (int)Math.Ceiling(left / (double)settings.GlassMl);
                line += "  ·  " + glasses + (glasses == 1 ? " glass" : " glasses") + " to go";
            }
            return line;
        }

        void ShowToast(WaterToast t)
        {
            if (toast != null) toast.Dismiss();
            toast = t;
            t.FormClosed += delegate { if (toast == t) toast = null; };
            t.Show();
        }

        public string WaterStatus()
        {
            int interval = settings.WaterReminderMinutes;
            if (interval <= 0) return "Water reminders are off. Turn them on under Water settings in the tray menu.";
            if (water.TodayMl >= settings.WaterGoalMl) return "Goal reached, so no more water reminders today.";
            if (IsPaused) return "Reminders paused until " + pausedUntil.ToString("t") + ".";
            double left = interval * 60 - waterSinceSeconds;
            double snooze = (waterSnoozeUntil - DateTime.Now).TotalSeconds;
            if (snooze > left) left = snooze;
            int min = Math.Max(1, (int)Math.Ceiling(left / 60.0));
            return "Reminder every " + interval + " min of screen time; next in about " + min + " min.";
        }

        // ------------------------------------------------------------------ shared UI

        void OnHotkey(int id)
        {
            if (id == HotkeySkip) EndBreak(false);
            else if (id == HotkeyWater) LogWater(settings.GlassMl, true);
            else if (id == HotkeyStats && overlays == null) ShowStats(HealthTab.Eyes);
        }

        void ShowStats(HealthTab tab)
        {
            if (overlays != null) return;
            if (statsForm == null || statsForm.IsDisposed)
            {
                statsForm = new StatsForm(stats, water, settings, this, tab);
                statsForm.FormClosed += delegate { statsForm = null; };
                statsForm.Show();
            }
            else statsForm.ShowTab(tab);
            if (statsForm.WindowState == FormWindowState.Minimized) statsForm.WindowState = FormWindowState.Normal;
            statsForm.Activate();
        }

        void TogglePause()
        {
            if (IsPaused) { pausedUntil = DateTime.MinValue; ResetCountdown(); }
            else pausedUntil = DateTime.Now.AddHours(1);
            UpdateTooltip();
        }

        public string BreakStatus()
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
            string text = "Blink Break\n" + BreakStatus() + "\nWater: " +
                          WaterLog.Litres(water.TodayMl) + " of " + WaterLog.Litres(settings.WaterGoalMl);
            if (text.Length > 63) text = text.Substring(0, 63);
            tray.Text = text;
        }

        void RefreshMenu()
        {
            statusItem.Text = BreakStatus();
            waterStatusItem.Text = "Water: " + WaterLog.Litres(water.TodayMl) + " of " + WaterLog.Litres(settings.WaterGoalMl) + " today";
            logWaterItem.Text = "Log a glass of water (+" + settings.GlassMl + " ml)";
            undoWaterItem.Enabled = water.CanUndo;
            pauseItem.Text = IsPaused ? "Resume reminders" : "Pause reminders for 1 hour";
            Check(intervalMenu, settings.IntervalMinutes);
            Check(lengthMenu, settings.BreakSeconds);
            Check(goalMenu, settings.WaterGoalMl);
            Check(glassMenu, settings.GlassMl);
            Check(waterReminderMenu, settings.WaterReminderMinutes);
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
            if (toast != null) toast.Dismiss();
            if (statsForm != null) statsForm.Close();
            messages.Dispose();
            tray.Visible = false;
            tray.Dispose();
            Log.Write("Exited");
            base.ExitThreadCore();
        }
    }
}
