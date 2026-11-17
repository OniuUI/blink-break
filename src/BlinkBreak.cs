// Blink Break - a tiny tray app that covers your screens with a short "blink break"
// every N minutes (default: the 20-20-20 rule - every 20 min, 20 s, look ~6 m away).
// Written in C# 5 so it compiles with the csc.exe that ships with Windows (.NET Framework 4.x).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Blink Break")]
[assembly: System.Reflection.AssemblyProduct("Blink Break")]
[assembly: System.Reflection.AssemblyDescription("Reminds you to take regular blink breaks")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace BlinkBreak
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--export-icon")
            {
                IconArt.ExportIco(args[1]);
                return;
            }

            Native.EnableDpiAwareness();

            bool createdNew;
            using (var mutex = new Mutex(true, "BlinkBreak.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Blink Break is already running.\n\nLook for the eye icon in the system tray (bottom right, next to the clock).",
                        "Blink Break", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool breakNow = Array.IndexOf(args, "--break-now") >= 0;
                Application.Run(new TrayApp(breakNow));
                GC.KeepAlive(mutex);
            }
        }
    }

    // ------------------------------------------------------------------ settings

    class Settings
    {
        public int IntervalMinutes = 20;
        public int BreakSeconds = 20;
        public bool FirstRun;

        static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlinkBreak", "settings.ini");
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) { s.FirstRun = true; return s; }
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    int value;
                    if (!int.TryParse(raw.Substring(eq + 1).Trim(), out value)) continue;
                    if (key == "IntervalMinutes") s.IntervalMinutes = Clamp(value, 1, 240);
                    else if (key == "BreakSeconds") s.BreakSeconds = Clamp(value, 5, 300);
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[]
                {
                    "IntervalMinutes=" + IntervalMinutes,
                    "BreakSeconds=" + BreakSeconds
                });
            }
            catch { }
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }

    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "BlinkBreak";

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled) k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ tray app

    class TrayApp : ApplicationContext
    {
        static readonly int[] IntervalChoices = { 10, 15, 20, 30, 45, 60 };
        static readonly int[] LengthChoices = { 10, 15, 20, 30, 60 };

        readonly Settings settings;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer ticker;
        readonly ToolStripMenuItem statusItem, pauseItem, autoStartItem, intervalMenu, lengthMenu;

        DateTime nextBreak;
        DateTime pausedUntil = DateTime.MinValue;
        List<BreakForm> overlays;
        volatile bool locked, resetRequested;

        public TrayApp(bool breakNow)
        {
            settings = Settings.Load();
            // Start with Windows by default; refresh the path if the exe moved.
            if (settings.FirstRun || AutoStart.IsEnabled()) AutoStart.Set(true);

            var menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem("Blink Break") { Enabled = false };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());

            var nowItem = new ToolStripMenuItem("Take a blink break now", null, delegate { StartBreak(); });
            nowItem.Font = new Font(nowItem.Font, FontStyle.Bold);
            menu.Items.Add(nowItem);
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
                if (e.Button == MouseButtons.Left)
                    tray.ShowBalloonTip(3000, "Blink Break", StatusText(), ToolTipIcon.None);
            };
            tray.Visible = true;

            ResetCountdown();
            if (breakNow) nextBreak = DateTime.Now;

            if (settings.FirstRun)
            {
                settings.Save();
                tray.ShowBalloonTip(8000, "Blink Break is running",
                    "You'll get a " + settings.BreakSeconds + "-second blink break every " + settings.IntervalMinutes +
                    " minutes. Right-click the eye icon for options.", ToolTipIcon.Info);
            }

            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            ticker = new System.Windows.Forms.Timer();
            ticker.Interval = 1000;
            ticker.Tick += delegate { Tick(); };
            ticker.Start();
        }

        bool IsPaused { get { return pausedUntil != DateTime.MinValue; } }

        void ResetCountdown()
        {
            nextBreak = DateTime.Now.AddMinutes(settings.IntervalMinutes);
            UpdateTooltip();
        }

        void Tick()
        {
            if (overlays != null) return;

            if (resetRequested) { resetRequested = false; ResetCountdown(); }

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
            overlays = new List<BreakForm>();
            var clock = Stopwatch.StartNew();
            BreakForm primary = null;

            foreach (Screen screen in Screen.AllScreens)
            {
                var form = new BreakForm(screen, clock, settings.BreakSeconds);
                form.Done += delegate { EndBreak(); };
                overlays.Add(form);
                if (screen.Primary) primary = form;
            }
            foreach (var f in overlays) f.Show();
            if (primary == null && overlays.Count > 0) primary = overlays[0];
            if (primary != null) primary.TakeFocus();
            UpdateTooltip();
        }

        void EndBreak()
        {
            if (overlays == null) return;
            var list = overlays;
            overlays = null;
            foreach (var f in list) { f.AllowClose = true; f.Close(); }
            ResetCountdown();
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
            EndBreak();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }

    // ------------------------------------------------------------------ full-screen overlay

    class BreakForm : Form
    {
        const double TargetOpacity = 1.0;
        const string Title = "Blink break";
        const string Subtitle = "Blink slowly along with the eye, then rest your gaze on something about 6 m (20 ft) away.";
        const string SkipHint = "Press Esc to skip";

        readonly Stopwatch clock;
        readonly int seconds;
        readonly System.Windows.Forms.Timer anim;
        readonly StringFormat center = new StringFormat { Alignment = StringAlignment.Center };

        public event EventHandler Done;
        public bool AllowClose;
        bool done, fadedIn;

        // layout (recomputed on resize)
        Font titleFont, subFont, countFont, hintFont;
        float eyeCx, eyeCy, eyeW, titleY, subY, countY, progY, hintY, unit;
        RectangleF glowRect, progRect, skipRect;
        int lastCount = -1;

        public BreakForm(Screen screen, Stopwatch clock, int seconds)
        {
            this.clock = clock;
            this.seconds = seconds;

            Text = "Blink break";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Color.FromArgb(11, 17, 32);
            Opacity = 0;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            anim = new System.Windows.Forms.Timer();
            anim.Interval = 16;
            anim.Tick += OnAnimTick;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void TakeFocus()
        {
            Native.ForceForeground(Handle);
            Activate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            anim.Start();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ComputeLayout();
        }

        void ComputeLayout()
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;
            float u = Math.Min(w, h) / 100f;
            unit = u;

            eyeW = 34 * u;
            eyeCx = w / 2f;
            eyeCy = h * 0.34f;
            glowRect = new RectangleF(eyeCx - eyeW * 0.85f, eyeCy - eyeW * 0.5f, eyeW * 1.7f, eyeW * 1.0f);
            titleY = eyeCy + 17 * u;
            subY = titleY + 9 * u;
            countY = subY + 8 * u;
            progY = countY + 13.5f * u;
            hintY = h - 6 * u;
            progRect = new RectangleF(eyeCx - 18 * u, progY, 36 * u, Math.Max(3, 0.7f * u));

            DisposeFonts();
            titleFont = new Font("Segoe UI Semibold", 6.2f * u, FontStyle.Regular, GraphicsUnit.Pixel);
            subFont = new Font("Segoe UI", 2.3f * u, FontStyle.Regular, GraphicsUnit.Pixel);
            countFont = new Font("Segoe UI Light", 9f * u, FontStyle.Regular, GraphicsUnit.Pixel);
            hintFont = new Font("Segoe UI", 1.7f * u, FontStyle.Regular, GraphicsUnit.Pixel);

            using (var g = CreateGraphics())
            {
                SizeF sz = g.MeasureString(SkipHint, hintFont);
                skipRect = new RectangleF(eyeCx - sz.Width / 2f - u, hintY - u * 0.5f, sz.Width + 2 * u, sz.Height + u);
            }
            Invalidate();
        }

        void OnAnimTick(object sender, EventArgs e)
        {
            double t = clock.Elapsed.TotalSeconds;
            if (t >= seconds) { Finish(); return; }

            if (!fadedIn)
            {
                double o = Math.Min(1.0, t / 0.6) * TargetOpacity;
                Opacity = o;
                if (o >= TargetOpacity) fadedIn = true;
            }

            // Only repaint the parts that move.
            var eyeArea = glowRect;
            eyeArea.Inflate(unit * 2, unit * 2);
            Invalidate(Rectangle.Ceiling(eyeArea));

            int count = Remaining(t);
            var barArea = progRect;
            barArea.Inflate(unit, unit);
            Invalidate(Rectangle.Ceiling(barArea));
            if (count != lastCount)
            {
                lastCount = count;
                Invalidate(new Rectangle(0, (int)countY, ClientSize.Width, (int)(13 * unit)));
            }
        }

        int Remaining(double t) { return Math.Max(1, (int)Math.Ceiling(seconds - t)); }

        // Slow, deliberate blink every 3 s: close 0.25 s, hold 0.15 s, open 0.3 s.
        static float BlinkOpenness(double t)
        {
            double p = (t + 2.2) % 3.0;
            if (p < 0.25) return 1f - Smooth(p / 0.25);
            if (p < 0.40) return 0f;
            if (p < 0.70) return Smooth((p - 0.40) / 0.30);
            return 1f;
        }

        static float Smooth(double x) { return (float)(x * x * (3 - 2 * x)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (titleFont == null) ComputeLayout();
            if (titleFont == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            int w = ClientSize.Width;
            double t = clock.Elapsed.TotalSeconds;

            // soft glow behind the eye
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(glowRect);
                using (var pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = Color.FromArgb(60, 70, 130, 230);
                    pb.SurroundColors = new[] { Color.FromArgb(0, 70, 130, 230) };
                    g.FillEllipse(pb, glowRect);
                }
            }

            EyeArt.Draw(g, eyeCx, eyeCy, eyeW, BlinkOpenness(t), true, Color.FromArgb(225, 232, 245));

            float textW = Math.Min(w * 0.85f, unit * 120);
            float textX = (w - textW) / 2f;
            using (var b = new SolidBrush(Color.White))
                g.DrawString(Title, titleFont, b, new RectangleF(0, titleY, w, unit * 9), center);
            using (var b = new SolidBrush(Color.FromArgb(160, 175, 205)))
                g.DrawString(Subtitle, subFont, b, new RectangleF(textX, subY, textW, unit * 7), center);
            using (var b = new SolidBrush(Color.FromArgb(235, 240, 250)))
                g.DrawString(Remaining(t).ToString(), countFont, b, new RectangleF(0, countY, w, unit * 13), center);

            // progress bar
            float frac = (float)Math.Min(1.0, t / seconds);
            using (var track = RoundRect(progRect, progRect.Height / 2f))
            using (var b = new SolidBrush(Color.FromArgb(36, 50, 80)))
                g.FillPath(b, track);
            if (frac > 0.001f)
            {
                var fill = new RectangleF(progRect.X, progRect.Y, Math.Max(progRect.Height, progRect.Width * frac), progRect.Height);
                using (var path = RoundRect(fill, fill.Height / 2f))
                using (var b = new LinearGradientBrush(progRect, Color.FromArgb(80, 150, 255), Color.FromArgb(140, 205, 255), 0f))
                    g.FillPath(b, path);
            }

            using (var b = new SolidBrush(Color.FromArgb(100, 115, 145)))
                g.DrawString(SkipHint, hintFont, b, new RectangleF(0, hintY, w, unit * 3), center);
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Max(0.1f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Finish();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = skipRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (skipRect.Contains(e.Location)) Finish();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 counts as "skip" and closes the overlays on every monitor.
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Finish(); }
            base.OnFormClosing(e);
        }

        void Finish()
        {
            if (done) return;
            done = true;
            anim.Stop();
            if (Done != null) Done(this, EventArgs.Empty);
        }

        void DisposeFonts()
        {
            if (titleFont != null) titleFont.Dispose();
            if (subFont != null) subFont.Dispose();
            if (countFont != null) countFont.Dispose();
            if (hintFont != null) hintFont.Dispose();
            titleFont = subFont = countFont = hintFont = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                anim.Dispose();
                center.Dispose();
                DisposeFonts();
            }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ artwork

    static class EyeArt
    {
        // open: 1 = fully open, 0 = closed
        public static void Draw(Graphics g, float cx, float cy, float W, float open, bool lashes, Color line)
        {
            float half = W / 2f;
            float d = W * 0.30f;                       // bezier control offset -> eye height ~0.45 W
            float lowerCtl = cy + d;
            float upperCtl = lowerCtl - 2f * d * open;  // closed: upper lid lies on the lower lid

            var L = new PointF(cx - half, cy);
            var R = new PointF(cx + half, cy);
            var u1 = new PointF(cx - half * 0.45f, upperCtl);
            var u2 = new PointF(cx + half * 0.45f, upperCtl);
            var l1 = new PointF(cx + half * 0.45f, lowerCtl);
            var l2 = new PointF(cx - half * 0.45f, lowerCtl);

            float upperWidth = Math.Max(1.2f, W * 0.035f);
            float lowerWidth = Math.Max(1f, W * 0.02f);

            if (open > 0.02f)
            {
                using (var eye = new GraphicsPath())
                {
                    eye.AddBezier(L, u1, u2, R);
                    eye.AddBezier(R, l1, l2, L);
                    eye.CloseFigure();

                    using (var sclera = new SolidBrush(Color.FromArgb(240, 244, 250)))
                        g.FillPath(sclera, eye);

                    GraphicsState state = g.Save();
                    g.SetClip(eye, CombineMode.Intersect);

                    float r = W * 0.17f;
                    var iris = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
                    using (var gp = new GraphicsPath())
                    {
                        gp.AddEllipse(iris);
                        using (var pb = new PathGradientBrush(gp))
                        {
                            pb.CenterPoint = new PointF(cx, cy);
                            pb.CenterColor = Color.FromArgb(125, 195, 255);
                            pb.SurroundColors = new[] { Color.FromArgb(30, 90, 195) };
                            g.FillEllipse(pb, iris);
                        }
                    }
                    using (var ring = new Pen(Color.FromArgb(20, 55, 130), Math.Max(1f, W * 0.012f)))
                        g.DrawEllipse(ring, iris);

                    float pr = r * 0.42f;
                    using (var b = new SolidBrush(Color.FromArgb(12, 18, 35)))
                        g.FillEllipse(b, cx - pr, cy - pr, 2 * pr, 2 * pr);

                    float hr = r * 0.2f;
                    using (var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                        g.FillEllipse(b, cx - r * 0.38f - hr, cy - r * 0.38f - hr, 2 * hr, 2 * hr);
                    float hr2 = r * 0.09f;
                    using (var b = new SolidBrush(Color.FromArgb(180, 255, 255, 255)))
                        g.FillEllipse(b, cx + r * 0.3f - hr2, cy + r * 0.28f - hr2, 2 * hr2, 2 * hr2);

                    g.Restore(state);
                }

                using (var pen = new Pen(Color.FromArgb(160, line), lowerWidth))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawBezier(pen, R, l1, l2, L);
                }
            }

            using (var pen = new Pen(line, upperWidth))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawBezier(pen, L, u1, u2, R);
            }

            if (lashes)
            {
                float dir = 2f * open - 1f;             // lashes point up when open, down when closed
                float len = W * 0.07f;
                using (var pen = new Pen(line, upperWidth * 0.8f))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    for (int i = 0; i < 5; i++)
                    {
                        float t = 0.22f + i * 0.14f;
                        PointF p = Bezier(L, u1, u2, R, t);
                        PointF tan = BezierTangent(L, u1, u2, R, t);
                        float tl = (float)Math.Sqrt(tan.X * tan.X + tan.Y * tan.Y);
                        if (tl < 0.0001f) continue;
                        float tx = tan.X / tl, ty = tan.Y / tl;
                        float nx = ty, ny = -tx;          // normal on the "upper" side
                        float splay = (t - 0.5f) * 0.9f;
                        var end = new PointF(p.X + nx * len * dir + tx * len * splay,
                                             p.Y + ny * len * dir + ty * len * splay);
                        if (Math.Abs(dir) > 0.05f) g.DrawLine(pen, p, end);
                    }
                }
            }
        }

        static PointF Bezier(PointF a, PointF b, PointF c, PointF d, float t)
        {
            float mt = 1 - t;
            float x = mt * mt * mt * a.X + 3 * mt * mt * t * b.X + 3 * mt * t * t * c.X + t * t * t * d.X;
            float y = mt * mt * mt * a.Y + 3 * mt * mt * t * b.Y + 3 * mt * t * t * c.Y + t * t * t * d.Y;
            return new PointF(x, y);
        }

        static PointF BezierTangent(PointF a, PointF b, PointF c, PointF d, float t)
        {
            float mt = 1 - t;
            float x = 3 * mt * mt * (b.X - a.X) + 6 * mt * t * (c.X - b.X) + 3 * t * t * (d.X - c.X);
            float y = 3 * mt * mt * (b.Y - a.Y) + 6 * mt * t * (c.Y - b.Y) + 3 * t * t * (d.Y - c.Y);
            return new PointF(x, y);
        }
    }

    static class IconArt
    {
        public static Bitmap Render(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                var rect = new RectangleF(0, 0, size, size);
                float rad = size * 0.22f;
                using (var path = new GraphicsPath())
                {
                    float d = rad * 2;
                    path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                    path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                    path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                    path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    using (var b = new LinearGradientBrush(rect, Color.FromArgb(72, 152, 255), Color.FromArgb(28, 70, 180), 90f))
                        g.FillPath(b, path);
                }
                EyeArt.Draw(g, size / 2f, size / 2f + (size >= 48 ? size * 0.04f : 0), size * 0.84f, 1f, size >= 48, Color.FromArgb(14, 30, 80));
            }
            return bmp;
        }

        // Multi-resolution .ico with PNG-compressed entries (supported since Windows Vista).
        public static void ExportIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Render(s))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }

            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim);
                    w.Write(dim);
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
        }
    }

    // ------------------------------------------------------------------ Win32

    static class Native
    {
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

        public static void EnableDpiAwareness()
        {
            try { SetProcessDpiAwareness(2); }            // per-monitor: crisp on every screen
            catch { try { SetProcessDPIAware(); } catch { } }
        }

        // QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4
        public static bool IsFullscreenExclusive()
        {
            try
            {
                int state;
                if (SHQueryUserNotificationState(out state) != 0) return false;
                return state == 3 || state == 4;
            }
            catch { return false; }
        }

        // Windows normally refuses to let a background app take focus; borrowing the
        // foreground thread's input state makes Esc work on the overlay straight away.
        public static void ForceForeground(IntPtr hwnd)
        {
            IntPtr fg = GetForegroundWindow();
            uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, IntPtr.Zero);
            uint me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            SetForegroundWindow(hwnd);
            if (attached) AttachThreadInput(me, fgThread, false);
        }
    }
}
