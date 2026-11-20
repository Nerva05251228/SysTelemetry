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
        public Brush TrackBackground { get; set; }
        public Brush DividerBrush { get; set; }
        public Brush AccentBlue { get; set; }
        public Brush AccentPurple { get; set; }
        public Brush AccentGreen { get; set; }
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
                TrackBackground = BrushFromHex(dark ? "#3894A3B8" : "#3864748B"),
                DividerBrush = BrushFromHex(dark ? "#2294A3B8" : "#2494A3B8"),
                AccentBlue = BrushFromHex("#38BDF8"),
                AccentPurple = BrushFromHex("#A78BFA"),
                AccentGreen = BrushFromHex("#34D399"),
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

        public static Brush BrushFromHex(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            if (b.CanFreeze) b.Freeze();
            return b;
        }
    }
}
