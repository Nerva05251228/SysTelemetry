using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using SysTelemetry.Interop;
using SysTelemetry.Models;
using SysTelemetry.Services;
using SysTelemetry.UI;
using Application = System.Windows.Application;

namespace SysTelemetry
{
    public sealed class SysTelemetryApp : Application
    {
        private const int HotkeyLockId = 0x1001;
        private const int HotkeyPopoverId = 0x1002;
        private const string MutexName = "Local\\SysTelemetry_SingleInstance_Mutex_v1";
        private const string WakeEventName = "Local\\SysTelemetry_ShowPopover_Event_v1";

        private AppConfig _config;
        private TelemetryService _telemetry;
        private TaskbarBarWindow _taskbarBar;
        private DesktopOverlayWindow _desktopOverlay;
        private TelemetryPopoverWindow _popover;
        private NotifyIcon _trayIcon;
        private Icon _trayDrawingIcon;
        private DispatcherTimer _pollTimer;
        private HwndSource _hotkeySource;
        private bool _isSampling;
        private bool _hiddenByFullscreen;
        private EventWaitHandle _wakeEvent;
        private NativeMethods.WinEventDelegate _winEventProc;
        private IntPtr _fgHook;
        private DispatcherTimer _zOrderGuardTimer;
        private int _zOrderGuardTicks;

        private ToolStripMenuItem _miDeskVisible;
        private ToolStripMenuItem _miDeskLocked;
        private ToolStripMenuItem _miDockToggle;
        private ToolStripMenuItem _miAutoStart;

