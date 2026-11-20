using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using SysTelemetry.Interop;
using SysTelemetry.Models;

namespace SysTelemetry.Services
{
    public sealed class TelemetryService : IDisposable
    {
        private sealed class ProcSample
        {
            public TimeSpan CpuTime;
            public ulong IoBytes;
            public DateTime Timestamp;
        }

        private readonly object _lhmLock = new object();
        private Computer _computer;
        private bool _lhmReady;

        private ulong _prevIdle;
        private ulong _prevKernel;
        private ulong _prevUser;
        private bool _hasCpuTimes;

        private long _prevNetRx;
        private long _prevNetTx;
        private readonly Stopwatch _netWatch = new Stopwatch();
        private bool _hasNetBaseline;

        private PerformanceCounter _cpuPerfCounter;
        private PerformanceCounter _diskReadCounter;
        private PerformanceCounter _diskWriteCounter;
        private double _baseClockMhz = 2500.0;

        private readonly Dictionary<string, ProcSample> _procHistory = new Dictionary<string, ProcSample>();
        private readonly List<double> _cpuHistory = new List<double>(60);
        private readonly List<double> _gpuHistory = new List<double>(60);
        private readonly List<double> _tempHistory = new List<double>(60);
        private readonly int _coreCount = Math.Max(1, Environment.ProcessorCount);

        public TelemetryService()
        {
            for (int i = 0; i < 60; i++)
            {
                _cpuHistory.Add(0);
                _gpuHistory.Add(0);
                _tempHistory.Add(0);
            }

            SampleCpuTimes();
            double dummyRx, dummyTx;
            SampleNetworkBytes(out dummyRx, out dummyTx);
            InitPerformanceCountersAsync();
            InitLibreHardwareMonitorAsync();
        }

        private void InitPerformanceCountersAsync()
        {
            Task.Run(() =>
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            object val = obj["MaxClockSpeed"];
                            if (val != null)
                            {
                                _baseClockMhz = Convert.ToDouble(val);
                                break;
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    _cpuPerfCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                    _cpuPerfCounter.NextValue();
                }
                catch { _cpuPerfCounter = null; }

                try
                {
                    _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
                    _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
                    _diskReadCounter.NextValue();
                    _diskWriteCounter.NextValue();
                }
                catch
                {
                    _diskReadCounter = null;
                    _diskWriteCounter = null;
                }
            });
        }

