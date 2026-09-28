using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipboardTrail
{
    internal static class NativeMethods
    {
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        public static void SetCueBanner(IntPtr hwnd, string text) { if (hwnd != IntPtr.Zero) SendMessage(hwnd, 0x1501, new IntPtr(1), text ?? ""); }

        public static void SuspendRedraw(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero) SendMessage(hwnd, 0x000B, IntPtr.Zero, IntPtr.Zero); // WM_SETREDRAW
        }

        public static void ResumeRedraw(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero) SendMessage(hwnd, 0x000B, new IntPtr(1), IntPtr.Zero); // WM_SETREDRAW
        }

        public static int GetFirstVisibleLine(IntPtr hwnd)
        {
            return hwnd == IntPtr.Zero ? 0 : SendMessage(hwnd, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32(); // EM_GETFIRSTVISIBLELINE
        }

        public static void RestoreFirstVisibleLine(IntPtr hwnd, int line)
        {
            if (hwnd == IntPtr.Zero) return;
            int current = GetFirstVisibleLine(hwnd);
            if (current != line) SendMessage(hwnd, 0x00B6, IntPtr.Zero, new IntPtr(line - current)); // EM_LINESCROLL
        }

        public static SourceInfo GetForegroundSource()
        {
            SourceInfo result = new SourceInfo { ProcessName = "未知", WindowTitle = "" };
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid != 0)
                {
                    using (Process p = Process.GetProcessById((int)pid))
                        result.ProcessName = p.ProcessName;
                }

                StringBuilder title = new StringBuilder(512);
                GetWindowText(hwnd, title, title.Capacity);
                result.WindowTitle = title.ToString();
            }
            catch { }
            return result;
        }

        public static void SendPasteKeystroke()
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static IntPtr GetForegroundWindowHandle() { return GetForegroundWindow(); }
        public static void RestoreForegroundWindow(IntPtr hwnd) { if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd); }
    }

    internal sealed class SourceInfo
    {
        public string ProcessName { get; set; }
        public string WindowTitle { get; set; }
    }
}
