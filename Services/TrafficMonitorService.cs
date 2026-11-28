using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using SysTelemetry.Models;

namespace SysTelemetry.Services
{
    public sealed class TrafficMonitorService : IDisposable
    {
        private const int MaxRetentionDays = 400;
        private const int DailyHistoryCount = 14;
        private const int MonthlyHistoryCount = 12;
        private const double SaveIntervalSeconds = 30.0;
        private const double PidRefreshIntervalSeconds = 2.0;

        private static readonly string[] VirtualAdapterKeywords = new string[]
        {
            "wintun", "sing-tun", "singbox", "mihomo", "clash", "tap-windows", "tap adapter",
            "verykuai", "tailscale", "wireguard", "zerotier", "vethernet", "vmware",
            "virtualbox", "hyper-v", "wsl", "teredo", "isatap", "6to4", "miniport",
            "loopback", "filter", "qos packet scheduler", "kernel debug", "bluetooth",
            "utun", "tun ", "openvpn", "hamachi", "radmin"
        };

        private readonly AppConfig _config;
        private readonly object _storeLock = new object();
        private readonly object _saveLock = new object();
        private readonly Dictionary<string, DailyTrafficRecord> _days = new Dictionary<string, DailyTrafficRecord>(StringComparer.Ordinal);
        private readonly EtwNetworkTracker _etw;
        private readonly ApplicationProcessCatalog _processCatalog = new ApplicationProcessCatalog();
        private readonly SingBoxTrafficRoutes _routes = new SingBoxTrafficRoutes();

        private DateTime _lastSaveUtc = DateTime.UtcNow;
        private DateTime _lastPidRefreshUtc = DateTime.MinValue;
        private bool _dirty;
        private bool _disposed;

