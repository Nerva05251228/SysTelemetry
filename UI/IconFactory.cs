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
        public const string GearSvg = "M12,8.5 A3.5,3.5 0 1 1 11.99,8.5 Z M12,2 V4.5 M12,19.5 V22 M4.9,4.9 L6.7,6.7 M17.3,17.3 L19.1,19.1 M2,12 H4.5 M19.5,12 H22 M4.9,19.1 L6.7,17.3 M17.3,6.7 L19.1,4.9";
        public const string CloseSvg = "M6,6 L18,18 M18,6 L6,18";

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
