using System.Collections.Generic;

namespace SysTelemetry.Models
{
    public sealed class ProcessMetric
    {
        public string Name { get; set; }
        public double CpuPercent { get; set; }
        public double MemoryMb { get; set; }
        public double IoBytesPerSec { get; set; }
    }

    public sealed class TelemetrySnapshot
    {
        public double CpuLoadPercent { get; set; }
        public double? CpuTempC { get; set; }
        public double? CpuPowerW { get; set; }
        public double CpuClockGhz { get; set; }

        public double GpuLoadPercent { get; set; }
        public double? GpuTempC { get; set; }
        public double? GpuPowerW { get; set; }
        public double GpuVramUsedGb { get; set; }
        public double GpuVramTotalGb { get; set; }

        public double MemoryPercent { get; set; }
        public double MemoryUsedGb { get; set; }
        public double MemoryTotalGb { get; set; }

        public double? DiskTempC { get; set; }
        public double DiskReadMbPerSec { get; set; }
        public double DiskWriteMbPerSec { get; set; }

        public double NetDownBytesPerSec { get; set; }
        public double NetUpBytesPerSec { get; set; }

        public List<double> CpuHistory { get; set; }
        public List<double> GpuHistory { get; set; }
        public List<double> TempHistory { get; set; }
        public List<ProcessMetric> TopProcesses { get; set; }

        public TelemetrySnapshot()
        {
            CpuHistory = new List<double>();
            GpuHistory = new List<double>();
            TempHistory = new List<double>();
            TopProcesses = new List<ProcessMetric>();
        }
    }
}
