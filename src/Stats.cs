using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BlinkBreak
{
    class DayStats
    {
        public int Completed;          // breaks that ran to the end
        public int Skipped;            // breaks ended early with Esc / click
        public int Blinks;             // guided blinks shown while the overlay was up
        public double RestSeconds;     // time spent on the break screen
        public double ScreenSeconds;   // active time at the computer (unlocked, not idle)
        public double LongestStretch;  // longest active stretch without a real break
    }

    // Per-day history in %APPDATA%\BlinkBreak\stats.csv
    class Stats
    {
        const string Header = "date,completed,skipped,blinks,rest_seconds,screen_seconds,longest_stretch_seconds";

        readonly Dictionary<DateTime, DayStats> days = new Dictionary<DateTime, DayStats>();
        DateTime lastSave = DateTime.Now;
        bool dirty;

        static string FilePath { get { return Path.Combine(Program.DataDir, "stats.csv"); } }

        public static Stats Load()
        {
            var stats = new Stats();
            try
            {
                if (!File.Exists(FilePath)) return stats;
                var inv = CultureInfo.InvariantCulture;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] f = line.Split(',');
                    DateTime date;
                    if (f.Length < 7 || !DateTime.TryParseExact(f[0], "yyyy-MM-dd", inv, DateTimeStyles.None, out date)) continue;
                    var d = new DayStats();
                    int.TryParse(f[1], NumberStyles.Integer, inv, out d.Completed);
                    int.TryParse(f[2], NumberStyles.Integer, inv, out d.Skipped);
                    int.TryParse(f[3], NumberStyles.Integer, inv, out d.Blinks);
                    double.TryParse(f[4], NumberStyles.Float, inv, out d.RestSeconds);
                    double.TryParse(f[5], NumberStyles.Float, inv, out d.ScreenSeconds);
                    double.TryParse(f[6], NumberStyles.Float, inv, out d.LongestStretch);
                    stats.days[date.Date] = d;
                }
            }
            catch (Exception ex) { Log.Write("Could not read stats: " + ex.Message); }
            return stats;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                var inv = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                sb.AppendLine(Header);
                var keys = new List<DateTime>(days.Keys);
                keys.Sort();
                foreach (var date in keys)
                {
                    var d = days[date];
                    sb.Append(date.ToString("yyyy-MM-dd", inv)).Append(',')
                      .Append(d.Completed.ToString(inv)).Append(',')
                      .Append(d.Skipped.ToString(inv)).Append(',')
                      .Append(d.Blinks.ToString(inv)).Append(',')
                      .Append(d.RestSeconds.ToString("0.#", inv)).Append(',')
                      .Append(d.ScreenSeconds.ToString("0", inv)).Append(',')
                      .Append(d.LongestStretch.ToString("0", inv)).AppendLine();
                }
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                dirty = false;
                lastSave = DateTime.Now;
            }
            catch (Exception ex) { Log.Write("Could not save stats: " + ex.Message); }
        }

        public void SaveIfDirty() { if (dirty) Save(); }

        // Returns the stored day (creating it) - use for writes.
        DayStats Day(DateTime date)
        {
            DayStats d;
            if (!days.TryGetValue(date.Date, out d)) { d = new DayStats(); days[date.Date] = d; }
            return d;
        }

        // Read-only view; never creates entries.
        public DayStats Peek(DateTime date)
        {
            DayStats d;
            return days.TryGetValue(date.Date, out d) ? d : new DayStats();
        }

        public DayStats Today { get { return Peek(DateTime.Today); } }

        public void AddScreenTime(double seconds, double currentStretch)
        {
            var d = Day(DateTime.Today);
            d.ScreenSeconds += seconds;
            if (currentStretch > d.LongestStretch) d.LongestStretch = currentStretch;
            dirty = true;
            if ((DateTime.Now - lastSave).TotalSeconds >= 60) Save();
        }

        public void RecordBreak(bool completed, double restSeconds)
        {
            var d = Day(DateTime.Today);
            if (completed) d.Completed++; else d.Skipped++;
            d.RestSeconds += restSeconds;
            d.Blinks += GuidedBlinks(restSeconds);
            dirty = true;
            Save();
        }

        // The break screen's eye blinks 0.8 s in, then every 3 s.
        public static int GuidedBlinks(double restSeconds)
        {
            return restSeconds < 0.8 ? 0 : (int)((restSeconds - 0.8) / 3.0) + 1;
        }

        public int TotalCompleted { get { int n = 0; foreach (var d in days.Values) n += d.Completed; return n; } }
        public int TotalBlinks { get { int n = 0; foreach (var d in days.Values) n += d.Blinks; return n; } }
        public double TotalRestSeconds { get { double n = 0; foreach (var d in days.Values) n += d.RestSeconds; return n; } }

        // Consecutive days (ending today, or yesterday if today has none yet) with at least one completed break.
        public int Streak()
        {
            DateTime day = DateTime.Today;
            if (Peek(day).Completed == 0) day = day.AddDays(-1);
            int streak = 0;
            while (Peek(day).Completed > 0) { streak++; day = day.AddDays(-1); }
            return streak;
        }
    }
}
