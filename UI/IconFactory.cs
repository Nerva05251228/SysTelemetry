using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SysTelemetry.UI
{
    internal static class IconFactory
    {
        public static Path CreateVectorIcon(string pathData, double size, System.Windows.Media.Brush strokeBrush, double strokeThickness = 1.7)
        {
            return new Path
            {
                Data = Geometry.Parse(pathData),
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                SnapsToDevicePixels = true
            };
        }

        // Icon paths normalized cleanly to 24x24 viewBox
        public const string DesktopOverlaySvg = "M3,4 H21 V18 H3 Z M13,7 H19 V10 H13 Z";
        public const string TrimBoltSvg = "M13,2 L4,14 H12 L11,22 L20,10 H12 Z";
        public const string TrafficSvg = "M7,4 V20 M3.5,7.5 L7,4 L10.5,7.5 M17,20 V4 M13.5,16.5 L17,20 L20.5,16.5";
        public const string GearSvg = "M12,8.5 A3.5,3.5 0 1 1 11.99,8.5 Z M12,2 V4.5 M12,19.5 V22 M4.9,4.9 L6.7,6.7 M17.3,17.3 L19.1,19.1 M2,12 H4.5 M19.5,12 H22 M4.9,19.1 L6.7,17.3 M17.3,6.7 L19.1,4.9";
        public const string CloseSvg = "M6,6 L18,18 M18,6 L6,18";

        // Settings row icons (24x24 viewBox, balanced bounds for Stretch.Uniform)
        public const string SkinSvg = "M12,3 L21,8 L12,13 L3,8 Z M3,13 L12,18 L21,13 M3,17.5 L12,22 L21,17.5";
        public const string ThemeSvg = "M20.2,14.5 A8.5,8.5 0 1 1 9.5,3.8 A6.6,6.6 0 0 0 20.2,14.5 Z";
        public const string DockSvg = "M3,4 H21 V20 H3 Z M3,15.5 H21 M7.5,18 H16.5";
        public const string OffsetSvg = "M12,4 V20 M4,12 H20 M7.5,8.5 L4,12 L7.5,15.5 M16.5,8.5 L20,12 L16.5,15.5";
        public const string ResetSvg = "M4,10 A8,8 0 1 1 6.5,17.5 M4,5 V10 H9";
        public const string LockSvg = "M5,11 H19 V21 H5 Z M8,11 V7.5 A4,4 0 0 1 16,7.5 V11";
        public const string OpacitySvg = "M12,3 L17.8,10.8 A7,7 0 1 1 6.2,10.8 Z";
        public const string RateSvg = "M2.5,12 H6.5 L9.5,4.5 L14.5,19.5 L17.5,12 H21.5";
        public const string TempSvg = "M10,13.5 V5 A2,2 0 0 1 14,5 V13.5 A4,4 0 1 1 10,13.5 Z M12,9 V16";
        public const string AutoStartSvg = "M12,3 V11.5 M6.8,6.5 A7.5,7.5 0 1 0 17.2,6.5";
        public const string FullscreenHideSvg = "M4,8.5 V4 H8.5 M15.5,4 H20 V8.5 M4,15.5 V20 H8.5 M15.5,20 H20 V15.5 M8,12 H16";
        public const string CalendarSvg = "M4,6 H20 V20 H4 Z M4,10.5 H20 M8,3 V6.5 M16,3 V6.5";
        public const string ProxySvg = "M12,3 L19,6 V12 C19,16.2 16,19.4 12,21 C8,19.4 5,16.2 5,12 V6 Z M9.5,12 L11.5,14 L15,10";
        public const string TrashSvg = "M4,7 H20 M9,4 H15 M7,7 L8,20 H16 L17,7 M10.5,11 V16.5 M13.5,11 V16.5";

        public static Icon CreateTrayIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);

                using (var bg = new SolidBrush(System.Drawing.Color.FromArgb(240, 18, 22, 30)))
                using (var border = new System.Drawing.Pen(System.Drawing.Color.FromArgb(140, 56, 189, 248), 1.6f))
                using (var path = CreateRoundedRect(2, 2, 28, 28, 6))
                {
                    g.FillPath(bg, path);
                    g.DrawPath(border, path);
                }

                using (var pulsePen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 56, 189, 248), 2.3f))
                {
                    pulsePen.LineJoin = LineJoin.Round;
                    pulsePen.StartCap = LineCap.Round;
                    pulsePen.EndCap = LineCap.Round;
                    g.DrawLines(pulsePen, new[]
                    {
                        new PointF(5f, 18f),
                        new PointF(10f, 18f),
                        new PointF(13f, 9f),
                        new PointF(17f, 23f),
                        new PointF(21f, 13f),
                        new PointF(24f, 18f),
                        new PointF(27f, 18f)
                    });
                }

                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private static GraphicsPath CreateRoundedRect(float x, float y, float w, float h, float r)
        {
            float d = r * 2;
            var gp = new GraphicsPath();
            gp.AddArc(x, y, d, d, 180, 90);
            gp.AddArc(x + w - d, y, d, d, 270, 90);
            gp.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            gp.AddArc(x, y + h - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }
    }
}
