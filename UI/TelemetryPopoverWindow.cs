using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SysTelemetry.Interop;
using SysTelemetry.Models;
using SysTelemetry.Services;

namespace SysTelemetry.UI
{
    public enum PopoverViewMode
    {
        Telemetry = 0,
        Traffic = 1,
        Settings = 2
    }

    public sealed class TelemetryPopoverWindow : Window
    {
        private static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable, Segoe UI, Microsoft YaHei UI");
        private static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");

        private readonly AppConfig _config;
        private readonly Func<bool, List<KeyValuePair<string, ApplicationTraffic>>> _getApplicationTraffic;
        private Border _shellBorder;
        private Ellipse _statusDot;
        private TextBlock _titleText;
        private Border _btnDesktopToggle;
        private Border _btnTrim;
        private Border _btnTraffic;
        private Border _btnGear;
        private Border _btnClose;

        private Grid _telemetryView;
        private ScrollViewer _trafficScroll;
        private StackPanel _trafficView;
        private ScrollViewer _settingsScroll;
        private StackPanel _settingsView;
        private PopoverViewMode _activeView = PopoverViewMode.Telemetry;
        private TaskbarBarWindow _anchorBar;

        private Canvas _chartCanvas;
        private Polyline _cpuLine;
        private Polyline _gpuLine;
        private Polyline _tempLine;

        private TextBlock _cpuMain;
        private TextBlock _cpuSub;
        private Ellipse _cpuDot;
        private TextBlock _gpuMain;
        private TextBlock _gpuSub;
        private Ellipse _gpuDot;
        private TextBlock _memMain;
        private TextBlock _memSub;
        private Ellipse _memDot;
        private Border _memCard;
        private TextBlock _diskMain;
        private TextBlock _diskSub;
        private Ellipse _diskDot;
        private StackPanel _processRows;

        // Compact NET / TRAFFIC summary strip on Telemetry view
        private Border _netSummaryCard;
        private TextBlock _netSummarySpeed;
        private TextBlock _netSummaryGlobal;
        private TextBlock _netSummaryGlobalSub;
        private TextBlock _netSummaryProxy;
        private Border _netSummaryBarTrack;
        private Border _netSummaryBarProxy;

        // Dedicated SYS.TRAFFIC view controls
        private bool _trafficMonthlyMode;
        private readonly List<Button> _trafficHistoryButtons = new List<Button>();
        private readonly List<Button> _trafficViewButtons = new List<Button>();
        private readonly List<Button> _applicationPeriodButtons = new List<Button>();
        private bool _showApplicationTraffic;
        private bool _applicationCycleMode;
        private StackPanel _trafficOverview;
        private StackPanel _applicationTrafficView;
        private StackPanel _applicationRows;
        private Border _applicationPeriodSelector;
        private TextBlock _applicationEmpty;
        private TextBlock _tfTodaySectionTitle;
        private TextBlock _tfLiveSpeed;
        private TextBlock _tfTodayGlobalMain;
        private TextBlock _tfTodayGlobalSub;
        private TextBlock _tfTodayProxyMain;
        private TextBlock _tfTodayProxySub;
        private TextBlock _tfTodayProxyBadgeText;
        private Border _tfTodayBarTrack;
        private Border _tfTodayBarProxy;
        private TextBlock _tfTodayDirectText;
        private TextBlock _tfTodayRatioText;

        private TextBlock _tfMonthSectionTitle;
        private TextBlock _tfResetDayHint;
        private TextBlock _tfMonthGlobalMain;
        private TextBlock _tfMonthGlobalSub;
        private TextBlock _tfMonthProxyMain;
        private TextBlock _tfMonthProxySub;
        private TextBlock _tfMonthProxyBadgeText;
        private Border _tfMonthBarTrack;
        private Border _tfMonthBarProxy;
        private TextBlock _tfMonthDirectText;
        private TextBlock _tfMonthRatioText;

        private TextBlock _tfChartPeak;
        private Grid _tfChartBarsGrid;
        private Grid _tfChartLabelsGrid;
        private StackPanel _tfHistoryRows;
        private Ellipse _tfStatusDot;
        private TextBlock _tfStatusText;
        private Button _tfBtnResetToday;
        private bool _confirmResetTodayFooter;
        private DispatcherTimer _confirmResetTimer;

        // Settings controls
        private CheckBox _chkAutoStart;
        private CheckBox _chkDeskEnabled;
        private CheckBox _chkDeskLocked;
        private CheckBox _chkFullscreenHide;
        private Slider _sliderOpacity;
        private TextBlock _lblOpacity;
        private Slider _sliderOffset;
        private TextBlock _lblOffset;
        private Border _offsetBadge;
        private Button _btnResetCorner;
        private Border _deskLockRow;
        private Border _deskOpacityRow;
        private TextBlock _lblTrafficResetDay;
        private TextBox _txtProxyProcesses;
        private TextBox _txtLocalProxyPorts;
        private Button _btnCfgResetToday;
        private Button _btnCfgClearAll;
        private bool _confirmCfgResetToday;
        private bool _confirmCfgClearAll;
        private readonly List<Button> _skinButtons = new List<Button>();
        private readonly List<Button> _themeButtons = new List<Button>();
        private readonly List<Button> _rateButtons = new List<Button>();
        private readonly List<Button> _tempButtons = new List<Button>();
        private readonly List<Button> _dockButtons = new List<Button>();
        private Brush _rowHoverBrush = Brushes.Transparent;
        private Brush _accentPrimaryBrush = Brushes.SkyBlue;
        private Brush _mutedTextBrush = Brushes.Gray;

        private TelemetrySnapshot _lastSnap;
        private DateTime _lastDeactivatedUtc = DateTime.MinValue;
        private IntPtr _hwnd;

        public IntPtr Handle { get { return _hwnd; } }
        public bool WasJustDeactivated { get { return (DateTime.UtcNow - _lastDeactivatedUtc).TotalMilliseconds < 220; } }
        public bool IsTelemetryViewActive { get { return _activeView == PopoverViewMode.Telemetry; } }

        public event Action ConfigChanged;
        public event Action DesktopOverlayToggled;
        public event Action DesktopLockChanged;
        public event Action ResetDesktopCornerRequested;
        public event Action TrimRequested;
        public event Action PollRateChanged;
        public event Action TrafficConfigChanged;
        public event Action ResetTodayTrafficRequested;
        public event Action ClearAllTrafficRequested;

