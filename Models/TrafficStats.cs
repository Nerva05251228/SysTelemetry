using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SysTelemetry.Models
{
    [DataContract]
    public sealed class ApplicationTraffic
    {
        [DataMember] public long Received { get; set; }
        [DataMember] public long Sent { get; set; }
        [DataMember(EmitDefaultValue = false)] public long? LocalProxyBytes { get; set; }
        [DataMember(EmitDefaultValue = false)] public long DirectBytes { get; set; }
        [DataMember(EmitDefaultValue = false)] public long NodeBytes { get; set; }
        [DataMember(EmitDefaultValue = false)] public string ApplicationName { get; set; }
        [DataMember(EmitDefaultValue = false)] public Dictionary<string, ApplicationTraffic> Processes { get; set; }

        public long Total
        {
            get
            {
                if (Processes == null || Processes.Count == 0)
                    return Math.Max(Received + Sent, DirectBytes + NodeBytes);
                long total = 0;
                foreach (var process in Processes.Values)
                    total += process.Total;
                return total;
            }
        }
        public long UnclassifiedBytes { get { return Total - DirectBytes - NodeBytes; } }

        internal ApplicationTraffic GetProcessTraffic(ApplicationProcess process)
        {
            if (Processes == null)
                Processes = new Dictionary<string, ApplicationTraffic>(StringComparer.OrdinalIgnoreCase);
            ApplicationTraffic counter;
            if (!Processes.TryGetValue(process.ProcessKey, out counter))
            {
                counter = new ApplicationTraffic { ApplicationName = process.ProcessName };
                Processes.Add(process.ProcessKey, counter);
            }
            return counter;
        }

        internal static ApplicationTraffic GetAppTraffic(Dictionary<string, ApplicationTraffic> counters, ApplicationProcess process)
        {
            ApplicationTraffic counter;
            if (!counters.TryGetValue(process.AppKey, out counter))
            {
                counter = new ApplicationTraffic { ApplicationName = process.AppName };
                counters.Add(process.AppKey, counter);
            }
            return counter;
        }
    }

    internal sealed class ApplicationProcess
    {
        public string ProcessKey { get; private set; }
        public string ProcessName { get; private set; }
        public string AppKey { get; private set; }
        public string AppName { get; private set; }
        public string AppPath { get; private set; }
        public string Package { get; private set; }
        public long Created { get; private set; }

        public ApplicationProcess(string path, string name, string appPath, string package, long created)
        {
            ProcessKey = path ?? "process:" + name;
            ProcessName = name;
            AppPath = appPath ?? path;
            Package = package;
            AppKey = package != null ? "package:" + package : AppPath ?? ProcessKey;
            int publisherSeparator = package == null ? -1 : package.LastIndexOf('_');
            AppName = package != null ? (publisherSeparator > 0 ? package.Substring(0, publisherSeparator) : package) :
                AppPath != null ? System.IO.Path.GetFileNameWithoutExtension(AppPath) : name;
            Created = created;
        }
    }

    [DataContract]
    public sealed class DailyTrafficRecord
    {
        [DataMember] public string Date { get; set; }
        [DataMember] public long GlobalRx { get; set; }
        [DataMember] public long GlobalTx { get; set; }
        [DataMember] public long ProxyRx { get; set; }
        [DataMember] public long ProxyTx { get; set; }
        [DataMember(EmitDefaultValue = false)] public long NodeRx { get; set; }
        [DataMember(EmitDefaultValue = false)] public long NodeTx { get; set; }
        [DataMember(EmitDefaultValue = false)] public long ClassifiedRx { get; set; }
        [DataMember(EmitDefaultValue = false)] public long ClassifiedTx { get; set; }
        [DataMember(EmitDefaultValue = false)] public Dictionary<string, ApplicationTraffic> Applications { get; set; }

    }

    [DataContract]
    public sealed class TrafficStoreData
    {
        [DataMember] public int Version { get; set; }
        [DataMember] public List<DailyTrafficRecord> Days { get; set; }

        public TrafficStoreData()
        {
            Version = 3;
            Days = new List<DailyTrafficRecord>();
        }
    }

    public sealed class TrafficPeriodSummary
    {
        public string Label { get; set; }
        public string ShortLabel { get; set; }
        public string RangeText { get; set; }
        public long GlobalRx { get; set; }
        public long GlobalTx { get; set; }
        public long ProxyRx { get; set; }
        public long ProxyTx { get; set; }
        public long ClassifiedRx { get; set; }
        public long ClassifiedTx { get; set; }
        public bool IsCurrent { get; set; }

        public long GlobalTotal { get { return GlobalRx + GlobalTx; } }
        public long ProxyTotal { get { return ProxyRx + ProxyTx; } }
        public long DirectTotal
        {
            get { return Math.Max(0, ClassifiedRx - ProxyRx) + Math.Max(0, ClassifiedTx - ProxyTx); }
        }
        public long UnclassifiedTotal { get { return Math.Max(0, GlobalTotal - ProxyTotal - DirectTotal); } }
        public double ProxyRatio
        {
            get
            {
                long g = GlobalTotal;
                if (g <= 0) return 0.0;
                return Math.Max(0.0, Math.Min(1.0, (double)ProxyTotal / g));
            }
        }
    }

    public sealed class TrafficSnapshot
    {
        public TrafficPeriodSummary Today { get; set; }
        public TrafficPeriodSummary CurrentCycle { get; set; }
        public List<TrafficPeriodSummary> DailyHistory { get; set; }
        public List<TrafficPeriodSummary> MonthlyHistory { get; set; }
        public bool EtwActive { get; set; }
        public string EtwStatusText { get; set; }
        public int ResetDay { get; set; }

        public TrafficSnapshot()
        {
            Today = new TrafficPeriodSummary { Label = DateTime.Now.ToString("yyyy-MM-dd"), ShortLabel = "今日", IsCurrent = true };
            CurrentCycle = new TrafficPeriodSummary { Label = DateTime.Now.ToString("yyyy-MM"), ShortLabel = "本月", IsCurrent = true };
            DailyHistory = new List<TrafficPeriodSummary>();
            MonthlyHistory = new List<TrafficPeriodSummary>();
            EtwStatusText = string.Empty;
            ResetDay = 1;
        }
    }
}
