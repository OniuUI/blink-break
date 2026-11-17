// Blink Break - a tiny tray app that covers your screens with a short "blink break"
// every N minutes (default: the 20-20-20 rule - every 20 min, 20 s, look ~6 m away).
// Written in C# 5 so it compiles with the csc.exe that ships with Windows (.NET Framework 4.x).

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Blink Break")]
[assembly: System.Reflection.AssemblyProduct("Blink Break")]
[assembly: System.Reflection.AssemblyDescription("Reminds you to take regular blink breaks")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.0.0")]

namespace BlinkBreak
{
    static class Program
    {
        public const string Version = "1.1.0";

        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlinkBreak"); }
        }

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
                bool breakNow = Array.IndexOf(args, "--break-now") >= 0;
                if (!createdNew)
                {
                    // Already running: ask that copy to start a break, or to open its stats window.
                    if (!MessageWindow.SignalRunningInstance(breakNow))
                        MessageBox.Show("Blink Break is already running.\n\nLook for the eye icon in the system tray (bottom right, next to the clock).",
                            "Blink Break", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Log.Install();
                bool showStats = Array.IndexOf(args, "--stats") >= 0;
                Application.Run(new TrayApp(breakNow, showStats));
                GC.KeepAlive(mutex);
            }
        }
    }

    // Small diagnostic log in %APPDATA%\BlinkBreak\blinkbreak.log, plus a watchdog that
    // records where the UI thread is stuck if the app ever stops responding.
    static class Log
    {
        static readonly object gate = new object();
        static Thread uiThread;
        static volatile int lastBeat;

        static string FilePath { get { return Path.Combine(Program.DataDir, "blinkbreak.log"); } }

        public static void Write(string message)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Program.DataDir);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > 256 * 1024)
                    {
                        string old = FilePath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void Install()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Write("ERROR (UI thread): " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Write("FATAL: " + e.ExceptionObject);

            uiThread = Thread.CurrentThread;
            Heartbeat();
            var watchdog = new Thread(WatchdogLoop);
            watchdog.IsBackground = true;
            watchdog.Name = "BlinkBreak watchdog";
            watchdog.Start();
        }

        // Called by the UI thread once a second.
        public static void Heartbeat() { lastBeat = Environment.TickCount; }

        static void WatchdogLoop()
        {
            int strikes = 0;
            bool reported = false;
            while (true)
            {
                Thread.Sleep(2000);
                int gap = Environment.TickCount - lastBeat;
                if (gap > 6000) strikes++;
                else
                {
                    strikes = 0;
                    if (reported) { Write("UI thread responsive again"); reported = false; }
                }
                // Two strikes in a row so a wake-from-sleep jump isn't mistaken for a hang.
                if (strikes == 2 && !reported)
                {
                    reported = true;
                    Write("HANG: UI thread unresponsive for " + gap / 1000 + " s. Stack:" + Environment.NewLine + CaptureUiStack());
                }
            }
        }

#pragma warning disable 618
        static string CaptureUiStack()
        {
            try
            {
                uiThread.Suspend();
                try { return new StackTrace(uiThread, false).ToString(); }
                finally { uiThread.Resume(); }
            }
            catch (Exception ex) { return "(could not capture stack: " + ex.Message + ")"; }
        }
#pragma warning restore 618
    }
}
