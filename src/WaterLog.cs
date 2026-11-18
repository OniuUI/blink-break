using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BlinkBreak
{
    class Drink
    {
        public DateTime Time;
        public int Ml;
    }

    // Every drink you log, in %APPDATA%\BlinkBreak\water.csv (time, millilitres).
    class WaterLog
    {
        const string Header = "time,ml";
        const string TimeFormat = "yyyy-MM-ddTHH:mm:ss";

        readonly List<Drink> drinks = new List<Drink>();

        static string FilePath { get { return Path.Combine(Program.DataDir, "water.csv"); } }

        public static WaterLog Load()
        {
            var log = new WaterLog();
            try
            {
                if (!File.Exists(FilePath)) return log;
                var inv = CultureInfo.InvariantCulture;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] f = line.Split(',');
                    DateTime time;
                    int ml;
                    if (f.Length < 2) continue;
                    if (!DateTime.TryParseExact(f[0], TimeFormat, inv, DateTimeStyles.None, out time)) continue;
                    if (!int.TryParse(f[1], NumberStyles.Integer, inv, out ml) || ml <= 0) continue;
                    log.drinks.Add(new Drink { Time = time, Ml = ml });
                }
                log.drinks.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
            catch (Exception ex) { Log.Write("Could not read water log: " + ex.Message); }
            return log;
        }

        static string Line(Drink d)
        {
            return d.Time.ToString(TimeFormat, CultureInfo.InvariantCulture) + "," + d.Ml.ToString(CultureInfo.InvariantCulture);
        }

        public Drink Add(int ml)
        {
            var drink = new Drink { Time = DateTime.Now, Ml = ml };
            drinks.Add(drink);
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Header + Environment.NewLine);
                File.AppendAllText(FilePath, Line(drink) + Environment.NewLine);
            }
            catch (Exception ex) { Log.Write("Could not save drink: " + ex.Message); }
            return drink;
        }

        // Removes the most recent drink if it was logged today.
        public Drink UndoLast()
        {
            if (drinks.Count == 0) return null;
            var last = drinks[drinks.Count - 1];
            if (last.Time.Date != DateTime.Today) return null;
            drinks.RemoveAt(drinks.Count - 1);
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(Header);
                foreach (var d in drinks) sb.AppendLine(Line(d));
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("Could not save water log: " + ex.Message); }
            return last;
        }

        public int MlOn(DateTime day)
        {
            int ml = 0;
            for (int i = drinks.Count - 1; i >= 0; i--)
            {
                DateTime d = drinks[i].Time.Date;
                if (d == day.Date) ml += drinks[i].Ml;
                else if (d < day.Date) break;
            }
            return ml;
        }

        public int CountOn(DateTime day)
        {
            int n = 0;
            for (int i = drinks.Count - 1; i >= 0; i--)
            {
                DateTime d = drinks[i].Time.Date;
                if (d == day.Date) n++;
                else if (d < day.Date) break;
            }
            return n;
        }

        public int TodayMl { get { return MlOn(DateTime.Today); } }
        public int TodayCount { get { return CountOn(DateTime.Today); } }
        public bool CanUndo { get { return drinks.Count > 0 && drinks[drinks.Count - 1].Time.Date == DateTime.Today; } }

        public DateTime? LastDrink
        {
            get { return drinks.Count == 0 ? (DateTime?)null : drinks[drinks.Count - 1].Time; }
        }

        // Consecutive days reaching the goal (today counts once it's reached).
        public int GoalStreak(int goalMl)
        {
            DateTime day = DateTime.Today;
            if (MlOn(day) < goalMl) day = day.AddDays(-1);
            int streak = 0;
            while (MlOn(day) >= goalMl) { streak++; day = day.AddDays(-1); }
            return streak;
        }

        public static string Litres(int ml)
        {
            return (ml / 1000.0).ToString(ml % 100 == 0 ? "0.0" : "0.0#") + " L";
        }
    }
}
