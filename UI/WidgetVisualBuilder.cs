using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SysTelemetry.Models;
using SysTelemetry.Services;

namespace SysTelemetry.UI
{
    internal static class WidgetVisualBuilder
    {
        private static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");

        public static UIElement BuildTaskbarVisual(TelemetrySnapshot snap, AppConfig cfg, ThemePalette pal, bool isHovered)
        {
            string cpuTempText = snap.CpuTempC.HasValue ? string.Format("{0:0}°C", snap.CpuTempC.Value) : string.Format("{0:0.0}G", snap.CpuClockGhz);
            string gpuTempText = snap.GpuTempC.HasValue ? string.Format("{0:0}°C", snap.GpuTempC.Value) : "--";
            string rxVal = ThemeHelper.FormatNetSpeed(snap.NetDownBytesPerSec);
            string txVal = ThemeHelper.FormatNetSpeed(snap.NetUpBytesPerSec);

            Brush cpuCol = ThemeHelper.GetStatusBrush(pal, snap.CpuLoadPercent, snap.CpuTempC, cfg.TempWarnThreshold);
            Brush gpuCol = ThemeHelper.GetStatusBrush(pal, snap.GpuLoadPercent, snap.GpuTempC, cfg.TempWarnThreshold);
            Brush memCol = ThemeHelper.GetStatusBrush(pal, snap.MemoryPercent, null, cfg.TempWarnThreshold);

            var root = new Border
            {
                Height = 36,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(cfg.Skin == SkinStyle.Minimal ? 6 : 7, 0, cfg.Skin == SkinStyle.Minimal ? 6 : 7, 0),
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            if (cfg.Skin == SkinStyle.Hud)
            {
                // Compact borderless monochrome HUD with colored waveform and zero-jitter fixed-width columns (~243px)
                root.Background = isHovered ? pal.TaskbarHoverBg : ThemeHelper.BrushFromHex("#01000000");
                root.BorderThickness = new Thickness(0);

                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(BuildMiniSparkline(snap.CpuHistory, pal.AccentBlue, 30, 18));
                row.Children.Add(MakeSpacer(6));

                string cpuTop = snap.CpuTempC.HasValue ? string.Format("CPU {0:0}°C", snap.CpuTempC.Value) : "CPU";
                string cpuBot = snap.CpuPowerW.HasValue
                    ? string.Format("{0:0}% {1:0}W", snap.CpuLoadPercent, snap.CpuPowerW.Value)
                    : string.Format("{0:0}% {1:0.0}G", snap.CpuLoadPercent, snap.CpuClockGhz);
                row.Children.Add(MakeTwoLineColumn(cpuTop, cpuBot, pal.TaskbarTextMuted, pal.TaskbarTextPrimary, 60));
                row.Children.Add(MakeSpacer(5));

                string gpuTop = snap.GpuTempC.HasValue ? string.Format("GPU {0:0}°C", snap.GpuTempC.Value) : "GPU";
                string gpuBot = string.Format("{0:0}%", snap.GpuLoadPercent);
                row.Children.Add(MakeTwoLineColumn(gpuTop, gpuBot, pal.TaskbarTextMuted, pal.TaskbarTextPrimary, 46));
                row.Children.Add(MakeSpacer(5));

                row.Children.Add(MakeTwoLineColumn("MEM", string.Format("{0:0}%", snap.MemoryPercent), pal.TaskbarTextMuted, pal.TaskbarTextPrimary, 28));
                row.Children.Add(MakeSpacer(5));

                row.Children.Add(MakeFixedNetColumn("↓", rxVal, "↑", txVal, pal.TaskbarTextPrimary, pal.TaskbarTextMuted, 44));
                root.Child = row;
                return root;
            }

            if (cfg.Skin == SkinStyle.Fluent)
            {
                root.Background = isHovered ? pal.CardBorder : pal.CardBackground;
                root.BorderBrush = pal.CardBorder;
                root.BorderThickness = new Thickness(1);

                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(MakeFluentColumn("CPU", cpuCol, string.Format("{0:0}% {1}", snap.CpuLoadPercent, cpuTempText), pal, 62));
                row.Children.Add(MakeSpacer(5));
                row.Children.Add(MakeFluentColumn("GPU", gpuCol, string.Format("{0:0}% {1}", snap.GpuLoadPercent, gpuTempText), pal, 62));
                row.Children.Add(MakeSpacer(5));
                row.Children.Add(MakeTwoLineColumn("MEM", string.Format("{0:0}%", snap.MemoryPercent), pal.TaskbarTextMuted, memCol, 28));
                row.Children.Add(MakeSpacer(5));
                row.Children.Add(MakeFixedNetColumn("↓", rxVal + "/s", "↑", txVal + "/s", pal.AccentBlue, pal.AccentPurple, 54));
                root.Child = row;
                return root;
            }

            if (cfg.Skin == SkinStyle.Lite)
            {
                root.Background = isHovered ? pal.CardBorder : pal.CardBackground;
                root.BorderBrush = pal.CardBorder;
                root.BorderThickness = new Thickness(1);

                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(MakeLiteBarColumn("CPU", snap.CpuLoadPercent, snap.CpuTempC, cpuCol, pal, 64));
                row.Children.Add(MakeSpacer(5));
                row.Children.Add(MakeLiteBarColumn("GPU", snap.GpuLoadPercent, snap.GpuTempC, gpuCol, pal, 64));
                row.Children.Add(MakeSpacer(5));
                row.Children.Add(MakeLiteBarColumn("MEM", snap.MemoryPercent, null, memCol, pal, 42));
                row.Children.Add(MakeSpacer(5));
                row.Children.Add(MakeFixedNetColumn("↓", rxVal, "↑", txVal, pal.AccentBlue, pal.AccentGreen, 44));
                root.Child = row;
                return root;
            }

            // Minimal
            root.Background = isHovered ? pal.TaskbarHoverBg : ThemeHelper.BrushFromHex("#01000000");
            var minRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            minRow.Children.Add(MakeMinimalPairColumn("CPU", snap.CpuLoadPercent, cpuCol, cpuTempText, "GPU", snap.GpuLoadPercent, gpuCol, gpuTempText, pal, 106));
            minRow.Children.Add(MakeSpacer(6));

            var minRight = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 94 };
            minRight.Children.Add(MakeText(string.Format("MEM {0:0}%", snap.MemoryPercent), 10, pal.TaskbarTextPrimary, FontWeights.SemiBold));
            var minNetRow = new StackPanel { Orientation = Orientation.Horizontal };
            minNetRow.Children.Add(MakeFixedNetCell("↓", rxVal, pal.TaskbarTextMuted, pal.TaskbarTextMuted, 44, 9.5));
            minNetRow.Children.Add(MakeSpacer(6));
            minNetRow.Children.Add(MakeFixedNetCell("↑", txVal, pal.TaskbarTextMuted, pal.TaskbarTextMuted, 44, 9.5));
            minRight.Children.Add(minNetRow);
            minRow.Children.Add(minRight);

            root.Child = minRow;
            return root;
        }

