using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SysTelemetry.Models
{
    [DataContract]
    public sealed class DailyTrafficRecord
    {
        [DataMember] public string Date { get; set; }
        [DataMember] public long GlobalRx { get; set; }
        [DataMember] public long GlobalTx { get; set; }
        [DataMember] public long ProxyRx { get; set; }
        [DataMember] public long ProxyTx { get; set; }

        public long GlobalTotal { get { return GlobalRx + GlobalTx; } }
        public long ProxyTotal { get { return ProxyRx + ProxyTx; } }
        public long DirectTotal { get { return Math.Max(0L, GlobalTotal - ProxyTotal); } }
    }

    [DataContract]
    public sealed class TrafficStoreData
    {
        [DataMember] public int Version { get; set; }
        [DataMember] public List<DailyTrafficRecord> Days { get; set; }

        public TrafficStoreData()
        {
            Version = 1;
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
        public bool IsCurrent { get; set; }

        public long GlobalTotal { get { return GlobalRx + GlobalTx; } }
        public long ProxyTotal { get { return Math.Min(GlobalTotal, ProxyRx + ProxyTx); } }
        public long DirectRx { get { return Math.Max(0L, GlobalRx - ProxyRx); } }
        public long DirectTx { get { return Math.Max(0L, GlobalTx - ProxyTx); } }
        public long DirectTotal { get { return Math.Max(0L, GlobalTotal - ProxyTotal); } }
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
