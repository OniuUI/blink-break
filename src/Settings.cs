using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BlinkBreak
{
    class Settings
    {
        public int IntervalMinutes = 20;
        public int BreakSeconds = 20;
        public bool FirstRun;

        static string FilePath { get { return Path.Combine(Program.DataDir, "settings.ini"); } }

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
                Directory.CreateDirectory(Program.DataDir);
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
}
