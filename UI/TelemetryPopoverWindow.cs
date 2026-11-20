using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SysTelemetry.Interop;
using SysTelemetry.Models;
using SysTelemetry.Services;

namespace SysTelemetry.UI
{
    public sealed class TelemetryPopoverWindow : Window
    {
        private static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable, Segoe UI, Microsoft YaHei UI");
        private static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");

        private readonly AppConfig _config;
        private Border _shellBorder;
        private Ellipse _statusDot;
        private TextBlock _titleText;
        private Border _btnDesktopToggle;
        private Border _btnTrim;
        private Border _btnGear;
        private Border _btnClose;

        private Grid _telemetryView;
        private ScrollViewer _settingsScroll;
        private StackPanel _settingsView;
        private bool _settingsOpen;
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

        // Settings controls
        private CheckBox _chkAutoStart;
        private CheckBox _chkDeskEnabled;
        private CheckBox _chkDeskLocked;
        private CheckBox _chkFullscreenHide;
        private Slider _sliderOpacity;
        private TextBlock _lblOpacity;
        private Slider _sliderOffset;
        private TextBlock _lblOffset;
        private readonly List<Button> _skinButtons = new List<Button>();
        private readonly List<Button> _themeButtons = new List<Button>();
        private readonly List<Button> _rateButtons = new List<Button>();
        private readonly List<Button> _tempButtons = new List<Button>();
        private readonly List<Button> _dockButtons = new List<Button>();

        private TelemetrySnapshot _lastSnap;
        private DateTime _lastDeactivatedUtc = DateTime.MinValue;
        private IntPtr _hwnd;

        public IntPtr Handle { get { return _hwnd; } }
        public bool WasJustDeactivated { get { return (DateTime.UtcNow - _lastDeactivatedUtc).TotalMilliseconds < 220; } }

        public event Action ConfigChanged;
        public event Action DesktopOverlayToggled;
        public event Action DesktopLockChanged;
        public event Action ResetDesktopCornerRequested;
        public event Action TrimRequested;
        public event Action PollRateChanged;

        public TelemetryPopoverWindow(AppConfig config)
        {
            _config = config;
            Width = 372;
            SizeToContent = SizeToContent.Height;
            AllowsTransparency = true;
            WindowStyle = WindowStyle.None;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;

            BuildLayout();

            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.ApplyToolWindowStyle(_hwnd, clickThrough: false);
            };
            Deactivated += (s, e) =>
            {
                _lastDeactivatedUtc = DateTime.UtcNow;
                Hide();
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
            SetSettingsMode(true);
            ShowNearTaskbar(bar);
        }

        public void UpdateMetrics(TelemetrySnapshot snap)
        {
            _lastSnap = snap;
            if (!IsVisible || _settingsOpen) return;

            var pal = ThemeHelper.GetPalette(_config.Theme);

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

            UpdateChartLines(snap);
            UpdateProcessRows(snap.TopProcesses, pal);
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

            // Header: dot + SYS.TELEMETRY (no 1000ms!) + 4 icon-only square buttons
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
            _btnGear = MakeIconButton(IconFactory.GearSvg, "设置 (SYS.CONFIG)", () => SetSettingsMode(!_settingsOpen));
            _btnClose = MakeIconButton(IconFactory.CloseSvg, "关闭面板", Hide, borderless: true);

            rightButtons.Children.Add(_btnDesktopToggle);
            rightButtons.Children.Add(new Border { Width = 5 });
            rightButtons.Children.Add(_btnTrim);
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
            BuildSettingsView();

            _settingsScroll = new ScrollViewer
            {
                Visibility = Visibility.Collapsed,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 110),
                Content = _settingsView
            };

            rootStack.Children.Add(_telemetryView);
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
            var sensorGrid = new Grid { Margin = new Thickness(0, 0, 0, 9) };
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
            Grid.SetRow(procSection, 2);
            _telemetryView.Children.Add(procSection);
        }

