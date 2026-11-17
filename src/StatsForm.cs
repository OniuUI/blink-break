using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BlinkBreak
{
    // "Your eye health" window: today's numbers, a 7-day chart and what the research says.
    class StatsForm : Form
    {
        static readonly Color Bg = Color.FromArgb(11, 17, 32);
        static readonly Color Card = Color.FromArgb(20, 29, 50);
        static readonly Color Ink = Color.FromArgb(236, 241, 250);
        static readonly Color Muted = Color.FromArgb(138, 153, 184);
        static readonly Color Faint = Color.FromArgb(36, 50, 80);
        static readonly Color Accent = Color.FromArgb(88, 166, 255);
        static readonly Color SkipColor = Color.FromArgb(70, 86, 120);
        static readonly Color Good = Color.FromArgb(86, 204, 148);
        static readonly Color Warn = Color.FromArgb(245, 184, 88);

        const float LogicalWidth = 940;
        const float Pad = 32;

        static readonly string[][] Facts =
        {
            new[] { "Screens slow your blinking",
                    "Relaxed, you blink about 15–20 times a minute. Reading a screen, that can fall to 4–7, and more blinks stop halfway, so your tears evaporate." },
            new[] { "Full blinks refresh your tears",
                    "Blinking slowly and completely, like the eye on the break screen, spreads a fresh layer of tears over the eye." },
            new[] { "Looking far relaxes focus",
                    "Your focusing muscles work non-stop at screen distance. Looking about 6 m (20 ft) away lets them relax." },
            new[] { "Consistency is what counts",
                    "In a 2023 study, office workers using 20-20-20 reminders reported less eye strain and dryness after two weeks. The benefit faded within a week of stopping." },
        };

        static readonly string[][] Sources =
        {
            new[] { "20-20-20 rule (AAO)", "https://www.aao.org/eye-health/tips-prevention/computer-usage" },
            new[] { "Screens & dry eye (review, 2021)", "https://pmc.ncbi.nlm.nih.gov/articles/PMC8439964/" },
            new[] { "20-20-20 study (2023)", "https://research.aston.ac.uk/en/publications/the-effects-of-breaks-on-digital-eye-strain-dry-eye-and-binocular/" },
        };

        readonly Stats stats;
        readonly Settings settings;
        readonly Func<string> status;
        readonly System.Windows.Forms.Timer refresh;
        readonly List<LinkLabel> links = new List<LinkLabel>();
        readonly StringFormat centerFmt = new StringFormat { Alignment = StringAlignment.Center };
        readonly StringFormat rightFmt = new StringFormat { Alignment = StringAlignment.Far };
        float s = 1f;
        float footerY;
        Font fTitle, fSub, fHeading, fTileLabel, fTileValue, fSmall, fBody, fBodyBold;

        public StatsForm(Stats stats, Settings settings, Func<string> status)
        {
            this.stats = stats;
            this.settings = settings;
            this.status = status;

            Text = "Blink Break · Eye health";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Ink;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            // Scale with the display, but always fit on screen.
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            s = Math.Min(s, work.Width * 0.95f / LogicalWidth);
            CreateFonts();
            float height = Measure();
            if (height > work.Height * 0.95f)
            {
                s *= work.Height * 0.95f / height;
                CreateFonts();
                height = Measure();
            }
            ClientSize = new Size((int)Math.Ceiling(S(LogicalWidth)), (int)Math.Ceiling(height));

            foreach (var src in Sources)
            {
                string url = src[1];
                var link = new LinkLabel();
                link.Text = src[0];
                link.AutoSize = true;
                link.UseMnemonic = false;
                link.Font = fSmall;
                link.LinkColor = Accent;
                link.VisitedLinkColor = Accent;
                link.ActiveLinkColor = Ink;
                link.BackColor = Bg;
                link.LinkBehavior = LinkBehavior.HoverUnderline;
                link.LinkClicked += delegate
                {
                    try { Process.Start(url); } catch (Exception ex) { Log.Write("Could not open link: " + ex.Message); }
                };
                links.Add(link);
                Controls.Add(link);
            }
            PlaceLinks();

            refresh = new System.Windows.Forms.Timer();
            refresh.Interval = 1000;
            refresh.Tick += delegate { Invalidate(); };
            refresh.Start();
        }

        float S(float v) { return v * s; }

        void CreateFonts()
        {
            DisposeFonts();
            fTitle = new Font("Segoe UI Semibold", S(24), FontStyle.Regular, GraphicsUnit.Pixel);
            fSub = new Font("Segoe UI", S(13), FontStyle.Regular, GraphicsUnit.Pixel);
            fHeading = new Font("Segoe UI Semibold", S(15), FontStyle.Regular, GraphicsUnit.Pixel);
            fTileLabel = new Font("Segoe UI", S(12.5f), FontStyle.Regular, GraphicsUnit.Pixel);
            fTileValue = new Font("Segoe UI Semibold", S(30), FontStyle.Regular, GraphicsUnit.Pixel);
            fSmall = new Font("Segoe UI", S(12), FontStyle.Regular, GraphicsUnit.Pixel);
            fBody = new Font("Segoe UI", S(13), FontStyle.Regular, GraphicsUnit.Pixel);
            fBodyBold = new Font("Segoe UI Semibold", S(13.5f), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        float Measure()
        {
            using (var g = CreateGraphics())
            {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                return Render(g, false);
            }
        }

        void PlaceLinks()
        {
            if (links.Count == 0) return;
            float x = S(Pad);
            using (var g = CreateGraphics()) x += g.MeasureString("Sources:", fSmall).Width + S(4);
            foreach (var link in links)
            {
                link.Location = new Point((int)x, (int)footerY);
                x += link.PreferredWidth + S(14);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.UseDarkTitleBar(Handle);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float oldFooter = footerY;
            float height = Render(g, true);

            // Text can wrap differently as the numbers change: grow/shrink to fit.
            if (Math.Abs(height - ClientSize.Height) > 2) ClientSize = new Size(ClientSize.Width, (int)Math.Ceiling(height));
            if (Math.Abs(oldFooter - footerY) > 0.5f) PlaceLinks();
        }

        // Lays out (and optionally draws) the whole window; returns the content height.
        float Render(Graphics g, bool draw)
        {
            DayStats today = stats.Today;
            int interval = settings.IntervalMinutes;
            int recommended = (int)(today.ScreenSeconds / (interval * 60.0));
            float x0 = S(Pad), cw = S(LogicalWidth) - 2 * S(Pad);

            // ---- header
            float y = S(26);
            if (draw)
            {
                EyeArt.Draw(g, x0 + S(22), y + S(20), S(44), 1f, false, Color.FromArgb(225, 232, 245));
                DrawText(g, "Your eye health", fTitle, Ink, x0 + S(60), y - S(4));
                DrawText(g, DateTime.Today.ToString("dddd d MMMM") + "   ·   " + status(), fSub, Muted, x0 + S(62), y + S(28));
            }
            y += S(72);

            // ---- today tiles
            float gap = S(14), tileW = (cw - 3 * gap) / 4f, tileH = S(108);
            bool longStretch = today.LongestStretch > 2 * interval * 60 + 60;
            if (draw)
            {
                Tile(g, x0, y, tileW, tileH, "Blink breaks today", today.Completed.ToString(),
                     recommended > 0 ? "of " + recommended + " recommended" : "first one after " + interval + " min", Ink);
                Tile(g, x0 + (tileW + gap), y, tileW, tileH, "Eyes rested", Dur(today.RestSeconds),
                     "≈ " + today.Blinks + " slow, full blinks", Ink);
                Tile(g, x0 + 2 * (tileW + gap), y, tileW, tileH, "Longest stretch", Minutes(today.LongestStretch),
                     "at the screen without a break", longStretch ? Warn : Ink);
                Tile(g, x0 + 3 * (tileW + gap), y, tileW, tileH, "Skipped today", today.Skipped.ToString(),
                     today.Skipped == 1 ? "break cut short" : "breaks cut short", today.Skipped > today.Completed ? Warn : Ink);
            }
            y += tileH + S(28);

            // ---- two columns
            float colTop = y;
            float rightW = S(372), colGap = S(32);
            float leftW = cw - rightW - colGap;
            float lx = x0, rx = x0 + leftW + colGap;

            // coverage
            float ly = colTop;
            float frac = recommended > 0 ? Math.Min(1f, today.Completed / (float)recommended) : 0f;
            Color barColor = recommended == 0 ? Accent : (frac >= 0.8f ? Good : (frac >= 0.5f ? Accent : Warn));
            if (draw)
            {
                DrawText(g, "Break coverage today", fHeading, Ink, lx, ly);
                using (var b = new SolidBrush(recommended > 0 ? barColor : Muted))
                    g.DrawString(recommended > 0 ? (int)Math.Round(frac * 100) + "%" : "–", fHeading, b,
                                 new RectangleF(lx, ly, leftW, S(24)), rightFmt);
            }
            ly += S(30);
            if (draw)
            {
                Draw.FillRound(g, Faint, new RectangleF(lx, ly, leftW, S(10)), S(5));
                if (frac > 0) Draw.FillRound(g, barColor, new RectangleF(lx, ly, Math.Max(S(10), leftW * frac), S(10)), S(5));
            }
            ly += S(20);
            string explain = recommended > 0
                ? today.Completed + " of " + recommended + " breaks during " + Dur(today.ScreenSeconds) +
                  " of active screen time (one every " + interval + " min)."
                : "Coverage starts after " + interval + " min of screen time today (" + Dur(today.ScreenSeconds) + " so far).";
            ly += Para(g, draw, explain, fBody, Muted, lx, ly, leftW) + S(6);
            ly += Para(g, draw, Insight(today, recommended, frac), fBody, Ink, lx, ly, leftW) + S(26);

            // 7-day chart
            if (draw)
            {
                DrawText(g, "Last 7 days", fHeading, Ink, lx, ly);
                float lgX = lx + leftW;
                lgX -= g.MeasureString("Skipped", fSmall).Width;
                DrawText(g, "Skipped", fSmall, Muted, lgX, ly + S(2));
                lgX -= S(14);
                Draw.FillRound(g, SkipColor, new RectangleF(lgX, ly + S(6), S(9), S(9)), S(2));
                lgX -= S(16) + g.MeasureString("Taken", fSmall).Width;
                DrawText(g, "Taken", fSmall, Muted, lgX, ly + S(2));
                lgX -= S(14);
                Draw.FillRound(g, Accent, new RectangleF(lgX, ly + S(6), S(9), S(9)), S(2));
            }
            ly += S(34);
            float chartH = S(120), labelH = S(18);
            if (draw) DrawChart(g, lx, ly, leftW, chartH, labelH);
            ly += chartH + labelH + S(16);

            int streak = stats.Streak();
            string totals = "All time: " + stats.TotalCompleted + (stats.TotalCompleted == 1 ? " break" : " breaks") +
                            "  ·  " + Dur(stats.TotalRestSeconds) + " resting  ·  ≈ " + stats.TotalBlinks.ToString("N0") +
                            " full blinks  ·  " + streak + "-day streak";
            ly += Para(g, draw, totals, fBody, Muted, lx, ly, leftW);
            float leftH = ly - colTop;

            // right column card: what this does for your eyes
            float rightH = Math.Max(Facts_(g, false, rx, colTop, rightW), leftH);
            if (draw)
            {
                Draw.FillRound(g, Card, new RectangleF(rx, colTop, rightW, rightH), S(14));
                Facts_(g, true, rx, colTop, rightW);
            }
            y = colTop + rightH + S(22);

            // ---- footer
            footerY = y;
            if (draw) DrawText(g, "Sources:", fSmall, Muted, x0, y);
            y += S(24);
            if (draw)
            {
                DrawText(g, "Not medical advice. See an eye-care professional if your eyes stay sore, dry or blurry.", fSmall, Muted, x0, y);
                using (var b = new SolidBrush(Muted))
                    g.DrawString("Open this anytime: " + TrayApp.StatsHotkeyText, fSmall, b, new RectangleF(x0, y, cw, S(20)), rightFmt);
            }
            y += S(20) + S(22);
            return y;
        }

        float Facts_(Graphics g, bool draw, float x, float top, float width)
        {
            float pad = S(22);
            float y = top + pad;
            x += pad;
            width -= 2 * pad;
            if (draw) DrawText(g, "How breaks help your eyes", fHeading, Ink, x, y);
            y += S(34);
            foreach (var fact in Facts)
            {
                if (draw)
                {
                    using (var b = new SolidBrush(Accent)) g.FillEllipse(b, x, y + S(6), S(7), S(7));
                    DrawText(g, fact[0], fBodyBold, Ink, x + S(16), y - S(1));
                }
                y += S(21);
                y += Para(g, draw, fact[1], fBody, Muted, x + S(16), y, width - S(16)) + S(14);
            }
            return y - top + S(4);
        }

        void DrawChart(Graphics g, float x, float y, float w, float h, float labelH)
        {
            const int Days = 7;
            int max = 1;
            for (int i = 0; i < Days; i++)
            {
                var d = stats.Peek(DateTime.Today.AddDays(i - (Days - 1)));
                max = Math.Max(max, d.Completed + d.Skipped);
            }

            float colW = w / Days;
            float barW = Math.Min(S(30), colW * 0.5f);
            float valueH = S(18);
            float baseY = y + h;

            using (var axis = new Pen(Faint, Math.Max(1f, S(1))))
                g.DrawLine(axis, x, baseY + S(0.5f), x + w, baseY + S(0.5f));

            for (int i = 0; i < Days; i++)
            {
                DateTime day = DateTime.Today.AddDays(i - (Days - 1));
                var d = stats.Peek(day);
                int total = d.Completed + d.Skipped;
                float cx = x + colW * i + colW / 2f;
                bool isToday = i == Days - 1;

                if (total > 0)
                {
                    float fullH = (h - valueH) * total / max;
                    float doneH = (h - valueH) * d.Completed / max;
                    Draw.FillRound(g, SkipColor, new RectangleF(cx - barW / 2, baseY - fullH, barW, fullH), S(5));
                    if (d.Completed > 0)
                        Draw.FillRound(g, Accent, new RectangleF(cx - barW / 2, baseY - doneH, barW, doneH), S(5));
                    using (var b = new SolidBrush(Ink))
                        g.DrawString(d.Completed.ToString(), fSmall, b, new RectangleF(cx - colW / 2, baseY - fullH - valueH, colW, valueH), centerFmt);
                }

                using (var b = new SolidBrush(isToday ? Ink : Muted))
                    g.DrawString(isToday ? "Today" : day.ToString("ddd"), fSmall, b,
                                 new RectangleF(cx - colW / 2, baseY + S(4), colW, labelH), centerFmt);
            }
        }

        string Insight(DayStats d, int recommended, float frac)
        {
            if (recommended == 0)
                return d.Completed > 0
                    ? "Good start. Keep it up as your screen time adds up."
                    : "No breaks yet today. Your first one comes after " + settings.IntervalMinutes + " minutes at the screen.";
            if (frac >= 0.8f) return "Great consistency. Regular breaks are exactly what the research rewards.";
            if (frac >= 0.5f) return "Decent. Skipping or pausing fewer breaks during long sessions will help.";
            return "Your eyes went long stretches without rest today. Try to let the breaks run to the end.";
        }

        void Tile(Graphics g, float x, float y, float w, float h, string label, string value, string sub, Color valueColor)
        {
            Draw.FillRound(g, Card, new RectangleF(x, y, w, h), S(14));
            DrawText(g, label, fTileLabel, Muted, x + S(18), y + S(14));
            DrawText(g, value, fTileValue, valueColor, x + S(15), y + S(32));
            using (var b = new SolidBrush(Muted))
            using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(sub, fSmall, b, new RectangleF(x + S(18), y + S(78), w - S(26), S(20)), fmt);
        }

        static void DrawText(Graphics g, string text, Font f, Color c, float x, float y)
        {
            using (var b = new SolidBrush(c)) g.DrawString(text, f, b, x, y);
        }

        static float Para(Graphics g, bool draw, string text, Font f, Color c, float x, float y, float w)
        {
            SizeF size = g.MeasureString(text, f, (int)w);
            if (draw)
                using (var b = new SolidBrush(c))
                    g.DrawString(text, f, b, new RectangleF(x, y, w, size.Height + 2));
            return size.Height;
        }

        public static string Dur(double seconds)
        {
            int t = (int)Math.Round(seconds);
            if (t < 60) return t + "s";
            if (t < 3600) return (t / 60) + "m " + (t % 60) + "s";
            return (t / 3600) + "h " + ((t % 3600) / 60) + "m";
        }

        static string Minutes(double seconds)
        {
            int m = (int)Math.Round(seconds / 60.0);
            if (seconds < 60) return (int)Math.Round(seconds) + "s";
            if (m < 60) return m + " min";
            return (m / 60) + "h " + (m % 60) + "m";
        }

        void DisposeFonts()
        {
            foreach (var f in new[] { fTitle, fSub, fHeading, fTileLabel, fTileValue, fSmall, fBody, fBodyBold })
                if (f != null) f.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && refresh != null) refresh.Dispose();
            base.Dispose(disposing);          // child links go first; they share fSmall
            if (disposing)
            {
                centerFmt.Dispose();
                rightFmt.Dispose();
                DisposeFonts();
            }
        }
    }
}
