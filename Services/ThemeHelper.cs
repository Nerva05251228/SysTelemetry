using System;
using System.Windows.Media;
using Microsoft.Win32;
using SysTelemetry.Models;

namespace SysTelemetry.Services
{
    public sealed class ThemePalette
    {
        public bool IsDark { get; set; }
        public Brush TaskbarTextPrimary { get; set; }
        public Brush TaskbarTextMuted { get; set; }
        public Brush TaskbarHoverBg { get; set; }
        public Brush CardBackground { get; set; }
        public Brush CardBorder { get; set; }
        public Brush PopoverBackground { get; set; }
        public Brush PopoverBorder { get; set; }
        public Brush SubSurfaceBackground { get; set; }
        public Brush SubSurfaceBorder { get; set; }
        public Brush GroupCardBackground { get; set; }
        public Brush GroupCardBorder { get; set; }
        public Brush HairlineBrush { get; set; }
        public Brush RowHoverBackground { get; set; }
        public Brush SegmentedTrackBackground { get; set; }
        public Brush SegmentedTrackBorder { get; set; }
        public Brush SegmentedPillBackground { get; set; }
        public Brush SegmentedPillBorder { get; set; }
        public Brush SegmentedInactiveText { get; set; }
        public Brush AccentPrimary { get; set; }
        public Brush HeaderActiveBackground { get; set; }
        public Brush SwitchOffBackground { get; set; }
        public Brush SwitchOnBackground { get; set; }
        public Brush KeycapBackground { get; set; }
        public Brush KeycapBorder { get; set; }
        public Brush TrackBackground { get; set; }
        public Brush DividerBrush { get; set; }
        public Brush AccentBlue { get; set; }
        public Brush AccentPurple { get; set; }
        public Brush AccentGreen { get; set; }
        public Brush ProxyBadgeBackground { get; set; }
        public Brush DirectBarBrush { get; set; }
        public Brush ProxyBarBrush { get; set; }
        public Brush InputBackground { get; set; }
        public Brush InputBorder { get; set; }
        public Brush StatusOk { get; set; }
        public Brush StatusWarn { get; set; }
        public Brush StatusHot { get; set; }
        public Brush OverlayTextShadow { get; set; }
    }

