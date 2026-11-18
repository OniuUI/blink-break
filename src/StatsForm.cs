using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BlinkBreak
{
    enum HealthTab { Eyes, Water }

    // What the stats window needs from the running app.
    interface IHealthHost
    {
        string BreakStatus();
        string WaterStatus();
        void LogWater(int ml, bool showToast);
        void UndoWater();
    }

    // Health window with two tabs: eye breaks and water. Everything is custom-drawn.
    class StatsForm : Form
    {
        static readonly Color Bg = Color.FromArgb(11, 17, 32);
        static readonly Color Card = Color.FromArgb(20, 29, 50);
        static readonly Color CardHover = Color.FromArgb(30, 42, 70);
        static readonly Color Ink = Color.FromArgb(236, 241, 250);
        static readonly Color Muted = Color.FromArgb(138, 153, 184);
        static readonly Color Faint = Color.FromArgb(36, 50, 80);
        static readonly Color Accent = Color.FromArgb(88, 166, 255);
        static readonly Color SkipColor = Color.FromArgb(70, 86, 120);
        static readonly Color Good = Color.FromArgb(86, 204, 148);
        static readonly Color Warn = Color.FromArgb(245, 184, 88);
        static readonly Color Water = Color.FromArgb(64, 186, 255);
        static readonly Color WaterHover = Color.FromArgb(120, 210, 255);
        static readonly Color WaterDim = Color.FromArgb(44, 92, 150);

        const float LogicalWidth = 940;
        const float Pad = 32;

        static readonly string[][] EyeFacts =
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

        static readonly string[][] EyeSources =
        {
            new[] { "20-20-20 rule (AAO)", "https://www.aao.org/eye-health/tips-prevention/computer-usage" },
            new[] { "Screens & dry eye (review, 2021)", "https://pmc.ncbi.nlm.nih.gov/articles/PMC8439964/" },
            new[] { "20-20-20 study (2023)", "https://research.aston.ac.uk/en/publications/the-effects-of-breaks-on-digital-eye-strain-dry-eye-and-binocular/" },
        };

        static readonly string[][] WaterFacts =
        {
            new[] { "How much to drink",
                    "Europe's food-safety agency (EFSA) advises about 2.0 L of water a day for women and 2.5 L for men, from food and drinks. About 20% comes from food, so roughly 1.6 L and 2.0 L to drink." },
            new[] { "More in heat and exercise",
                    "Those amounts assume mild weather and light activity, so drink more when it's hot or you sweat. US guidance is higher overall: 2.7 L for women and 3.7 L for men." },
            new[] { "Small losses add up",
                    "In lab studies, losing just 1–2% of body water left people more tired and tense, lowered their mood and concentration, and gave some of them headaches." },
            new[] { "Thirst is a good guide",
                    "For most healthy adults, drinking when thirsty and with meals is enough. Water, tea and coffee all count. Pale yellow urine is a good sign." },
        };

        static readonly string[][] WaterSources =
        {
            new[] { "EFSA water guidance (2010)", "https://www.efsa.europa.eu/en/press/news/nda100326" },
            new[] { "US National Academies (2004)", "https://www.nationalacademies.org/news/report-sets-dietary-intake-levels-for-water-salt-and-potassium-to-maintain-health-and-reduce-chronic-disease-risk" },
            new[] { "Mild dehydration study (2011)", "https://www.cambridge.org/core/journals/british-journal-of-nutrition/article/mild-dehydration-impairs-cognitive-performance-and-mood-of-men/3388AB36B8DF73E844C9AD19271A75BF" },
        };

        class Hit
        {
            public RectangleF Rect;
            public Action Click;
            public string Id;
        }

        readonly Stats stats;
        readonly WaterLog water;
        readonly Settings settings;
        readonly IHealthHost host;
        readonly System.Windows.Forms.Timer refresh;
        readonly List<LinkLabel> links = new List<LinkLabel>();
        readonly List<Hit> hits = new List<Hit>();
        readonly StringFormat centerFmt = new StringFormat { Alignment = StringAlignment.Center };
        readonly StringFormat rightFmt = new StringFormat { Alignment = StringAlignment.Far };
        readonly StringFormat middleFmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        HealthTab tab;
        string hoverId;
        float s = 1f;
        float footerY = -1;
        Font fTitle, fSub, fHeading, fTileLabel, fTileValue, fSmall, fBody, fBodyBold;

        public StatsForm(Stats stats, WaterLog water, Settings settings, IHealthHost host, HealthTab tab)
        {
            this.stats = stats;
            this.water = water;
            this.settings = settings;
            this.host = host;
            this.tab = tab;

            Text = "Blink Break";
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
            float height = Math.Max(Measure(HealthTab.Eyes), Measure(HealthTab.Water));
            if (height > work.Height * 0.95f)
            {
                s *= work.Height * 0.95f / height;
                CreateFonts();
            }
            ClientSize = new Size((int)Math.Ceiling(S(LogicalWidth)), (int)Math.Ceiling(Measure(tab)));
            BuildLinks();

            refresh = new System.Windows.Forms.Timer();
            refresh.Interval = 1000;
            refresh.Tick += delegate { Invalidate(); };
            refresh.Start();
        }

        public void ShowTab(HealthTab newTab)
        {
            if (tab == newTab) return;
            tab = newTab;
            BuildLinks();
            Invalidate();
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

        float Measure(HealthTab which)
        {
            var saved = tab;
            tab = which;
            try
            {
                using (var g = CreateGraphics())
                {
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    return Render(g, false);
                }
            }
            finally { tab = saved; }
        }

        void BuildLinks()
        {
            SuspendLayout();
            foreach (var old in links) { Controls.Remove(old); old.Dispose(); }
            links.Clear();
            foreach (var src in tab == HealthTab.Eyes ? EyeSources : WaterSources)
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
                link.Visible = false;
                link.LinkClicked += delegate
                {
                    try { Process.Start(url); } catch (Exception ex) { Log.Write("Could not open link: " + ex.Message); }
                };
                links.Add(link);
                Controls.Add(link);
            }
            ResumeLayout();
            footerY = -1;      // placed after the next paint
        }

        void PlaceLinks()
        {
            float x = S(Pad);
            using (var g = CreateGraphics()) x += g.MeasureString("Sources:", fSmall).Width + S(4);
            foreach (var link in links)
            {
                link.Location = new Point((int)x, (int)footerY);
                link.Visible = true;
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
            else if (e.KeyCode == Keys.Tab && e.Control) ShowTab(tab == HealthTab.Eyes ? HealthTab.Water : HealthTab.Eyes);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Hit h = HitAt(e.Location);
            string id = h == null ? null : h.Id;
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
            if (id != hoverId) { hoverId = id; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverId != null) { hoverId = null; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            Hit h = HitAt(e.Location);
            if (h != null) { h.Click(); Invalidate(); }
        }

        Hit HitAt(Point p)
        {
            foreach (var h in hits) if (h.Click != null && h.Rect.Contains(p)) return h;
            return null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            hits.Clear();
            float oldFooter = footerY;
            float height = Render(g, true);

            // Each tab (and changing text) can need a different height: grow/shrink to fit.
            if (Math.Abs(height - ClientSize.Height) > 2) ClientSize = new Size(ClientSize.Width, (int)Math.Ceiling(height));
            if (Math.Abs(oldFooter - footerY) > 0.5f) PlaceLinks();
        }

        // ------------------------------------------------------------------ layout

        // Lays out (and optionally draws) the current tab; returns the content height.
        float Render(Graphics g, bool draw)
        {
            float x0 = S(Pad), cw = S(LogicalWidth) - 2 * S(Pad);
            float y = S(26);

            // header
            if (draw)
            {
                if (tab == HealthTab.Eyes)
                    EyeArt.Draw(g, x0 + S(22), y + S(20), S(44), 1f, false, Color.FromArgb(225, 232, 245));
                else
                    DropArt.Draw(g, x0 + S(22), y + S(20), S(44), Math.Max(0.12f, water.TodayMl / (float)settings.WaterGoalMl));

                DrawText(g, tab == HealthTab.Eyes ? "Your eye health" : "Your hydration", fTitle, Ink, x0 + S(60), y - S(4));
                string sub = tab == HealthTab.Eyes
                    ? host.BreakStatus()
                    : WaterLog.Litres(water.TodayMl) + " of " + WaterLog.Litres(settings.WaterGoalMl) + " today";
                DrawText(g, DateTime.Today.ToString("dddd d MMMM") + "   ·   " + sub, fSub, Muted, x0 + S(62), y + S(28));

                // tabs
                float pillW = S(96), pillH = S(34), pillY = y + S(2);
                float px = x0 + cw - 2 * pillW - S(4);
                Draw.FillRound(g, Card, new RectangleF(px - S(4), pillY - S(4), 2 * pillW + S(8), pillH + S(8)), S(12));
                Pill(g, new RectangleF(px, pillY, pillW, pillH), "Eyes", HealthTab.Eyes);
                Pill(g, new RectangleF(px + pillW, pillY, pillW, pillH), "Water", HealthTab.Water);
            }
            y += S(72);

            y = tab == HealthTab.Eyes ? RenderEyes(g, draw, x0, y, cw) : RenderWater(g, draw, x0, y, cw);

            // footer
            footerY = y;
            if (draw) DrawText(g, "Sources:", fSmall, Muted, x0, y);
            y += S(24);
            if (draw)
            {
                string disclaimer = tab == HealthTab.Eyes
                    ? "Not medical advice. See an eye-care professional if your eyes stay sore, dry or blurry."
                    : "Not medical advice. If a doctor has limited your fluids (e.g. heart or kidney problems), follow their advice.";
                string hint = tab == HealthTab.Eyes
                    ? "Open this anytime: " + TrayApp.StatsHotkeyText
                    : "Log a glass anytime: " + TrayApp.WaterHotkeyText;
                float hintW = g.MeasureString(hint, fSmall).Width;
                using (var b = new SolidBrush(Muted))
                using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(disclaimer, fSmall, b, new RectangleF(x0, y, cw - hintW - S(16), S(20)), fmt);
                using (var b = new SolidBrush(Muted))
                    g.DrawString(hint, fSmall, b, new RectangleF(x0, y, cw, S(20)), rightFmt);
            }
            y += S(20) + S(22);
            return y;
        }

        void Pill(Graphics g, RectangleF r, string text, HealthTab target)
        {
            bool selected = tab == target;
            string id = "tab-" + text;
            bool hot = hoverId == id;
            if (selected) Draw.FillRound(g, target == HealthTab.Water ? WaterDim : Faint, r, S(9));
            else if (hot) Draw.FillRound(g, CardHover, r, S(9));
            using (var b = new SolidBrush(selected || hot ? Ink : Muted))
                g.DrawString(text, fBodyBold, b, r, middleFmt);
            if (!selected) hits.Add(new Hit { Rect = r, Id = id, Click = () => ShowTab(target) });
        }

        float RenderEyes(Graphics g, bool draw, float x0, float y, float cw)
        {
            DayStats today = stats.Today;
            int interval = settings.IntervalMinutes;
            int recommended = (int)(today.ScreenSeconds / (interval * 60.0));

            // today tiles
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

            float colTop = y, rightW = S(372), colGap = S(32);
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
            if (draw) Bar(g, lx, ly, leftW, S(10), frac, barColor);
            ly += S(20);
            string explain = recommended > 0
                ? today.Completed + " of " + recommended + " breaks during " + Dur(today.ScreenSeconds) +
                  " of active screen time (one every " + interval + " min)."
                : "Coverage starts after " + interval + " min of screen time today (" + Dur(today.ScreenSeconds) + " so far).";
            ly += Para(g, draw, explain, fBody, Muted, lx, ly, leftW) + S(6);
            ly += Para(g, draw, EyeInsight(today, recommended, frac), fBody, Ink, lx, ly, leftW) + S(26);

            // 7-day chart
            if (draw)
            {
                DrawText(g, "Last 7 days", fHeading, Ink, lx, ly);
                float lgX = lx + leftW;
                lgX = Legend(g, lgX, ly, "Skipped", SkipColor);
                Legend(g, lgX - S(16), ly, "Taken", Accent);
            }
            ly += S(34);
            float chartH = S(120), labelH = S(18);
            if (draw) DrawEyeChart(g, lx, ly, leftW, chartH, labelH);
            ly += chartH + labelH + S(16);

            int streak = stats.Streak();
            string totals = "All time: " + stats.TotalCompleted + (stats.TotalCompleted == 1 ? " break" : " breaks") +
                            "  ·  " + Dur(stats.TotalRestSeconds) + " resting  ·  ≈ " + stats.TotalBlinks.ToString("N0") +
                            " full blinks  ·  " + streak + "-day streak";
            ly += Para(g, draw, totals, fBody, Muted, lx, ly, leftW);

            return FactsCard(g, draw, "How breaks help your eyes", EyeFacts, Accent, rx, colTop, rightW, ly - colTop) + S(22);
        }

        float RenderWater(Graphics g, bool draw, float x0, float y, float cw)
        {
            int goal = settings.WaterGoalMl, glass = settings.GlassMl;
            int todayMl = water.TodayMl, count = water.TodayCount;
            float frac = Math.Min(1f, todayMl / (float)goal);
            bool reached = todayMl >= goal;
            DateTime? last = water.LastDrink;
            int streak = water.GoalStreak(goal);

            // today tiles
            float gap = S(14), tileW = (cw - 3 * gap) / 4f, tileH = S(108);
            if (draw)
            {
                Tile(g, x0, y, tileW, tileH, "Water today", WaterLog.Litres(todayMl),
                     "of " + WaterLog.Litres(goal) + " goal", reached ? Good : Ink);
                Tile(g, x0 + (tileW + gap), y, tileW, tileH, "Drinks", count.ToString(),
                     count == 1 ? "logged today" : "logged today", Ink);
                bool stale = last.HasValue && !reached && (DateTime.Now - last.Value).TotalHours >= 2 && DateTime.Now.Hour < 22;
                Tile(g, x0 + 2 * (tileW + gap), y, tileW, tileH, "Last drink", last.HasValue ? Ago(last.Value) : "–",
                     last.HasValue ? (last.Value.Date == DateTime.Today ? "at " + last.Value.ToString("t") : last.Value.ToString("ddd t"))
                                   : "nothing logged yet", stale ? Warn : Ink);
                Tile(g, x0 + 3 * (tileW + gap), y, tileW, tileH, "Goal streak", streak + (streak == 1 ? " day" : " days"),
                     "goal reached in a row", streak > 0 ? Good : Ink);
            }
            y += tileH + S(28);

            float colTop = y, rightW = S(372), colGap = S(32);
            float leftW = cw - rightW - colGap;
            float lx = x0, rx = x0 + leftW + colGap;

            // progress + quick add
            float ly = colTop;
            if (draw)
            {
                DrawText(g, "Today's progress", fHeading, Ink, lx, ly);
                using (var b = new SolidBrush(reached ? Good : Water))
                    g.DrawString((int)Math.Round(frac * 100) + "%", fHeading, b, new RectangleF(lx, ly, leftW, S(24)), rightFmt);
            }
            ly += S(30);
            if (draw) Bar(g, lx, ly, leftW, S(12), frac, reached ? Good : Water);
            ly += S(22);

            string remaining;
            if (reached) remaining = "Goal reached. Keep sipping if it's hot or you're active.";
            else
            {
                int left = goal - todayMl;
                int glasses = (int)Math.Ceiling(left / (double)glass);
                remaining = WaterLog.Litres(left) + " to go: about " + glasses + (glasses == 1 ? " more glass" : " more glasses") +
                            " of " + glass + " ml.";
            }
            ly += Para(g, draw, remaining, fBody, Muted, lx, ly, leftW) + S(6);
            ly += Para(g, draw, WaterInsight(todayMl, goal), fBody, Ink, lx, ly, leftW) + S(16);

            // quick-add buttons
            float bh = S(38), bgap = S(10);
            float undoW = S(74);
            float bw = (leftW - undoW - 3 * bgap) / 3f;
            if (draw)
            {
                float bx = lx;
                Button(g, new RectangleF(bx, ly, bw, bh), "+ " + glass + " ml glass", true, true, "add-glass",
                       () => host.LogWater(glass, false));
                bx += bw + bgap;
                Button(g, new RectangleF(bx, ly, bw, bh), "+ 500 ml bottle", false, true, "add-bottle",
                       () => host.LogWater(500, false));
                bx += bw + bgap;
                Button(g, new RectangleF(bx, ly, bw, bh), "+ 100 ml sip", false, true, "add-sip",
                       () => host.LogWater(100, false));
                bx += bw + bgap;
                Button(g, new RectangleF(bx, ly, undoW, bh), "Undo", false, water.CanUndo, "undo",
                       () => host.UndoWater());
            }
            ly += bh + S(10);
            ly += Para(g, draw, host.WaterStatus(), fSmall, Muted, lx, ly, leftW) + S(22);

            // 7-day chart
            if (draw)
            {
                DrawText(g, "Last 7 days", fHeading, Ink, lx, ly);
                string goalLabel = "Goal " + WaterLog.Litres(goal);
                float gw = g.MeasureString(goalLabel, fSmall).Width;
                float gx = lx + leftW - gw;
                DrawText(g, goalLabel, fSmall, Muted, gx, ly + S(2));
                using (var pen = new Pen(Muted, Math.Max(1f, S(1.5f))) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, gx - S(26), ly + S(11), gx - S(6), ly + S(11));
            }
            ly += S(34);
            float chartH = S(120), labelH = S(18);
            if (draw) DrawWaterChart(g, lx, ly, leftW, chartH, labelH);
            ly += chartH + labelH + S(16);

            int days = 0, sum = 0, hit = 0;
            for (int i = 0; i < 7; i++)
            {
                int ml = water.MlOn(DateTime.Today.AddDays(-i));
                if (ml > 0) { days++; sum += ml; }
                if (ml >= goal) hit++;
            }
            string totals = days == 0
                ? "Log your first drink to start your history."
                : "Average on days you logged: " + WaterLog.Litres((int)Math.Round(sum / (double)days / 10) * 10) +
                  "  ·  goal reached on " + hit + " of the last 7 days";
            ly += Para(g, draw, totals, fBody, Muted, lx, ly, leftW);

            return FactsCard(g, draw, "Why water matters", WaterFacts, Water, rx, colTop, rightW, ly - colTop) + S(22);
        }

        // Right-hand card; returns the y below both columns.
        float FactsCard(Graphics g, bool draw, string heading, string[][] facts, Color dot,
                        float rx, float top, float width, float leftHeight)
        {
            float h = Math.Max(Facts(g, false, heading, facts, dot, rx, top, width), leftHeight);
            if (draw)
            {
                Draw.FillRound(g, Card, new RectangleF(rx, top, width, h), S(14));
                Facts(g, true, heading, facts, dot, rx, top, width);
            }
            return top + h;
        }

        float Facts(Graphics g, bool draw, string heading, string[][] facts, Color dot, float x, float top, float width)
        {
            float pad = S(22);
            float y = top + pad;
            x += pad;
            width -= 2 * pad;
            if (draw) DrawText(g, heading, fHeading, Ink, x, y);
            y += S(34);
            foreach (var fact in facts)
            {
                if (draw)
                {
                    using (var b = new SolidBrush(dot)) g.FillEllipse(b, x, y + S(6), S(7), S(7));
                    DrawText(g, fact[0], fBodyBold, Ink, x + S(16), y - S(1));
                }
                y += S(21);
                y += Para(g, draw, fact[1], fBody, Muted, x + S(16), y, width - S(16)) + S(14);
            }
            return y - top + S(4);
        }

        // ------------------------------------------------------------------ charts

        void DrawEyeChart(Graphics g, float x, float y, float w, float h, float labelH)
        {
            const int Days = 7;
            int max = 1;
            for (int i = 0; i < Days; i++)
            {
                var d = stats.Peek(DateTime.Today.AddDays(i - (Days - 1)));
                max = Math.Max(max, d.Completed + d.Skipped);
            }

            float colW = w / Days, barW = Math.Min(S(30), colW * 0.5f), valueH = S(18), baseY = y + h;
            Axis(g, x, w, baseY);
            for (int i = 0; i < Days; i++)
            {
                DateTime day = DateTime.Today.AddDays(i - (Days - 1));
                var d = stats.Peek(day);
                int total = d.Completed + d.Skipped;
                float cx = x + colW * i + colW / 2f;
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
                DayLabel(g, day, i == Days - 1, cx, colW, baseY, labelH);
            }
        }

        void DrawWaterChart(Graphics g, float x, float y, float w, float h, float labelH)
        {
            const int Days = 7;
            int goal = settings.WaterGoalMl;
            float max = goal * 1.2f;
            for (int i = 0; i < Days; i++) max = Math.Max(max, water.MlOn(DateTime.Today.AddDays(i - (Days - 1))));

            float colW = w / Days, barW = Math.Min(S(30), colW * 0.5f), valueH = S(18), baseY = y + h;
            float usable = h - valueH;
            Axis(g, x, w, baseY);
            for (int i = 0; i < Days; i++)
            {
                DateTime day = DateTime.Today.AddDays(i - (Days - 1));
                int ml = water.MlOn(day);
                float cx = x + colW * i + colW / 2f;
                if (ml > 0)
                {
                    float bh = usable * ml / max;
                    Draw.FillRound(g, ml >= goal ? Water : WaterDim, new RectangleF(cx - barW / 2, baseY - bh, barW, bh), S(5));
                    using (var b = new SolidBrush(Ink))
                        g.DrawString((ml / 1000.0).ToString("0.0"), fSmall, b, new RectangleF(cx - colW / 2, baseY - bh - valueH, colW, valueH), centerFmt);
                }
                DayLabel(g, day, i == Days - 1, cx, colW, baseY, labelH);
            }

            float goalY = baseY - usable * goal / max;
            using (var pen = new Pen(Color.FromArgb(150, Muted), Math.Max(1f, S(1.5f))) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, x, goalY, x + w, goalY);
        }

        void Axis(Graphics g, float x, float w, float baseY)
        {
            using (var axis = new Pen(Faint, Math.Max(1f, S(1))))
                g.DrawLine(axis, x, baseY + S(0.5f), x + w, baseY + S(0.5f));
        }

        void DayLabel(Graphics g, DateTime day, bool isToday, float cx, float colW, float baseY, float labelH)
        {
            using (var b = new SolidBrush(isToday ? Ink : Muted))
                g.DrawString(isToday ? "Today" : day.ToString("ddd"), fSmall, b,
                             new RectangleF(cx - colW / 2, baseY + S(4), colW, labelH), centerFmt);
        }

        float Legend(Graphics g, float right, float y, string text, Color color)
        {
            float x = right - g.MeasureString(text, fSmall).Width;
            DrawText(g, text, fSmall, Muted, x, y + S(2));
            x -= S(14);
            Draw.FillRound(g, color, new RectangleF(x, y + S(6), S(9), S(9)), S(2));
            return x;
        }

        // ------------------------------------------------------------------ text

        string EyeInsight(DayStats d, int recommended, float frac)
        {
            if (recommended == 0)
                return d.Completed > 0
                    ? "Good start. Keep it up as your screen time adds up."
                    : "No breaks yet today. Your first one comes after " + settings.IntervalMinutes + " minutes at the screen.";
            if (frac >= 0.8f) return "Great consistency. Regular breaks are exactly what the research rewards.";
            if (frac >= 0.5f) return "Decent. Skipping or pausing fewer breaks during long sessions will help.";
            return "Your eyes went long stretches without rest today. Try to let the breaks run to the end.";
        }

        // Paces the goal evenly from 08:00 to 22:00.
        static string WaterInsight(int todayMl, int goal)
        {
            if (todayMl >= goal) return "Nice work: today's goal is done.";
            double dayFrac = (DateTime.Now.TimeOfDay.TotalHours - 8) / 14.0;
            if (dayFrac <= 0) return "Starting the day with a glass of water is an easy win.";
            int expected = (int)(goal * Math.Min(1.0, dayFrac));
            if (todayMl >= expected * 0.85) return "You're on track for this time of day.";
            int rounded = Math.Max(100, (int)Math.Round(expected / 50.0) * 50);
            return "By now you'd ideally be at about " + WaterLog.Litres(rounded) + ". A glass now helps you catch up.";
        }

        // ------------------------------------------------------------------ widgets

        void Tile(Graphics g, float x, float y, float w, float h, string label, string value, string sub, Color valueColor)
        {
            Draw.FillRound(g, Card, new RectangleF(x, y, w, h), S(14));
            DrawText(g, label, fTileLabel, Muted, x + S(18), y + S(14));
            DrawText(g, value, fTileValue, valueColor, x + S(15), y + S(32));
            using (var b = new SolidBrush(Muted))
            using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(sub, fSmall, b, new RectangleF(x + S(18), y + S(78), w - S(26), S(20)), fmt);
        }

        void Bar(Graphics g, float x, float y, float w, float h, float frac, Color color)
        {
            Draw.FillRound(g, Faint, new RectangleF(x, y, w, h), h / 2);
            if (frac > 0) Draw.FillRound(g, color, new RectangleF(x, y, Math.Max(h, w * frac), h), h / 2);
        }

        void Button(Graphics g, RectangleF r, string text, bool primary, bool enabled, string id, Action click)
        {
            bool hot = enabled && hoverId == id;
            Color fill = primary ? (hot ? WaterHover : Water) : (hot ? CardHover : Card);
            if (!enabled) fill = Card;
            Draw.FillRound(g, fill, r, S(9));
            if (!primary)
                using (var path = Draw.RoundRect(r, S(9)))
                using (var pen = new Pen(Faint, Math.Max(1f, S(1))))
                    g.DrawPath(pen, path);
            Color textColor = !enabled ? Faint : (primary ? Color.FromArgb(8, 20, 36) : Ink);
            using (var b = new SolidBrush(textColor))
                g.DrawString(text, fBodyBold, b, r, middleFmt);
            if (enabled) hits.Add(new Hit { Rect = r, Id = id, Click = click });
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

        static string Ago(DateTime time)
        {
            double min = (DateTime.Now - time).TotalMinutes;
            if (min < 1) return "just now";
            if (min < 60) return (int)min + " min ago";
            if (min < 60 * 24) return (int)(min / 60) + "h " + (int)(min % 60) + "m ago";
            return (int)(min / (60 * 24)) + "d ago";
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
                middleFmt.Dispose();
                DisposeFonts();
            }
        }
    }
}
