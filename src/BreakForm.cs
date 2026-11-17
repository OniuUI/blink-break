using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BlinkBreak
{
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

        // true = ran to the end, false = skipped
        public event Action<bool> Finished;
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
            if (t >= seconds) { Finish(true); return; }

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
            using (var track = Draw.RoundRect(progRect, progRect.Height / 2f))
            using (var b = new SolidBrush(Color.FromArgb(36, 50, 80)))
                g.FillPath(b, track);
            if (frac > 0.001f)
            {
                var fill = new RectangleF(progRect.X, progRect.Y, Math.Max(progRect.Height, progRect.Width * frac), progRect.Height);
                using (var path = Draw.RoundRect(fill, fill.Height / 2f))
                using (var b = new LinearGradientBrush(progRect, Color.FromArgb(80, 150, 255), Color.FromArgb(140, 205, 255), 0f))
                    g.FillPath(b, path);
            }

            using (var b = new SolidBrush(Color.FromArgb(100, 115, 145)))
                g.DrawString(SkipHint, hintFont, b, new RectangleF(0, hintY, w, unit * 3), center);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Finish(false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = skipRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (skipRect.Contains(e.Location)) Finish(false);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 counts as "skip" and closes the overlays on every monitor.
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Finish(false); }
            base.OnFormClosing(e);
        }

        void Finish(bool completed)
        {
            if (done) return;
            done = true;
            anim.Stop();
            // Let the current input/timer message finish before the overlays are torn down.
            if (Finished != null && IsHandleCreated)
                BeginInvoke((Action)(() => Finished(completed)));
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

}