        private void BuildSettingsView()
        {
            _settingsView = new StackPanel();

            _settingsView.Children.Add(MakeSectionLabel("SYSTEM & OVERLAY"));

            _chkAutoStart = MakeSwitch();
            _chkAutoStart.Checked += (s, e) => OnAutoStartToggled(true);
            _chkAutoStart.Unchecked += (s, e) => OnAutoStartToggled(false);
            _settingsView.Children.Add(MakeSettingRow("开机静默自启", "任务计划高权限自启，免 UAC 弹窗", _chkAutoStart));

            _chkDeskEnabled = MakeSwitch();
            _chkDeskEnabled.Checked += (s, e) => OnDeskEnabledToggled(true);
            _chkDeskEnabled.Unchecked += (s, e) => OnDeskEnabledToggled(false);
            var deskRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var resetBtn = new Button
            {
                Content = "归位",
                FontFamily = UiFont,
                FontSize = 10.5,
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 7, 0),
                Cursor = Cursors.Hand,
                Template = MakeFlatButtonTemplate(new CornerRadius(5)),
                Tag = "MiniBtn"
            };
            resetBtn.Click += (s, e) => { if (ResetDesktopCornerRequested != null) ResetDesktopCornerRequested(); };
            deskRight.Children.Add(resetBtn);
            deskRight.Children.Add(_chkDeskEnabled);
            _settingsView.Children.Add(MakeSettingRow("桌面单行透明角标", "透明底单行基础读数", deskRight));

            _chkDeskLocked = MakeSwitch();
            _chkDeskLocked.Checked += (s, e) => OnDeskLockToggled(true);
            _chkDeskLocked.Unchecked += (s, e) => OnDeskLockToggled(false);
            _settingsView.Children.Add(MakeSettingRow("固定桌面角标位置", "固定即穿透；取消固定可拖拽改位置", _chkDeskLocked));

            _chkFullscreenHide = MakeSwitch();
            _chkFullscreenHide.Checked += (s, e) => { _config.FullscreenAutoHide = true; _config.Save(); };
            _chkFullscreenHide.Unchecked += (s, e) => { _config.FullscreenAutoHide = false; _config.Save(); };
            _settingsView.Children.Add(MakeSettingRow("全屏应用自动隐藏", "全屏游戏 / 视频播放时避让遮挡", _chkFullscreenHide));

            _settingsView.Children.Add(MakeSectionLabel("VISUAL & TELEMETRY"));

            _settingsView.Children.Add(MakeOptionGroupRow("皮肤外观", new[]
            {
                Tuple.Create("黑白", (object)SkinStyle.Hud),
                Tuple.Create("Fluent", (object)SkinStyle.Fluent),
                Tuple.Create("色条", (object)SkinStyle.Lite),
                Tuple.Create("极简", (object)SkinStyle.Minimal)
            }, _skinButtons, val =>
            {
                _config.Skin = (SkinStyle)val;
                _config.Save();
                if (ConfigChanged != null) ConfigChanged();
                SyncSettingsControls();
            }));

            _settingsView.Children.Add(MakeOptionGroupRow("深浅主题", new[]
            {
                Tuple.Create("自动", (object)ThemeMode.Auto),
                Tuple.Create("深色", (object)ThemeMode.Dark),
                Tuple.Create("浅色", (object)ThemeMode.Light)
            }, _themeButtons, val =>
            {
                _config.Theme = (ThemeMode)val;
                _config.Save();
                ApplyThemeColors();
                if (ConfigChanged != null) ConfigChanged();
                SyncSettingsControls();
            }));

            _settingsView.Children.Add(MakeOptionGroupRow("采样周期", new[]
            {
                Tuple.Create("0.5s", (object)500),
                Tuple.Create("1.0s", (object)1000),
                Tuple.Create("2.0s", (object)2000)
            }, _rateButtons, val =>
            {
                _config.PollIntervalMs = (int)val;
                _config.Save();
                if (PollRateChanged != null) PollRateChanged();
                SyncSettingsControls();
            }));

            _settingsView.Children.Add(MakeOptionGroupRow("高温阈值", new[]
            {
                Tuple.Create("75°C", (object)75),
                Tuple.Create("80°C", (object)80),
                Tuple.Create("85°C", (object)85)
            }, _tempButtons, val =>
            {
                _config.TempWarnThreshold = (int)val;
                _config.Save();
                if (ConfigChanged != null) ConfigChanged();
                SyncSettingsControls();
            }));

            _lblOpacity = new TextBlock { FontFamily = MonoFont, FontSize = 10.5, Tag = "Muted" };
            _sliderOpacity = new Slider { Width = 115, Minimum = 35, Maximum = 100, IsSnapToTickEnabled = true, TickFrequency = 1 };
            _sliderOpacity.ValueChanged += (s, e) =>
            {
                if (!IsLoaded) return;
                _config.DesktopOpacity = Math.Round(_sliderOpacity.Value / 100.0, 2);
                _lblOpacity.Text = string.Format("{0:0}%", _sliderOpacity.Value);
                _config.Save();
                if (DesktopLockChanged != null) DesktopLockChanged();
            };
            _settingsView.Children.Add(MakeSliderRow("角标字体透明度", _lblOpacity, _sliderOpacity));

