using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SysTelemetry.Models
{
    public enum SkinStyle
    {
        Fluent = 0,
        Lite = 1,
        Hud = 2,
        Minimal = 3
    }

    public enum ThemeMode
    {
        Auto = 0,
        Dark = 1,
        Light = 2
    }

    public enum TaskbarDockMode
    {
        Left = 0,
        Right = 1
    }

    [DataContract]
    public sealed class AppConfig
    {
        public const string DefaultProxyProcessNames = "sing-box,xray,mihomo,v2ray";

        [DataMember] public SkinStyle Skin { get; set; }
        [DataMember] public ThemeMode Theme { get; set; }
        [DataMember] public TaskbarDockMode TaskbarDock { get; set; }
        [DataMember] public bool DesktopOverlayEnabled { get; set; }
        [DataMember] public bool DesktopLocked { get; set; }
        [DataMember] public double DesktopLeft { get; set; }
        [DataMember] public double DesktopTop { get; set; }
        [DataMember] public double DesktopOpacity { get; set; }
        [DataMember] public bool FullscreenAutoHide { get; set; }
        [DataMember] public int PollIntervalMs { get; set; }
        [DataMember] public int TempWarnThreshold { get; set; }
        [DataMember] public int TaskbarOffsetX { get; set; }
        [DataMember] public int TrafficResetDay { get; set; }
        [DataMember] public string ProxyProcessNames { get; set; }
        [DataMember(EmitDefaultValue = false)] public string LocalProxyPorts { get; set; }
        [DataMember(EmitDefaultValue = false)] public string ProxyNodeEndpoints { get; set; }
        [DataMember(EmitDefaultValue = false)] public string ProxyController { get; set; }
        [DataMember(EmitDefaultValue = false)] public string[] ApplicationGroups { get; set; }

        public AppConfig()
        {
            Skin = SkinStyle.Hud;
            Theme = ThemeMode.Auto;
            TaskbarDock = TaskbarDockMode.Left;
            DesktopOverlayEnabled = true;
            DesktopLocked = true;
            DesktopLeft = double.NaN;
            DesktopTop = 16.0;
            DesktopOpacity = 0.92;
            FullscreenAutoHide = true;
            PollIntervalMs = 1000;
            TempWarnThreshold = 80;
            TaskbarOffsetX = 0;
            TrafficResetDay = 1;
            ProxyProcessNames = DefaultProxyProcessNames;
        }

        public static string ConfigFilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SysTelemetry");
                return Path.Combine(dir, "config.json");
            }
        }

        public static AppConfig Load()
        {
            try
            {
                string path = ConfigFilePath;
                if (!File.Exists(path)) return new AppConfig();
                string json = File.ReadAllText(path, Encoding.UTF8);
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppConfig));
                    var cfg = ser.ReadObject(ms) as AppConfig;
                    if (cfg == null) return new AppConfig();
                    cfg.Validate();
                    return cfg;
                }
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save()
        {
            try
            {
                Validate();
                string path = ConfigFilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var ms = new MemoryStream())
                {
                    var ser = new DataContractJsonSerializer(typeof(AppConfig));
                    ser.WriteObject(ms, this);
                    File.WriteAllText(path, Encoding.UTF8.GetString(ms.ToArray()), Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        private void Validate()
        {
            if (PollIntervalMs != 500 && PollIntervalMs != 1000 && PollIntervalMs != 2000)
                PollIntervalMs = 1000;
            if (DesktopOpacity < 0.35 || DesktopOpacity > 1.0)
                DesktopOpacity = 0.92;
            if (TempWarnThreshold < 60 || TempWarnThreshold > 100)
                TempWarnThreshold = 80;
            if (TaskbarOffsetX < -300 || TaskbarOffsetX > 300)
                TaskbarOffsetX = 0;
            if (TrafficResetDay < 1 || TrafficResetDay > 31)
                TrafficResetDay = 1;
            if (ProxyProcessNames == null)
                ProxyProcessNames = DefaultProxyProcessNames;
        }
    }
}
