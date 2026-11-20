using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SysTelemetry.Interop
{
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int GWLP_HWNDPARENT = -8;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;
        public const uint GW_HWNDPREV = 3;
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;

            public ulong ToUInt64()
            {
                return ((ulong)dwHighDateTime << 32) | dwLowDateTime;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        public const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static long GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex).ToInt64() : GetWindowLongPtr32(hWnd, nIndex).ToInt64();
        }

        public static void SetWindowLongPtr(IntPtr hWnd, int nIndex, long value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, nIndex, new IntPtr(value));
            else SetWindowLongPtr32(hWnd, nIndex, new IntPtr((int)value));
        }

        public static void SetWindowOwner(IntPtr hwnd, IntPtr ownerHwnd)
        {
            if (hwnd == IntPtr.Zero || ownerHwnd == IntPtr.Zero) return;
            SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, ownerHwnd.ToInt64());
        }

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        public delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        public static void ApplyToolWindowStyle(IntPtr hwnd, bool clickThrough)
        {
            if (hwnd == IntPtr.Zero) return;
            long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (clickThrough) style |= WS_EX_TRANSPARENT;
            else style &= ~((long)WS_EX_TRANSPARENT);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, style);
        }

        public static void EnsureTopmostNoActivate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public static void EnsureAboveTaskbarNoActivate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private static readonly string[] ShellProcessNames = new string[]
        {
            "explorer",
            "shellexperiencehost",
            "shellhost",
            "startmenuexperiencehost",
            "searchhost",
            "textinputhost",
            "systemsettings",
            "lockapp",
            "applicationframehost"
        };

        private static bool IsSystemShellClass(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            if (className == "Progman" ||
                className == "WorkerW" ||
                className == "Shell_TrayWnd" ||
                className == "Shell_SecondaryTrayWnd" ||
                className == "ControlCenterWindow" ||
                className == "Windows.UI.Core.CoreWindow" ||
                className == "Xaml_WindowedPopupClass" ||
                className == "XamlWindowedPopupClass" ||
                className == "Shell_LightDismissContent" ||
                className == "Shell_CharmWindow" ||
                className == "NotifyIconOverflowWindow" ||
                className == "TopLevelWindowForOverflow" ||
                className == "TaskListThumbnailWnd" ||
                className == "TaskListOverlayWnd" ||
                className == "MSTaskListWClass" ||
                className == "DV2ControlHost" ||
                className == "Button")
            {
                return true;
            }

            if (className.IndexOf("LightDismiss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("Overflow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("ControlCenter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("Popup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static bool IsSystemShellProcess(uint pid)
        {
            if (pid == 0) return true;
            try
            {
                using (var proc = Process.GetProcessById((int)pid))
                {
                    string name = proc.ProcessName;
                    for (int i = 0; i < ShellProcessNames.Length; i++)
                    {
                        if (name.Equals(ShellProcessNames[i], StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        public static bool IsFullscreenAppActive(IntPtr ownTaskbarHwnd, IntPtr ownOverlayHwnd, IntPtr ownPopoverHwnd)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == ownTaskbarHwnd || fg == ownOverlayHwnd || fg == ownPopoverHwnd)
                return false;

            var cls = new StringBuilder(128);
            GetClassName(fg, cls, cls.Capacity);
            string className = cls.ToString();
            if (IsSystemShellClass(className))
                return false;

            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            if (pid == (uint)Process.GetCurrentProcess().Id || IsSystemShellProcess(pid))
                return false;

            RECT r;
            if (!GetWindowRect(fg, out r)) return false;
            IntPtr mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(mon, ref mi)) return false;

            return r.Left <= mi.rcMonitor.Left &&
                   r.Top <= mi.rcMonitor.Top &&
                   r.Right >= mi.rcMonitor.Right &&
                   r.Bottom >= mi.rcMonitor.Bottom;
        }
    }
}