        public static string TrafficFilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SysTelemetry");
                return Path.Combine(dir, "traffic.json");
            }
        }

        public TrafficMonitorService(AppConfig config)
        {
            _config = config;
            LoadFromDisk();
            _etw = new EtwNetworkTracker(_processCatalog);
            RefreshProxyProcesses(force: true);
            _etw.Start();
        }

        public static void ReadPhysicalNetworkBytes(out long totalRx, out long totalTx)
        {
            totalRx = 0;
            totalTx = 0;

            try
            {
                var all = NetworkInterface.GetAllNetworkInterfaces();
                long strictRx = 0;
                long strictTx = 0;
                int strictCount = 0;

                long fallbackRx = 0;
                long fallbackTx = 0;

                foreach (var ni in all)
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    // 53 = IF_TYPE_PROP_VIRTUAL (e.g., singbox_tun / Wintun)
                    if ((int)ni.NetworkInterfaceType == 53) continue;

                    IPInterfaceStatistics ipStats;
                    try { ipStats = ni.GetIPStatistics(); }
                    catch { continue; }

                    long rx = ipStats.BytesReceived;
                    long tx = ipStats.BytesSent;
                    if (rx < 0) rx = 0;
                    if (tx < 0) tx = 0;

                    fallbackRx += rx;
                    fallbackTx += tx;

                    if (!LooksLikeVirtualAdapter(ni.Name, ni.Description))
                    {
                        strictRx += rx;
                        strictTx += tx;
                        strictCount++;
                    }
                }

                if (strictCount > 0)
                {
                    totalRx = strictRx;
                    totalTx = strictTx;
                }
                else
                {
                    totalRx = fallbackRx;
                    totalTx = fallbackTx;
                }
            }
            catch { }
        }

        private static bool LooksLikeVirtualAdapter(string name, string description)
        {
            string combined = ((name ?? string.Empty) + " " + (description ?? string.Empty)).ToLowerInvariant();
            for (int i = 0; i < VirtualAdapterKeywords.Length; i++)
            {
                if (combined.IndexOf(VirtualAdapterKeywords[i], StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        public void ReloadProxyConfig()
        {
            RefreshProxyProcesses(force: true);
        }

        public TrafficSnapshot RecordSampleAndBuildSnapshot(long globalDeltaRx, long globalDeltaTx)
        {
            if (_disposed) return new TrafficSnapshot();

            RefreshProxyProcesses(force: false);

            // Guard against spurious negative deltas or impossible counter jumps (> 50 GB in one tick)
            const long maxTickBytes = 50L * 1024 * 1024 * 1024;
            if (globalDeltaRx < 0 || globalDeltaRx > maxTickBytes) globalDeltaRx = 0;
            if (globalDeltaTx < 0 || globalDeltaTx > maxTickBytes) globalDeltaTx = 0;

            DateTime now = DateTime.Now;
            string todayKey = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            TrafficSnapshot snap;
            bool shouldSave = false;

            lock (_storeLock)
            {
                _routes.Refresh(_config.ProxyNodeEndpoints, _config.ProxyController);
                _etw.Outbounds = _routes.Outbounds;
                long proxyDeltaRx, proxyDeltaTx, unknownRx, unknownTx;
                var applications = _etw.DrainBytes(out proxyDeltaRx, out proxyDeltaTx, out unknownRx, out unknownTx);
                _routes.Collect(applications, _processCatalog);
                if (proxyDeltaRx < 0 || proxyDeltaRx > maxTickBytes) proxyDeltaRx = 0;
                if (proxyDeltaTx < 0 || proxyDeltaTx > maxTickBytes) proxyDeltaTx = 0;

                DailyTrafficRecord rec;
                if (!_days.TryGetValue(todayKey, out rec))
                {
                    rec = new DailyTrafficRecord { Date = todayKey };
                    _days[todayKey] = rec;
                    PruneOldRecordsLocked(now.Date);
                    _dirty = true;
                }

                if (applications.Count > 0)
                {
                    if (rec.Applications == null)
                        rec.Applications = new Dictionary<string, ApplicationTraffic>(StringComparer.OrdinalIgnoreCase);
                    AddApplicationTraffic(rec.Applications, applications);
                    _dirty = true;
                }

                if (globalDeltaRx > 0 || globalDeltaTx > 0 || proxyDeltaRx > 0 || proxyDeltaTx > 0)
                {
                    rec.GlobalRx += globalDeltaRx;
                    rec.GlobalTx += globalDeltaTx;
                    rec.NodeRx += proxyDeltaRx;
                    rec.NodeTx += proxyDeltaTx;
                    if (_etw.IsRunning)
                    {
                        rec.ClassifiedRx += Math.Max(0, globalDeltaRx - unknownRx);
                        rec.ClassifiedTx += Math.Max(0, globalDeltaTx - unknownTx);
                    }

                    _dirty = true;
                }

                if (_dirty && (DateTime.UtcNow - _lastSaveUtc).TotalSeconds >= SaveIntervalSeconds)
                {
                    shouldSave = true;
                }

                snap = BuildSnapshotLocked(now.Date);
            }

            if (shouldSave) SaveToDisk();
            return snap;
        }

        public TrafficSnapshot GetCurrentSnapshot()
        {
            lock (_storeLock)
            {
                return BuildSnapshotLocked(DateTime.Now.Date);
            }
        }

        public List<KeyValuePair<string, ApplicationTraffic>> GetApplicationTraffic(bool currentCycle)
        {
            DateTime today = DateTime.Today;
            int resetDay = _config.TrafficResetDay;
            DateTime month = GetCurrentCycleAnchorMonth(today, resetDay);
            DateTime start = currentCycle ? GetCycleStartForMonth(month.Year, month.Month, resetDay) : today;
            var totals = new Dictionary<string, ApplicationTraffic>(StringComparer.OrdinalIgnoreCase);
            lock (_storeLock)
            {
                for (DateTime day = start; day <= today; day = day.AddDays(1))
                {
                    DailyTrafficRecord record;
                    if (_days.TryGetValue(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out record) &&
                        record.Applications != null)
                        AddApplicationTraffic(totals, record.Applications);
                }
            }
            return totals.OrderByDescending(item => item.Value.Total)
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddApplicationTraffic(Dictionary<string, ApplicationTraffic> totals,
            Dictionary<string, ApplicationTraffic> samples)
        {
            foreach (var item in samples)
            {
                if (string.IsNullOrWhiteSpace(item.Key) || item.Value == null) continue;
                ApplicationTraffic total;
                if (!totals.TryGetValue(item.Key, out total))
                {
                    total = new ApplicationTraffic { ApplicationName = item.Value.ApplicationName };
                    totals.Add(item.Key, total);
                }
                total.Received += Math.Max(0L, item.Value.Received);
                total.Sent += Math.Max(0L, item.Value.Sent);
                total.DirectBytes += Math.Max(0L, item.Value.DirectBytes);
                total.NodeBytes += Math.Max(0L, item.Value.NodeBytes);
                if (item.Value.LocalProxyBytes.HasValue)
                    total.LocalProxyBytes = total.LocalProxyBytes.GetValueOrDefault() +
                        Math.Max(0L, item.Value.LocalProxyBytes.Value);
                if (item.Value.Processes != null)
                {
                    if (total.Processes == null)
                        total.Processes = new Dictionary<string, ApplicationTraffic>(StringComparer.OrdinalIgnoreCase);
                    AddApplicationTraffic(total.Processes, item.Value.Processes);
                }
            }
        }

        public void ResetToday()
        {
            string todayKey = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            lock (_storeLock)
            {
                long discarded;
                _etw.DrainBytes(out discarded, out discarded, out discarded, out discarded);
                _routes.Reset();
                _days[todayKey] = new DailyTrafficRecord { Date = todayKey };
                _dirty = true;
            }
            SaveToDisk();
        }

        public void ClearAllHistory()
        {
            string todayKey = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            lock (_storeLock)
            {
                long discarded;
                _etw.DrainBytes(out discarded, out discarded, out discarded, out discarded);
                _routes.Reset();
                _days.Clear();
                _days[todayKey] = new DailyTrafficRecord { Date = todayKey };
                _dirty = true;
            }
            SaveToDisk();
        }

        public void Flush()
        {
            SaveToDisk();
        }

        private void RefreshProxyProcesses(bool force)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (!force && (nowUtc - _lastPidRefreshUtc).TotalSeconds < PidRefreshIntervalSeconds)
                return;
            _lastPidRefreshUtc = nowUtc;

            var names = ParseProxyProcessNames(_config.ProxyProcessNames);
            _etw.UpdateProcesses(names, ReadLocalProxyPorts(_config.LocalProxyPorts), _config.ApplicationGroups);
        }

        private static HashSet<string> ParseProxyProcessNames(string raw)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw)) return set;

            string[] parts = raw.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed.Substring(0, trimmed.Length - 4);
                if (!string.IsNullOrWhiteSpace(trimmed))
                    set.Add(trimmed);
            }
            return set;
        }

        private static HashSet<int> ReadLocalProxyPorts(string additionalPorts)
        {
            var ports = new HashSet<int>();
            foreach (string value in (additionalPorts ?? string.Empty).Split(',', ' ', ';'))
            {
                int port;
                if (int.TryParse(value, out port) && port > 0 && port <= 65535)
                    ports.Add(port);
            }

            try
            {
                // The saved Windows endpoint is also useful for apps with their own proxy settings.
                string server = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings",
                    "ProxyServer", null) as string;
                foreach (string entry in (server ?? string.Empty).Split(';'))
                {
                    string endpoint = entry.Substring(entry.IndexOf('=') + 1).Trim();
                    if (endpoint.IndexOf("://", StringComparison.Ordinal) < 0)
                        endpoint = "http://" + endpoint;
                    Uri uri;
                    if (Uri.TryCreate(endpoint, UriKind.Absolute, out uri) && uri.IsLoopback && uri.Port > 0)
                        ports.Add(uri.Port);
                }
            }
            catch { }
            return ports;
        }

        private TrafficSnapshot BuildSnapshotLocked(DateTime today)
        {
            int resetDay = Math.Max(1, Math.Min(31, _config.TrafficResetDay));
            var snap = new TrafficSnapshot
            {
                ResetDay = resetDay,
                EtwActive = _etw.IsRunning,
                EtwStatusText = !_etw.IsRunning ? _etw.StatusText :
                    _routes.ControllerAvailable ? "ETW · 应用路由已连接" : "ETW · 应用路由未连接"
            };

            string todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            DailyTrafficRecord todayRec;
            _days.TryGetValue(todayKey, out todayRec);
            snap.Today = MakePeriodSummary(
                todayKey,
                "今日",
                todayKey,
                todayRec != null ? todayRec.GlobalRx : 0,
                todayRec != null ? todayRec.GlobalTx : 0,
                todayRec != null ? todayRec.NodeRx : 0,
                todayRec != null ? todayRec.NodeTx : 0,
                todayRec != null ? todayRec.ClassifiedRx : 0,
                todayRec != null ? todayRec.ClassifiedTx : 0,
                true);

            // Daily history: last 14 days (oldest -> newest)
            for (int i = DailyHistoryCount - 1; i >= 0; i--)
            {
                DateTime d = today.AddDays(-i);
                string key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                DailyTrafficRecord rec;
                _days.TryGetValue(key, out rec);
                string shortLbl = d.ToString("MM-dd", CultureInfo.InvariantCulture);
                snap.DailyHistory.Add(MakePeriodSummary(
                    i == 0 ? shortLbl + " 今日" : shortLbl,
                    d.ToString("dd", CultureInfo.InvariantCulture),
                    key,
                    rec != null ? rec.GlobalRx : 0,
                    rec != null ? rec.GlobalTx : 0,
                    rec != null ? rec.NodeRx : 0,
                    rec != null ? rec.NodeTx : 0,
                    rec != null ? rec.ClassifiedRx : 0,
                    rec != null ? rec.ClassifiedTx : 0,
                    i == 0));
            }

            // Monthly / billing-cycle history: last 12 cycles (oldest -> newest)
            DateTime anchorMonth = GetCurrentCycleAnchorMonth(today, resetDay);
            for (int i = MonthlyHistoryCount - 1; i >= 0; i--)
            {
                DateTime m = anchorMonth.AddMonths(-i);
                DateTime start = GetCycleStartForMonth(m.Year, m.Month, resetDay);
                DateTime nextM = m.AddMonths(1);
                DateTime end = GetCycleStartForMonth(nextM.Year, nextM.Month, resetDay).AddDays(-1);

                long gRx = 0, gTx = 0, pRx = 0, pTx = 0, cRx = 0, cTx = 0;
                for (DateTime d = start; d <= end; d = d.AddDays(1))
                {
                    DailyTrafficRecord rec;
                    if (_days.TryGetValue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out rec))
                    {
                        gRx += rec.GlobalRx;
                        gTx += rec.GlobalTx;
                        pRx += rec.NodeRx;
                        pTx += rec.NodeTx;
                        cRx += rec.ClassifiedRx;
                        cTx += rec.ClassifiedTx;
                    }
                }

                string label;
                string shortLabel;
                string rangeText = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:MM/dd}–{1:MM/dd}",
                    start, end);

                if (resetDay == 1)
                {
                    label = start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    shortLabel = start.Month + "月";
                }
                else
                {
                    label = rangeText;
                    shortLabel = start.ToString("M/d", CultureInfo.InvariantCulture);
                }

                var summary = MakePeriodSummary(label, shortLabel, rangeText, gRx, gTx, pRx, pTx, cRx, cTx, i == 0);
                snap.MonthlyHistory.Add(summary);
                if (i == 0) snap.CurrentCycle = summary;
            }

            return snap;
        }

        private static TrafficPeriodSummary MakePeriodSummary(
            string label, string shortLabel, string rangeText,
            long gRx, long gTx, long pRx, long pTx, long cRx, long cTx, bool isCurrent)
        {
            return new TrafficPeriodSummary
            {
                Label = label,
                ShortLabel = shortLabel,
                RangeText = rangeText,
                GlobalRx = Math.Max(0L, gRx),
                GlobalTx = Math.Max(0L, gTx),
                ProxyRx = Math.Max(0L, pRx),
                ProxyTx = Math.Max(0L, pTx),
                ClassifiedRx = Math.Min(gRx, Math.Max(0L, cRx)),
                ClassifiedTx = Math.Min(gTx, Math.Max(0L, cTx)),
                IsCurrent = isCurrent
            };
        }

        public static DateTime GetCurrentCycleAnchorMonth(DateTime today, int resetDay)
        {
            DateTime thisMonthStart = GetCycleStartForMonth(today.Year, today.Month, resetDay);
            if (today >= thisMonthStart)
                return new DateTime(today.Year, today.Month, 1);
            return new DateTime(today.Year, today.Month, 1).AddMonths(-1);
        }

        public static DateTime GetCycleStartForMonth(int year, int month, int resetDay)
        {
            int day = Math.Max(1, Math.Min(resetDay, DateTime.DaysInMonth(year, month)));
            return new DateTime(year, month, day);
        }

        private void PruneOldRecordsLocked(DateTime today)
        {
            DateTime cutoff = today.AddDays(-MaxRetentionDays);
            var stale = new List<string>();
            foreach (string key in _days.Keys)
            {
                DateTime dt;
                if (!DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) || dt < cutoff)
                    stale.Add(key);
            }
            for (int i = 0; i < stale.Count; i++)
                _days.Remove(stale[i]);
        }

        private void LoadFromDisk()
        {
            try
            {
                string path = TrafficFilePath;
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return;

                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var ser = CreateTrafficSerializer();
                    var data = ser.ReadObject(ms) as TrafficStoreData;
                    if (data == null || data.Days == null) return;

                    lock (_storeLock)
                    {
                        _days.Clear();
                        foreach (var d in data.Days)
                        {
                            if (d == null || string.IsNullOrWhiteSpace(d.Date)) continue;
                            _days[d.Date] = new DailyTrafficRecord
                            {
                                Date = d.Date,
                                GlobalRx = Math.Max(0L, d.GlobalRx),
                                GlobalTx = Math.Max(0L, d.GlobalTx),
                                ProxyRx = Math.Max(0L, d.ProxyRx),
                                ProxyTx = Math.Max(0L, d.ProxyTx),
                                NodeRx = Math.Max(0L, d.NodeRx),
                                NodeTx = Math.Max(0L, d.NodeTx),
                                ClassifiedRx = Math.Max(0L, d.ClassifiedRx),
                                ClassifiedTx = Math.Max(0L, d.ClassifiedTx),
                                Applications = d.Applications
                            };
                        }
                        PruneOldRecordsLocked(DateTime.Now.Date);
                    }
                }
            }
            catch { }
        }

        private void SaveToDisk()
        {
            lock (_saveLock)
            {
                try
                {
                    string path = TrafficFilePath;
                    string dir = Path.GetDirectoryName(path);
                    Directory.CreateDirectory(dir);
                    string tmp = path + ".tmp";
                    using (var ms = new MemoryStream())
                    {
                        lock (_storeLock)
                        {
                            var payload = new TrafficStoreData
                            {
                                Days = _days.Values.OrderBy(x => x.Date, StringComparer.Ordinal).ToList()
                            };
                            var ser = CreateTrafficSerializer();
                            ser.WriteObject(ms, payload);
                            _dirty = false;
                            _lastSaveUtc = DateTime.UtcNow;
                        }
                        File.WriteAllText(tmp, Encoding.UTF8.GetString(ms.ToArray()), Encoding.UTF8);
                    }
                    if (File.Exists(path))
                        File.Replace(tmp, path, null, true);
                    else
                        File.Move(tmp, path);
                }
                catch
                {
                    lock (_storeLock)
                    {
                        _dirty = true;
                        _lastSaveUtc = DateTime.UtcNow;
                    }
                }
            }
        }

        private static DataContractJsonSerializer CreateTrafficSerializer()
        {
            return new DataContractJsonSerializer(typeof(TrafficStoreData),
                new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = int.MaxValue });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _etw.Dispose(); } catch { }
            SaveToDisk();
        }

        /// <summary>
        /// Zero-dependency real-time ETW consumer for Microsoft-Windows-Kernel-Network.
        /// Collects TCP/UDP traffic by executable and preserves the proxy-core totals.
        /// </summary>
        private sealed class EtwNetworkTracker : IDisposable
        {
            private sealed class ProcessLookup
            {
                public readonly HashSet<string> ProxyNames;
                public readonly HashSet<int> LocalProxyPorts;
                public readonly HashSet<uint> ProxyListeners;

                public ProcessLookup(HashSet<string> proxyNames, HashSet<int> localProxyPorts)
                {
                    ProxyNames = proxyNames;
                    LocalProxyPorts = localProxyPorts;
                    ProxyListeners = FindProxyListeners(localProxyPorts);
                }
            }

            private const string SessionName = "SysTelemetry_NetTraffic_ETW";
            private static readonly Guid KernelNetworkProviderGuid = new Guid("7dd42a49-5329-4832-8dfd-43d979153a88");
            private static readonly Guid KernelProcessProviderGuid = new Guid("22fb2cd6-0e7b-422b-a0c7-2fad1fd0e716");
            private const uint KernelNetworkGuidData1 = 0x7dd42a49;
            private const uint KernelProcessGuidData1 = 0x22fb2cd6;

            private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
            private const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
            private const uint EVENT_TRACE_CONTROL_STOP = 1;
            private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
            private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
            private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
            private const byte TRACE_LEVEL_INFORMATION = 4;
            private const ulong KEYWORD_IPV4_IPV6 = 0x30;
            private const ulong KEYWORD_PROCESS = 0x10;
            private const uint ERROR_ALREADY_EXISTS = 183;
            private const uint ERROR_ACCESS_DENIED = 5;
            private static readonly ulong INVALID_PROCESSTRACE_HANDLE = unchecked((ulong)(-1));

            [StructLayout(LayoutKind.Sequential)]
            private struct WNODE_HEADER
            {
                public uint BufferSize;
                public uint ProviderId;
                public ulong HistoricalContext;
                public long TimeStamp;
                public Guid Guid;
                public uint ClientContext;
                public uint Flags;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct EVENT_TRACE_PROPERTIES
            {
                public WNODE_HEADER Wnode;
                public uint BufferSize;
                public uint MinimumBuffers;
                public uint MaximumBuffers;
                public uint MaximumFileSize;
                public uint LogFileMode;
                public uint FlushTimer;
                public uint EnableFlags;
                public int AgeLimit;
                public uint NumberOfBuffers;
                public uint FreeBuffers;
                public uint EventsLost;
                public uint BuffersWritten;
                public uint LogBuffersLost;
                public uint RealTimeBuffersLost;
                public IntPtr LoggerThreadId;
                public uint LogFileNameOffset;
                public uint LoggerNameOffset;
            }

            [StructLayout(LayoutKind.Sequential, Size = 172)]
            private struct TIME_ZONE_INFORMATION_RAW
            {
                public int Bias;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct TRACE_LOGFILE_HEADER
            {
                public uint BufferSize;
                public uint Version;
                public uint ProviderVersion;
                public uint NumberOfProcessors;
                public long EndTime;
                public uint TimerResolution;
                public uint MaximumFileSize;
                public uint LogFileMode;
                public uint BuffersWritten;
                public Guid LogInstanceGuid;
                public IntPtr LoggerName;
                public IntPtr LogFileName;
                public TIME_ZONE_INFORMATION_RAW TimeZone;
                public long BootTime;
                public long PerfFreq;
                public long StartTime;
                public uint ReservedFlags;
                public uint BuffersLost;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct EVENT_TRACE_HEADER
            {
                public ushort Size;
                public ushort HeaderType;
                public uint Class;
                public uint ThreadId;
                public uint ProcessId;
                public long TimeStamp;
                public Guid Guid;
                public ulong ProcessorTime;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct EVENT_TRACE
            {
                public EVENT_TRACE_HEADER Header;
                public uint InstanceId;
                public uint ParentInstanceId;
                public Guid ParentGuid;
                public IntPtr MofData;
                public uint MofLength;
                public uint ClientContext;
            }

            private delegate void EventRecordCallback(IntPtr eventRecord);

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct EVENT_TRACE_LOGFILEW
            {
                [MarshalAs(UnmanagedType.LPWStr)]
                public string LogFileName;
                [MarshalAs(UnmanagedType.LPWStr)]
                public string LoggerName;
                public long CurrentTime;
                public uint BuffersRead;
                public uint ProcessTraceMode;
                public EVENT_TRACE CurrentEvent;
                public TRACE_LOGFILE_HEADER LogfileHeader;
                public IntPtr BufferCallback;
                public uint BufferSize;
                public uint Filled;
                public uint EventsLost;
                public EventRecordCallback EventCallback;
                public uint IsKernelTrace;
                public IntPtr Context;
            }

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
            private static extern uint StartTraceW(out ulong sessionHandle, string sessionName, IntPtr properties);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
            private static extern uint ControlTraceW(ulong sessionHandle, string sessionName, IntPtr properties, uint controlCode);

            [DllImport("advapi32.dll")]
            private static extern uint EnableTraceEx2(
                ulong traceHandle,
                ref Guid providerId,
                uint controlCode,
                byte level,
                ulong matchAnyKeyword,
                ulong matchAllKeyword,
                uint timeout,
                IntPtr enableParameters);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
            private static extern ulong OpenTraceW(ref EVENT_TRACE_LOGFILEW logfile);

            [DllImport("advapi32.dll")]
            private static extern uint ProcessTrace(
                ulong[] handleArray,
                uint handleCount,
                IntPtr startTime,
                IntPtr endTime);

            [DllImport("advapi32.dll")]
            private static extern uint CloseTrace(ulong traceHandle);

            [DllImport("iphlpapi.dll")]
            private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order,
                int addressFamily, int tableClass, uint reserved);

            [StructLayout(LayoutKind.Sequential)]
            private struct PropertyDataDescriptor
            {
                public IntPtr PropertyName;
                public uint ArrayIndex;
                public uint Reserved;
            }

            [DllImport("tdh.dll")]
            private static extern uint TdhGetPropertySize(IntPtr record, uint contextCount, IntPtr context,
                uint propertyCount, ref PropertyDataDescriptor property, out uint size);

            [DllImport("tdh.dll")]
            private static extern uint TdhGetProperty(IntPtr record, uint contextCount, IntPtr context,
                uint propertyCount, ref PropertyDataDescriptor property, uint size, [Out] byte[] value);

            private ulong _sessionHandle;
            private ulong _traceHandle = INVALID_PROCESSTRACE_HANDLE;
            private Thread _workerThread;
            private EventRecordCallback _callbackDelegate;
            private volatile bool _running;
            private volatile bool _stopping;

            private volatile ProcessLookup _processes;
            private readonly ApplicationProcessCatalog _catalog;
            private static readonly ApplicationProcess UnknownProcess =
                new ApplicationProcess(null, "未知进程", null, null, 0);
            private volatile string _activeProxyNames = string.Empty;
            private volatile string _errorReason = string.Empty;

            private readonly object _pendingLock = new object();
            private Dictionary<string, ApplicationTraffic> _pendingApplications =
                new Dictionary<string, ApplicationTraffic>(StringComparer.OrdinalIgnoreCase);
            private long _pendingRx;
            private long _pendingTx;
            private long _unknownRx;
            private long _unknownTx;
            public volatile SingBoxTrafficRoutes.OutboundMap Outbounds;

            public EtwNetworkTracker(ApplicationProcessCatalog catalog)
            {
                _catalog = catalog;
            }

            public bool IsRunning { get { return _running; } }

            public string StatusText
            {
                get
                {
                    if (!_running)
                        return string.IsNullOrEmpty(_errorReason) ? "应用统计未就绪" : _errorReason;
                    string active = _activeProxyNames;
                    if (!string.IsNullOrEmpty(active))
                        return "ETW · " + active;
                    var processes = _processes;
                    return processes == null || processes.ProxyNames.Count == 0
                        ? "ETW · 未配置代理进程"
                        : "ETW · 未检测到代理进程";
                }
            }

            public void UpdateProcesses(HashSet<string> targetNames, HashSet<int> localProxyPorts, string[] groups)
            {
                var lookup = new ProcessLookup(targetNames, localProxyPorts);
                var foundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var process in _catalog.Refresh(groups).Values)
                {
                    if (targetNames.Contains(process.ProcessName)) foundNames.Add(process.ProcessName);
                }

                _processes = lookup;
                _activeProxyNames = foundNames.Count > 0
                    ? string.Join(", ", foundNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    : string.Empty;
            }

            private void RememberProcessStart(IntPtr record)
            {
                int length = (ushort)Marshal.ReadInt16(record, 86);
                IntPtr data = Marshal.ReadIntPtr(record, 96);
                if (length < sizeof(uint) || data == IntPtr.Zero) return;

                // Windows has multiple versions of this event. Resolve the name using its manifest.
                byte[] image = ReadEventProperty(record, "ImageName", length);
                byte[] parent = ReadEventProperty(record, "ParentProcessID", length);
                byte[] created = ReadEventProperty(record, "CreateTime", length);
                if (image == null || image.Length < 2 || created == null || created.Length != sizeof(long)) return;
                uint parentPid = parent != null && parent.Length == sizeof(uint) ? BitConverter.ToUInt32(parent, 0) : 0;
                _catalog.RememberStart((uint)Marshal.ReadInt32(data), parentPid, BitConverter.ToInt64(created, 0),
                    Encoding.Unicode.GetString(image).TrimEnd('\0'));
            }

            private static byte[] ReadEventProperty(IntPtr record, string name, int recordLength)
            {
                IntPtr propertyName = Marshal.StringToHGlobalUni(name);
                try
                {
                    var property = new PropertyDataDescriptor { PropertyName = propertyName, ArrayIndex = uint.MaxValue };
                    uint size;
                    if (TdhGetPropertySize(record, 0, IntPtr.Zero, 1, ref property, out size) != 0 ||
                        size == 0 || size > recordLength) return null;
                    var value = new byte[size];
                    return TdhGetProperty(record, 0, IntPtr.Zero, 1, ref property, size, value) == 0 ? value : null;
                }
                finally { Marshal.FreeHGlobal(propertyName); }
            }

            private static HashSet<uint> FindProxyListeners(HashSet<int> ports)
            {
                var listeners = new HashSet<uint>();
                if (ports.Count == 0) return listeners;
                // TCP_TABLE_OWNER_PID_LISTENER for IPv4 and IPv6.
                ReadProxyListeners(ports, listeners, 2, 24, 8, 20);
                ReadProxyListeners(ports, listeners, 23, 56, 20, 52);
                return listeners;
            }

            private static void ReadProxyListeners(HashSet<int> ports, HashSet<uint> listeners,
                int family, int rowSize, int portOffset, int pidOffset)
            {
                const int ownerPidListener = 3;
                const uint insufficientBuffer = 122;
                int size = 0;
                if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, ownerPidListener, 0) != insufficientBuffer)
                    return;

                // The TCP table may grow between querying its size and copying its rows.
                for (int retry = 0; retry < 2; retry++)
                {
                    IntPtr table = Marshal.AllocHGlobal(size);
                    try
                    {
                        uint status = GetExtendedTcpTable(table, ref size, false, family, ownerPidListener, 0);
                        if (status == insufficientBuffer) continue;
                        if (status != 0) return;
                        int count = Marshal.ReadInt32(table);
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr row = IntPtr.Add(table, 4 + i * rowSize);
                            if (ports.Contains(ReadNetworkPort(row, portOffset)))
                                listeners.Add((uint)Marshal.ReadInt32(row, pidOffset));
                        }
                        return;
                    }
                    finally { Marshal.FreeHGlobal(table); }
                }
            }

            public void Start()
            {
                if (_running) return;
                _stopping = false;

                try
                {
                    // Clean up any leftover session from a previous crash
                    StopExistingSession();

                    int propsSize = Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES));
                    int nameBytes = (SessionName.Length + 1) * 2;
                    int totalSize = propsSize + Math.Max(512, nameBytes + 64);

                    IntPtr propsPtr = Marshal.AllocHGlobal(totalSize);
                    try
                    {
                        ZeroMemory(propsPtr, totalSize);
                        var props = new EVENT_TRACE_PROPERTIES();
                        props.Wnode.BufferSize = (uint)totalSize;
                        props.Wnode.ClientContext = 1; // QPC
                        props.Wnode.Flags = WNODE_FLAG_TRACED_GUID;
                        props.BufferSize = 64; // 64 KB
                        props.MinimumBuffers = 2;
                        props.MaximumBuffers = 16;
                        props.LogFileMode = EVENT_TRACE_REAL_TIME_MODE;
                        props.FlushTimer = 1; // 1s real-time flush
                        props.LoggerNameOffset = (uint)propsSize;
                        Marshal.StructureToPtr(props, propsPtr, false);

                        uint status = StartTraceW(out _sessionHandle, SessionName, propsPtr);
                        if (status == ERROR_ALREADY_EXISTS)
                        {
                            StopExistingSession();
                            status = StartTraceW(out _sessionHandle, SessionName, propsPtr);
                        }

                        if (status != 0)
                        {
                            _errorReason = status == ERROR_ACCESS_DENIED
                                ? "需管理员权限采集流量"
                                : "ETW 启动失败 (" + status + ")";
                            return;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(propsPtr);
                    }

                    Guid provider = KernelNetworkProviderGuid;
                    uint enableStatus = EnableTraceEx2(
                        _sessionHandle,
                        ref provider,
                        EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                        TRACE_LEVEL_INFORMATION,
                        KEYWORD_IPV4_IPV6,
                        0,
                        0,
                        IntPtr.Zero);

                    if (enableStatus != 0)
                    {
                        _errorReason = "ETW 订阅失败 (" + enableStatus + ")";
                        StopExistingSession();
                        return;
                    }

                    // Process start events keep attribution for clients that exit before a network buffer flushes.
                    provider = KernelProcessProviderGuid;
                    EnableTraceEx2(_sessionHandle, ref provider, EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                        TRACE_LEVEL_INFORMATION, KEYWORD_PROCESS, 0, 0, IntPtr.Zero);

                    _callbackDelegate = OnEventRecord;
                    var logfile = new EVENT_TRACE_LOGFILEW
                    {
                        LogFileName = null,
                        LoggerName = SessionName,
                        ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD,
                        EventCallback = _callbackDelegate
                    };

                    _traceHandle = OpenTraceW(ref logfile);
                    if (_traceHandle == INVALID_PROCESSTRACE_HANDLE)
                    {
                        _errorReason = "ETW 打开通道失败";
                        StopExistingSession();
                        return;
                    }

                    _running = true;
                    _errorReason = string.Empty;

                    _workerThread = new Thread(TraceWorkerLoop)
                    {
                        IsBackground = true,
                        Name = "SysTelemetry_ETW_Net"
                    };
                    _workerThread.Start();
                }
                catch (Exception ex)
                {
                    _errorReason = "ETW 异常: " + ex.Message;
                    _running = false;
                }
            }

            private void TraceWorkerLoop()
            {
                try
                {
                    ProcessTrace(new[] { _traceHandle }, 1, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }
                finally
                {
                    _running = false;
                }
            }

            private void OnEventRecord(IntPtr recordPtr)
            {
                if (_stopping || recordPtr == IntPtr.Zero) return;

                // EVENT_RECORD offsets on x64:
                // 24: EventHeader.ProviderId (Data1 = first 4 bytes)
                // 40: EventHeader.EventDescriptor.Id (ushort)
                // 86: UserDataLength (ushort)
                // 96: UserData (IntPtr)
                uint guidData1 = (uint)Marshal.ReadInt32(recordPtr, 24);
                ushort eventId = (ushort)Marshal.ReadInt16(recordPtr, 40);
                if (guidData1 == KernelProcessGuidData1)
                {
                    if (eventId == 1) RememberProcessStart(recordPtr);
                    return;
                }
                if (guidData1 != KernelNetworkGuidData1) return;

                bool isSend;
                bool isIpv6;
                switch (eventId)
                {
                    case 10: // TCPv4 Send
                    case 42: // UDPv4 Send
                        isSend = true;
                        isIpv6 = false;
                        break;
                    case 11: // TCPv4 Recv
                    case 43: // UDPv4 Recv
                        isSend = false;
                        isIpv6 = false;
                        break;
                    case 26: // TCPv6 Send
                    case 58: // UDPv6 Send
                        isSend = true;
                        isIpv6 = true;
                        break;
                    case 27: // TCPv6 Recv
                    case 59: // UDPv6 Recv
                        isSend = false;
                        isIpv6 = true;
                        break;
                    default:
                        return;
                }

                ushort dataLen = (ushort)Marshal.ReadInt16(recordPtr, 86);
                int minLen = isIpv6 ? 40 : 16;
                if (dataLen < minLen) return;

                IntPtr userData = Marshal.ReadIntPtr(recordPtr, 96);
                if (userData == IntPtr.Zero) return;

                // UserData layout for Microsoft-Windows-Kernel-Network:
                // 0..3: uint PID
                // 4..7: uint size
                // IPv4: 8..11 daddr, 12..15 saddr
                // IPv6: 8..23 daddr, 24..39 saddr
                uint size = (uint)Marshal.ReadInt32(userData, 4);
                if (size == 0) return;

                bool loopback = isIpv6
                    ? IsIpv6Loopback(userData, 8) || IsIpv6Loopback(userData, 24)
                    : Marshal.ReadByte(userData, 8) == 127 || Marshal.ReadByte(userData, 12) == 127;
                var processes = _processes;
                if (processes == null) return;
                if (loopback && (dataLen < minLen + 4 ||
                    (!processes.LocalProxyPorts.Contains(ReadNetworkPort(userData, minLen)) &&
                     !processes.LocalProxyPorts.Contains(ReadNetworkPort(userData, minLen + 2)))))
                    return;

                uint pid = (uint)Marshal.ReadInt32(userData, 0);
                var process = _catalog.FindByPid(pid) ?? UnknownProcess;
                bool isProxy = processes.ProxyNames.Contains(process.ProcessName) || processes.ProxyListeners.Contains(pid);
                if (loopback && isProxy) return;

                var routes = Outbounds;
                bool knownCore = routes != null && (routes.CorePid == 0 || routes.CorePid == pid);
                bool isNode = isProxy && knownCore && dataLen >= minLen + 4 &&
                    (routes.IsNode(ReadAddress(userData, 8, isIpv6), ReadNetworkPort(userData, minLen)) ||
                     routes.IsNode(ReadAddress(userData, isIpv6 ? 24 : 12, isIpv6), ReadNetworkPort(userData, minLen + 2)));
                lock (_pendingLock)
                {
                    if (isProxy)
                    {
                        if (isNode)
                        {
                            if (isSend) _pendingTx += size;
                            else _pendingRx += size;
                        }
                        else if (!knownCore || !routes.FullyResolved || dataLen < minLen + 4)
                        {
                            if (isSend) _unknownTx += size;
                            else _unknownRx += size;
                        }
                        return;
                    }
                    var traffic = ApplicationTraffic.GetAppTraffic(_pendingApplications, process);
                    var detail = traffic.GetProcessTraffic(process);
                    if (!loopback)
                    {
                        traffic.DirectBytes += size;
                        detail.DirectBytes += size;
                    }
                    if (isSend)
                    {
                        traffic.Sent += size;
                        detail.Sent += size;
                    }
                    else
                    {
                        traffic.Received += size;
                        detail.Received += size;
                    }
                }
            }

            private static IPAddress ReadAddress(IntPtr data, int offset, bool ipv6)
            {
                var bytes = new byte[ipv6 ? 16 : 4];
                Marshal.Copy(IntPtr.Add(data, offset), bytes, 0, bytes.Length);
                return new IPAddress(bytes);
            }

            private static int ReadNetworkPort(IntPtr data, int offset)
            {
                return (Marshal.ReadByte(data, offset) << 8) | Marshal.ReadByte(data, offset + 1);
            }

            private static bool IsIpv6Loopback(IntPtr ptr, int offset)
            {
                // Check ::1 (15 zero bytes + 0x01)
                bool allFirst10Zero = true;
                for (int i = 0; i < 10; i++)
                {
                    if (Marshal.ReadByte(ptr, offset + i) != 0)
                    {
                        allFirst10Zero = false;
                        break;
                    }
                }
                if (!allFirst10Zero) return false;

                byte b10 = Marshal.ReadByte(ptr, offset + 10);
                byte b11 = Marshal.ReadByte(ptr, offset + 11);

                // IPv4-mapped ::ffff:127.x.x.x
                if (b10 == 0xFF && b11 == 0xFF && Marshal.ReadByte(ptr, offset + 12) == 127)
                    return true;

                // Pure ::1
                return b10 == 0 && b11 == 0 &&
                       Marshal.ReadByte(ptr, offset + 12) == 0 &&
                       Marshal.ReadByte(ptr, offset + 13) == 0 &&
                       Marshal.ReadByte(ptr, offset + 14) == 0 &&
                       Marshal.ReadByte(ptr, offset + 15) == 1;
            }

            public Dictionary<string, ApplicationTraffic> DrainBytes(out long rx, out long tx,
                out long unknownRx, out long unknownTx)
            {
                lock (_pendingLock)
                {
                    rx = _pendingRx;
                    tx = _pendingTx;
                    _pendingRx = _pendingTx = 0;
                    unknownRx = _unknownRx;
                    unknownTx = _unknownTx;
                    _unknownRx = _unknownTx = 0;
                    var applications = _pendingApplications;
                    _pendingApplications = new Dictionary<string, ApplicationTraffic>(StringComparer.OrdinalIgnoreCase);
                    return applications;
                }
            }

            private static void StopExistingSession()
            {
                int propsSize = Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES));
                int totalSize = propsSize + 512;
                IntPtr propsPtr = Marshal.AllocHGlobal(totalSize);
                try
                {
                    ZeroMemory(propsPtr, totalSize);
                    var props = new EVENT_TRACE_PROPERTIES();
                    props.Wnode.BufferSize = (uint)totalSize;
                    props.LoggerNameOffset = (uint)propsSize;
                    Marshal.StructureToPtr(props, propsPtr, false);
                    ControlTraceW(0, SessionName, propsPtr, EVENT_TRACE_CONTROL_STOP);
                }
                catch { }
                finally
                {
                    Marshal.FreeHGlobal(propsPtr);
                }
            }

            private static void ZeroMemory(IntPtr ptr, int length)
            {
                for (int i = 0; i < length; i++)
                    Marshal.WriteByte(ptr, i, 0);
            }

            public void Dispose()
            {
                _stopping = true;
                _running = false;
                StopExistingSession();
                if (_traceHandle != INVALID_PROCESSTRACE_HANDLE)
                {
                    try { CloseTrace(_traceHandle); } catch { }
                    _traceHandle = INVALID_PROCESSTRACE_HANDLE;
                }
                if (_workerThread != null && _workerThread.IsAlive)
                {
                    try { _workerThread.Join(1200); } catch { }
                }
            }
        }
    }
}
