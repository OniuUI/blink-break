using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BlinkBreak
{
    // Small pop-up in the bottom-right corner. Never takes keyboard focus.
    class WaterToast : Form
    {
        static readonly Color Bg = Color.FromArgb(20, 29, 50);
        static readonly Color Border = Color.FromArgb(48, 66, 102);
        static readonly Color Ink = Color.FromArgb(236, 241, 250);
        static readonly Color Muted = Color.FromArgb(150, 165, 195);
        static readonly Color Water = Color.FromArgb(64, 186, 255);
        static readonly Color WaterHover = Color.FromArgb(120, 210, 255);
        static readonly Color Ghost = Color.FromArgb(32, 44, 72);
        static readonly Color GhostHover = Color.FromArgb(44, 60, 96);

        const float W = 384, H = 138;

        readonly string title, line, primaryText, secondaryText;
        readonly float progress;
        readonly Action primary, secondary, body;
        readonly int lifeMs;
        readonly float s;
        readonly Stopwatch age = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer;
        readonly Font fTitle, fLine, fButton, fClose;
        RectangleF primaryRect, secondaryRect, closeRect;
        int hover = -1;                 // 0 primary, 1 secondary, 2 close, 3 body
        int hoveredMs, lastTick;
        readonly Point startCursor = Cursor.Position;
        bool mouseMoved;                // ignore a cursor that was just parked where the toast appeared
        Point finalLocation;
        bool closing;

        public WaterToast(string title, string line, float progress,
                          string primaryText, Action primary, string secondaryText, Action secondary,
                          Action body, int lifeMs)
        {
            this.title = title;
            this.line = line;
            this.progress = progress;
            this.primaryText = primaryText;
            this.primary = primary;
            this.secondaryText = secondaryText;
            this.secondary = secondary;
            this.body = body;
            this.lifeMs = lifeMs;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Bg;
            Opacity = 0;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            fTitle = new Font("Segoe UI Semibold", 15 * s, FontStyle.Regular, GraphicsUnit.Pixel);
            fLine = new Font("Segoe UI", 12.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            fButton = new Font("Segoe UI Semibold", 12.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            fClose = new Font("Segoe UI", 16 * s, FontStyle.Regular, GraphicsUnit.Pixel);

            Size = new Size((int)(W * s), (int)(H * s));
            var work = Screen.PrimaryScreen.WorkingArea;
            finalLocation = new Point(work.Right - Width - (int)(16 * s), work.Bottom - Height - (int)(16 * s));
            Location = new Point(finalLocation.X, finalLocation.Y + (int)(14 * s));
            using (var path = Draw.RoundRect(new RectangleF(0, 0, Width, Height), 12 * s))
                Region = new Region(path);

            float bw = (W - 18 * 2 - 10) / 2f * s, bh = 34 * s, by = (H - 18 - 34) * s;
            primaryRect = new RectangleF(18 * s, by, bw, bh);
            secondaryRect = new RectangleF(18 * s + bw + 10 * s, by, bw, bh);
            closeRect = new RectangleF((W - 38) * s, 8 * s, 28 * s, 28 * s);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 16;
            timer.Tick += OnTick;
            lastTick = Environment.TickCount;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000008 /* TOPMOST */ | 0x00000080 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
        }

        void OnTick(object sender, EventArgs e)
        {
            int now = Environment.TickCount;
            // Don't time out while you're reading it with the mouse on it (but never forever).
            if (hover >= 0 && mouseMoved && hoveredMs < 60000) hoveredMs += now - lastTick;
            lastTick = now;

            double t = age.Elapsed.TotalMilliseconds;
            if (t < 220)
            {
                double k = t / 220.0;
                k = 1 - (1 - k) * (1 - k);
                Opacity = k;
                Top = finalLocation.Y + (int)((1 - k) * 14 * s);
            }
            else if (Opacity < 1)
            {
                Opacity = 1;
                Top = finalLocation.Y;
            }

            if (lifeMs > 0 && t - hoveredMs > lifeMs) Dismiss();
        }

        public void Dismiss()
        {
            if (closing) return;
            closing = true;
            timer.Stop();
            Close();
        }

        void Fire(Action action)
        {
            Dismiss();
            if (action != null) action();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var path = Draw.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12 * s))
            using (var pen = new Pen(Border, Math.Max(1f, s)))
                g.DrawPath(pen, path);

            DropArt.Draw(g, 38 * s, 44 * s, 40 * s, Math.Max(0.12f, progress));

            using (var b = new SolidBrush(Ink)) g.DrawString(title, fTitle, b, 66 * s, 16 * s);
            using (var b = new SolidBrush(Muted)) g.DrawString(line, fLine, b, 67 * s, 40 * s);

            var bar = new RectangleF(68 * s, 66 * s, (W - 68 - 22) * s, 5 * s);
            Draw.FillRound(g, Ghost, bar, 2.5f * s);
            if (progress > 0)
                Draw.FillRound(g, Water, new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * Math.Min(1f, progress)), bar.Height), 2.5f * s);

            Button(g, primaryRect, primaryText, true, hover == 0);
            if (secondaryText != null) Button(g, secondaryRect, secondaryText, false, hover == 1);

            using (var b = new SolidBrush(hover == 2 ? Ink : Muted))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("×", fClose, b, closeRect, fmt);
        }

        void Button(Graphics g, RectangleF r, string text, bool isPrimary, bool hot)
        {
            Color fill = isPrimary ? (hot ? WaterHover : Water) : (hot ? GhostHover : Ghost);
            Draw.FillRound(g, fill, r, 8 * s);
            using (var b = new SolidBrush(isPrimary ? Color.FromArgb(8, 20, 36) : Ink))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, fButton, b, r, fmt);
        }

        int HitTest(Point p)
        {
            if (primaryRect.Contains(p)) return 0;
            if (secondaryText != null && secondaryRect.Contains(p)) return 1;
            if (closeRect.Contains(p)) return 2;
            return ClientRectangle.Contains(p) ? 3 : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Cursor.Position != startCursor) mouseMoved = true;
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != -1) { hover = -1; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            switch (HitTest(e.Location))
            {
                case 0: Fire(primary); break;
                case 1: Fire(secondary); break;
                case 2: Dismiss(); break;
                case 3: Fire(body); break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                fTitle.Dispose();
                fLine.Dispose();
                fButton.Dispose();
                fClose.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