        private void InitLibreHardwareMonitorAsync()
        {
            Task.Run(() =>
            {
                try
                {
                    var comp = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsStorageEnabled = true,
                        IsMotherboardEnabled = true,
                        IsMemoryEnabled = false,
                        IsNetworkEnabled = false,
                        IsControllerEnabled = false
                    };
                    comp.Open();
                    lock (_lhmLock)
                    {
                        _computer = comp;
                        _lhmReady = true;
                    }
                }
                catch
                {
                    lock (_lhmLock) { _lhmReady = false; }
                }
            });
        }

        public TelemetrySnapshot Collect(bool includeProcesses)
        {
            var snap = new TelemetrySnapshot();

            snap.CpuLoadPercent = SampleCpuTimes();
            SampleMemory(snap);
            double rxRate, txRate;
            SampleNetworkBytes(out rxRate, out txRate);
            snap.NetDownBytesPerSec = rxRate;
            snap.NetUpBytesPerSec = txRate;

            SampleDiskCounters(snap);
            SampleHardwareSensors(snap);

            if (snap.CpuClockGhz <= 0.01 && _cpuPerfCounter != null)
            {
                try
                {
                    double perfPct = _cpuPerfCounter.NextValue();
                    if (perfPct > 0) snap.CpuClockGhz = (_baseClockMhz * (perfPct / 100.0)) / 1000.0;
                }
                catch { }
            }
            if (snap.CpuClockGhz <= 0.01) snap.CpuClockGhz = _baseClockMhz / 1000.0;

            PushHistory(_cpuHistory, snap.CpuLoadPercent);
            PushHistory(_gpuHistory, snap.GpuLoadPercent);
            PushHistory(_tempHistory, snap.CpuTempC ?? snap.GpuTempC ?? 0);

            snap.CpuHistory = new List<double>(_cpuHistory);
            snap.GpuHistory = new List<double>(_gpuHistory);
            snap.TempHistory = new List<double>(_tempHistory);

            if (includeProcesses)
                snap.TopProcesses = SampleTopProcesses();

            return snap;
        }

        private static void PushHistory(List<double> list, double value)
        {
            if (list.Count >= 60) list.RemoveAt(0);
            list.Add(Math.Max(0, Math.Min(100, value)));
        }

        private double SampleCpuTimes()
        {
            NativeMethods.FILETIME idleFt, kernelFt, userFt;
            if (!NativeMethods.GetSystemTimes(out idleFt, out kernelFt, out userFt))
                return 0;

            ulong idle = idleFt.ToUInt64();
            ulong kernel = kernelFt.ToUInt64();
            ulong user = userFt.ToUInt64();

            if (!_hasCpuTimes)
            {
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _hasCpuTimes = true;
                return 0;
            }

            ulong idleDiff = idle - _prevIdle;
            ulong kernelDiff = kernel - _prevKernel;
            ulong userDiff = user - _prevUser;

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;

            ulong total = kernelDiff + userDiff;
            if (total == 0) return 0;
            double busy = (double)(total - idleDiff) / total * 100.0;
            return Math.Max(0, Math.Min(100, busy));
        }

        private static void SampleMemory(TelemetrySnapshot snap)
        {
            var mem = new NativeMethods.MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf(typeof(NativeMethods.MEMORYSTATUSEX))
            };
            if (!NativeMethods.GlobalMemoryStatusEx(ref mem)) return;

            const double gb = 1024.0 * 1024.0 * 1024.0;
            snap.MemoryTotalGb = mem.ullTotalPhys / gb;
            snap.MemoryUsedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / gb;
            snap.MemoryPercent = mem.dwMemoryLoad;
        }

        private void SampleNetworkBytes(out double downBytesPerSec, out double upBytesPerSec)
        {
            downBytesPerSec = 0;
            upBytesPerSec = 0;
            long totalRx = 0;
            long totalTx = 0;

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    var stats = ni.GetIPStatistics();
                    totalRx += stats.BytesReceived;
                    totalTx += stats.BytesSent;
                }
            }
            catch { }

            double sec = _netWatch.Elapsed.TotalSeconds;
            if (_hasNetBaseline && sec > 0.05)
            {
                long dRx = totalRx - _prevNetRx;
                long dTx = totalTx - _prevNetTx;
                if (dRx > 0) downBytesPerSec = dRx / sec;
                if (dTx > 0) upBytesPerSec = dTx / sec;
            }

            _prevNetRx = totalRx;
            _prevNetTx = totalTx;
            _hasNetBaseline = true;
            _netWatch.Restart();
        }

        private void SampleDiskCounters(TelemetrySnapshot snap)
        {
            try
            {
                if (_diskReadCounter != null) snap.DiskReadMbPerSec = _diskReadCounter.NextValue() / (1024.0 * 1024.0);
                if (_diskWriteCounter != null) snap.DiskWriteMbPerSec = _diskWriteCounter.NextValue() / (1024.0 * 1024.0);
            }
            catch { }
        }

        private void SampleHardwareSensors(TelemetrySnapshot snap)
        {
            lock (_lhmLock)
            {
                if (!_lhmReady || _computer == null) return;

                try
                {
                    double? cpuClockMaxMhz = null;
                    double? cpuPkgTemp = null;
                    double? cpuCoreMaxTemp = null;
                    double? boardTemp = null;
                    double? storageTemp = null;
                    double gpuUsedMb = 0;
                    double gpuTotalMb = 0;
                    double d3dUsedMb = 0;

                    foreach (var hw in _computer.Hardware)
                    {
                        hw.Update();
                        foreach (var sub in hw.SubHardware) sub.Update();

                        if (hw.HardwareType == HardwareType.Cpu)
                        {
                            foreach (var s in hw.Sensors)
                            {
                                if (!s.Value.HasValue) continue;
                                double v = s.Value.Value;
                                if (s.SensorType == SensorType.Temperature)
                                {
                                    if (s.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0) cpuPkgTemp = v;
                                    else if (s.Name.IndexOf("Core Max", StringComparison.OrdinalIgnoreCase) >= 0) cpuCoreMaxTemp = v;
                                    else if (!cpuCoreMaxTemp.HasValue && s.Name.IndexOf("TjMax", StringComparison.OrdinalIgnoreCase) < 0) cpuCoreMaxTemp = v;
                                }
                                else if (s.SensorType == SensorType.Power && s.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    snap.CpuPowerW = v;
                                }
                                else if (s.SensorType == SensorType.Clock && s.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    if (!cpuClockMaxMhz.HasValue || v > cpuClockMaxMhz.Value) cpuClockMaxMhz = v;
                                }
                            }
                        }
                        else if (hw.HardwareType == HardwareType.GpuNvidia ||
                                 hw.HardwareType == HardwareType.GpuAmd ||
                                 hw.HardwareType == HardwareType.GpuIntel)
                        {
                            foreach (var s in hw.Sensors)
                            {
                                if (!s.Value.HasValue) continue;
                                double v = s.Value.Value;
                                if (s.SensorType == SensorType.Temperature && s.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                                    snap.GpuTempC = v;
                                else if (s.SensorType == SensorType.Load && s.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                                    snap.GpuLoadPercent = v;
                                else if (s.SensorType == SensorType.Power && (s.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 || !snap.GpuPowerW.HasValue))
                                    snap.GpuPowerW = v;
                                else if (s.SensorType == SensorType.SmallData)
                                {
                                    if (s.Name == "GPU Memory Used") gpuUsedMb = v;
                                    else if (s.Name == "GPU Memory Total") gpuTotalMb = v;
                                    else if (s.Name == "D3D Dedicated Memory Used") d3dUsedMb = v;
                                }
                            }
                        }
                        else if (hw.HardwareType == HardwareType.Storage)
                        {
                            foreach (var s in hw.Sensors)
                            {
                                if (s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Name == "Temperature")
                                {
                                    if (!storageTemp.HasValue || s.Value.Value > storageTemp.Value)
                                        storageTemp = s.Value.Value;
                                }
                            }
                        }
                        else if (hw.HardwareType == HardwareType.Motherboard)
                        {
                            foreach (var sub in hw.SubHardware)
                            {
                                foreach (var s in sub.Sensors)
                                {
                                    if (s.SensorType == SensorType.Temperature && s.Value.HasValue &&
                                        s.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        boardTemp = s.Value.Value;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    snap.CpuTempC = cpuPkgTemp ?? cpuCoreMaxTemp ?? boardTemp;
                    if (cpuClockMaxMhz.HasValue && cpuClockMaxMhz.Value > 100)
                        snap.CpuClockGhz = cpuClockMaxMhz.Value / 1000.0;
                    snap.DiskTempC = storageTemp;

                    double used = gpuUsedMb > 0 ? gpuUsedMb : d3dUsedMb;
                    snap.GpuVramUsedGb = used / 1024.0;
                    snap.GpuVramTotalGb = gpuTotalMb / 1024.0;
                }
                catch { }
            }
        }

        private List<ProcessMetric> SampleTopProcesses()
        {
            var results = new List<ProcessMetric>();
            var activeKeys = new HashSet<string>();
            DateTime now = DateTime.UtcNow;

            Process[] procs = null;
            try { procs = Process.GetProcesses(); }
            catch { return results; }

            foreach (var p in procs)
            {
                try
                {
                    if (p.Id == 0 || p.Id == 4) continue;
                    string key = p.Id + "_" + p.ProcessName;
                    activeKeys.Add(key);

                    TimeSpan cpuTime = p.TotalProcessorTime;
                    long wsBytes = p.WorkingSet64;
                    ulong ioBytes = 0;
                    NativeMethods.IO_COUNTERS io;
                    if (NativeMethods.GetProcessIoCounters(p.Handle, out io))
                        ioBytes = io.ReadTransferCount + io.WriteTransferCount;

                    double cpuPct = 0;
                    double ioRate = 0;
                    ProcSample prev;
                    if (_procHistory.TryGetValue(key, out prev))
                    {
                        double wallMs = (now - prev.Timestamp).TotalMilliseconds;
                        if (wallMs > 100)
                        {
                            double cpuMs = (cpuTime - prev.CpuTime).TotalMilliseconds;
                            cpuPct = Math.Max(0, Math.Min(100, cpuMs / (wallMs * _coreCount) * 100.0));
                            if (ioBytes >= prev.IoBytes)
                                ioRate = (ioBytes - prev.IoBytes) / (wallMs / 1000.0);
                        }
                    }

                    _procHistory[key] = new ProcSample
                    {
                        CpuTime = cpuTime,
                        IoBytes = ioBytes,
                        Timestamp = now
                    };

                    results.Add(new ProcessMetric
                    {
                        Name = p.ProcessName + ".exe",
                        CpuPercent = cpuPct,
                        MemoryMb = wsBytes / (1024.0 * 1024.0),
                        IoBytesPerSec = ioRate
                    });
                }
                catch { }
                finally { p.Dispose(); }
            }

            var stale = _procHistory.Keys.Where(k => !activeKeys.Contains(k)).ToList();
            foreach (var k in stale) _procHistory.Remove(k);

            return results
                .OrderByDescending(x => x.CpuPercent)
                .ThenByDescending(x => x.MemoryMb)
                .Take(5)
                .ToList();
        }

        public static double TrimWorkingSets()
        {
            ulong availBefore = GetAvailPhysBytes();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id <= 4) continue;
                    NativeMethods.EmptyWorkingSet(p.Handle);
                }
                catch { }
                finally { p.Dispose(); }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            ulong availAfter = GetAvailPhysBytes();
            return availAfter > availBefore ? (availAfter - availBefore) / (1024.0 * 1024.0) : 0;
        }

        private static ulong GetAvailPhysBytes()
        {
            var mem = new NativeMethods.MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf(typeof(NativeMethods.MEMORYSTATUSEX))
            };
            return NativeMethods.GlobalMemoryStatusEx(ref mem) ? mem.ullAvailPhys : 0;
        }

        public void Dispose()
        {
            lock (_lhmLock)
            {
                try
                {
                    if (_computer != null) _computer.Close();
                }
                catch { }
                _computer = null;
                _lhmReady = false;
            }
            try { if (_cpuPerfCounter != null) _cpuPerfCounter.Dispose(); } catch { }
            try { if (_diskReadCounter != null) _diskReadCounter.Dispose(); } catch { }
            try { if (_diskWriteCounter != null) _diskWriteCounter.Dispose(); } catch { }
        }
    }
}
