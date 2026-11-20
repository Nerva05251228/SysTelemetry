using System;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace SysTelemetry.Services
{
    public static class AutoStartService
    {
        private const string TaskName = "SysTelemetry_AutoStart";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "SysTelemetry";

        public static bool IsElevated()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static bool IsEnabled()
        {
            if (HasScheduledTask()) return true;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return key != null && key.GetValue(RunValueName) != null;
                }
            }
            catch { return false; }
        }

        public static bool SetEnabled(bool enable, out string message)
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            if (enable)
            {
                // Prefer elevated Task Scheduler entry to avoid UAC prompt at logon
                if (IsElevated() && TryCreateScheduledTask(exePath))
                {
                    RemoveRegistryRun();
                    message = "已开启开机静默自启（任务计划高权限）";
                    return true;
                }

                // Fallback to HKCU Run when running without admin privileges
                if (TrySetRegistryRun(exePath))
                {
                    message = "已开启开机自启（当前用户注册表）";
                    return true;
                }

                message = "设置开机自启失败";
                return false;
            }
            else
            {
                TryDeleteScheduledTask();
                RemoveRegistryRun();
                message = "已关闭开机自启";
                return true;
            }
        }

        private static bool HasScheduledTask()
        {
            try
            {
                int code = RunSchtasks("/Query /TN \"" + TaskName + "\"");
                return code == 0;
            }
            catch { return false; }
        }

        private static bool TryCreateScheduledTask(string exePath)
        {
            try
            {
                string args = string.Format(
                    "/Create /F /TN \"{0}\" /SC ONLOGON /RL HIGHEST /TR \"\\\"{1}\\\"\"",
                    TaskName, exePath);
                return RunSchtasks(args) == 0;
            }
            catch { return false; }
        }

        private static void TryDeleteScheduledTask()
        {
            try { RunSchtasks("/Delete /F /TN \"" + TaskName + "\""); }
            catch { }
        }

        private static int RunSchtasks(string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit(3000);
                return p.ExitCode;
            }
        }

        private static bool TrySetRegistryRun(string exePath)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return false;
                    key.SetValue(RunValueName, "\"" + exePath + "\"");
                    return true;
                }
            }
            catch { return false; }
        }

        private static void RemoveRegistryRun()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null && key.GetValue(RunValueName) != null)
                        key.DeleteValue(RunValueName, false);
                }
            }
            catch { }
        }
    }
}