            _settingsView.Children.Add(MakeOptionGroupRow("任务栏停靠", new[]
            {
                Tuple.Create("左侧", (object)TaskbarDockMode.Left),
                Tuple.Create("右侧", (object)TaskbarDockMode.Right)
            }, _dockButtons, val =>
            {
                _config.TaskbarDock = (TaskbarDockMode)val;
                _config.Save();
                if (ConfigChanged != null) ConfigChanged();
                RepositionBottomAligned();
                SyncSettingsControls();
            }));

            _lblOffset = new TextBlock { FontFamily = MonoFont, FontSize = 10.5, Tag = "Muted" };
            _sliderOffset = new Slider { Width = 115, Minimum = -200, Maximum = 200, IsSnapToTickEnabled = true, TickFrequency = 5 };
            _sliderOffset.ValueChanged += (s, e) =>
            {
                if (!IsLoaded) return;
                _config.TaskbarOffsetX = (int)_sliderOffset.Value;
                _lblOffset.Text = string.Format("{0:+0;-0;0}px", _config.TaskbarOffsetX);
                _config.Save();
                if (ConfigChanged != null) ConfigChanged();
            };
            _settingsView.Children.Add(MakeSliderRow("任务栏横向微调", _lblOffset, _sliderOffset));

            var hkRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var hkVal = new TextBlock { Text = "Ctrl+Alt+L / M", FontFamily = MonoFont, FontSize = 10, Tag = "Muted" };
            DockPanel.SetDock(hkVal, Dock.Right);
            hkRow.Children.Add(hkVal);
            hkRow.Children.Add(new TextBlock { Text = "热键：固定切换 / 呼出面板", FontFamily = UiFont, FontSize = 11, Tag = "Muted" });
            _settingsView.Children.Add(hkRow);
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
                _lblOffset.Text = string.Format("{0:+0;-0;0}px", _config.TaskbarOffsetX);