        public static UIElement BuildDesktopSingleLineVisual(TelemetrySnapshot snap, AppConfig cfg, ThemePalette pal, bool unlocked)
        {
            var root = new Border
            {
                Padding = new Thickness(6, 3, 6, 3),
                CornerRadius = new CornerRadius(5),
                Background = unlocked ? ThemeHelper.BrushFromHex("#1C38BDF8") : Brushes.Transparent,
                BorderBrush = unlocked ? pal.AccentBlue : Brushes.Transparent,
                BorderThickness = new Thickness(unlocked ? 1 : 0),
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            if (!unlocked)
            {
                root.Effect = new DropShadowEffect
                {
                    Color = pal.IsDark ? Colors.Black : Colors.White,
                    ShadowDepth = 0,
                    BlurRadius = 4,
                    Opacity = 0.85
                };
            }

            string cpuVal = snap.CpuTempC.HasValue
                ? string.Format("{0,3:0}% {1,2:0}°", snap.CpuLoadPercent, snap.CpuTempC.Value)
                : string.Format("{0,3:0}%", snap.CpuLoadPercent);
            string gpuVal = snap.GpuTempC.HasValue
                ? string.Format("{0,3:0}% {1,2:0}°", snap.GpuLoadPercent, snap.GpuTempC.Value)
                : string.Format("{0,3:0}%", snap.GpuLoadPercent);
            string memVal = string.Format("{0,3:0}%", snap.MemoryPercent);
            string rxVal = ThemeHelper.FormatNetSpeed(snap.NetDownBytesPerSec);
            string txVal = ThemeHelper.FormatNetSpeed(snap.NetUpBytesPerSec);

            double cpuW = snap.CpuTempC.HasValue ? 78 : 54;
            double gpuW = snap.GpuTempC.HasValue ? 78 : 54;

            Brush cpuCol = ThemeHelper.GetStatusBrush(pal, snap.CpuLoadPercent, snap.CpuTempC, cfg.TempWarnThreshold);
            Brush gpuCol = ThemeHelper.GetStatusBrush(pal, snap.GpuLoadPercent, snap.GpuTempC, cfg.TempWarnThreshold);
            Brush memCol = ThemeHelper.GetStatusBrush(pal, snap.MemoryPercent, null, cfg.TempWarnThreshold);

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            if (cfg.Skin == SkinStyle.Fluent)
            {
                AddDot(row, cpuCol);
                row.Children.Add(MakeFixedMetricCell("CPU", cpuVal, pal.TaskbarTextPrimary, pal.TaskbarTextPrimary, cpuW));
                AddSeparator(row, "|", pal.TaskbarTextMuted);
                AddDot(row, gpuCol);
                row.Children.Add(MakeFixedMetricCell("GPU", gpuVal, pal.TaskbarTextPrimary, pal.TaskbarTextPrimary, gpuW));
                AddSeparator(row, "|", pal.TaskbarTextMuted);
                row.Children.Add(MakeFixedMetricCell("MEM", memVal, pal.TaskbarTextPrimary, memCol, 52));
                AddSeparator(row, "|", pal.TaskbarTextMuted);
                row.Children.Add(MakeFixedNetCell("↓", rxVal, pal.AccentBlue, pal.AccentBlue, 48, 10.5));
                row.Children.Add(MakeSpacer(6));
                row.Children.Add(MakeFixedNetCell("↑", txVal, pal.AccentPurple, pal.AccentPurple, 48, 10.5));
            }
            else if (cfg.Skin == SkinStyle.Lite)
            {
                row.Children.Add(MakeUnderlinedMetricCell("CPU", cpuVal, cpuCol, pal.TaskbarTextPrimary, cpuW));
                row.Children.Add(MakeSpacer(8));
                row.Children.Add(MakeUnderlinedMetricCell("GPU", gpuVal, gpuCol, pal.TaskbarTextPrimary, gpuW));
                row.Children.Add(MakeSpacer(8));
                row.Children.Add(MakeUnderlinedMetricCell("MEM", memVal, memCol, pal.TaskbarTextPrimary, 52));
                row.Children.Add(MakeSpacer(8));
                row.Children.Add(MakeFixedNetCell("↓", rxVal, pal.AccentBlue, pal.AccentBlue, 48, 10.5));
                row.Children.Add(MakeSpacer(6));
                row.Children.Add(MakeFixedNetCell("↑", txVal, pal.AccentGreen, pal.AccentGreen, 48, 10.5));
            }
            else if (cfg.Skin == SkinStyle.Hud)
            {
                // Monochrome single-line readout, NO waveform, fixed-width cells for zero jitter
                row.Children.Add(MakeFixedMetricCell("CPU", cpuVal, pal.TaskbarTextMuted, pal.TaskbarTextPrimary, cpuW));
                AddSeparator(row, "·", pal.TaskbarTextMuted);
                row.Children.Add(MakeFixedMetricCell("GPU", gpuVal, pal.TaskbarTextMuted, pal.TaskbarTextPrimary, gpuW));
                AddSeparator(row, "·", pal.TaskbarTextMuted);
                row.Children.Add(MakeFixedMetricCell("MEM", memVal, pal.TaskbarTextMuted, pal.TaskbarTextPrimary, 52));
                AddSeparator(row, "·", pal.TaskbarTextMuted);
                row.Children.Add(MakeFixedNetCell("↓", rxVal, pal.TaskbarTextPrimary, pal.TaskbarTextPrimary, 48, 10.5));
                row.Children.Add(MakeSpacer(6));
                row.Children.Add(MakeFixedNetCell("↑", txVal, pal.TaskbarTextMuted, pal.TaskbarTextMuted, 48, 10.5));
            }
            else
            {
                // Minimal
                row.Children.Add(MakeFixedMetricCell("CPU", cpuVal, pal.TaskbarTextPrimary, pal.TaskbarTextPrimary, cpuW));
                row.Children.Add(MakeSpacer(8));
                row.Children.Add(MakeFixedMetricCell("GPU", gpuVal, pal.TaskbarTextPrimary, pal.TaskbarTextPrimary, gpuW));
                row.Children.Add(MakeSpacer(8));
                row.Children.Add(MakeFixedMetricCell("MEM", memVal, pal.TaskbarTextPrimary, pal.TaskbarTextPrimary, 52));
                row.Children.Add(MakeSpacer(8));
                row.Children.Add(MakeFixedNetCell("↓", rxVal, pal.TaskbarTextPrimary, pal.TaskbarTextPrimary, 48, 10.5));
                row.Children.Add(MakeSpacer(6));
                row.Children.Add(MakeFixedNetCell("↑", txVal, pal.TaskbarTextMuted, pal.TaskbarTextMuted, 48, 10.5));
            }

            root.Child = row;
            return root;
        }

        private static UIElement BuildMiniSparkline(List<double> history, Brush stroke, double width, double height)
        {
            var canvas = new Canvas { Width = width, Height = height, VerticalAlignment = VerticalAlignment.Center };
            var poly = new Polyline
            {
                Stroke = stroke,
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };

            int count = Math.Min(20, history.Count);
            if (count > 1)
            {
                int start = history.Count - count;
                for (int i = 0; i < count; i++)
                {
                    double x = (double)i / (count - 1) * width;
                    double val = Math.Max(0, Math.Min(100, history[start + i]));
                    double y = height - 2 - (val / 100.0) * (height - 4);
                    poly.Points.Add(new Point(x, y));
                }
            }
            canvas.Children.Add(poly);
            return canvas;
        }

        private static UIElement MakeTwoLineColumn(string topText, string botText, Brush topBrush, Brush botBrush, double width)
        {
            var sp = new StackPanel { Width = width, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(MakeText(topText, 9.0, topBrush, FontWeights.SemiBold));
            sp.Children.Add(MakeText(botText, 10.5, botBrush, FontWeights.SemiBold));
            return sp;
        }

        private static UIElement MakeFixedNetColumn(string topArrow, string topVal, string botArrow, string botVal, Brush topBrush, Brush botBrush, double width)
        {
            var sp = new StackPanel { Width = width, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(MakeFixedNetCell(topArrow, topVal, topBrush, topBrush, width, 9.5));
            sp.Children.Add(MakeFixedNetCell(botArrow, botVal, botBrush, botBrush, width, 9.5));
            return sp;
        }

        private static UIElement MakeFixedNetCell(string arrow, string val, Brush arrowBrush, Brush valBrush, double width, double fontSize)
        {
            var dp = new DockPanel { Width = width, VerticalAlignment = VerticalAlignment.Center };
            var arrowTb = MakeText(arrow, fontSize, arrowBrush, FontWeights.SemiBold);
            DockPanel.SetDock(arrowTb, Dock.Left);
            dp.Children.Add(arrowTb);

            var valTb = MakeText(val, fontSize, valBrush, FontWeights.SemiBold);
            valTb.HorizontalAlignment = HorizontalAlignment.Right;
            valTb.TextAlignment = TextAlignment.Right;
            dp.Children.Add(valTb);
            return dp;
        }

        private static UIElement MakeFixedMetricCell(string label, string val, Brush labelBrush, Brush valBrush, double width)
        {
            var dp = new DockPanel { Width = width, VerticalAlignment = VerticalAlignment.Center };
            var lblTb = MakeText(label, 10.5, labelBrush, FontWeights.Normal);
            DockPanel.SetDock(lblTb, Dock.Left);
            dp.Children.Add(lblTb);

            var valTb = MakeText(val, 10.5, valBrush, FontWeights.Bold);
            valTb.HorizontalAlignment = HorizontalAlignment.Right;
            valTb.TextAlignment = TextAlignment.Right;
            dp.Children.Add(valTb);
            return dp;
        }

        private static UIElement MakeUnderlinedMetricCell(string label, string val, Brush lineBrush, Brush textBrush, double width)
        {
            return new Border
            {
                Width = width,
                BorderBrush = lineBrush,
                BorderThickness = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(0, 0, 0, 1),
                Child = MakeFixedMetricCell(label, val, textBrush, textBrush, width)
            };
        }

        private static UIElement MakeFluentColumn(string label, Brush dotBrush, string valueText, ThemePalette pal, double width)
        {
            var sp = new StackPanel { Width = width, VerticalAlignment = VerticalAlignment.Center };
            var topRow = new StackPanel { Orientation = Orientation.Horizontal };
            topRow.Children.Add(MakeText(label + " ", 9.0, pal.TaskbarTextMuted, FontWeights.SemiBold));
            topRow.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = dotBrush, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(topRow);
            sp.Children.Add(MakeText(valueText, 10.5, pal.TaskbarTextPrimary, FontWeights.SemiBold));
            return sp;
        }

        private static UIElement MakeLiteBarColumn(string label, double pct, double? temp, Brush barBrush, ThemePalette pal, double width)
        {
            var sp = new StackPanel { Width = width, VerticalAlignment = VerticalAlignment.Center };
            var top = new DockPanel();
            var lbl = MakeText(label, 9.0, pal.TaskbarTextMuted, FontWeights.SemiBold);
            string rightStr = temp.HasValue ? string.Format("{0:0}% {1:0}°", pct, temp.Value) : string.Format("{0:0}%", pct);
            var val = MakeText(rightStr, 9.0, pal.TaskbarTextPrimary, FontWeights.Bold);
            DockPanel.SetDock(val, Dock.Right);
            top.Children.Add(val);
            top.Children.Add(lbl);
            sp.Children.Add(top);

            var track = new Border
            {
                Height = 3,
                Margin = new Thickness(0, 2, 0, 0),
                CornerRadius = new CornerRadius(1.5),
                Background = pal.TrackBackground,
                ClipToBounds = true
            };
            track.Child = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(2, width * Math.Min(1.0, Math.Max(0, pct / 100.0))),
                Background = barBrush,
                CornerRadius = new CornerRadius(1.5)
            };
            sp.Children.Add(track);
            return sp;
        }

        private static UIElement MakeMinimalPairColumn(
            string l1, double p1, Brush c1, string t1,
            string l2, double p2, Brush c2, string t2, ThemePalette pal, double width)
        {
            var sp = new StackPanel { Width = width, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(MakeRichPairLine(l1, p1, c1, t1, pal));
            sp.Children.Add(MakeRichPairLine(l2, p2, c2, t2, pal));
            return sp;
        }

        private static UIElement MakeRichPairLine(string label, double pct, Brush valBrush, string extra, ThemePalette pal)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(MakeText(label + " ", 10.5, pal.TaskbarTextPrimary, FontWeights.SemiBold));
            sp.Children.Add(MakeText(string.Format("{0:0}% ", pct), 10.5, valBrush, FontWeights.Bold));
            sp.Children.Add(MakeText(extra, 9.5, pal.TaskbarTextMuted, FontWeights.Normal));
            return sp;
        }

        private static void AddDot(StackPanel row, Brush brush)
        {
            row.Children.Add(new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = brush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
        }

        private static void AddSeparator(StackPanel row, string sep, Brush brush)
        {
            var tb = MakeText(" " + sep + " ", 10.5, brush, FontWeights.Normal);
            tb.Opacity = 0.55;
            row.Children.Add(tb);
        }

        private static TextBlock MakeText(string text, double size, Brush brush, FontWeight weight)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = MonoFont,
                FontSize = size,
                Foreground = brush,
                FontWeight = weight,
                VerticalAlignment = VerticalAlignment.Center
            };
            Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
            return tb;
        }

        private static FrameworkElement MakeSpacer(double w)
        {
            return new Border { Width = w };
        }
    }
}