        [STAThread]
        public static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(WakeEventName))
                            ev.Set();
                    }
                    catch { }
                    return;
                }

                var app = new SysTelemetryApp();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.Run();
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _config = AppConfig.Load();
            _telemetry = new TelemetryService();

            _taskbarBar = new TaskbarBarWindow(_config);
            _desktopOverlay = new DesktopOverlayWindow(_config);
            _popover = new TelemetryPopoverWindow(_config);

            WireEvents();
            SetupTrayIcon();

            _taskbarBar.Show();
            if (_config.DesktopOverlayEnabled)
                _desktopOverlay.Show();

            // Create popover handle once invisibly so hotkeys & metrics work immediately
            _popover.Show();
            _popover.Hide();

            RegisterGlobalHotkeys();
            StartWakeListener();
            StartForegroundWatch();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_config.PollIntervalMs) };
            _pollTimer.Tick += async (s, args) => await PollTickAsync();
            _pollTimer.Start();

            // Trigger immediate first sample & quiet post-init memory trim
            Dispatcher.BeginInvoke(new Action(async () => await PollTickAsync()));
            Task.Delay(4000).ContinueWith(_ =>
            {
                try
                {
                    GC.Collect(2, GCCollectionMode.Optimized);
                    GC.WaitForPendingFinalizers();
                    NativeMethods.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
                }
                catch { }
            });
        }

        private void WireEvents()
        {
            _taskbarBar.BarClicked += TogglePopoverFromBar;
            _taskbarBar.OpenSettingsRequested += () => _popover.ShowSettingsNearTaskbar(_taskbarBar);
            _taskbarBar.ToggleTaskbarDockRequested += ToggleTaskbarDock;
            _taskbarBar.ToggleDesktopOverlayRequested += ToggleDesktopOverlay;
            _taskbarBar.ToggleDesktopLockRequested += ToggleDesktopLock;
            _taskbarBar.TrimMemoryRequested += PerformMemoryTrim;
            _taskbarBar.ExitRequested += ExitCleanly;

            _popover.ConfigChanged += () =>
            {
                _taskbarBar.Redraw();
                _taskbarBar.RepositionToTaskbar();
                _desktopOverlay.Redraw();
                SyncTrayMenuChecks();
            };

            _popover.DesktopOverlayToggled += () =>
            {
                ApplyOverlayVisibility();
                SyncTrayMenuChecks();
            };

            _popover.DesktopLockChanged += () =>
            {
                _desktopOverlay.ApplyLockAndOpacity();
                SyncTrayMenuChecks();
            };

            _popover.ResetDesktopCornerRequested += () =>
            {
                _desktopOverlay.ResetToTopRight();
            };

            _popover.TrimRequested += PerformMemoryTrim;

            _popover.PollRateChanged += () =>
            {
                _pollTimer.Interval = TimeSpan.FromMilliseconds(_config.PollIntervalMs);
            };

            _popover.Deactivated += (s, e) =>
            {
                if (!_hiddenByFullscreen)
                {
                    _taskbarBar.EnsureAboveTaskbar();
                    ScheduleZOrderGuard();
                }
            };

            _popover.IsVisibleChanged += (s, e) =>
            {
                if (!_popover.IsVisible && !_hiddenByFullscreen)
                {
                    _taskbarBar.EnsureAboveTaskbar();
                    ScheduleZOrderGuard();
                }
            };
        }

        private void SetupTrayIcon()
        {
            _trayDrawingIcon = IconFactory.CreateTrayIcon();
            var menu = new ContextMenuStrip();

            menu.Items.Add("展开 / 收起面板 (Ctrl+Alt+M)", null, (s, e) => TogglePopoverExplicit());
            menu.Items.Add("设置 (SYS.CONFIG)", null, (s, e) => _popover.ShowSettingsNearTaskbar(_taskbarBar));
            menu.Items.Add(new ToolStripSeparator());

            _miDeskVisible = new ToolStripMenuItem("显示桌面单行角标") { Checked = _config.DesktopOverlayEnabled };
            _miDeskVisible.Click += (s, e) => ToggleDesktopOverlay();
            menu.Items.Add(_miDeskVisible);

            _miDeskLocked = new ToolStripMenuItem("固定桌面角标位置 (Ctrl+Alt+L)") { Checked = _config.DesktopLocked };
            _miDeskLocked.Click += (s, e) => ToggleDesktopLock();
            menu.Items.Add(_miDeskLocked);

            _miDockToggle = new ToolStripMenuItem("任务栏靠左停靠") { Checked = _config.TaskbarDock == TaskbarDockMode.Left };
            _miDockToggle.Click += (s, e) => ToggleTaskbarDock();
            menu.Items.Add(_miDockToggle);

            menu.Items.Add("桌面角标归位右上角", null, (s, e) => _desktopOverlay.ResetToTopRight());
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("清理闲置内存 (TRIM)", null, (s, e) => PerformMemoryTrim());

            _miAutoStart = new ToolStripMenuItem("开机静默自启") { Checked = AutoStartService.IsEnabled() };
            _miAutoStart.Click += (s, e) =>
            {
                bool next = !AutoStartService.IsEnabled();
                string msg;
                AutoStartService.SetEnabled(next, out msg);
                _miAutoStart.Checked = AutoStartService.IsEnabled();
            };
            menu.Items.Add(_miAutoStart);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => ExitCleanly());

            _trayIcon = new NotifyIcon
            {
                Icon = _trayDrawingIcon,
                Text = "SYS.TELEMETRY",
                Visible = true,
                ContextMenuStrip = menu
            };
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) TogglePopoverExplicit();
            };
        }

        private void SyncTrayMenuChecks()
        {
            if (_miDeskVisible != null) _miDeskVisible.Checked = _config.DesktopOverlayEnabled;
            if (_miDeskLocked != null) _miDeskLocked.Checked = _config.DesktopLocked;
            if (_miDockToggle != null) _miDockToggle.Checked = _config.TaskbarDock == TaskbarDockMode.Left;
            if (_miAutoStart != null) _miAutoStart.Checked = AutoStartService.IsEnabled();
        }

        private void TogglePopoverFromBar()
        {
            if (_popover.IsVisible)
            {
                _popover.Hide();
            }
            else if (_popover.WasJustDeactivated)
            {
                // Avoid instant reopen if Deactivated fired on the same click
                return;
            }
            else
            {
                _popover.SetSettingsMode(false);
                _popover.ShowNearTaskbar(_taskbarBar);
                TriggerImmediateSampleAsync();
            }
        }

        private void TogglePopoverExplicit()
        {
            if (_popover.IsVisible)
            {
                _popover.Hide();
            }
            else
            {
                _popover.SetSettingsMode(false);
                _popover.ShowNearTaskbar(_taskbarBar);
                TriggerImmediateSampleAsync();
            }
        }

        private void ToggleDesktopOverlay()
        {
            _config.DesktopOverlayEnabled = !_config.DesktopOverlayEnabled;
            _config.Save();
            ApplyOverlayVisibility();
            SyncTrayMenuChecks();
            if (_popover.IsVisible) _popover.ApplyThemeColors();
        }

        private void ToggleDesktopLock()
        {
            _config.DesktopLocked = !_config.DesktopLocked;
            _config.Save();
            _desktopOverlay.ApplyLockAndOpacity();
            SyncTrayMenuChecks();
        }

        private void ToggleTaskbarDock()
        {
            _config.TaskbarDock = _config.TaskbarDock == TaskbarDockMode.Left
                ? TaskbarDockMode.Right
                : TaskbarDockMode.Left;
            _config.Save();
            _taskbarBar.RepositionToTaskbar();
            SyncTrayMenuChecks();
            if (_popover.IsVisible) _popover.ShowNearTaskbar(_taskbarBar);
        }

        private void ApplyOverlayVisibility()
        {
            if (_hiddenByFullscreen) return;
            if (_config.DesktopOverlayEnabled)
            {
                if (!_desktopOverlay.IsVisible) _desktopOverlay.Show();
                _desktopOverlay.ApplyLockAndOpacity();
            }
            else
            {
                if (_desktopOverlay.IsVisible) _desktopOverlay.Hide();
            }
        }

        private async void PerformMemoryTrim()
        {
            await Task.Run(() => TelemetryService.TrimWorkingSets());
            await PollTickAsync();
            _popover.PlayTrimAnimation();
        }

        private async Task PollTickAsync()
        {
            if (_isSampling) return;
            _isSampling = true;
            try
            {
                bool fullscreen = _config.FullscreenAutoHide &&
                                  NativeMethods.IsFullscreenAppActive(_taskbarBar.Handle, _desktopOverlay.Handle, _popover.Handle);

                if (fullscreen != _hiddenByFullscreen)
                {
                    _hiddenByFullscreen = fullscreen;
                    if (_hiddenByFullscreen)
                    {
                        if (_taskbarBar.IsVisible) _taskbarBar.Hide();
                        if (_desktopOverlay.IsVisible) _desktopOverlay.Hide();
                    }
                    else
                    {
                        if (!_taskbarBar.IsVisible) _taskbarBar.Show();
                        if (_config.DesktopOverlayEnabled && !_desktopOverlay.IsVisible) _desktopOverlay.Show();
                    }
                }

                bool needProcesses = _popover.IsVisible;
                TelemetrySnapshot snap = await Task.Run(() => _telemetry.Collect(needProcesses));

                if (!_hiddenByFullscreen)
                {
                    _taskbarBar.UpdateMetrics(snap);
                    if (_config.DesktopOverlayEnabled)
                        _desktopOverlay.UpdateMetrics(snap);
                }

                if (_popover.IsVisible)
                    _popover.UpdateMetrics(snap);
            }
            catch { }
            finally
            {
                _isSampling = false;
            }
        }

        private async void TriggerImmediateSampleAsync()
        {
            await PollTickAsync();
        }

        private void RegisterGlobalHotkeys()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(_taskbarBar).Handle;
                _hotkeySource = HwndSource.FromHwnd(hwnd);
                if (_hotkeySource != null)
                {
                    _hotkeySource.AddHook(HotkeyWndProc);
                    NativeMethods.RegisterHotKey(hwnd, HotkeyLockId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, (uint)Keys.L);
                    NativeMethods.RegisterHotKey(hwnd, HotkeyPopoverId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, (uint)Keys.M);
                }
            }
            catch { }
        }

        private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HotkeyLockId)
                {
                    ToggleDesktopLock();
                    handled = true;
                }
                else if (id == HotkeyPopoverId)
                {
                    TogglePopoverExplicit();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void StartWakeListener()
        {
            try
            {
                _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName);
                ThreadPool.RegisterWaitForSingleObject(_wakeEvent, (state, timedOut) =>
                {
                    if (!timedOut)
                        Dispatcher.BeginInvoke(new Action(TogglePopoverExplicit));
                }, null, -1, false);
            }
            catch { }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_config.Theme != ThemeMode.Auto) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _taskbarBar.Redraw();
                _desktopOverlay.Redraw();
                if (_popover.IsVisible) _popover.ApplyThemeColors();
            }));
        }

        private void StartForegroundWatch()
        {
            _zOrderGuardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
            _zOrderGuardTimer.Tick += (s, e) =>
            {
                if (!_hiddenByFullscreen && _taskbarBar != null && _taskbarBar.IsVisible)
                    _taskbarBar.EnsureAboveTaskbar();
                _zOrderGuardTicks--;
                if (_zOrderGuardTicks <= 0) _zOrderGuardTimer.Stop();
            };

            try
            {
                _winEventProc = OnForegroundChanged;
                _fgHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _winEventProc,
                    0, 0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
            }
            catch { }
        }

        private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (_hiddenByFullscreen || _taskbarBar == null || !_taskbarBar.IsVisible) return;
            _taskbarBar.EnsureAboveTaskbar();
            ScheduleZOrderGuard();
        }

        private void ScheduleZOrderGuard()
        {
            if (_zOrderGuardTimer == null) return;
            _zOrderGuardTicks = 4;
            if (!_zOrderGuardTimer.IsEnabled) _zOrderGuardTimer.Start();
        }

        private void ExitCleanly()
        {
            try { if (_pollTimer != null) _pollTimer.Stop(); } catch { }
            try { if (_zOrderGuardTimer != null) _zOrderGuardTimer.Stop(); } catch { }
            try { if (_fgHook != IntPtr.Zero) { NativeMethods.UnhookWinEvent(_fgHook); _fgHook = IntPtr.Zero; } } catch { }
            try { SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; } catch { }

            try
            {
                if (_taskbarBar != null && _taskbarBar.Handle != IntPtr.Zero)
                {
                    NativeMethods.UnregisterHotKey(_taskbarBar.Handle, HotkeyLockId);
                    NativeMethods.UnregisterHotKey(_taskbarBar.Handle, HotkeyPopoverId);
                }
            }
            catch { }

            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                if (_trayDrawingIcon != null) _trayDrawingIcon.Dispose();
            }
            catch { }

            try { if (_telemetry != null) _telemetry.Dispose(); } catch { }
            try { if (_wakeEvent != null) _wakeEvent.Dispose(); } catch { }
            Shutdown();
        }
    }
}