                var pal = ThemeHelper.GetPalette(_config.Theme);
                HighlightSelected(_skinButtons, _config.Skin, pal);
                HighlightSelected(_themeButtons, _config.Theme, pal);
                HighlightSelected(_rateButtons, _config.PollIntervalMs, pal);
                HighlightSelected(_tempButtons, _config.TempWarnThreshold, pal);
                HighlightSelected(_dockButtons, _config.TaskbarDock, pal);
            }
            finally { _suppressSwitchEvents = false; }
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
            _settingsOpen = open;
            _telemetryView.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            _settingsScroll.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            _titleText.Text = open ? "SYS.CONFIG" : "SYS.TELEMETRY";
            ApplyThemeColors();
            if (open) SyncSettingsControls();
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
            _shellBorder.Background = pal.PopoverBackground;
            _shellBorder.BorderBrush = pal.PopoverBorder;
            _statusDot.Fill = pal.StatusOk;
            _titleText.Foreground = pal.TaskbarTextPrimary;

            _cpuLine.Stroke = pal.AccentBlue;
            _gpuLine.Stroke = pal.AccentPurple;
            _tempLine.Stroke = pal.AccentGreen;

            StyleHeaderButton(_btnDesktopToggle, pal, _config.DesktopOverlayEnabled);
            StyleHeaderButton(_btnTrim, pal, false);
            StyleHeaderButton(_btnGear, pal, _settingsOpen);
            StyleHeaderButton(_btnClose, pal, false, borderless: true);

            ApplyPaletteToTree(_telemetryView, pal);
            ApplyPaletteToTree(_settingsView, pal);
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
            btn.Background = active ? ThemeHelper.BrushFromHex("#2438BDF8") : (borderless ? Brushes.Transparent : pal.SubSurfaceBackground);
            btn.BorderBrush = active ? pal.AccentBlue : (borderless ? Brushes.Transparent : pal.SubSurfaceBorder);
            var path = btn.Child as Path;
            if (path != null)
                path.Stroke = active ? pal.AccentBlue : pal.TaskbarTextPrimary;
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
                var button = child as Button;
                if (b != null)
                {
                    if (Equals(b.Tag, "SubCard"))
                    {
                        b.Background = pal.SubSurfaceBackground;
                        b.BorderBrush = pal.SubSurfaceBorder;
                    }
                    else if (Equals(b.Tag, "Divider"))
                    {
                        b.BorderBrush = pal.DividerBrush;
                    }
                }
                else if (tb != null)
                {
                    tb.Foreground = Equals(tb.Tag, "Muted") ? pal.TaskbarTextMuted : pal.TaskbarTextPrimary;
                }
                else if (button != null && Equals(button.Tag, "MiniBtn"))
                {
                    button.Background = pal.SubSurfaceBackground;
                    button.BorderBrush = pal.SubSurfaceBorder;
                    button.Foreground = pal.TaskbarTextPrimary;
                }
                ApplyPaletteToTree(child, pal);
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
            sp.Children.Add(new TextBlock { Text = label, FontFamily = MonoFont, FontSize = 10, Foreground = ThemeHelper.BrushFromHex(hex) });
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
                FontSize = 10,
                Tag = "Muted",
                Margin = new Thickness(0, 4, 0, 4)
            };
        }

        private static Border MakeSettingRow(string title, string desc, UIElement rightControl)
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 6, 9, 6),
                Margin = new Thickness(0, 0, 0, 5),
                Tag = "SubCard"
            };
            var dp = new DockPanel();
            DockPanel.SetDock(rightControl, Dock.Right);
            var fe = rightControl as FrameworkElement;
            if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center;
            dp.Children.Add(rightControl);

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = title, FontFamily = UiFont, FontSize = 11.5, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = desc, FontFamily = UiFont, FontSize = 10, Tag = "Muted" });
            dp.Children.Add(left);
            row.Child = dp;
            return row;
        }

        private static Border MakeSliderRow(string title, TextBlock readout, Slider slider)
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 6, 9, 6),
                Margin = new Thickness(0, 0, 0, 5),
                Tag = "SubCard"
            };
            var dp = new DockPanel();
            slider.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(slider, Dock.Right);
            dp.Children.Add(slider);

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = title, FontFamily = UiFont, FontSize = 11.5, FontWeight = FontWeights.SemiBold });
            left.Children.Add(readout);
            dp.Children.Add(left);
            row.Child = dp;
            return row;
        }

        private static Border MakeOptionGroupRow(string title, Tuple<string, object>[] options, List<Button> bucket, Action<object> onSelect)
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 6, 9, 6),
                Margin = new Thickness(0, 0, 0, 5),
                Tag = "SubCard"
            };
            var dp = new DockPanel();
            var pillGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(pillGroup, Dock.Right);

            foreach (var opt in options)
            {
                var btn = new Button
                {
                    Content = opt.Item1,
                    Tag = opt.Item2,
                    FontFamily = UiFont,
                    FontSize = 10.5,
                    Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(3, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1),
                    Template = MakeFlatButtonTemplate(new CornerRadius(5))
                };
                object captured = opt.Item2;
                btn.Click += (s, e) => onSelect(captured);
                bucket.Add(btn);
                pillGroup.Children.Add(btn);
            }

            dp.Children.Add(pillGroup);
            dp.Children.Add(new TextBlock { Text = title, FontFamily = UiFont, FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            row.Child = dp;
            return row;
        }

        private static ControlTemplate MakeFlatButtonTemplate(CornerRadius radius)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, radius);
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            template.VisualTree = border;
            return template;
        }

        private static void HighlightSelected(List<Button> buttons, object activeVal, ThemePalette pal)
        {
            foreach (var b in buttons)
            {
                bool active = Equals(b.Tag, activeVal);
                b.Background = active ? ThemeHelper.BrushFromHex("#2563EB") : pal.SubSurfaceBackground;
                b.BorderBrush = active ? ThemeHelper.BrushFromHex("#3B82F6") : pal.SubSurfaceBorder;
                b.Foreground = active ? Brushes.White : pal.TaskbarTextMuted;
            }
        }

        private static CheckBox MakeSwitch()
        {
            var template = new ControlTemplate(typeof(CheckBox));
            var track = new FrameworkElementFactory(typeof(Border), "Track");
            track.SetValue(Border.WidthProperty, 34.0);
            track.SetValue(Border.HeightProperty, 18.0);
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            track.SetValue(Border.BackgroundProperty, ThemeHelper.BrushFromHex("#4494A3B8"));

            var thumb = new FrameworkElementFactory(typeof(Border), "Thumb");
            thumb.SetValue(Border.WidthProperty, 12.0);
            thumb.SetValue(Border.HeightProperty, 12.0);
            thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            thumb.SetValue(Border.BackgroundProperty, Brushes.White);
            thumb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            thumb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            thumb.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 0, 3, 0));
            track.AppendChild(thumb);
            template.VisualTree = track;

            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, ThemeHelper.BrushFromHex("#3B82F6"), "Track"));
            checkedTrigger.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Thumb"));
            template.Triggers.Add(checkedTrigger);

            return new CheckBox { Template = template, Cursor = Cursors.Hand };
        }
    }
}
