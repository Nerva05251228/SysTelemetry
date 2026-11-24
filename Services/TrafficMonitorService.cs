using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
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
        private readonly Dictionary<string, DailyTrafficRecord> _days = new Dictionary<string, DailyTrafficRecord>(StringComparer.Ordinal);
        private readonly EtwProxyNetworkTracker _etw;

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
            _etw = new EtwProxyNetworkTracker();
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

            long proxyDeltaRx, proxyDeltaTx;
            _etw.DrainBytes(out proxyDeltaRx, out proxyDeltaTx);

            // Guard against spurious negative deltas or impossible counter jumps (> 50 GB in one tick)
            const long maxTickBytes = 50L * 1024 * 1024 * 1024;
            if (globalDeltaRx < 0 || globalDeltaRx > maxTickBytes) globalDeltaRx = 0;
            if (globalDeltaTx < 0 || globalDeltaTx > maxTickBytes) globalDeltaTx = 0;
            if (proxyDeltaRx < 0 || proxyDeltaRx > maxTickBytes) proxyDeltaRx = 0;
            if (proxyDeltaTx < 0 || proxyDeltaTx > maxTickBytes) proxyDeltaTx = 0;

            DateTime now = DateTime.Now;
            string todayKey = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            TrafficSnapshot snap;
            bool shouldSave = false;

            lock (_storeLock)
            {
                DailyTrafficRecord rec;
                if (!_days.TryGetValue(todayKey, out rec))
                {
                    rec = new DailyTrafficRecord { Date = todayKey };
                    _days[todayKey] = rec;
                    PruneOldRecordsLocked(now.Date);
                    _dirty = true;
                }

                if (globalDeltaRx > 0 || globalDeltaTx > 0 || proxyDeltaRx > 0 || proxyDeltaTx > 0)
                {
                    rec.GlobalRx += globalDeltaRx;
                    rec.GlobalTx += globalDeltaTx;
                    rec.ProxyRx += proxyDeltaRx;
                    rec.ProxyTx += proxyDeltaTx;

                    // Prevent unbounded drift if ETW ever counts non-NIC traffic, while allowing brief flush skew
                    const long skewAllowance = 32L * 1024 * 1024;
                    if (rec.ProxyRx > rec.GlobalRx + skewAllowance) rec.ProxyRx = rec.GlobalRx;
                    if (rec.ProxyTx > rec.GlobalTx + skewAllowance) rec.ProxyTx = rec.GlobalTx;

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

        public void ResetToday()
        {
            string todayKey = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            long dummyRx, dummyTx;
            _etw.DrainBytes(out dummyRx, out dummyTx);

            lock (_storeLock)
            {
                _days[todayKey] = new DailyTrafficRecord { Date = todayKey };
                _dirty = true;
            }
            SaveToDisk();
        }

        public void ClearAllHistory()
        {
            string todayKey = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            long dummyRx, dummyTx;
            _etw.DrainBytes(out dummyRx, out dummyTx);

            lock (_storeLock)
            {
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
            _etw.UpdateTrackedProcessNames(names);
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

        private TrafficSnapshot BuildSnapshotLocked(DateTime today)
        {
            int resetDay = Math.Max(1, Math.Min(31, _config.TrafficResetDay));
            var snap = new TrafficSnapshot
            {
                ResetDay = resetDay,
                EtwActive = _etw.IsRunning,
                EtwStatusText = _etw.StatusText
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
                todayRec != null ? todayRec.ProxyRx : 0,
                todayRec != null ? todayRec.ProxyTx : 0,
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
                    rec != null ? rec.ProxyRx : 0,
                    rec != null ? rec.ProxyTx : 0,
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

                long gRx = 0, gTx = 0, pRx = 0, pTx = 0;
                for (DateTime d = start; d <= end; d = d.AddDays(1))
                {
                    DailyTrafficRecord rec;
                    if (_days.TryGetValue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out rec))
                    {
                        gRx += rec.GlobalRx;
                        gTx += rec.GlobalTx;
                        pRx += Math.Min(rec.GlobalRx, rec.ProxyRx);
                        pTx += Math.Min(rec.GlobalTx, rec.ProxyTx);
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

                var summary = MakePeriodSummary(label, shortLabel, rangeText, gRx, gTx, pRx, pTx, i == 0);
                snap.MonthlyHistory.Add(summary);
                if (i == 0) snap.CurrentCycle = summary;
            }

            return snap;
        }

        private static TrafficPeriodSummary MakePeriodSummary(
            string label, string shortLabel, string rangeText,
            long gRx, long gTx, long pRx, long pTx, bool isCurrent)
        {
            long effProxyRx = Math.Max(0L, Math.Min(gRx, pRx));
            long effProxyTx = Math.Max(0L, Math.Min(gTx, pTx));
            return new TrafficPeriodSummary
            {
                Label = label,
                ShortLabel = shortLabel,
                RangeText = rangeText,
                GlobalRx = Math.Max(0L, gRx),
                GlobalTx = Math.Max(0L, gTx),
                ProxyRx = effProxyRx,
                ProxyTx = effProxyTx,
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
                    var ser = new DataContractJsonSerializer(typeof(TrafficStoreData));
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
                                ProxyTx = Math.Max(0L, d.ProxyTx)
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
            TrafficStoreData payload;
            lock (_storeLock)
            {
                payload = new TrafficStoreData
                {
                    Version = 1,
                    Days = _days.Values.OrderBy(x => x.Date, StringComparer.Ordinal).ToList()
                };
                _dirty = false;
                _lastSaveUtc = DateTime.UtcNow;
            }

            try
            {
                string path = TrafficFilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string tmp = path + ".tmp";
                using (var ms = new MemoryStream())
                {
                    var ser = new DataContractJsonSerializer(typeof(TrafficStoreData));
                    ser.WriteObject(ms, payload);
                    File.WriteAllText(tmp, Encoding.UTF8.GetString(ms.ToArray()), Encoding.UTF8);
                }

                if (File.Exists(path))
                    File.Replace(tmp, path, null, true);
                else
                    File.Move(tmp, path);
            }
            catch { }
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
        /// Captures non-loopback TCP/UDP Send &amp; Recv bytes for configured proxy kernel PIDs.
        /// </summary>
        private sealed class EtwProxyNetworkTracker : IDisposable
        {
            private const string SessionName = "SysTelemetry_NetTraffic_ETW";
            private static readonly Guid KernelNetworkProviderGuid = new Guid("7dd42a49-5329-4832-8dfd-43d979153a88");
            private const uint KernelNetworkGuidData1 = 0x7dd42a49;

            private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
            private const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
            private const uint EVENT_TRACE_CONTROL_STOP = 1;
            private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
            private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
            private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
            private const byte TRACE_LEVEL_INFORMATION = 4;
            private const ulong KEYWORD_IPV4_IPV6 = 0x30;
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

            private ulong _sessionHandle;
            private ulong _traceHandle = INVALID_PROCESSTRACE_HANDLE;
            private Thread _workerThread;
            private EventRecordCallback _callbackDelegate;
            private volatile bool _running;
            private volatile bool _stopping;

            private volatile HashSet<uint> _proxyPids = new HashSet<uint>();
            private volatile string _activeProxyNames = string.Empty;
            private volatile string _errorReason = string.Empty;

            private long _pendingRx;
            private long _pendingTx;

            public bool IsRunning { get { return _running; } }

            public string StatusText
            {
                get
                {
                    if (!_running)
                        return string.IsNullOrEmpty(_errorReason) ? "代理统计未就绪" : _errorReason;
                    string active = _activeProxyNames;
                    if (!string.IsNullOrEmpty(active))
                        return "ETW · " + active;
                    return "ETW 待命 · 未运行代理进程";
                }
            }

            public void UpdateTrackedProcessNames(HashSet<string> targetNames)
            {
                var pids = new HashSet<uint>();
                var foundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (targetNames != null && targetNames.Count > 0)
                {
                    Process[] procs = null;
                    try { procs = Process.GetProcesses(); }
                    catch { procs = null; }

                    if (procs != null)
                    {
                        foreach (var p in procs)
                        {
                            try
                            {
                                string name = p.ProcessName;
                                if (targetNames.Contains(name))
                                {
                                    pids.Add((uint)p.Id);
                                    foundNames.Add(name);
                                }
                            }
                            catch { }
                            finally { p.Dispose(); }
                        }
                    }
                }

                _proxyPids = pids;
                _activeProxyNames = foundNames.Count > 0
                    ? string.Join(", ", foundNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    : string.Empty;
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
                                ? "需管理员权限统计代理"
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
                if (guidData1 != KernelNetworkGuidData1) return;

                ushort eventId = (ushort)Marshal.ReadInt16(recordPtr, 40);
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
                uint pid = (uint)Marshal.ReadInt32(userData, 0);
                var pids = _proxyPids;
                if (pids == null || !pids.Contains(pid)) return;

                uint size = (uint)Marshal.ReadInt32(userData, 4);
                if (size == 0) return;

                if (isIpv6)
                {
                    if (IsIpv6Loopback(userData, 8) || IsIpv6Loopback(userData, 24))
                        return;
                }
                else
                {
                    // Skip IPv4 loopback 127.0.0.0/8 (network byte order => first byte == 127)
                    if (Marshal.ReadByte(userData, 8) == 127 || Marshal.ReadByte(userData, 12) == 127)
                        return;
                }

                if (isSend)
                    Interlocked.Add(ref _pendingTx, size);
                else
                    Interlocked.Add(ref _pendingRx, size);
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

            public void DrainBytes(out long rx, out long tx)
            {
                rx = Interlocked.Exchange(ref _pendingRx, 0);
                tx = Interlocked.Exchange(ref _pendingTx, 0);
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
