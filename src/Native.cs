using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BlinkBreak
{
    static class Native
    {
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

        public static void EnableDpiAwareness()
        {
            try { SetProcessDpiAwareness(2); }            // per-monitor: crisp on every screen
            catch { try { SetProcessDPIAware(); } catch { } }
        }

        static int NotificationState()
        {
            try
            {
                int state;
                return SHQueryUserNotificationState(out state) == 0 ? state : 0;
            }
            catch { return 0; }
        }

        // QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4
        public static bool IsFullscreenExclusive()
        {
            int s = NotificationState();
            return s == 3 || s == 4;
        }

        // Any full-screen app, including a full-screen video in a browser (QUNS_BUSY = 2).
        public static bool IsFullscreenAnything()
        {
            int s = NotificationState();
            return s == 2 || s == 3 || s == 4;
        }

        public static double IdleSeconds()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);
            if (!GetLastInputInfo(ref info)) return 0;
            return unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
        }

        public static string ForegroundProcessName()
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                using (var p = Process.GetProcessById((int)pid)) return p.ProcessName;
            }
            catch { return "?"; }
        }

        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)   // DWMWA_USE_IMMERSIVE_DARK_MODE
                    DwmSetWindowAttribute(hwnd, 19, ref on, 4);       // pre-20H1 builds
            }
            catch { }
        }
    }

    // Hidden message-only window: receives global hotkeys and "show stats" requests
    // from a second copy of the program.
    class MessageWindow : NativeWindow, IDisposable
    {
        const string Caption = "BlinkBreak.MessageWindow";
        const int WM_HOTKEY = 0x0312;
        const int WM_SHOW_STATS = 0x8000 + 1;          // WM_APP + 1
        const int WM_BREAK_NOW = 0x8000 + 2;
        const int WM_SHOW_WATER = 0x8000 + 3;
        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        readonly HashSet<int> registered = new HashSet<int>();
        public event Action<int> HotkeyPressed;
        public event Action ShowStatsRequested;
        public event Action BreakNowRequested;
        public event Action ShowWaterRequested;

        public MessageWindow()
        {
            var cp = new CreateParams();
            cp.Caption = Caption;
            cp.Parent = HWND_MESSAGE;
            CreateHandle(cp);
        }

        public bool Register(int id, uint modifiers, Keys key)
        {
            if (registered.Contains(id)) return true;
            bool ok = Native.RegisterHotKey(Handle, id, modifiers | Native.MOD_NOREPEAT, (uint)key);
            if (ok) registered.Add(id);
            return ok;
        }

        public void Unregister(int id)
        {
            if (registered.Remove(id)) Native.UnregisterHotKey(Handle, id);
        }

        public static bool SignalRunningInstance(bool breakNow, bool showWater)
        {
            IntPtr h = Native.FindWindowEx(HWND_MESSAGE, IntPtr.Zero, null, Caption);
            if (h == IntPtr.Zero) return false;
            Native.AllowSetForegroundWindow(-1);      // let the running copy bring its window to the front
            int msg = breakNow ? WM_BREAK_NOW : (showWater ? WM_SHOW_WATER : WM_SHOW_STATS);
            return Native.PostMessage(h, msg, IntPtr.Zero, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                if (HotkeyPressed != null) HotkeyPressed(m.WParam.ToInt32());
                return;
            }
            if (m.Msg == WM_SHOW_WATER)
            {
                if (ShowWaterRequested != null) ShowWaterRequested();
                return;
            }
            if (m.Msg == WM_BREAK_NOW)
            {
                if (BreakNowRequested != null) BreakNowRequested();
                return;
            }
            if (m.Msg == WM_SHOW_STATS)
            {
                if (ShowStatsRequested != null) ShowStatsRequested();
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (int id in new List<int>(registered)) Unregister(id);
            DestroyHandle();
        }
    }
}
