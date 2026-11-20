using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using SysTelemetry.Interop;
using SysTelemetry.Models;
using SysTelemetry.Services;

namespace SysTelemetry.UI
{
    public sealed class TaskbarBarWindow : Window
    {
        private readonly AppConfig _config;
        private readonly ContentPresenter _host = new ContentPresenter();
        private bool _isHovered;
        private TelemetrySnapshot _lastSnap;
        private IntPtr _hwnd;
        private IntPtr _lastOwnerHwnd;
        private MenuItem _miDockToggle;

        public event Action BarClicked;
        public event Action OpenSettingsRequested;
        public event Action ToggleTaskbarDockRequested;
        public event Action ToggleDesktopOverlayRequested;
        public event Action ToggleDesktopLockRequested;
        public event Action TrimMemoryRequested;
        public event Action ExitRequested;

        public IntPtr Handle { get { return _hwnd; } }

        public TaskbarBarWindow(AppConfig config)
        {
            _config = config;
            AllowsTransparency = true;
            WindowStyle = WindowStyle.None;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Cursor = Cursors.Hand;
            Content = _host;

            SourceInitialized += OnSourceInitialized;
            SizeChanged += (s, e) => RepositionToTaskbar();
            MouseEnter += (s, e) => { _isHovered = true; Redraw(); };
            MouseLeave += (s, e) => { _isHovered = false; Redraw(); };
            MouseLeftButtonUp += (s, e) => { if (BarClicked != null) BarClicked(); };
            ContextMenu = BuildContextMenu();
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ApplyToolWindowStyle(_hwnd, false);
            var src = HwndSource.FromHwnd(_hwnd);
            if (src != null) src.AddHook(WndProc);

            IntPtr tbHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tbHwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowOwner(_hwnd, tbHwnd);
                _lastOwnerHwnd = tbHwnd;
            }