        public TelemetryPopoverWindow(AppConfig config,
            Func<bool, List<KeyValuePair<string, ApplicationTraffic>>> getApplicationTraffic)
        {
            _config = config;
            _getApplicationTraffic = getApplicationTraffic;
            Width = 372;
            SizeToContent = SizeToContent.Height;
            AllowsTransparency = true;
            WindowStyle = WindowStyle.None;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = true;
            ResizeMode = ResizeMode.NoResize;

            BuildLayout();

            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.ApplyToolWindowStyle(_hwnd, clickThrough: false);
            };
            Deactivated += (s, e) =>
            {
                CommitProxyProcessNames();
                CommitLocalProxyPorts();
                ClearPendingConfirmStates();
                _lastDeactivatedUtc = DateTime.UtcNow;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!IsActive && IsVisible) Hide();
                }));
            };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Hide();
            };
            SizeChanged += (s, e) =>
            {
                if (IsVisible) RepositionBottomAligned();
            };
        }

        public void ShowNearTaskbar(TaskbarBarWindow bar)
        {
            _anchorBar = bar;
            ApplyThemeColors();
            SyncSettingsControls();
            if (!IsVisible) Show();
            UpdateLayout();
            RepositionBottomAligned();
            Activate();
        }

        private void RepositionBottomAligned()
        {
            var work = SystemParameters.WorkArea;
            double targetLeft;
            if (_anchorBar != null && _anchorBar.IsVisible)
            {
                targetLeft = _config.TaskbarDock == TaskbarDockMode.Left
                    ? _anchorBar.Left
                    : _anchorBar.Left + _anchorBar.ActualWidth - ActualWidth;
            }
            else
            {
                targetLeft = _config.TaskbarDock == TaskbarDockMode.Left
                    ? work.Left + 14
                    : work.Right - ActualWidth - 14;
            }
            targetLeft = Math.Max(work.Left + 10, Math.Min(work.Right - ActualWidth - 10, targetLeft));

            double targetTop = work.Bottom - ActualHeight - 10;
            targetTop = Math.Max(work.Top + 10, targetTop);

            Left = targetLeft;
            Top = targetTop;
        }

        public void ShowSettingsNearTaskbar(TaskbarBarWindow bar)
        {
            SetViewMode(PopoverViewMode.Settings);
            ShowNearTaskbar(bar);
        }

        public void ShowTrafficNearTaskbar(TaskbarBarWindow bar)
        {
            SetViewMode(PopoverViewMode.Traffic);
            ShowNearTaskbar(bar);
        }

        public void UpdateTrafficOnly(TrafficSnapshot traffic)
        {
            if (traffic == null) return;
            if (_lastSnap == null) _lastSnap = new TelemetrySnapshot();
            _lastSnap.Traffic = traffic;
            if (!IsVisible) return;
            var pal = ThemeHelper.GetPalette(_config.Theme);
            if (_activeView == PopoverViewMode.Telemetry)
                UpdateNetSummaryStrip(_lastSnap, pal);
            else if (_activeView == PopoverViewMode.Traffic)
                UpdateTrafficView(_lastSnap, pal);
        }

        public void UpdateMetrics(TelemetrySnapshot snap)
        {
            _lastSnap = snap;
            if (!IsVisible || _activeView == PopoverViewMode.Settings) return;

            var pal = ThemeHelper.GetPalette(_config.Theme);

            if (_activeView == PopoverViewMode.Traffic)
            {
                UpdateTrafficView(snap, pal);
                return;
            }

            _cpuMain.Text = snap.CpuTempC.HasValue
                ? string.Format("{0:0}% · {1:0}°C", snap.CpuLoadPercent, snap.CpuTempC.Value)
                : string.Format("{0:0}%", snap.CpuLoadPercent);
            _cpuSub.Text = snap.CpuPowerW.HasValue
                ? string.Format("{0:0}W · {1:0.00} GHz", snap.CpuPowerW.Value, snap.CpuClockGhz)
                : string.Format("{0:0.00} GHz", snap.CpuClockGhz);
            _cpuDot.Fill = ThemeHelper.GetStatusBrush(pal, snap.CpuLoadPercent, snap.CpuTempC, _config.TempWarnThreshold);

            _gpuMain.Text = snap.GpuTempC.HasValue
                ? string.Format("{0:0}% · {1:0}°C", snap.GpuLoadPercent, snap.GpuTempC.Value)
                : string.Format("{0:0}%", snap.GpuLoadPercent);
            string vram = snap.GpuVramTotalGb > 0
                ? string.Format("{0:0.0}/{1:0.0}G", snap.GpuVramUsedGb, snap.GpuVramTotalGb)
                : string.Format("{0:0.0}G", snap.GpuVramUsedGb);
            _gpuSub.Text = snap.GpuPowerW.HasValue
                ? string.Format("{0:0}W · {1}", snap.GpuPowerW.Value, vram)
                : vram;
            _gpuDot.Fill = ThemeHelper.GetStatusBrush(pal, snap.GpuLoadPercent, snap.GpuTempC, _config.TempWarnThreshold);

            _memMain.Text = string.Format("{0:0}% · {1:0.0}G", snap.MemoryPercent, snap.MemoryUsedGb);
            double freeGb = Math.Max(0, snap.MemoryTotalGb - snap.MemoryUsedGb);
            _memSub.Text = string.Format("FREE {0:0.0} / {1:0} GB", freeGb, snap.MemoryTotalGb);
            _memDot.Fill = ThemeHelper.GetStatusBrush(pal, snap.MemoryPercent, null, _config.TempWarnThreshold);

            _diskMain.Text = snap.DiskTempC.HasValue ? string.Format("{0:0}°C", snap.DiskTempC.Value) : "--°C";
            _diskSub.Text = string.Format("R {0:0.#}M/s · W {1:0.#}M/s", snap.DiskReadMbPerSec, snap.DiskWriteMbPerSec);
            _diskDot.Fill = snap.DiskTempC.HasValue
                ? ThemeHelper.GetStatusBrush(pal, snap.DiskTempC.Value, snap.DiskTempC, _config.TempWarnThreshold)
                : pal.StatusOk;

            UpdateNetSummaryStrip(snap, pal);
            UpdateChartLines(snap);
            UpdateProcessRows(snap.TopProcesses, pal);
        }

        private void UpdateNetSummaryStrip(TelemetrySnapshot snap, ThemePalette pal)
        {
            if (_netSummarySpeed == null) return;
            _netSummarySpeed.Text = string.Format("↓{0}/s · ↑{1}/s",
                ThemeHelper.FormatNetSpeed(snap.NetDownBytesPerSec),
                ThemeHelper.FormatNetSpeed(snap.NetUpBytesPerSec));

            var tf = snap.Traffic;
            if (tf == null || tf.Today == null)
            {
                _netSummaryGlobal.Text = "全局 0 B";
                _netSummaryGlobalSub.Text = "↓0B ↑0B";
                _netSummaryProxy.Text = "代理 0 B";
                UpdateStackedRatioBar(_netSummaryBarTrack, _netSummaryBarProxy, 0, 0, pal);
                return;
            }

            var t = tf.Today;
            _netSummaryGlobal.Text = "全局 " + ThemeHelper.FormatDataSize(t.GlobalTotal);
            _netSummaryGlobalSub.Text = string.Format(" ↓{0} ↑{1}",
                ThemeHelper.FormatDataSize(t.GlobalRx, compact: true),
                ThemeHelper.FormatDataSize(t.GlobalTx, compact: true));
            _netSummaryProxy.Text = t.GlobalTotal > 0
                ? string.Format("代理 {0} · {1:0}%", ThemeHelper.FormatDataSize(t.ProxyTotal, compact: true), t.ProxyRatio * 100.0)
                : "代理 0B";
            UpdateStackedRatioBar(_netSummaryBarTrack, _netSummaryBarProxy, t.GlobalTotal, t.ProxyRatio, pal);
        }

        private void UpdateTrafficView(TelemetrySnapshot snap, ThemePalette pal)
        {
            if (_tfTodayGlobalMain == null) return;
            _tfLiveSpeed.Text = string.Format("↓ {0}/s · ↑ {1}/s",
                ThemeHelper.FormatNetSpeed(snap.NetDownBytesPerSec),
                ThemeHelper.FormatNetSpeed(snap.NetUpBytesPerSec));

            var tf = snap.Traffic;
            if (tf == null) return;

            if (_tfStatusDot != null && _tfStatusText != null)
            {
                _tfStatusDot.Fill = tf.EtwActive ? pal.StatusOk : pal.StatusWarn;
                _tfStatusText.Text = tf.EtwStatusText ?? string.Empty;
            }
            if (_showApplicationTraffic)
            {
                RenderApplicationTraffic(tf.EtwActive, pal);
                return;
            }

            var today = tf.Today;
            if (today != null)
            {
                _tfTodaySectionTitle.Text = "TODAY · " + today.RangeText;
                _tfTodayGlobalMain.Text = ThemeHelper.FormatDataSize(today.GlobalTotal);
                _tfTodayGlobalSub.Text = string.Format("↓ {0}  ↑ {1}",
                    ThemeHelper.FormatDataSize(today.GlobalRx, compact: true),
                    ThemeHelper.FormatDataSize(today.GlobalTx, compact: true));
                _tfTodayProxyMain.Text = ThemeHelper.FormatDataSize(today.ProxyTotal);
                _tfTodayProxySub.Text = string.Format("↓ {0}  ↑ {1}",
                    ThemeHelper.FormatDataSize(today.ProxyRx, compact: true),
                    ThemeHelper.FormatDataSize(today.ProxyTx, compact: true));
                _tfTodayProxyBadgeText.Text = string.Format("{0:0.0}%", today.ProxyRatio * 100.0);
                UpdateStackedRatioBar(_tfTodayBarTrack, _tfTodayBarProxy, today.GlobalTotal, today.ProxyRatio, pal);
                _tfTodayDirectText.Text = string.Format("直连 {0} ({1:0.0}%)",
                    ThemeHelper.FormatDataSize(today.DirectTotal),
                    today.GlobalTotal > 0 ? (1.0 - today.ProxyRatio) * 100.0 : 0.0);
                _tfTodayRatioText.Text = string.Format("代理 {0:0.0}%", today.ProxyRatio * 100.0);
            }

            var cycle = tf.CurrentCycle;
            if (cycle != null)
            {
                _tfMonthSectionTitle.Text = tf.ResetDay == 1
                    ? "MONTH · " + cycle.Label
                    : "CYCLE · " + cycle.RangeText;
                _tfResetDayHint.Text = string.Format("每月 {0} 日重置 ›", tf.ResetDay);
                _tfMonthGlobalMain.Text = ThemeHelper.FormatDataSize(cycle.GlobalTotal);
                _tfMonthGlobalSub.Text = string.Format("↓ {0}  ↑ {1}",
                    ThemeHelper.FormatDataSize(cycle.GlobalRx, compact: true),
                    ThemeHelper.FormatDataSize(cycle.GlobalTx, compact: true));
                _tfMonthProxyMain.Text = ThemeHelper.FormatDataSize(cycle.ProxyTotal);
                _tfMonthProxySub.Text = string.Format("↓ {0}  ↑ {1}",
                    ThemeHelper.FormatDataSize(cycle.ProxyRx, compact: true),
                    ThemeHelper.FormatDataSize(cycle.ProxyTx, compact: true));
                _tfMonthProxyBadgeText.Text = string.Format("{0:0.0}%", cycle.ProxyRatio * 100.0);
                UpdateStackedRatioBar(_tfMonthBarTrack, _tfMonthBarProxy, cycle.GlobalTotal, cycle.ProxyRatio, pal);
                _tfMonthDirectText.Text = string.Format("直连 {0} ({1:0.0}%)",
                    ThemeHelper.FormatDataSize(cycle.DirectTotal),
                    cycle.GlobalTotal > 0 ? (1.0 - cycle.ProxyRatio) * 100.0 : 0.0);
                _tfMonthRatioText.Text = string.Format("代理 {0:0.0}%", cycle.ProxyRatio * 100.0);
            }

            var series = _trafficMonthlyMode ? tf.MonthlyHistory : tf.DailyHistory;
            RenderTrafficHistory(series, pal);
        }

        private static void UpdateStackedRatioBar(Border track, Border proxyFill, long globalTotal, double proxyRatio, ThemePalette pal)
        {
            if (track == null || proxyFill == null) return;
            double trackWidth = track.ActualWidth > 10 ? track.ActualWidth : 304.0;
            if (globalTotal <= 0)
            {
                track.Background = pal.TrackBackground;
                proxyFill.Width = 0;
                return;
            }

            track.Background = pal.DirectBarBrush;
            proxyFill.Background = pal.ProxyBarBrush;
            double clamped = Math.Max(0.0, Math.Min(1.0, proxyRatio));
            double px = clamped * trackWidth;
            if (clamped > 0.002 && px < 3.0) px = 3.0;
            proxyFill.Width = Math.Min(trackWidth, px);
        }

        private void RenderTrafficHistory(List<TrafficPeriodSummary> items, ThemePalette pal)
        {
            if (_tfChartBarsGrid == null || _tfChartLabelsGrid == null || _tfHistoryRows == null) return;

            _tfChartBarsGrid.Children.Clear();
            _tfChartBarsGrid.ColumnDefinitions.Clear();
            _tfChartLabelsGrid.Children.Clear();
            _tfChartLabelsGrid.ColumnDefinitions.Clear();
            _tfHistoryRows.Children.Clear();

            if (items == null || items.Count == 0)
            {
                if (_tfChartPeak != null) _tfChartPeak.Text = "峰值 0 B";
                return;
            }

            long maxVal = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].GlobalTotal > maxVal) maxVal = items[i].GlobalTotal;
            }
            if (_tfChartPeak != null)
                _tfChartPeak.Text = "峰值 " + ThemeHelper.FormatDataSize(maxVal);

            const double maxBarHeight = 46.0;
            double barWidth = items.Count > 12 ? 10.0 : 12.0;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                _tfChartBarsGrid.ColumnDefinitions.Add(new ColumnDefinition());
                _tfChartLabelsGrid.ColumnDefinitions.Add(new ColumnDefinition());

                string tipText = string.Format(
                    "{0}\n全局: {1} (↓ {2} / ↑ {3})\n代理: {4} (↓ {5} / ↑ {6})\n直连: {7} · 代理占比 {8:0.0}%",
                    item.RangeText,
                    ThemeHelper.FormatDataSize(item.GlobalTotal),
                    ThemeHelper.FormatDataSize(item.GlobalRx),
                    ThemeHelper.FormatDataSize(item.GlobalTx),
                    ThemeHelper.FormatDataSize(item.ProxyTotal),
                    ThemeHelper.FormatDataSize(item.ProxyRx),
                    ThemeHelper.FormatDataSize(item.ProxyTx),
                    ThemeHelper.FormatDataSize(item.DirectTotal),
                    item.ProxyRatio * 100.0);

                var colHost = new Border
                {
                    Background = Brushes.Transparent,
                    CornerRadius = new CornerRadius(3),
                    Cursor = Cursors.Hand,
                    ToolTip = tipText,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                colHost.MouseEnter += (s, e) => { colHost.Background = _rowHoverBrush; };
                colHost.MouseLeave += (s, e) => { colHost.Background = Brushes.Transparent; };

                var barStack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                if (maxVal <= 0 || item.GlobalTotal <= 0)
                {
                    barStack.Children.Add(new Border
                    {
                        Width = barWidth,
                        Height = 2,
                        CornerRadius = new CornerRadius(1),
                        Background = pal.TrackBackground,
                        Opacity = 0.5
                    });
                }
                else
                {
                    double totalH = Math.Max(3.0, ((double)item.GlobalTotal / maxVal) * maxBarHeight);
                    double proxyH = item.ProxyTotal > 0 ? Math.Max(2.0, totalH * item.ProxyRatio) : 0.0;
                    if (proxyH > totalH) proxyH = totalH;
                    double directH = Math.Max(0.0, totalH - proxyH);

                    if (directH > 0.5)
                    {
                        barStack.Children.Add(new Border
                        {
                            Width = barWidth,
                            Height = directH,
                            CornerRadius = proxyH > 0.5 ? new CornerRadius(2.5, 2.5, 0, 0) : new CornerRadius(2.5),
                            Background = pal.DirectBarBrush,
                            Opacity = item.IsCurrent ? 1.0 : 0.82
                        });
                    }
                    if (proxyH > 0.5)
                    {
                        barStack.Children.Add(new Border
                        {
                            Width = barWidth,
                            Height = proxyH,
                            CornerRadius = directH > 0.5 ? new CornerRadius(0, 0, 1.5, 1.5) : new CornerRadius(2.5),
                            Background = pal.ProxyBarBrush,
                            Opacity = item.IsCurrent ? 1.0 : 0.88
                        });
                    }
                }

                colHost.Child = barStack;
                Grid.SetColumn(colHost, i);
                _tfChartBarsGrid.Children.Add(colHost);

                // Sparse axis ticks to avoid crowding
                bool showTick = i == 0 || i == items.Count - 1 ||
                                (items.Count == 14 && (i == 4 || i == 9)) ||
                                (items.Count == 12 && (i == 3 || i == 6 || i == 9));
                if (showTick)
                {
                    var tick = new TextBlock
                    {
                        Text = item.ShortLabel,
                        FontFamily = MonoFont,
                        FontSize = 8.5,
                        Foreground = item.IsCurrent ? pal.TaskbarTextPrimary : pal.TaskbarTextMuted,
                        FontWeight = item.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 3, 0, 0)
                    };
                    Typography.SetNumeralAlignment(tick, FontNumeralAlignment.Tabular);
                    Grid.SetColumn(tick, i);
                    _tfChartLabelsGrid.Children.Add(tick);
                }
            }

            // Detail rows newest -> oldest
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                string rowTip = string.Format(
                    "{0}\n全局: {1} (↓ {2} / ↑ {3})\n代理: {4} (↓ {5} / ↑ {6})\n直连: {7}",
                    item.RangeText,
                    ThemeHelper.FormatDataSize(item.GlobalTotal),
                    ThemeHelper.FormatDataSize(item.GlobalRx),
                    ThemeHelper.FormatDataSize(item.GlobalTx),
                    ThemeHelper.FormatDataSize(item.ProxyTotal),
                    ThemeHelper.FormatDataSize(item.ProxyRx),
                    ThemeHelper.FormatDataSize(item.ProxyTx),
                    ThemeHelper.FormatDataSize(item.DirectTotal));

                var rowBorder = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 3.5, 6, 3.5),
                    Background = item.IsCurrent ? ThemeHelper.BrushFromHex("#1638BDF8") : Brushes.Transparent,
                    ToolTip = rowTip
                };
                if (!item.IsCurrent)
                {
                    rowBorder.MouseEnter += (s, e) => { rowBorder.Background = _rowHoverBrush; };
                    rowBorder.MouseLeave += (s, e) => { rowBorder.Background = Brushes.Transparent; };
                }

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

                var lblTb = new TextBlock
                {
                    Text = item.Label,
                    FontFamily = MonoFont,
                    FontSize = 10.5,
                    FontWeight = item.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = pal.TaskbarTextPrimary,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Typography.SetNumeralAlignment(lblTb, FontNumeralAlignment.Tabular);
                Grid.SetColumn(lblTb, 0);

                var globTb = new TextBlock
                {
                    Text = ThemeHelper.FormatDataSize(item.GlobalTotal),
                    FontFamily = MonoFont,
                    FontSize = 10.5,
                    Foreground = pal.TaskbarTextPrimary,
                    TextAlignment = TextAlignment.Right
                };
                Typography.SetNumeralAlignment(globTb, FontNumeralAlignment.Tabular);
                Grid.SetColumn(globTb, 1);

                var proxyTb = new TextBlock
                {
                    Text = ThemeHelper.FormatDataSize(item.ProxyTotal),
                    FontFamily = MonoFont,
                    FontSize = 10.5,
                    Foreground = item.ProxyTotal > 0 ? pal.AccentPurple : pal.TaskbarTextMuted,
                    TextAlignment = TextAlignment.Right
                };
                Typography.SetNumeralAlignment(proxyTb, FontNumeralAlignment.Tabular);
                Grid.SetColumn(proxyTb, 2);

                var pctTb = new TextBlock
                {
                    Text = item.GlobalTotal > 0 ? string.Format("{0:0}%", item.ProxyRatio * 100.0) : "--",
                    FontFamily = MonoFont,
                    FontSize = 10,
                    Foreground = pal.TaskbarTextMuted,
                    TextAlignment = TextAlignment.Right
                };
                Typography.SetNumeralAlignment(pctTb, FontNumeralAlignment.Tabular);
                Grid.SetColumn(pctTb, 3);

                rowGrid.Children.Add(lblTb);
                rowGrid.Children.Add(globTb);
                rowGrid.Children.Add(proxyTb);
                rowGrid.Children.Add(pctTb);
                rowBorder.Child = rowGrid;
                _tfHistoryRows.Children.Add(rowBorder);
            }
        }

        private void UpdateChartLines(TelemetrySnapshot snap)
        {
            double w = _chartCanvas.ActualWidth > 10 ? _chartCanvas.ActualWidth : 316;
            double h = _chartCanvas.Height;
            FillPolyline(_cpuLine, snap.CpuHistory, w, h);
            FillPolyline(_gpuLine, snap.GpuHistory, w, h);
            FillPolyline(_tempLine, snap.TempHistory, w, h);
        }

        private static void FillPolyline(Polyline line, List<double> values, double w, double h)
        {
            line.Points.Clear();
            if (values == null || values.Count < 2) return;
            for (int i = 0; i < values.Count; i++)
            {
                double x = (double)i / (values.Count - 1) * w;
                double v = Math.Max(0, Math.Min(100, values[i]));
                double y = h - 3 - (v / 100.0) * (h - 6);
                line.Points.Add(new Point(x, y));
            }
        }

        private void UpdateProcessRows(List<ProcessMetric> list, ThemePalette pal)
        {
            _processRows.Children.Clear();
            if (list == null) return;
            foreach (var p in list)
            {
                bool isSelf = p.Name.StartsWith("SysTelemetry", StringComparison.OrdinalIgnoreCase);
                var rowBorder = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 3, 6, 3),
                    Background = isSelf ? ThemeHelper.BrushFromHex("#1A38BDF8") : Brushes.Transparent
                };
                var dp = new DockPanel();
                string stats = string.Format("{0:0.0}% · {1:0} MB · {2}/s",
                    p.CpuPercent, p.MemoryMb, ThemeHelper.FormatNetSpeed(p.IoBytesPerSec));
                var rightTb = new TextBlock
                {
                    Text = stats,
                    FontFamily = MonoFont,
                    FontSize = 11,
                    Foreground = pal.TaskbarTextMuted
                };
                Typography.SetNumeralAlignment(rightTb, FontNumeralAlignment.Tabular);
                DockPanel.SetDock(rightTb, Dock.Right);
                dp.Children.Add(rightTb);

                dp.Children.Add(new TextBlock
                {
                    Text = p.Name,
                    FontFamily = MonoFont,
                    FontSize = 11,
                    FontWeight = isSelf ? FontWeights.Bold : FontWeights.Medium,
                    Foreground = pal.TaskbarTextPrimary,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                rowBorder.Child = dp;
                _processRows.Children.Add(rowBorder);
            }
        }

        private void BuildLayout()
        {
            _shellBorder = new Border
            {
                Margin = new Thickness(10),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 18,
                    ShadowDepth = 4,
                    Opacity = 0.45
                }
            };

            var rootStack = new StackPanel();

            // Header: dot + SYS.TELEMETRY + 5 icon-only square buttons
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            var rightButtons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(rightButtons, Dock.Right);

            _btnDesktopToggle = MakeIconButton(IconFactory.DesktopOverlaySvg, "开启 / 关闭桌面单行悬浮角标", () =>
            {
                _config.DesktopOverlayEnabled = !_config.DesktopOverlayEnabled;
                _config.Save();
                if (DesktopOverlayToggled != null) DesktopOverlayToggled();
                ApplyThemeColors();
                SyncSettingsControls();
            });
            _btnTrim = MakeIconButton(IconFactory.TrimBoltSvg, "清理闲置工作集内存", () => { if (TrimRequested != null) TrimRequested(); });
            _btnTraffic = MakeIconButton(IconFactory.TrafficSvg, "流量统计 (SYS.TRAFFIC)", () =>
            {
                SetViewMode(_activeView == PopoverViewMode.Traffic ? PopoverViewMode.Telemetry : PopoverViewMode.Traffic);
            });
            _btnGear = MakeIconButton(IconFactory.GearSvg, "设置 (SYS.CONFIG)", () =>
            {
                SetViewMode(_activeView == PopoverViewMode.Settings ? PopoverViewMode.Telemetry : PopoverViewMode.Settings);
            });
            _btnClose = MakeIconButton(IconFactory.CloseSvg, "关闭面板", Hide, borderless: true);

            rightButtons.Children.Add(_btnDesktopToggle);
            rightButtons.Children.Add(new Border { Width = 5 });
            rightButtons.Children.Add(_btnTrim);
            rightButtons.Children.Add(new Border { Width = 5 });
            rightButtons.Children.Add(_btnTraffic);
            rightButtons.Children.Add(new Border { Width = 5 });
            rightButtons.Children.Add(_btnGear);
            rightButtons.Children.Add(new Border { Width = 4 });
            rightButtons.Children.Add(_btnClose);
            header.Children.Add(rightButtons);

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _statusDot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            _titleText = new TextBlock
            {
                Text = "SYS.TELEMETRY",
                FontFamily = MonoFont,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleRow.Children.Add(_statusDot);
            titleRow.Children.Add(_titleText);
            header.Children.Add(titleRow);
            rootStack.Children.Add(header);

            BuildTelemetryView();
            BuildTrafficView();
            BuildSettingsView();

            double maxScrollH = Math.Max(360, SystemParameters.WorkArea.Height - 110);
            _trafficScroll = new ScrollViewer
            {
                Visibility = Visibility.Collapsed,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = maxScrollH,
                Content = _trafficView
            };
            ApplyThinScrollBarStyle(_trafficScroll);

            _settingsScroll = new ScrollViewer
            {
                Visibility = Visibility.Collapsed,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = maxScrollH,
                Content = _settingsView
            };
            ApplyThinScrollBarStyle(_settingsScroll);

            rootStack.Children.Add(_telemetryView);
            rootStack.Children.Add(_trafficScroll);
            rootStack.Children.Add(_settingsScroll);

            _shellBorder.Child = rootStack;
            Content = _shellBorder;
            ApplyThemeColors();
        }

        private void BuildTelemetryView()
        {
            _telemetryView = new Grid();
            _telemetryView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _telemetryView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _telemetryView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _telemetryView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 60S TRACE
            var chartCard = new Border
            {
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 0, 0, 9),
                Tag = "SubCard"
            };
            var chartStack = new StackPanel();
            var chartHead = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var legend = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(legend, Dock.Right);
            legend.Children.Add(MakeLegendDot("CPU", "#38BDF8"));
            legend.Children.Add(new Border { Width = 8 });
            legend.Children.Add(MakeLegendDot("GPU", "#A78BFA"));
            legend.Children.Add(new Border { Width = 8 });
            legend.Children.Add(MakeLegendDot("TEMP", "#34D399"));
            chartHead.Children.Add(legend);
            chartHead.Children.Add(new TextBlock { Text = "60S TRACE", FontFamily = MonoFont, FontSize = 10.5, Tag = "Muted" });
            chartStack.Children.Add(chartHead);

            _chartCanvas = new Canvas { Height = 50, ClipToBounds = true };
            _cpuLine = new Polyline { StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _gpuLine = new Polyline { StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _tempLine = new Polyline { StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _chartCanvas.Children.Add(_cpuLine);
            _chartCanvas.Children.Add(_gpuLine);
            _chartCanvas.Children.Add(_tempLine);
            chartStack.Children.Add(_chartCanvas);
            chartCard.Child = chartStack;
            Grid.SetRow(chartCard, 0);
            _telemetryView.Children.Add(chartCard);

            // 2x2 Sensor Cards: CPU, GPU, MEM, DISK
            var sensorGrid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            sensorGrid.ColumnDefinitions.Add(new ColumnDefinition());
            sensorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
            sensorGrid.ColumnDefinitions.Add(new ColumnDefinition());
            sensorGrid.RowDefinitions.Add(new RowDefinition());
            sensorGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(7) });
            sensorGrid.RowDefinitions.Add(new RowDefinition());

            var cpuCard = MakeSensorCard("CPU", out _cpuDot, out _cpuMain, out _cpuSub);
            var gpuCard = MakeSensorCard("GPU", out _gpuDot, out _gpuMain, out _gpuSub);
            _memCard = MakeSensorCard("MEM", out _memDot, out _memMain, out _memSub);
            _memCard.RenderTransformOrigin = new Point(0.5, 0.5);
            _memCard.RenderTransform = new ScaleTransform(1.0, 1.0);
            var diskCard = MakeSensorCard("DISK", out _diskDot, out _diskMain, out _diskSub);

            Grid.SetRow(cpuCard, 0); Grid.SetColumn(cpuCard, 0);
            Grid.SetRow(gpuCard, 0); Grid.SetColumn(gpuCard, 2);
            Grid.SetRow(_memCard, 2); Grid.SetColumn(_memCard, 0);
            Grid.SetRow(diskCard, 2); Grid.SetColumn(diskCard, 2);

            sensorGrid.Children.Add(cpuCard);
            sensorGrid.Children.Add(gpuCard);
            sensorGrid.Children.Add(_memCard);
            sensorGrid.Children.Add(diskCard);
            Grid.SetRow(sensorGrid, 1);
            _telemetryView.Children.Add(sensorGrid);

            // Compact clickable NET / TRAFFIC summary strip
            _netSummaryCard = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 6, 9, 6),
                Margin = new Thickness(0, 0, 0, 9),
                Cursor = Cursors.Hand,
                ToolTip = "点击查看每日 / 每月全局与代理流量统计",
                Tag = "SubCard"
            };
            _netSummaryCard.MouseEnter += (s, e) =>
            {
                var pal = ThemeHelper.GetPalette(_config.Theme);
                _netSummaryCard.BorderBrush = pal.AccentPrimary;
            };
            _netSummaryCard.MouseLeave += (s, e) =>
            {
                var pal = ThemeHelper.GetPalette(_config.Theme);
                _netSummaryCard.BorderBrush = pal.SubSurfaceBorder;
            };
            _netSummaryCard.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                SetViewMode(PopoverViewMode.Traffic);
            };

            var netStack = new StackPanel();
            var netTop = new DockPanel();
            var netDetailsHint = new TextBlock
            {
                Text = "详情 ›",
                FontFamily = UiFont,
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Tag = "AccentText",
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(netDetailsHint, Dock.Right);
            netTop.Children.Add(netDetailsHint);

            var netTopLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            netTopLeft.Children.Add(new TextBlock
            {
                Text = "NET · 今日",
                FontFamily = MonoFont,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Tag = "Muted",
                Margin = new Thickness(0, 0, 8, 0)
            });
            _netSummarySpeed = new TextBlock
            {
                Text = "↓0.0K/s · ↑0.0K/s",
                FontFamily = MonoFont,
                FontSize = 9.5,
                Tag = "Muted"
            };
            Typography.SetNumeralAlignment(_netSummarySpeed, FontNumeralAlignment.Tabular);
            netTopLeft.Children.Add(_netSummarySpeed);
            netTop.Children.Add(netTopLeft);
            netStack.Children.Add(netTop);

            var netMid = new DockPanel { Margin = new Thickness(0, 3, 0, 4) };
            _netSummaryProxy = new TextBlock
            {
                Text = "代理 0B",
                FontFamily = MonoFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Tag = "PurpleText",
                VerticalAlignment = VerticalAlignment.Center
            };
            Typography.SetNumeralAlignment(_netSummaryProxy, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(_netSummaryProxy, Dock.Right);
            netMid.Children.Add(_netSummaryProxy);

            var netMidLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _netSummaryGlobal = new TextBlock
            {
                Text = "全局 0 B",
                FontFamily = MonoFont,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Typography.SetNumeralAlignment(_netSummaryGlobal, FontNumeralAlignment.Tabular);
            _netSummaryGlobalSub = new TextBlock
            {
                Text = " ↓0B ↑0B",
                FontFamily = MonoFont,
                FontSize = 9.5,
                Tag = "Muted",
                VerticalAlignment = VerticalAlignment.Center
            };
            Typography.SetNumeralAlignment(_netSummaryGlobalSub, FontNumeralAlignment.Tabular);
            netMidLeft.Children.Add(_netSummaryGlobal);
            netMidLeft.Children.Add(_netSummaryGlobalSub);
            netMid.Children.Add(netMidLeft);
            netStack.Children.Add(netMid);

            _netSummaryBarProxy = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
                CornerRadius = new CornerRadius(1.5)
            };
            _netSummaryBarTrack = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                ClipToBounds = true,
                Child = _netSummaryBarProxy
            };
            netStack.Children.Add(_netSummaryBarTrack);

            _netSummaryCard.Child = netStack;
            Grid.SetRow(_netSummaryCard, 2);
            _telemetryView.Children.Add(_netSummaryCard);

            // Processes (no "TOP 5" text!)
            var procSection = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 0),
                Tag = "Divider"
            };
            var procStack = new StackPanel();
            var procHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var procCols = new TextBlock { Text = "CPU / MEM / IO", FontFamily = MonoFont, FontSize = 10.5, Tag = "Muted" };
            DockPanel.SetDock(procCols, Dock.Right);
            procHeader.Children.Add(procCols);
            procHeader.Children.Add(new TextBlock { Text = "PROCESSES", FontFamily = MonoFont, FontSize = 10.5, Tag = "Muted" });
            procStack.Children.Add(procHeader);

            _processRows = new StackPanel();
            procStack.Children.Add(_processRows);
            procSection.Child = procStack;
            Grid.SetRow(procSection, 3);
            _telemetryView.Children.Add(procSection);
        }

        private void BuildTrafficView()
        {
            _trafficView = new StackPanel();
            _trafficOverview = new StackPanel();
            _applicationTrafficView = new StackPanel { Visibility = Visibility.Collapsed };

            var navigation = new DockPanel { Margin = new Thickness(0, 0, 0, 9), LastChildFill = false };
            var viewSelector = MakeSegmentedControl(new[]
            {
                Tuple.Create("总览", (object)false), Tuple.Create("按应用", (object)true)
            }, _trafficViewButtons, false, value =>
            {
                _showApplicationTraffic = (bool)value;
                _trafficOverview.Visibility = _showApplicationTraffic ? Visibility.Collapsed : Visibility.Visible;
                _applicationTrafficView.Visibility = _showApplicationTraffic ? Visibility.Visible : Visibility.Collapsed;
                _applicationPeriodSelector.Visibility = _applicationTrafficView.Visibility;
                var pal = ThemeHelper.GetPalette(_config.Theme);
                HighlightSelected(_trafficViewButtons, _showApplicationTraffic, pal);
                if (_trafficScroll != null) _trafficScroll.ScrollToTop();
                if (_lastSnap != null) UpdateTrafficView(_lastSnap, pal);
            });
            navigation.Children.Add(viewSelector);

            _applicationPeriodSelector = MakeSegmentedControl(new[]
            {
                Tuple.Create("今日", (object)false), Tuple.Create("本周期", (object)true)
            }, _applicationPeriodButtons, false, value =>
            {
                _applicationCycleMode = (bool)value;
                var pal = ThemeHelper.GetPalette(_config.Theme);
                HighlightSelected(_applicationPeriodButtons, _applicationCycleMode, pal);
                if (_lastSnap != null) UpdateTrafficView(_lastSnap, pal);
            });
            _applicationPeriodSelector.Visibility = Visibility.Collapsed;
            DockPanel.SetDock(_applicationPeriodSelector, Dock.Right);
            navigation.Children.Add(_applicationPeriodSelector);
            _trafficView.Children.Add(navigation);
            _trafficView.Children.Add(_trafficOverview);
            _trafficView.Children.Add(_applicationTrafficView);
            BuildApplicationTrafficView();

            // ── Section 1: TODAY ──
            var todayHeader = new DockPanel { Margin = new Thickness(3, 0, 3, 4) };
            _tfLiveSpeed = new TextBlock
            {
                Text = "↓ 0.0K/s · ↑ 0.0K/s",
                FontFamily = MonoFont,
                FontSize = 9.5,
                Tag = "Muted",
                VerticalAlignment = VerticalAlignment.Center
            };
            Typography.SetNumeralAlignment(_tfLiveSpeed, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(_tfLiveSpeed, Dock.Right);
            todayHeader.Children.Add(_tfLiveSpeed);

            _tfTodaySectionTitle = MakeSectionLabel("TODAY");
            _tfTodaySectionTitle.Margin = new Thickness(0);
            todayHeader.Children.Add(_tfTodaySectionTitle);
            _trafficOverview.Children.Add(todayHeader);

            _trafficOverview.Children.Add(BuildTrafficPeriodBlock(
                out _tfTodayGlobalMain, out _tfTodayGlobalSub,
                out _tfTodayProxyMain, out _tfTodayProxySub, out _tfTodayProxyBadgeText,
                out _tfTodayBarTrack, out _tfTodayBarProxy,
                out _tfTodayDirectText, out _tfTodayRatioText));

            // ── Section 2: MONTH / BILLING CYCLE ──
            var monthHeader = new DockPanel { Margin = new Thickness(3, 2, 3, 4) };
            _tfResetDayHint = new TextBlock
            {
                Text = "每月 1 日重置 ›",
                FontFamily = UiFont,
                FontSize = 10,
                Tag = "AccentText",
                Cursor = Cursors.Hand,
                ToolTip = "点击前往设置修改流量重置日",
                VerticalAlignment = VerticalAlignment.Center
            };
            _tfResetDayHint.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                SetViewMode(PopoverViewMode.Settings);
            };
            DockPanel.SetDock(_tfResetDayHint, Dock.Right);
            monthHeader.Children.Add(_tfResetDayHint);

            _tfMonthSectionTitle = MakeSectionLabel("MONTH");
            _tfMonthSectionTitle.Margin = new Thickness(0);
            monthHeader.Children.Add(_tfMonthSectionTitle);
            _trafficOverview.Children.Add(monthHeader);

            _trafficOverview.Children.Add(BuildTrafficPeriodBlock(
                out _tfMonthGlobalMain, out _tfMonthGlobalSub,
                out _tfMonthProxyMain, out _tfMonthProxySub, out _tfMonthProxyBadgeText,
                out _tfMonthBarTrack, out _tfMonthBarProxy,
                out _tfMonthDirectText, out _tfMonthRatioText));

            // ── Section 3: HISTORY CHART + LIST ──
            var histHead = new DockPanel { Margin = new Thickness(3, 2, 0, 5) };
            var segTrack = new Border
            {
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "SegTrack"
            };
            var segPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var segTpl = MakeSegmentButtonTemplate();
            var modeOpts = new[]
            {
                Tuple.Create("按天", (object)false),
                Tuple.Create("按月", (object)true)
            };
            foreach (var opt in modeOpts)
            {
                var btn = new Button
                {
                    Content = opt.Item1,
                    Tag = opt.Item2,
                    FontFamily = UiFont,
                    FontSize = 10,
                    Padding = new Thickness(8, 2, 8, 2),
                    Cursor = Cursors.Hand,
                    Focusable = false,
                    BorderThickness = new Thickness(1),
                    Template = segTpl
                };
                bool monthly = (bool)opt.Item2;
                btn.Click += (s, e) =>
                {
                    _trafficMonthlyMode = monthly;
                    var pal = ThemeHelper.GetPalette(_config.Theme);
                    HighlightSelected(_trafficHistoryButtons, _trafficMonthlyMode, pal);
                    if (_lastSnap != null) UpdateTrafficView(_lastSnap, pal);
                };
                _trafficHistoryButtons.Add(btn);
                segPanel.Children.Add(btn);
            }
            segTrack.Child = segPanel;
            DockPanel.SetDock(segTrack, Dock.Right);
            histHead.Children.Add(segTrack);

            var histLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var histTitle = MakeSectionLabel("HISTORY");
            histTitle.Margin = new Thickness(0, 0, 8, 0);
            histLeft.Children.Add(histTitle);
            histLeft.Children.Add(MakeLegendDot("代理", "#A78BFA"));
            histLeft.Children.Add(new Border { Width = 7 });
            histLeft.Children.Add(MakeLegendDot("直连", "#38BDF8"));
            histHead.Children.Add(histLeft);
            _trafficOverview.Children.Add(histHead);

            // Stacked bar chart card
            var chartCard = new Border
            {
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 7),
                Tag = "SubCard"
            };
            var chartCardStack = new StackPanel();
            _tfChartPeak = new TextBlock
            {
                Text = "峰值 0 B",
                FontFamily = MonoFont,
                FontSize = 9.5,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 2, 3),
                Tag = "Muted"
            };
            Typography.SetNumeralAlignment(_tfChartPeak, FontNumeralAlignment.Tabular);
            chartCardStack.Children.Add(_tfChartPeak);

            _tfChartBarsGrid = new Grid { Height = 48 };
            chartCardStack.Children.Add(_tfChartBarsGrid);

            _tfChartLabelsGrid = new Grid { Height = 15 };
            chartCardStack.Children.Add(_tfChartLabelsGrid);

            chartCard.Child = chartCardStack;
            _trafficOverview.Children.Add(chartCard);

            // Table column header
            var colHead = new Grid { Margin = new Thickness(6, 0, 12, 3) };
            colHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            colHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            colHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            colHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

            var c0 = new TextBlock { Text = "日期 / 周期", FontFamily = UiFont, FontSize = 9.5, Tag = "Muted" };
            var c1 = new TextBlock { Text = "全局", FontFamily = UiFont, FontSize = 9.5, TextAlignment = TextAlignment.Right, Tag = "Muted" };
            var c2 = new TextBlock { Text = "代理", FontFamily = UiFont, FontSize = 9.5, TextAlignment = TextAlignment.Right, Tag = "Muted" };
            var c3 = new TextBlock { Text = "占比", FontFamily = UiFont, FontSize = 9.5, TextAlignment = TextAlignment.Right, Tag = "Muted" };
            Grid.SetColumn(c0, 0); Grid.SetColumn(c1, 1); Grid.SetColumn(c2, 2); Grid.SetColumn(c3, 3);
            colHead.Children.Add(c0); colHead.Children.Add(c1); colHead.Children.Add(c2); colHead.Children.Add(c3);
            _trafficOverview.Children.Add(colHead);

            _tfHistoryRows = new StackPanel { Margin = new Thickness(0, 0, 2, 0) };
            var rowsScroll = new ScrollViewer
            {
                MaxHeight = 148,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _tfHistoryRows,
                Margin = new Thickness(0, 0, 0, 7)
            };
            ApplyThinScrollBarStyle(rowsScroll);
            _trafficOverview.Children.Add(rowsScroll);

            // ── Footer: ETW status + quick reset ──
            var footer = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(2, 7, 2, 0),
                Tag = "Divider"
            };
            var footerDp = new DockPanel();
            _tfBtnResetToday = MakeGhostIconButton(IconFactory.ResetSvg, "重置今日", OnFooterResetTodayClicked);
            DockPanel.SetDock(_tfBtnResetToday, Dock.Right);
            footerDp.Children.Add(_tfBtnResetToday);

            var statusLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _tfStatusDot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            _tfStatusText = new TextBlock
            {
                Text = "ETW 初始化中...",
                FontFamily = MonoFont,
                FontSize = 9.5,
                Tag = "Muted",
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 215,
                VerticalAlignment = VerticalAlignment.Center
            };
            statusLeft.Children.Add(_tfStatusDot);
            statusLeft.Children.Add(_tfStatusText);
            footerDp.Children.Add(statusLeft);

            footer.Child = footerDp;
            _trafficView.Children.Add(footer);
        }

        private void BuildApplicationTrafficView()
        {
            var header = MakeApplicationTrafficRow(true);
            string[] labels = { "应用 / 进程", "下载 ↓", "上传 ↑", "合计", "代理" };
            for (int i = 0; i < labels.Length; i++)
                ((TextBlock)header.Children[i]).Text = labels[i];
            ((TextBlock)header.Children[4]).ToolTip = "应用经本机代理端口的下载与上传合计，包含在本行合计内。";
            _applicationTrafficView.Children.Add(header);

            _applicationRows = new StackPanel();
            var scroll = new ScrollViewer
            {
                MaxHeight = 310,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _applicationRows,
                Margin = new Thickness(0, 0, 0, 7)
            };
            ApplyThinScrollBarStyle(scroll);
            _applicationTrafficView.Children.Add(scroll);
            _applicationEmpty = new TextBlock
            {
                Text = "暂无应用流量",
                FontFamily = UiFont,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 24, 0, 30),
                Tag = "Muted"
            };
            _applicationTrafficView.Children.Add(_applicationEmpty);
        }

        private void RenderApplicationTraffic(bool etwActive, ThemePalette pal)
        {
            var items = _getApplicationTraffic(_applicationCycleMode);
            _applicationEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _applicationEmpty.Text = etwActive ? "暂无应用流量" : "应用流量采集未启动";
            while (_applicationRows.Children.Count > items.Count)
                _applicationRows.Children.RemoveAt(_applicationRows.Children.Count - 1);

            for (int i = 0; i < items.Count; i++)
            {
                if (i == _applicationRows.Children.Count)
                    _applicationRows.Children.Add(MakeApplicationTrafficRow(false));
                var row = (Grid)_applicationRows.Children[i];
                var name = (TextBlock)row.Children[0];
                name.Text = items[i].Key;
                name.ToolTip = items[i].Key;
                var traffic = items[i].Value;
                ((TextBlock)row.Children[1]).Text = ThemeHelper.FormatDataSize(traffic.Received);
                ((TextBlock)row.Children[2]).Text = ThemeHelper.FormatDataSize(traffic.Sent);
                ((TextBlock)row.Children[3]).Text = ThemeHelper.FormatDataSize(traffic.Total);
                var proxy = (TextBlock)row.Children[4];
                proxy.Text = traffic.LocalProxyBytes.HasValue
                    ? ThemeHelper.FormatDataSize(traffic.LocalProxyBytes.Value)
                    : "—";
                proxy.ToolTip = traffic.LocalProxyBytes.HasValue ? null : "旧记录无代理明细";
                row.Background = i % 2 == 0 ? pal.SubSurfaceBackground : Brushes.Transparent;
                ApplyPaletteToTree(row, pal);
            }
        }

        private static Grid MakeApplicationTrafficRow(bool header)
        {
            var row = new Grid
            {
                Height = header ? 23 : 24,
                Margin = new Thickness(0, 0, header ? 5 : 0, 0)
            };
            for (int i = 0; i < 5; i++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = i == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(58)
                });
                var cell = new TextBlock
                {
                    FontFamily = i == 0 || header ? UiFont : MonoFont,
                    FontSize = header ? 10 : 11,
                    FontWeight = i == 3 && !header ? FontWeights.SemiBold : FontWeights.Normal,
                    Margin = i == 0 ? new Thickness(7, 0, 5, 0) : new Thickness(0, 0, 7, 0),
                    TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Right,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = header ? "Muted" : null
                };
                Typography.SetNumeralAlignment(cell, FontNumeralAlignment.Tabular);
                Grid.SetColumn(cell, i);
                row.Children.Add(cell);
            }
            return row;
        }

        private StackPanel BuildTrafficPeriodBlock(
            out TextBlock globalMain, out TextBlock globalSub,
            out TextBlock proxyMain, out TextBlock proxySub, out TextBlock proxyBadgeText,
            out Border barTrack, out Border barProxy,
            out TextBlock directText, out TextBlock ratioText)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            var cardsGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition());

            // Global card
            var gCard = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 7, 9, 7),
                Tag = "SubCard"
            };
            var gStack = new StackPanel();
            var gHead = new DockPanel();
            var gDot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Tag = "DirectDot" };
            DockPanel.SetDock(gDot, Dock.Right);
            gHead.Children.Add(gDot);
            gHead.Children.Add(new TextBlock { Text = "全局 GLOBAL", FontFamily = MonoFont, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Tag = "Muted" });
            gStack.Children.Add(gHead);

            globalMain = new TextBlock { Text = "0 B", FontFamily = MonoFont, FontSize = 13.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 1) };
            Typography.SetNumeralAlignment(globalMain, FontNumeralAlignment.Tabular);
            globalSub = new TextBlock { Text = "↓ 0B  ↑ 0B", FontFamily = MonoFont, FontSize = 9.5, Tag = "Muted" };
            Typography.SetNumeralAlignment(globalSub, FontNumeralAlignment.Tabular);
            gStack.Children.Add(globalMain);
            gStack.Children.Add(globalSub);
            gCard.Child = gStack;

            // Proxy card
            var pCard = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 7, 9, 7),
                Tag = "SubCard"
            };
            var pStack = new StackPanel();
            var pHead = new DockPanel();
            proxyBadgeText = new TextBlock
            {
                Text = "0.0%",
                FontFamily = MonoFont,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Tag = "PurpleText"
            };
            Typography.SetNumeralAlignment(proxyBadgeText, FontNumeralAlignment.Tabular);
            var pBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                Tag = "ProxyBadge",
                Child = proxyBadgeText
            };
            DockPanel.SetDock(pBadge, Dock.Right);
            pHead.Children.Add(pBadge);
            pHead.Children.Add(new TextBlock { Text = "代理 PROXY", FontFamily = MonoFont, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Tag = "Muted", VerticalAlignment = VerticalAlignment.Center });
            pStack.Children.Add(pHead);

            proxyMain = new TextBlock { Text = "0 B", FontFamily = MonoFont, FontSize = 13.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 1) };
            Typography.SetNumeralAlignment(proxyMain, FontNumeralAlignment.Tabular);
            proxySub = new TextBlock { Text = "↓ 0B  ↑ 0B", FontFamily = MonoFont, FontSize = 9.5, Tag = "Muted" };
            Typography.SetNumeralAlignment(proxySub, FontNumeralAlignment.Tabular);
            pStack.Children.Add(proxyMain);
            pStack.Children.Add(proxySub);
            pCard.Child = pStack;

            Grid.SetColumn(gCard, 0);
            Grid.SetColumn(pCard, 2);
            cardsGrid.Children.Add(gCard);
            cardsGrid.Children.Add(pCard);
            container.Children.Add(cardsGrid);

            barProxy = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
                CornerRadius = new CornerRadius(2)
            };
            barTrack = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                ClipToBounds = true,
                Margin = new Thickness(1, 0, 1, 3),
                Child = barProxy
            };
            container.Children.Add(barTrack);

            var caption = new DockPanel { Margin = new Thickness(2, 0, 2, 0) };
            ratioText = new TextBlock { Text = "代理 0.0%", FontFamily = MonoFont, FontSize = 9.5, Tag = "PurpleText" };
            Typography.SetNumeralAlignment(ratioText, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(ratioText, Dock.Right);
            caption.Children.Add(ratioText);

            directText = new TextBlock { Text = "直连 0 B (0.0%)", FontFamily = MonoFont, FontSize = 9.5, Tag = "Muted" };
            Typography.SetNumeralAlignment(directText, FontNumeralAlignment.Tabular);
            caption.Children.Add(directText);
            container.Children.Add(caption);

            return container;
        }

        private void OnFooterResetTodayClicked()
        {
            if (!_confirmResetTodayFooter)
            {
                _confirmResetTodayFooter = true;
                SetGhostButtonLabel(_tfBtnResetToday, "确认重置?");
                RestartConfirmResetTimer();
                return;
            }
            ClearPendingConfirmStates();
            if (ResetTodayTrafficRequested != null) ResetTodayTrafficRequested();
        }

        private void RestartConfirmResetTimer()
        {
            if (_confirmResetTimer == null)
            {
                _confirmResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _confirmResetTimer.Tick += (s, e) =>
                {
                    _confirmResetTimer.Stop();
                    ClearPendingConfirmStates();
                };
            }
            _confirmResetTimer.Stop();
            _confirmResetTimer.Start();
        }

        private void ClearPendingConfirmStates()
        {
            _confirmResetTodayFooter = false;
            _confirmCfgResetToday = false;
            _confirmCfgClearAll = false;
            SetGhostButtonLabel(_tfBtnResetToday, "重置今日");
            SetGhostButtonLabel(_btnCfgResetToday, "重置今日");
            SetGhostButtonLabel(_btnCfgClearAll, "清空全部");
        }

        private static void SetGhostButtonLabel(Button btn, string text)
        {
            if (btn == null) return;
            var sp = btn.Content as StackPanel;
            if (sp == null) return;
            for (int i = 0; i < sp.Children.Count; i++)
            {
                var tb = sp.Children[i] as TextBlock;
                if (tb != null)
                {
                    tb.Text = text;
                    break;
                }
            }
        }

        private void BuildSettingsView()
        {
            _settingsView = new StackPanel();

            // ── Group 1: APPEARANCE ──
            _settingsView.Children.Add(MakeSectionLabel("APPEARANCE"));

            var skinRow = MakeSegmentedRow(IconFactory.SkinSvg, "皮肤", new[]
            {
                Tuple.Create("黑白", (object)SkinStyle.Hud),
                Tuple.Create("Fluent", (object)SkinStyle.Fluent),
                Tuple.Create("色条", (object)SkinStyle.Lite),
                Tuple.Create("极简", (object)SkinStyle.Minimal)
            }, _skinButtons, false, val =>
            {
                _config.Skin = (SkinStyle)val;
                _config.Save();
                if (ConfigChanged != null) ConfigChanged();
                SyncSettingsControls();
            });

            var themeRow = MakeSegmentedRow(IconFactory.ThemeSvg, "主题", new[]
            {
                Tuple.Create("自动", (object)ThemeMode.Auto),
                Tuple.Create("深色", (object)ThemeMode.Dark),
                Tuple.Create("浅色", (object)ThemeMode.Light)
            }, _themeButtons, false, val =>
            {
                _config.Theme = (ThemeMode)val;
                _config.Save();
                ApplyThemeColors();
                if (ConfigChanged != null) ConfigChanged();
                SyncSettingsControls();
            });

            var dockRow = MakeSegmentedRow(IconFactory.DockSvg, "停靠", new[]
            {
                Tuple.Create("左侧", (object)TaskbarDockMode.Left),
                Tuple.Create("右侧", (object)TaskbarDockMode.Right)
            }, _dockButtons, false, val =>
            {
                _config.TaskbarDock = (TaskbarDockMode)val;
                _config.Save();
                if (ConfigChanged != null) ConfigChanged();
                RepositionBottomAligned();
                SyncSettingsControls();
            });

            _lblOffset = new TextBlock
            {
                FontFamily = MonoFont,
                FontSize = 10.5,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "OffsetVal"
            };
            Typography.SetNumeralAlignment(_lblOffset, FontNumeralAlignment.Tabular);
            _offsetBadge = new Border
            {
                Width = 42,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2, 1, 2, 1),
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _lblOffset
            };
            _offsetBadge.MouseLeftButtonUp += (s, e) =>
            {
                if (_config.TaskbarOffsetX != 0)
                {
                    e.Handled = true;
                    _sliderOffset.Value = 0;
                }
            };
            _sliderOffset = MakeMinimalSlider(-200, 200, 5, 114, showFillBar: false, showCenterTick: true);
            _sliderOffset.ValueChanged += (s, e) =>
            {
                if (!IsLoaded || _suppressSwitchEvents) return;
                _config.TaskbarOffsetX = (int)_sliderOffset.Value;
                UpdateOffsetReadout();
                _config.Save();
                if (ConfigChanged != null) ConfigChanged();
            };
            var offsetRow = MakeSliderRow(IconFactory.OffsetSvg, "偏移", _sliderOffset, _offsetBadge);

            _settingsView.Children.Add(MakeGroupCard(skinRow, themeRow, dockRow, offsetRow));

            // ── Group 2: OVERLAY ──
            _settingsView.Children.Add(MakeSectionLabel("OVERLAY"));

            _chkDeskEnabled = MakeSwitch();
            _chkDeskEnabled.Checked += (s, e) => OnDeskEnabledToggled(true);
            _chkDeskEnabled.Unchecked += (s, e) => OnDeskEnabledToggled(false);
            _btnResetCorner = MakeGhostIconButton(IconFactory.ResetSvg, "归位", () =>
            {
                if (ResetDesktopCornerRequested != null) ResetDesktopCornerRequested();
            });
            _btnResetCorner.Margin = new Thickness(0, 0, 8, 0);
            var deskRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            deskRight.Children.Add(_btnResetCorner);
            deskRight.Children.Add(_chkDeskEnabled);
            var deskRow = MakeToggleRow(IconFactory.DesktopOverlaySvg, "桌面角标", deskRight, _chkDeskEnabled);

            _chkDeskLocked = MakeSwitch();
            _chkDeskLocked.Checked += (s, e) => OnDeskLockToggled(true);
            _chkDeskLocked.Unchecked += (s, e) => OnDeskLockToggled(false);
            _deskLockRow = MakeToggleRow(IconFactory.LockSvg, "锁定穿透", _chkDeskLocked, _chkDeskLocked);

            _lblOpacity = new TextBlock
            {
                FontFamily = MonoFont,
                FontSize = 10.5,
                Width = 42,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 0, 0),
                Tag = "Muted"
            };
            Typography.SetNumeralAlignment(_lblOpacity, FontNumeralAlignment.Tabular);
            _sliderOpacity = MakeMinimalSlider(35, 100, 1, 114, showFillBar: true, showCenterTick: false);
            _sliderOpacity.ValueChanged += (s, e) =>
            {
                if (!IsLoaded || _suppressSwitchEvents) return;
                _config.DesktopOpacity = Math.Round(_sliderOpacity.Value / 100.0, 2);
                _lblOpacity.Text = string.Format("{0:0}%", _sliderOpacity.Value);
                _config.Save();
                if (DesktopLockChanged != null) DesktopLockChanged();
            };
            _deskOpacityRow = MakeSliderRow(IconFactory.OpacitySvg, "透明度", _sliderOpacity, _lblOpacity);

            _settingsView.Children.Add(MakeGroupCard(deskRow, _deskLockRow, _deskOpacityRow));

            // ── Group 3: SYSTEM ──
            _settingsView.Children.Add(MakeSectionLabel("SYSTEM"));

            var rateRow = MakeSegmentedRow(IconFactory.RateSvg, "采样", new[]
            {
                Tuple.Create("0.5s", (object)500),
                Tuple.Create("1.0s", (object)1000),
                Tuple.Create("2.0s", (object)2000)
            }, _rateButtons, true, val =>
            {
                _config.PollIntervalMs = (int)val;
                _config.Save();
                if (PollRateChanged != null) PollRateChanged();
                SyncSettingsControls();
            });

            var tempRow = MakeSegmentedRow(IconFactory.TempSvg, "预警", new[]
            {
                Tuple.Create("75°C", (object)75),
                Tuple.Create("80°C", (object)80),
                Tuple.Create("85°C", (object)85)
            }, _tempButtons, true, val =>
            {
                _config.TempWarnThreshold = (int)val;
                _config.Save();
                if (ConfigChanged != null) ConfigChanged();
                SyncSettingsControls();
            });

            _chkAutoStart = MakeSwitch();
            _chkAutoStart.Checked += (s, e) => OnAutoStartToggled(true);
            _chkAutoStart.Unchecked += (s, e) => OnAutoStartToggled(false);
            var autoStartRow = MakeToggleRow(IconFactory.AutoStartSvg, "开机自启", _chkAutoStart, _chkAutoStart);

            _chkFullscreenHide = MakeSwitch();
            _chkFullscreenHide.Checked += (s, e) =>
            {
                if (_suppressSwitchEvents) return;
                _config.FullscreenAutoHide = true;
                _config.Save();
            };
            _chkFullscreenHide.Unchecked += (s, e) =>
            {
                if (_suppressSwitchEvents) return;
                _config.FullscreenAutoHide = false;
                _config.Save();
            };
            var fullscreenRow = MakeToggleRow(IconFactory.FullscreenHideSvg, "全屏隐藏", _chkFullscreenHide, _chkFullscreenHide);

            _settingsView.Children.Add(MakeGroupCard(rateRow, tempRow, autoStartRow, fullscreenRow));

            // ── Group 4: TRAFFIC ──
            _settingsView.Children.Add(MakeSectionLabel("TRAFFIC"));

            var stepTrack = new Border
            {
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "SegTrack"
            };
            var stepPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var stepTpl = MakeSegmentButtonTemplate();
            var btnDayDec = new Button
            {
                Content = "−",
                FontFamily = MonoFont,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Width = 22,
                Padding = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                Focusable = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Template = stepTpl,
                Tag = "StepBtn"
            };
            btnDayDec.Click += (s, e) => AdjustTrafficResetDay(-1);

            _lblTrafficResetDay = new TextBlock
            {
                Text = "每月 1 日",
                FontFamily = MonoFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Width = 68,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Typography.SetNumeralAlignment(_lblTrafficResetDay, FontNumeralAlignment.Tabular);

            var btnDayInc = new Button
            {
                Content = "+",
                FontFamily = MonoFont,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Width = 22,
                Padding = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                Focusable = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Template = stepTpl,
                Tag = "StepBtn"
            };
            btnDayInc.Click += (s, e) => AdjustTrafficResetDay(1);

            stepPanel.Children.Add(btnDayDec);
            stepPanel.Children.Add(_lblTrafficResetDay);
            stepPanel.Children.Add(btnDayInc);
            stepTrack.Child = stepPanel;
            var resetDayRow = MakeRowShell(IconFactory.CalendarSvg, "流量重置日", stepTrack);
            resetDayRow.ToolTip = "设置每月统计周期的起始日期。";

            // Proxy process names row (header + inline textbox)
            var proxyRow = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(10, 6, 10, 7),
                SnapsToDevicePixels = true
            };
            proxyRow.MouseEnter += (s, e) => { proxyRow.Background = _rowHoverBrush; };
            proxyRow.MouseLeave += (s, e) => { proxyRow.Background = Brushes.Transparent; };

            var proxyStack = new StackPanel();
            var proxyTop = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var btnDefaultProxy = MakeGhostIconButton(IconFactory.ResetSvg, "默认", () =>
            {
                _config.ProxyProcessNames = AppConfig.DefaultProxyProcessNames;
                _config.Save();
                if (_txtProxyProcesses != null) _txtProxyProcesses.Text = _config.ProxyProcessNames;
                if (TrafficConfigChanged != null) TrafficConfigChanged();
            });
            DockPanel.SetDock(btnDefaultProxy, Dock.Right);
            proxyTop.Children.Add(btnDefaultProxy);

            var proxyLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var proxyIcon = IconFactory.CreateVectorIcon(IconFactory.ProxySvg, 13, Brushes.Gray, 1.6);
            proxyIcon.Tag = "RowIcon";
            proxyIcon.Opacity = 0.85;
            proxyIcon.Margin = new Thickness(0, 0, 8, 0);
            proxyIcon.VerticalAlignment = VerticalAlignment.Center;
            proxyLeft.Children.Add(proxyIcon);
            proxyLeft.Children.Add(new TextBlock
            {
                Text = "代理进程",
                FontFamily = UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            proxyTop.Children.Add(proxyLeft);
            proxyStack.Children.Add(proxyTop);

            _txtProxyProcesses = new TextBox
            {
                Text = _config.ProxyProcessNames ?? AppConfig.DefaultProxyProcessNames,
                FontFamily = MonoFont,
                FontSize = 10.5,
                Padding = new Thickness(6, 4, 6, 4),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                ToolTip = "填代理核心的进程名，如 sing-box。多个名称用逗号分隔，留空关闭总览代理统计，按回车保存。",
                Tag = "ConfigInput"
            };
            _txtProxyProcesses.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitProxyProcessNames();
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
            };
            _txtProxyProcesses.LostFocus += (s, e) => CommitProxyProcessNames();
            var proxyInputShell = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                SnapsToDevicePixels = true,
                Tag = "InputShell",
                Child = _txtProxyProcesses
            };
            proxyStack.Children.Add(proxyInputShell);
            proxyStack.Children.Add(new TextBlock
            {
                Text = "额外本机代理端口（选填）",
                FontFamily = UiFont,
                FontSize = 10.5,
                Margin = new Thickness(0, 8, 0, 4),
                Tag = "Muted"
            });
            _txtLocalProxyPorts = new TextBox
            {
                Text = _config.LocalProxyPorts ?? string.Empty,
                FontFamily = MonoFont,
                FontSize = 10.5,
                Padding = new Thickness(6, 4, 6, 4),
                BorderThickness = new Thickness(0),
                ToolTip = "自动读取 Windows 保存的本机代理端口。应用单独指定的 HTTP 或 SOCKS 端口可填在这里，例如 10808。",
                Tag = "ConfigInput"
            };
            _txtLocalProxyPorts.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                CommitLocalProxyPorts();
                Keyboard.ClearFocus();
                e.Handled = true;
            };
            _txtLocalProxyPorts.LostFocus += (s, e) => CommitLocalProxyPorts();
            proxyStack.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Tag = "InputShell",
                Child = _txtLocalProxyPorts
            });
            proxyRow.Child = proxyStack;

            // Data reset actions row
            var dataBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _btnCfgResetToday = MakeGhostIconButton(IconFactory.ResetSvg, "重置今日", () =>
            {
                if (!_confirmCfgResetToday)
                {
                    ClearPendingConfirmStates();
                    _confirmCfgResetToday = true;
                    SetGhostButtonLabel(_btnCfgResetToday, "确认重置?");
                    RestartConfirmResetTimer();
                    return;
                }
                ClearPendingConfirmStates();
                if (ResetTodayTrafficRequested != null) ResetTodayTrafficRequested();
            });
            _btnCfgResetToday.Margin = new Thickness(0, 0, 6, 0);

            _btnCfgClearAll = MakeGhostIconButton(IconFactory.TrashSvg, "清空全部", () =>
            {
                if (!_confirmCfgClearAll)
                {
                    ClearPendingConfirmStates();
                    _confirmCfgClearAll = true;
                    SetGhostButtonLabel(_btnCfgClearAll, "确认清空?");
                    RestartConfirmResetTimer();
                    return;
                }
                ClearPendingConfirmStates();
                if (ClearAllTrafficRequested != null) ClearAllTrafficRequested();
            });
            dataBtnPanel.Children.Add(_btnCfgResetToday);
            dataBtnPanel.Children.Add(_btnCfgClearAll);
            var trafficDataRow = MakeRowShell(IconFactory.TrashSvg, "统计记录", dataBtnPanel);

            _settingsView.Children.Add(MakeGroupCard(resetDayRow, proxyRow, trafficDataRow));

            // ── Footer: Minimal Keycap Hints ──
            var hkFooter = new DockPanel { Margin = new Thickness(3, 1, 3, 0) };
            var hkRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(hkRight, Dock.Right);
            hkRight.Children.Add(MakeKeycapPair("Ctrl+Alt+L", "穿透"));
            hkRight.Children.Add(new Border { Width = 10 });
            hkRight.Children.Add(MakeKeycapPair("Ctrl+Alt+M", "面板"));
            hkFooter.Children.Add(hkRight);

            var hkTitle = new TextBlock
            {
                Text = "HOTKEYS",
                FontFamily = MonoFont,
                FontSize = 9.5,
                Tag = "Muted",
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center
            };
            hkFooter.Children.Add(hkTitle);
            _settingsView.Children.Add(hkFooter);
        }

        private bool _suppressSwitchEvents;

        private void SyncSettingsControls()
        {
            _suppressSwitchEvents = true;
            try
            {
                _chkAutoStart.IsChecked = AutoStartService.IsEnabled();
                _chkDeskEnabled.IsChecked = _config.DesktopOverlayEnabled;
                _chkDeskLocked.IsChecked = _config.DesktopLocked;
                _chkFullscreenHide.IsChecked = _config.FullscreenAutoHide;
                _sliderOpacity.Value = Math.Round(_config.DesktopOpacity * 100.0);
                _lblOpacity.Text = string.Format("{0:0}%", _sliderOpacity.Value);
                _sliderOffset.Value = _config.TaskbarOffsetX;

                var pal = ThemeHelper.GetPalette(_config.Theme);
                UpdateOffsetReadout();
                UpdateOverlayRowState();
                if (_lblTrafficResetDay != null)
                    _lblTrafficResetDay.Text = string.Format("每月 {0} 日", _config.TrafficResetDay);
                if (_txtProxyProcesses != null && !_txtProxyProcesses.IsKeyboardFocusWithin)
                    _txtProxyProcesses.Text = _config.ProxyProcessNames ?? AppConfig.DefaultProxyProcessNames;
                if (_txtLocalProxyPorts != null && !_txtLocalProxyPorts.IsKeyboardFocusWithin)
                    _txtLocalProxyPorts.Text = _config.LocalProxyPorts ?? string.Empty;
                HighlightSelected(_skinButtons, _config.Skin, pal);
                HighlightSelected(_themeButtons, _config.Theme, pal);
                HighlightSelected(_rateButtons, _config.PollIntervalMs, pal);
                HighlightSelected(_tempButtons, _config.TempWarnThreshold, pal);
                HighlightSelected(_dockButtons, _config.TaskbarDock, pal);
                HighlightSelected(_trafficHistoryButtons, _trafficMonthlyMode, pal);
            }
            finally { _suppressSwitchEvents = false; }
        }

        private void AdjustTrafficResetDay(int delta)
        {
            int next = _config.TrafficResetDay + delta;
            if (next < 1) next = 31;
            else if (next > 31) next = 1;
            _config.TrafficResetDay = next;
            _config.Save();
            if (_lblTrafficResetDay != null)
                _lblTrafficResetDay.Text = string.Format("每月 {0} 日", _config.TrafficResetDay);
            if (TrafficConfigChanged != null) TrafficConfigChanged();
        }

        private void CommitProxyProcessNames()
        {
            if (_activeView != PopoverViewMode.Settings || _suppressSwitchEvents) return;
            string cleaned = (_txtProxyProcesses.Text ?? string.Empty).Trim();
            if (string.Equals(cleaned, _config.ProxyProcessNames, StringComparison.Ordinal)) return;
            _config.ProxyProcessNames = cleaned;
            _config.Save();
            if (TrafficConfigChanged != null) TrafficConfigChanged();
        }

        private void CommitLocalProxyPorts()
        {
            if (_activeView != PopoverViewMode.Settings || _suppressSwitchEvents) return;
            string ports = _txtLocalProxyPorts.Text.Trim();
            if (ports == (_config.LocalProxyPorts ?? string.Empty)) return;
            _config.LocalProxyPorts = ports;
            _config.Save();
            if (TrafficConfigChanged != null) TrafficConfigChanged();
        }

        private void UpdateOffsetReadout()
        {
            if (_lblOffset == null || _offsetBadge == null) return;
            _lblOffset.Text = _config.TaskbarOffsetX == 0
                ? "0px"
                : string.Format("{0:+0;-0;0}px", _config.TaskbarOffsetX);
            bool active = _config.TaskbarOffsetX != 0;
            _lblOffset.Foreground = active ? _accentPrimaryBrush : _mutedTextBrush;
            _offsetBadge.Cursor = active ? Cursors.Hand : Cursors.Arrow;
        }

        private void UpdateOverlayRowState()
        {
            double stateOpacity = _config.DesktopOverlayEnabled ? 1.0 : 0.45;
            if (_deskLockRow != null) _deskLockRow.Opacity = stateOpacity;
            if (_deskOpacityRow != null) _deskOpacityRow.Opacity = stateOpacity;
            if (_btnResetCorner != null) _btnResetCorner.Opacity = stateOpacity;
        }

        private void OnAutoStartToggled(bool enable)
        {
            if (_suppressSwitchEvents) return;
            string msg;
            AutoStartService.SetEnabled(enable, out msg);
        }

        private void OnDeskEnabledToggled(bool enable)
        {
            if (_suppressSwitchEvents) return;
            _config.DesktopOverlayEnabled = enable;
            _config.Save();
            if (DesktopOverlayToggled != null) DesktopOverlayToggled();
            ApplyThemeColors();
            UpdateOverlayRowState();
        }

        private void OnDeskLockToggled(bool locked)
        {
            if (_suppressSwitchEvents) return;
            _config.DesktopLocked = locked;
            _config.Save();
            if (DesktopLockChanged != null) DesktopLockChanged();
        }

        public void SetSettingsMode(bool open)
        {
            SetViewMode(open ? PopoverViewMode.Settings : PopoverViewMode.Telemetry);
        }

        public void SetViewMode(PopoverViewMode mode)
        {
            if (_activeView == PopoverViewMode.Settings && mode != PopoverViewMode.Settings)
            {
                CommitProxyProcessNames();
                CommitLocalProxyPorts();
            }

            ClearPendingConfirmStates();
            _activeView = mode;
            _telemetryView.Visibility = mode == PopoverViewMode.Telemetry ? Visibility.Visible : Visibility.Collapsed;
            _trafficScroll.Visibility = mode == PopoverViewMode.Traffic ? Visibility.Visible : Visibility.Collapsed;
            _settingsScroll.Visibility = mode == PopoverViewMode.Settings ? Visibility.Visible : Visibility.Collapsed;

            if (mode == PopoverViewMode.Settings) _titleText.Text = "SYS.CONFIG";
            else if (mode == PopoverViewMode.Traffic) _titleText.Text = "SYS.TRAFFIC";
            else _titleText.Text = "SYS.TELEMETRY";

            if (_btnTraffic != null)
                _btnTraffic.ToolTip = mode == PopoverViewMode.Traffic ? "返回监控视图" : "流量统计 (SYS.TRAFFIC)";
            if (_btnGear != null)
                _btnGear.ToolTip = mode == PopoverViewMode.Settings ? "返回监控视图" : "设置 (SYS.CONFIG)";

            ApplyThemeColors();
            if (mode == PopoverViewMode.Settings) SyncSettingsControls();
            else if (_lastSnap != null) UpdateMetrics(_lastSnap);

            if (IsVisible)
            {
                UpdateLayout();
                RepositionBottomAligned();
            }
        }

        public void ApplyThemeColors()
        {
            var pal = ThemeHelper.GetPalette(_config.Theme);
            _rowHoverBrush = pal.RowHoverBackground;
            _accentPrimaryBrush = pal.AccentPrimary;
            _mutedTextBrush = pal.TaskbarTextMuted;

            _shellBorder.Background = pal.PopoverBackground;
            _shellBorder.BorderBrush = pal.PopoverBorder;
            _statusDot.Fill = pal.StatusOk;
            _titleText.Foreground = pal.TaskbarTextPrimary;

            _cpuLine.Stroke = pal.AccentBlue;
            _gpuLine.Stroke = pal.AccentPurple;
            _tempLine.Stroke = pal.AccentGreen;

            StyleHeaderButton(_btnDesktopToggle, pal, _config.DesktopOverlayEnabled);
            StyleHeaderButton(_btnTrim, pal, false);
            StyleHeaderButton(_btnTraffic, pal, _activeView == PopoverViewMode.Traffic);
            StyleHeaderButton(_btnGear, pal, _activeView == PopoverViewMode.Settings);
            StyleHeaderButton(_btnClose, pal, false, borderless: true);

            ApplyPaletteToTree(_telemetryView, pal);
            ApplyPaletteToTree(_trafficView, pal);
            ApplyPaletteToTree(_settingsView, pal);

            HighlightSelected(_skinButtons, _config.Skin, pal);
            HighlightSelected(_themeButtons, _config.Theme, pal);
            HighlightSelected(_rateButtons, _config.PollIntervalMs, pal);
            HighlightSelected(_tempButtons, _config.TempWarnThreshold, pal);
            HighlightSelected(_dockButtons, _config.TaskbarDock, pal);
            HighlightSelected(_trafficHistoryButtons, _trafficMonthlyMode, pal);
            HighlightSelected(_trafficViewButtons, _showApplicationTraffic, pal);
            HighlightSelected(_applicationPeriodButtons, _applicationCycleMode, pal);
            UpdateOffsetReadout();

            if (_lastSnap != null)
            {
                if (_activeView == PopoverViewMode.Telemetry)
                    UpdateNetSummaryStrip(_lastSnap, pal);
                else if (_activeView == PopoverViewMode.Traffic)
                    UpdateTrafficView(_lastSnap, pal);
            }
        }

        public void PlayTrimAnimation()
        {
            if (!CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(PlayTrimAnimation));
                return;
            }

            var pal = ThemeHelper.GetPalette(_config.Theme);
            Color baseBg = GetSolidColor(pal.SubSurfaceBackground, Color.FromArgb(20, 255, 255, 255));
            Color baseBorder = GetSolidColor(pal.SubSurfaceBorder, Color.FromArgb(40, 255, 255, 255));
            Color baseFg = GetSolidColor(pal.TaskbarTextPrimary, Colors.White);

            Color emerald = Color.FromRgb(52, 211, 153);
            Color emeraldFill = Color.FromArgb(64, 16, 185, 129);
            Color emeraldCardFill = Color.FromArgb(36, 16, 185, 129);

            if (_btnTrim != null)
            {
                AnimateScalePulse(_btnTrim.RenderTransform as ScaleTransform, 1.22, 85, 320);
                FlashColor(_btnTrim, Border.BackgroundProperty, emeraldFill, baseBg, 150, 520);
                FlashColor(_btnTrim, Border.BorderBrushProperty, emerald, baseBorder, 150, 520);

                var path = _btnTrim.Child as Path;
                if (path != null)
                {
                    FlashColor(path, Shape.StrokeProperty, emerald, baseFg, 150, 520);
                    var tg = path.RenderTransform as TransformGroup;
                    if (tg != null && tg.Children.Count >= 2)
                    {
                        AnimateScalePulse(tg.Children[0] as ScaleTransform, 1.28, 85, 300);
                        var rt = tg.Children[1] as RotateTransform;
                        if (rt != null)
                        {
                            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
                            var rot = new DoubleAnimationUsingKeyFrames();
                            rot.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                            rot.KeyFrames.Add(new EasingDoubleKeyFrame(-16, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75)), easeOut));
                            rot.KeyFrames.Add(new EasingDoubleKeyFrame(9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(175)), easeOut));
                            rot.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), easeOut));
                            rt.BeginAnimation(RotateTransform.AngleProperty, rot);
                        }
                    }
                }
            }

            if (_memCard != null)
            {
                AnimateScalePulse(_memCard.RenderTransform as ScaleTransform, 1.022, 95, 260);
                FlashColor(_memCard, Border.BackgroundProperty, emeraldCardFill, baseBg, 140, 540);
                FlashColor(_memCard, Border.BorderBrushProperty, emerald, baseBorder, 140, 540);
            }
        }

        private static Color GetSolidColor(Brush brush, Color fallback)
        {
            var sb = brush as SolidColorBrush;
            return sb != null ? sb.Color : fallback;
        }

        private static void FlashColor(DependencyObject target, DependencyProperty prop, Color flash, Color rest, int holdMs, int fadeMs)
        {
            var brush = new SolidColorBrush(flash);
            target.SetValue(prop, brush);
            var anim = new ColorAnimation(flash, rest, TimeSpan.FromMilliseconds(fadeMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(holdMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }

        private static void AnimateScalePulse(ScaleTransform st, double peak, int upMs, int downMs)
        {
            if (st == null) return;
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            var ax = new DoubleAnimationUsingKeyFrames();
            ax.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            ax.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(upMs)), easeOut));
            ax.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(upMs + downMs)), easeOut));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, ax.Clone());
        }

        private static void StyleHeaderButton(Border btn, ThemePalette pal, bool active, bool borderless = false)
        {
            btn.Background = active ? pal.HeaderActiveBackground : (borderless ? Brushes.Transparent : pal.SubSurfaceBackground);
            btn.BorderBrush = active ? pal.AccentPrimary : (borderless ? Brushes.Transparent : pal.SubSurfaceBorder);
            var path = btn.Child as Path;
            if (path != null)
                path.Stroke = active ? pal.AccentPrimary : pal.TaskbarTextPrimary;
        }

        private static void ApplyPaletteToTree(DependencyObject root, ThemePalette pal)
        {
            if (root == null) return;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var b = child as Border;
                var tb = child as TextBlock;
                var path = child as Path;
                var button = child as Button;
                var slider = child as Slider;
                var chk = child as CheckBox;
                var txt = child as TextBox;
                var ell = child as Ellipse;
                if (b != null)
                {
                    if (Equals(b.Tag, "SubCard"))
                    {
                        b.Background = pal.SubSurfaceBackground;
                        b.BorderBrush = pal.SubSurfaceBorder;
                    }
                    else if (Equals(b.Tag, "GroupCard"))
                    {
                        b.Background = pal.GroupCardBackground;
                        b.BorderBrush = pal.GroupCardBorder;
                    }
                    else if (Equals(b.Tag, "Hairline"))
                    {
                        b.Background = pal.HairlineBrush;
                    }
                    else if (Equals(b.Tag, "SegTrack"))
                    {
                        b.Background = pal.SegmentedTrackBackground;
                        b.BorderBrush = pal.SegmentedTrackBorder;
                    }
                    else if (Equals(b.Tag, "Keycap"))
                    {
                        b.Background = pal.KeycapBackground;
                        b.BorderBrush = pal.KeycapBorder;
                    }
                    else if (Equals(b.Tag, "ProxyBadge"))
                    {
                        b.Background = pal.ProxyBadgeBackground;
                    }
                    else if (Equals(b.Tag, "InputShell"))
                    {
                        b.Background = pal.InputBackground;
                        b.BorderBrush = pal.InputBorder;
                    }
                    else if (Equals(b.Tag, "Divider"))
                    {
                        b.BorderBrush = pal.DividerBrush;
                    }
                }
                else if (tb != null)
                {
                    if (Equals(tb.Tag, "PurpleText"))
                        tb.Foreground = pal.AccentPurple;
                    else if (Equals(tb.Tag, "AccentText"))
                        tb.Foreground = pal.AccentPrimary;
                    else if (!Equals(tb.Tag, "OffsetVal") && !Equals(tb.Tag, "PreserveColor"))
                        tb.Foreground = Equals(tb.Tag, "Muted") ? pal.TaskbarTextMuted : pal.TaskbarTextPrimary;
                }
                else if (ell != null && Equals(ell.Tag, "DirectDot"))
                {
                    ell.Fill = pal.DirectBarBrush;
                }
                else if (txt != null && Equals(txt.Tag, "ConfigInput"))
                {
                    txt.Background = Brushes.Transparent;
                    txt.Foreground = pal.TaskbarTextPrimary;
                    txt.CaretBrush = pal.AccentPrimary;
                    continue;
                }
                else if (path != null && Equals(path.Tag, "RowIcon"))
                {
                    path.Stroke = pal.TaskbarTextMuted;
                }
                else if (button != null)
                {
                    if (Equals(button.Tag, "GhostBtn"))
                    {
                        button.Background = pal.SubSurfaceBackground;
                        button.BorderBrush = pal.SubSurfaceBorder;
                        button.Foreground = pal.TaskbarTextMuted;
                        ApplyPaletteToLogicalSubtree(button.Content as DependencyObject, pal);
                    }
                    else if (Equals(button.Tag, "StepBtn"))
                    {
                        button.Foreground = pal.TaskbarTextPrimary;
                    }
                    continue;
                }
                else if (slider != null)
                {
                    slider.Background = pal.TrackBackground;
                    slider.Foreground = pal.AccentPrimary;
                    slider.BorderBrush = pal.TaskbarTextMuted;
                    continue;
                }
                else if (chk != null)
                {
                    chk.Background = pal.SwitchOffBackground;
                    chk.BorderBrush = pal.SwitchOnBackground;
                    continue;
                }
                ApplyPaletteToTree(child, pal);
            }
        }

        private static void ApplyPaletteToLogicalSubtree(DependencyObject node, ThemePalette pal)
        {
            if (node == null) return;
            var tb = node as TextBlock;
            var path = node as Path;
            if (tb != null)
                tb.Foreground = Equals(tb.Tag, "Muted") ? pal.TaskbarTextMuted : pal.TaskbarTextPrimary;
            else if (path != null && Equals(path.Tag, "RowIcon"))
                path.Stroke = pal.TaskbarTextMuted;

            foreach (object child in LogicalTreeHelper.GetChildren(node))
            {
                var dep = child as DependencyObject;
                if (dep != null) ApplyPaletteToLogicalSubtree(dep, pal);
            }
        }

        private static Border MakeIconButton(string svgPath, string tooltip, Action onClick, bool borderless = false)
        {
            var icon = IconFactory.CreateVectorIcon(svgPath, 13, Brushes.White, 1.75);
            var iconGroup = new TransformGroup();
            iconGroup.Children.Add(new ScaleTransform(1.0, 1.0));
            iconGroup.Children.Add(new RotateTransform(0.0));
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = iconGroup;

            var btnScale = new ScaleTransform(1.0, 1.0);
            var btn = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(borderless ? 0 : 1),
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Child = icon,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = btnScale
            };
            btn.MouseLeftButtonDown += (s, e) =>
            {
                btnScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.90, TimeSpan.FromMilliseconds(60)));
                btnScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.90, TimeSpan.FromMilliseconds(60)));
            };
            btn.MouseLeave += (s, e) =>
            {
                btnScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100)));
                btnScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100)));
            };
            btn.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                btnScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(110)));
                btnScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(110)));
                onClick();
            };
            return btn;
        }

        private static StackPanel MakeLegendDot(string label, string hex)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = ThemeHelper.BrushFromHex(hex), Margin = new Thickness(0, 0, 4, 0) });
            sp.Children.Add(new TextBlock { Text = label, FontFamily = MonoFont, FontSize = 10, Foreground = ThemeHelper.BrushFromHex(hex), Tag = "PreserveColor" });
            return sp;
        }

        private static Border MakeSensorCard(string title, out Ellipse dot, out TextBlock mainTb, out TextBlock subTb)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 7, 9, 7),
                Tag = "SubCard"
            };
            var stack = new StackPanel();
            var head = new DockPanel();
            dot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dot, Dock.Right);
            head.Children.Add(dot);
            head.Children.Add(new TextBlock { Text = title, FontFamily = MonoFont, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Tag = "Muted" });
            stack.Children.Add(head);

            mainTb = new TextBlock { Text = "--", FontFamily = MonoFont, FontSize = 12.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 1) };
            Typography.SetNumeralAlignment(mainTb, FontNumeralAlignment.Tabular);
            subTb = new TextBlock { Text = "--", FontFamily = MonoFont, FontSize = 10, Tag = "Muted" };
            Typography.SetNumeralAlignment(subTb, FontNumeralAlignment.Tabular);

            stack.Children.Add(mainTb);
            stack.Children.Add(subTb);
            card.Child = stack;
            return card;
        }

        private static TextBlock MakeSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = MonoFont,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Tag = "Muted",
                Opacity = 0.78,
                Margin = new Thickness(3, 1, 0, 4)
            };
        }

        private static Border MakeGroupCard(params Border[] rows)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8),
                ClipToBounds = true,
                SnapsToDevicePixels = true,
                Tag = "GroupCard"
            };
            var stack = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                Border r = rows[i];
                if (rows.Length == 1)
                    r.CornerRadius = new CornerRadius(9);
                else if (i == 0)
                    r.CornerRadius = new CornerRadius(9, 9, 0, 0);
                else if (i == rows.Length - 1)
                    r.CornerRadius = new CornerRadius(0, 0, 9, 9);
                else
                    r.CornerRadius = new CornerRadius(0);

                stack.Children.Add(r);
                if (i < rows.Length - 1)
                {
                    stack.Children.Add(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(33, 0, 10, 0),
                        SnapsToDevicePixels = true,
                        Tag = "Hairline"
                    });
                }
            }
            card.Child = stack;
            return card;
        }

        private Border MakeRowShell(string svgPath, string title, UIElement rightControl)
        {
            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(10, 5, 10, 5),
                MinHeight = 32,
                SnapsToDevicePixels = true
            };
            row.MouseEnter += (s, e) => { row.Background = _rowHoverBrush; };
            row.MouseLeave += (s, e) => { row.Background = Brushes.Transparent; };

            var dp = new DockPanel();
            var feRight = rightControl as FrameworkElement;
            if (feRight != null) feRight.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(rightControl, Dock.Right);
            dp.Children.Add(rightControl);

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var icon = IconFactory.CreateVectorIcon(svgPath, 13, Brushes.Gray, 1.6);
            icon.Tag = "RowIcon";
            icon.Opacity = 0.85;
            icon.Margin = new Thickness(0, 0, 8, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(icon);
            left.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            dp.Children.Add(left);

            row.Child = dp;
            return row;
        }

        private Border MakeToggleRow(string svgPath, string title, UIElement rightControl, CheckBox targetSwitch)
        {
            var row = MakeRowShell(svgPath, title, rightControl);
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (s, e) =>
            {
                if (e.Handled || IsInsideButtonBase(e.OriginalSource as DependencyObject)) return;
                targetSwitch.IsChecked = !(targetSwitch.IsChecked == true);
                e.Handled = true;
            };
            return row;
        }

        private static bool IsInsideButtonBase(DependencyObject dep)
        {
            while (dep != null)
            {
                if (dep is ButtonBase) return true;
                dep = VisualTreeHelper.GetParent(dep);
            }
            return false;
        }

        private Border MakeSliderRow(string svgPath, string title, Slider slider, UIElement readoutElement)
        {
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            slider.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(slider);
            right.Children.Add(readoutElement);
            return MakeRowShell(svgPath, title, right);
        }

        private Border MakeSegmentedRow(string svgPath, string title, Tuple<string, object>[] options, List<Button> bucket, bool useMonoFont, Action<object> onSelect)
        {
            return MakeRowShell(svgPath, title, MakeSegmentedControl(options, bucket, useMonoFont, onSelect));
        }

        private static Border MakeSegmentedControl(Tuple<string, object>[] options, List<Button> bucket, bool useMonoFont, Action<object> onSelect)
        {
            var track = new Border
            {
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "SegTrack"
            };
            var segPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var btnTemplate = MakeSegmentButtonTemplate();

            foreach (var opt in options)
            {
                var btn = new Button
                {
                    Content = opt.Item1,
                    Tag = opt.Item2,
                    FontFamily = useMonoFont ? MonoFont : UiFont,
                    FontSize = 10.5,
                    Padding = new Thickness(8, 2.5, 8, 2.5),
                    Cursor = Cursors.Hand,
                    Focusable = false,
                    BorderThickness = new Thickness(1),
                    Template = btnTemplate
                };
                object captured = opt.Item2;
                btn.Click += (s, e) => onSelect(captured);
                bucket.Add(btn);
                segPanel.Children.Add(btn);
            }

            track.Child = segPanel;
            return track;
        }

        private static Button MakeGhostIconButton(string svgPath, string text, Action onClick)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var icon = IconFactory.CreateVectorIcon(svgPath, 10.5, Brushes.Gray, 1.7);
            icon.Tag = "RowIcon";
            icon.Margin = new Thickness(0, 0, 4, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(icon);
            sp.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = UiFont,
                FontSize = 10,
                Tag = "Muted",
                VerticalAlignment = VerticalAlignment.Center
            });

            var btn = new Button
            {
                Content = sp,
                Padding = new Thickness(6, 2.5, 6, 2.5),
                Cursor = Cursors.Hand,
                Focusable = false,
                BorderThickness = new Thickness(1),
                Template = MakeSegmentButtonTemplate(),
                Tag = "GhostBtn"
            };
            btn.Click += (s, e) => { e.Handled = true; onClick(); };
            return btn;
        }

        private static StackPanel MakeKeycapPair(string keyText, string labelText)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var kbd = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5, 1.5, 5, 1.5),
                Margin = new Thickness(0, 0, 4, 0),
                Tag = "Keycap",
                Child = new TextBlock
                {
                    Text = keyText,
                    FontFamily = MonoFont,
                    FontSize = 9.5,
                    Tag = "Muted"
                }
            };
            sp.Children.Add(kbd);
            sp.Children.Add(new TextBlock
            {
                Text = labelText,
                FontFamily = UiFont,
                FontSize = 10,
                Tag = "Muted",
                Opacity = 0.8,
                VerticalAlignment = VerticalAlignment.Center
            });
            return sp;
        }

        private static ControlTemplate MakeSegmentButtonTemplate()
        {
            const string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='{x:Type Button}'>
  <Border Background='{TemplateBinding Background}'
          BorderBrush='{TemplateBinding BorderBrush}'
          BorderThickness='{TemplateBinding BorderThickness}'
          CornerRadius='5'
          SnapsToDevicePixels='True'>
    <Grid>
      <Border x:Name='HoverVeil' CornerRadius='4' Background='Transparent'/>
      <ContentPresenter Margin='{TemplateBinding Padding}'
                        HorizontalAlignment='Center'
                        VerticalAlignment='Center'/>
    </Grid>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter TargetName='HoverVeil' Property='Background' Value='#14808080'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        private static Slider MakeMinimalSlider(double min, double max, double tick, double width, bool showFillBar, bool showCenterTick)
        {
            string centerTickXaml = showCenterTick
                ? "<Border Width='2' Height='8' CornerRadius='1' HorizontalAlignment='Center' VerticalAlignment='Center' Opacity='0.5' Background='{TemplateBinding BorderBrush}'/>"
                : "";
            string decreaseXaml = showFillBar
                ? @"<ControlTemplate TargetType='{x:Type RepeatButton}'>
                      <Grid Height='18' Background='Transparent'>
                        <Border Height='3' CornerRadius='1.5' VerticalAlignment='Center'
                                Background='{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type Slider}}}'/>
                      </Grid>
                    </ControlTemplate>"
                : @"<ControlTemplate TargetType='{x:Type RepeatButton}'>
                      <Border Height='18' Background='Transparent'/>
                    </ControlTemplate>";

            string xaml = string.Format(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='{{x:Type Slider}}'>
  <Grid Height='18' Background='Transparent' VerticalAlignment='Center' SnapsToDevicePixels='True'>
    <Border Height='3' CornerRadius='1.5' VerticalAlignment='Center' Background='{{TemplateBinding Background}}'/>
    {0}
    <Track x:Name='PART_Track' VerticalAlignment='Center'>
      <Track.DecreaseRepeatButton>
        <RepeatButton Command='{{x:Static Slider.DecreaseLarge}}' Focusable='False' Cursor='Hand'>
          <RepeatButton.Template>{1}</RepeatButton.Template>
        </RepeatButton>
      </Track.DecreaseRepeatButton>
      <Track.IncreaseRepeatButton>
        <RepeatButton Command='{{x:Static Slider.IncreaseLarge}}' Focusable='False' Cursor='Hand'>
          <RepeatButton.Template>
            <ControlTemplate TargetType='{{x:Type RepeatButton}}'>
              <Border Height='18' Background='Transparent'/>
            </ControlTemplate>
          </RepeatButton.Template>
        </RepeatButton>
      </Track.IncreaseRepeatButton>
      <Track.Thumb>
        <Thumb x:Name='Thumb' Width='13' Height='13' Cursor='Hand' Focusable='False'>
          <Thumb.Template>
            <ControlTemplate TargetType='{{x:Type Thumb}}'>
              <Grid Background='Transparent'>
                <Border x:Name='Cap' Width='11' Height='11' CornerRadius='5.5'
                        Background='#FFFFFFFF' BorderThickness='1.5'
                        BorderBrush='{{Binding Foreground, RelativeSource={{RelativeSource AncestorType={{x:Type Slider}}}}}}'
                        HorizontalAlignment='Center' VerticalAlignment='Center'/>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'>
                  <Setter TargetName='Cap' Property='Width' Value='12.5'/>
                  <Setter TargetName='Cap' Property='Height' Value='12.5'/>
                  <Setter TargetName='Cap' Property='CornerRadius' Value='6.25'/>
                </Trigger>
                <Trigger Property='IsDragging' Value='True'>
                  <Setter TargetName='Cap' Property='Width' Value='13'/>
                  <Setter TargetName='Cap' Property='Height' Value='13'/>
                  <Setter TargetName='Cap' Property='CornerRadius' Value='6.5'/>
                  <Setter TargetName='Cap' Property='BorderThickness' Value='2'/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
    </Track>
  </Grid>
</ControlTemplate>", centerTickXaml, decreaseXaml);

            return new Slider
            {
                Width = width,
                Minimum = min,
                Maximum = max,
                IsSnapToTickEnabled = true,
                TickFrequency = tick,
                IsMoveToPointEnabled = true,
                Focusable = false,
                Cursor = Cursors.Hand,
                Template = (ControlTemplate)XamlReader.Parse(xaml)
            };
        }

        private static void HighlightSelected(List<Button> buttons, object activeVal, ThemePalette pal)
        {
            foreach (var b in buttons)
            {
                bool active = Equals(b.Tag, activeVal);
                b.Background = active ? pal.SegmentedPillBackground : Brushes.Transparent;
                b.BorderBrush = active ? pal.SegmentedPillBorder : Brushes.Transparent;
                b.Foreground = active ? pal.TaskbarTextPrimary : pal.SegmentedInactiveText;
                b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        private static void ApplyThinScrollBarStyle(ScrollViewer sv)
        {
            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type ScrollBar}'>
  <Setter Property='Width' Value='5'/>
  <Setter Property='MinWidth' Value='5'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='{x:Type ScrollBar}'>
        <Grid Background='Transparent' Width='5'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/>
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='{x:Type Thumb}'>
                    <Border x:Name='ThumbBar' Background='#4894A3B8' CornerRadius='2.5' Margin='1,2,0,2'/>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='ThumbBar' Property='Background' Value='#8894A3B8'/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
            sv.Resources[typeof(ScrollBar)] = (Style)XamlReader.Parse(xaml);
        }

        private static CheckBox MakeSwitch()
        {
            const string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='{x:Type CheckBox}'>
  <Border x:Name='Track' Width='30' Height='16' CornerRadius='8'
          Background='{TemplateBinding Background}' SnapsToDevicePixels='True'>
    <Border x:Name='Thumb' Width='12' Height='12' CornerRadius='6' Background='#FFFFFFFF'
            HorizontalAlignment='Left' VerticalAlignment='Center' Margin='2,0,2,0'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'>
      <Setter TargetName='Track' Property='Background'
              Value='{Binding BorderBrush, RelativeSource={RelativeSource TemplatedParent}}'/>
      <Setter TargetName='Thumb' Property='HorizontalAlignment' Value='Right'/>
    </Trigger>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter TargetName='Track' Property='Opacity' Value='0.88'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
            return new CheckBox
            {
                Template = (ControlTemplate)XamlReader.Parse(xaml),
                Cursor = Cursors.Hand,
                Focusable = false
            };
        }
    }
}