    public static class ThemeHelper
    {
        public static bool IsSystemTaskbarDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("SystemUsesLightTheme") ?? key.GetValue("AppsUseLightTheme");
                        if (val is int) return ((int)val) == 0;
                    }
                }
            }
            catch { }
            return true;
        }

        public static bool ResolveIsDark(ThemeMode mode)
        {
            if (mode == ThemeMode.Dark) return true;
            if (mode == ThemeMode.Light) return false;
            return IsSystemTaskbarDark();
        }

        public static ThemePalette GetPalette(ThemeMode mode)
        {
            bool dark = ResolveIsDark(mode);
            return new ThemePalette
            {
                IsDark = dark,
                TaskbarTextPrimary = BrushFromHex(dark ? "#F4F4F5" : "#18181B"),
                TaskbarTextMuted = BrushFromHex(dark ? "#A1A1AA" : "#52525B"),
                TaskbarHoverBg = BrushFromHex(dark ? "#14FFFFFF" : "#12000000"),
                CardBackground = BrushFromHex(dark ? "#12FFFFFF" : "#A0FFFFFF"),
                CardBorder = BrushFromHex(dark ? "#1CFFFFFF" : "#18000000"),
                PopoverBackground = BrushFromHex(dark ? "#EE101218" : "#F2FAFAFC"),
                PopoverBorder = BrushFromHex(dark ? "#22FFFFFF" : "#220F172A"),
                SubSurfaceBackground = BrushFromHex(dark ? "#1294A3B8" : "#1494A3B8"),
                SubSurfaceBorder = BrushFromHex(dark ? "#1E94A3B8" : "#2494A3B8"),
                GroupCardBackground = BrushFromHex(dark ? "#0DFFFFFF" : "#E6FFFFFF"),
                GroupCardBorder = BrushFromHex(dark ? "#16FFFFFF" : "#140F172A"),
                HairlineBrush = BrushFromHex(dark ? "#12FFFFFF" : "#100F172A"),
                RowHoverBackground = BrushFromHex(dark ? "#09FFFFFF" : "#060F172A"),
                SegmentedTrackBackground = BrushFromHex(dark ? "#36000000" : "#0E0F172A"),
                SegmentedTrackBorder = BrushFromHex(dark ? "#12FFFFFF" : "#0D0F172A"),
                SegmentedPillBackground = BrushFromHex(dark ? "#FF262C38" : "#FFFFFFFF"),
                SegmentedPillBorder = BrushFromHex(dark ? "#28FFFFFF" : "#1A0F172A"),
                SegmentedInactiveText = BrushFromHex(dark ? "#7E8696" : "#64748B"),
                AccentPrimary = BrushFromHex(dark ? "#38BDF8" : "#0284C7"),
                HeaderActiveBackground = BrushFromHex(dark ? "#2438BDF8" : "#1A0284C7"),
                SwitchOffBackground = BrushFromHex(dark ? "#28FFFFFF" : "#240F172A"),
                SwitchOnBackground = BrushFromHex(dark ? "#0EA5E9" : "#0284C7"),
                KeycapBackground = BrushFromHex(dark ? "#14FFFFFF" : "#FFFFFFFF"),
                KeycapBorder = BrushFromHex(dark ? "#22FFFFFF" : "#1E0F172A"),
                TrackBackground = BrushFromHex(dark ? "#3894A3B8" : "#3864748B"),
                DividerBrush = BrushFromHex(dark ? "#2294A3B8" : "#2494A3B8"),
                AccentBlue = BrushFromHex(dark ? "#38BDF8" : "#0284C7"),
                AccentPurple = BrushFromHex(dark ? "#A78BFA" : "#7C3AED"),
                AccentGreen = BrushFromHex(dark ? "#34D399" : "#059669"),
                ProxyBadgeBackground = BrushFromHex(dark ? "#28A78BFA" : "#207C3AED"),
                DirectBarBrush = BrushFromHex(dark ? "#FF38BDF8" : "#FF0284C7"),
                ProxyBarBrush = BrushFromHex(dark ? "#FFA78BFA" : "#FF7C3AED"),
                InputBackground = BrushFromHex(dark ? "#28000000" : "#FFFFFFFF"),
                InputBorder = BrushFromHex(dark ? "#26FFFFFF" : "#240F172A"),
                StatusOk = BrushFromHex("#10B981"),
                StatusWarn = BrushFromHex("#F59E0B"),
                StatusHot = BrushFromHex("#F43F5E"),
                OverlayTextShadow = BrushFromHex(dark ? "#CC000000" : "#CCFFFFFF")
            };
        }

        public static Brush GetStatusBrush(ThemePalette p, double pct, double? temp, int warnTemp)
        {
            if (pct >= 80.0 || (temp.HasValue && temp.Value >= warnTemp)) return p.StatusHot;
            if (pct >= 55.0 || (temp.HasValue && temp.Value >= warnTemp - 15)) return p.StatusWarn;
            return p.StatusOk;
        }

        public static string FormatNetSpeed(double bytesPerSec)
        {
            double kb = bytesPerSec / 1024.0;
            if (kb >= 1024.0 * 1024.0)
                return string.Format("{0:0.0}G", kb / (1024.0 * 1024.0));
            if (kb >= 1024.0)
            {
                double mb = kb / 1024.0;
                return mb >= 100.0 ? string.Format("{0:0}M", mb) : string.Format("{0:0.0}M", mb);
            }
            if (kb >= 100.0) return string.Format("{0:0}K", kb);
            if (kb >= 0.1) return string.Format("{0:0.0}K", kb);
            return "0.0K";
        }

        public static string FormatDataSize(long bytes, bool compact = false)
        {
            if (bytes <= 0) return compact ? "0B" : "0 B";
            const double kb = 1024.0;
            const double mb = kb * 1024.0;
            const double gb = mb * 1024.0;
            const double tb = gb * 1024.0;

            double v = bytes;
            if (v >= tb)
            {
                double t = v / tb;
                string fmt = t >= 100 ? "{0:0}" : (t >= 10 ? "{0:0.0}" : "{0:0.00}");
                return string.Format(fmt + (compact ? "T" : " TB"), t);
            }
            if (v >= gb)
            {
                double g = v / gb;
                string fmt = g >= 100 ? "{0:0.0}" : (g >= 10 ? "{0:0.0}" : "{0:0.00}");
                return string.Format(fmt + (compact ? "G" : " GB"), g);
            }
            if (v >= mb)
            {
                double m = v / mb;
                string fmt = m >= 100 ? "{0:0}" : "{0:0.0}";
                return string.Format(fmt + (compact ? "M" : " MB"), m);
            }
            if (v >= kb)
            {
                double k = v / kb;
                string fmt = k >= 100 ? "{0:0}" : "{0:0.0}";
                return string.Format(fmt + (compact ? "K" : " KB"), k);
            }
            return string.Format("{0}" + (compact ? "B" : " B"), bytes);
        }

        public static Brush BrushFromHex(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            if (b.CanFreeze) b.Freeze();
            return b;
        }
    }
}