            RepositionToTaskbar();
            NativeMethods.EnsureAboveTaskbarNoActivate(_hwnd);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);
            }
            return IntPtr.Zero;
        }

        public void EnsureAboveTaskbar()
        {
            if (_hwnd != IntPtr.Zero && IsVisible)
                NativeMethods.EnsureAboveTaskbarNoActivate(_hwnd);
        }

        public void UpdateMetrics(TelemetrySnapshot snap)
        {
            _lastSnap = snap;
            Redraw();
            RepositionToTaskbar();
            EnsureAboveTaskbar();
        }

        public void Redraw()
        {
            if (_lastSnap == null) return;
            var pal = ThemeHelper.GetPalette(_config.Theme);
            _host.Content = WidgetVisualBuilder.BuildTaskbarVisual(_lastSnap, _config, pal, _isHovered);
        }

        public void RepositionToTaskbar()
        {
            if (_hwnd == IntPtr.Zero || ActualWidth <= 1 || ActualHeight <= 1) return;

            var source = PresentationSource.FromVisual(this);
            if (source == null || source.CompositionTarget == null) return;

            var toDevice = source.CompositionTarget.TransformToDevice;
            var fromDevice = source.CompositionTarget.TransformFromDevice;
            double dpiX = toDevice.M11;
            double dpiY = toDevice.M22;

            int physW = (int)Math.Ceiling(ActualWidth * dpiX);
            int physH = (int)Math.Ceiling(ActualHeight * dpiY);

            int targetX;
            int targetY;

            IntPtr tbHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tbHwnd != IntPtr.Zero && _lastOwnerHwnd != tbHwnd)
            {
                NativeMethods.SetWindowOwner(_hwnd, tbHwnd);
                _lastOwnerHwnd = tbHwnd;
            }

            IntPtr trayHwnd = tbHwnd != IntPtr.Zero
                ? NativeMethods.FindWindowEx(tbHwnd, IntPtr.Zero, "TrayNotifyWnd", null)
                : IntPtr.Zero;

            NativeMethods.RECT tbRect;
            if (tbHwnd != IntPtr.Zero &&
                NativeMethods.GetWindowRect(tbHwnd, out tbRect) &&
                (tbRect.Right - tbRect.Left) > 100 &&
                (tbRect.Bottom - tbRect.Top) > 20)
            {
                int tbHeight = tbRect.Bottom - tbRect.Top;
                if (_config.TaskbarDock == TaskbarDockMode.Left)
                {
                    int baseOffset = IsWin11WidgetsEnabled() ? 152 : 10;
                    targetX = tbRect.Left + (int)(baseOffset * dpiX) + (int)(_config.TaskbarOffsetX * dpiX);
                }
                else
                {
                    NativeMethods.RECT trayRect;
                    if (trayHwnd != IntPtr.Zero &&
                        NativeMethods.GetWindowRect(trayHwnd, out trayRect) &&
                        trayRect.Left > tbRect.Left + physW + 16)
                    {
                        targetX = trayRect.Left - physW - (int)(10 * dpiX) + (int)(_config.TaskbarOffsetX * dpiX);
                    }
                    else
                    {
                        targetX = tbRect.Right - physW - (int)(220 * dpiX) + (int)(_config.TaskbarOffsetX * dpiX);
                    }
                }
                targetY = tbRect.Top + (tbHeight - physH) / 2;
                targetX = Math.Max(tbRect.Left + (int)(6 * dpiX), Math.Min(tbRect.Right - physW - (int)(6 * dpiX), targetX));
            }
            else
            {
                var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                var work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                int tbTop = work.Bottom < bounds.Bottom ? work.Bottom : bounds.Bottom - (int)(48 * dpiY);
                int tbH = bounds.Bottom - tbTop;
                if (_config.TaskbarDock == TaskbarDockMode.Left)
                {
                    int baseOffset = IsWin11WidgetsEnabled() ? 152 : 10;
                    targetX = bounds.Left + (int)(baseOffset * dpiX) + (int)(_config.TaskbarOffsetX * dpiX);
                }
                else
                {
                    targetX = bounds.Right - physW - (int)(220 * dpiX) + (int)(_config.TaskbarOffsetX * dpiX);
                }
                targetY = tbTop + (tbH - physH) / 2;
                targetX = Math.Max(bounds.Left + (int)(6 * dpiX), Math.Min(bounds.Right - physW - (int)(6 * dpiX), targetX));
            }

            Point dipPos = fromDevice.Transform(new Point(targetX, targetY));
            if (double.IsNaN(Left) || Math.Abs(Left - dipPos.X) > 0.1) Left = dipPos.X;
            if (double.IsNaN(Top) || Math.Abs(Top - dipPos.Y) > 0.1) Top = dipPos.Y;
        }

        private static bool IsWin11WidgetsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", false))
                {
                    if (key == null) return true;
                    object val = key.GetValue("TaskbarDa");
                    if (val == null) return true;
                    return Convert.ToInt32(val) != 0;
                }
            }
            catch { return true; }
        }

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(MakeMenuItem("展开 / 收起详情面板", () => { if (BarClicked != null) BarClicked(); }));
            menu.Items.Add(MakeMenuItem("打开设置 (SYS.CONFIG)", () => { if (OpenSettingsRequested != null) OpenSettingsRequested(); }));
            _miDockToggle = MakeMenuItem("任务栏停靠：切换至右侧", () => { if (ToggleTaskbarDockRequested != null) ToggleTaskbarDockRequested(); });
            menu.Items.Add(_miDockToggle);
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("显示 / 隐藏桌面角标", () => { if (ToggleDesktopOverlayRequested != null) ToggleDesktopOverlayRequested(); }));
            menu.Items.Add(MakeMenuItem("固定 / 解锁桌面角标", () => { if (ToggleDesktopLockRequested != null) ToggleDesktopLockRequested(); }));
            menu.Items.Add(MakeMenuItem("清理闲置内存 (TRIM)", () => { if (TrimMemoryRequested != null) TrimMemoryRequested(); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("退出程序", () => { if (ExitRequested != null) ExitRequested(); }));
            menu.Opened += (s, e) =>
            {
                if (_miDockToggle != null)
                {
                    _miDockToggle.Header = _config.TaskbarDock == TaskbarDockMode.Left
                        ? "任务栏停靠：切换至右侧"
                        : "任务栏停靠：切换至左侧";
                }
            };
            return menu;
        }

        private static MenuItem MakeMenuItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => action();
            return item;
        }
    }
}
