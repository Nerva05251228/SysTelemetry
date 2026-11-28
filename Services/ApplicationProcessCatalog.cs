using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SysTelemetry.Models;

namespace SysTelemetry.Services
{
    internal sealed class ApplicationProcessCatalog
    {
        private const int MaxParentDepth = 16;
        private static readonly string WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private static readonly string[] SharedDirectories =
        {
            WindowsDirectory, Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86)
        };
        private static readonly string[] InstallContainers =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        private readonly object _gate = new object();
        private Dictionary<uint, ApplicationProcess> _live = new Dictionary<uint, ApplicationProcess>();
        private readonly Dictionary<uint, ApplicationProcess> _recent = new Dictionary<uint, ApplicationProcess>();
        private Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<uint, ApplicationProcess> Refresh(string[] groups)
        {
            var entries = ReadProcessList();
            lock (_gate)
            {
                _aliases = ParseAliases(groups);
                if (entries == null) return _live;

                var next = new Dictionary<uint, ApplicationProcess>();
                foreach (var entry in entries.Values)
                {
                    Resolve(entry, entries, next, 0);
                }
                long cutoff = DateTime.UtcNow.AddSeconds(-30).ToFileTimeUtc();
                var expired = new List<uint>();
                foreach (var eventProcess in _recent)
                {
                    ApplicationProcess current;
                    if (eventProcess.Value.Created < cutoff || (next.TryGetValue(eventProcess.Key, out current) &&
                        current.Created != 0 && current.Created != eventProcess.Value.Created))
                        expired.Add(eventProcess.Key);
                }
                foreach (uint pid in expired) _recent.Remove(pid);
                _live = next;
                return next;
            }
        }

        public void RememberStart(uint pid, uint parentPid, long created, string image)
        {
            var entry = ReadProcess(pid, parentPid, Path.GetFileNameWithoutExtension(image));
            // A delayed event can outlive its process, or arrive after the PID has been reused.
            if (entry.Created != created)
            {
                entry.Created = created;
                entry.Image = NormalizePath(image);
                entry.Package = null;
            }
            lock (_gate)
            {
                var parent = Find(parentPid);
                ApplicationProcess earlier;
                if (!_recent.TryGetValue(pid, out earlier) || earlier.Created <= created)
                    _recent[pid] = Group(entry, parent);
            }
        }

        public ApplicationProcess FindByPid(uint pid)
        {
            lock (_gate) return Find(pid);
        }

        public ApplicationProcess FindByImage(string image)
        {
            string path = NormalizePath(image);
            string name = Path.GetFileNameWithoutExtension(path ?? image);
            if (string.IsNullOrEmpty(name)) name = "未知进程";
            lock (_gate)
            {
                if (path != null)
                {
                    if (_aliases.ContainsKey(path))
                        return Group(new Entry { Image = path, Name = name }, null);

                    ApplicationProcess matched = null;
                    foreach (var process in _live.Values)
                    {
                        if (!string.Equals(path, process.ProcessKey, StringComparison.OrdinalIgnoreCase)) continue;
                        if (matched != null && !string.Equals(matched.AppKey, process.AppKey, StringComparison.OrdinalIgnoreCase))
                            return Group(new Entry { Image = path, Name = name }, null);
                        matched = process;
                    }
                    foreach (var item in _recent)
                    {
                        var process = item.Value;
                        if (_live.ContainsKey(item.Key) || !string.Equals(path, process.ProcessKey, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (matched != null && !string.Equals(matched.AppKey, process.AppKey, StringComparison.OrdinalIgnoreCase))
                            return Group(new Entry { Image = path, Name = name }, null);
                        matched = process;
                    }
                    if (matched != null) return matched;
                }
                return Group(new Entry { Image = path, Name = name }, null);
            }
        }

        private ApplicationProcess Find(uint pid)
        {
            ApplicationProcess live, recent;
            _live.TryGetValue(pid, out live);
            _recent.TryGetValue(pid, out recent);
            return recent != null && (live == null || recent.Created > live.Created) ? recent : live;
        }

        private ApplicationProcess Resolve(Entry entry, Dictionary<uint, Entry> entries,
            Dictionary<uint, ApplicationProcess> resolved, int depth)
        {
            ApplicationProcess result;
            if (resolved.TryGetValue(entry.Pid, out result)) return result;
            Entry parent;
            var owner = depth < MaxParentDepth && entry.Parent != entry.Pid &&
                entries.TryGetValue(entry.Parent, out parent) && parent.Created > 0 && parent.Created < entry.Created
                ? Resolve(parent, entries, resolved, depth + 1) : null;
            if (resolved.TryGetValue(entry.Pid, out result)) return result;

            result = Group(entry, owner);
            if (result.Created > 0 && result.AppKey == result.ProcessKey)
            {
                var previous = Find(entry.Pid);
                if (previous != null && previous.Created == result.Created &&
                    string.Equals(previous.ProcessKey, result.ProcessKey, StringComparison.OrdinalIgnoreCase) &&
                    previous.AppKey != previous.ProcessKey)
                    result = previous;
            }
            resolved.Add(entry.Pid, result);
            return result;
        }

        private ApplicationProcess Group(Entry entry, ApplicationProcess parent)
        {
            string owner = entry.Image;
            string package = entry.Package;
            string alias;
            if (entry.Image != null && _aliases.TryGetValue(entry.Image, out alias))
            {
                owner = alias;
                package = null;
            }
            else if (package == null && parent != null && parent.Created > 0 && parent.Created < entry.Created &&
                CanInheritOwner(parent.AppPath, entry.Image))
            {
                owner = parent.AppPath;
                package = parent.Package;
            }
            return new ApplicationProcess(entry.Image, entry.Name, owner, package, entry.Created);
        }

        private static bool CanInheritOwner(string owner, string child)
        {
            if (owner == null || child == null || IsSharedBinary(owner) || IsSharedBinary(child)) return false;
            string folder = Path.GetDirectoryName(owner);
            if (string.IsNullOrEmpty(folder) || folder.Length <= 3 || !IsBelow(child, folder)) return false;
            foreach (string container in InstallContainers)
            {
                if (string.Equals(folder, container, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static bool IsSharedBinary(string path)
        {
            foreach (string folder in SharedDirectories)
            {
                if (IsBelow(path, folder)) return true;
            }
            string name = Path.GetFileNameWithoutExtension(path);
            return string.Equals(name, "msedgewebview2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "svchost", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBelow(string path, string folder)
        {
            return !string.IsNullOrEmpty(folder) && path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> ParseAliases(string[] groups)
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rule in groups ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(rule)) continue;
                int equals = rule.IndexOf('=');
                if (equals <= 0) continue;
                string process = NormalizePath(rule.Substring(0, equals).Trim());
                string app = NormalizePath(rule.Substring(equals + 1).Trim());
                if (process != null && app != null && !IsSharedBinary(process))
                    aliases[process] = app;
            }
            return aliases;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                path = Environment.ExpandEnvironmentVariables(path);
                if (path.StartsWith(@"\??\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal))
                    path = path.Substring(4);
                if (path.Length < 4 || path[1] != ':' || (path[2] != '\\' && path[2] != '/')) return null;
                return Path.GetFullPath(path);
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        private static Dictionary<uint, Entry> ReadProcessList()
        {
            const uint snapshotProcesses = 2;
            IntPtr snapshot = CreateToolhelp32Snapshot(snapshotProcesses, 0);
            if (snapshot == new IntPtr(-1)) return null;
            try
            {
                var data = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                if (!Process32First(snapshot, ref data)) return null;
                var entries = new Dictionary<uint, Entry>();
                do
                {
                    entries[data.Pid] = ReadProcess(data.Pid, data.Parent, Path.GetFileNameWithoutExtension(data.Exe));
                } while (Process32Next(snapshot, ref data));
                return entries;
            }
            finally { CloseHandle(snapshot); }
        }

        private static Entry ReadProcess(uint pid, uint parent, string name)
        {
            var entry = new Entry { Pid = pid, Parent = parent, Name = string.IsNullOrEmpty(name) ? "未知进程" : name };
            const uint queryLimitedInformation = 0x1000;
            IntPtr process = OpenProcess(queryLimitedInformation, false, pid);
            if (process == IntPtr.Zero) return entry;
            try
            {
                long exit, kernel, user;
                GetProcessTimes(process, out entry.Created, out exit, out kernel, out user);
                var buffer = new StringBuilder(1024);
                int length = buffer.Capacity;
                if (QueryFullProcessImageName(process, 0, buffer, ref length))
                    entry.Image = NormalizePath(buffer.ToString());

                buffer.Clear();
                uint count = (uint)buffer.Capacity;
                if (GetPackageFamilyName(process, ref count, buffer) == 0)
                    entry.Package = buffer.ToString();
                return entry;
            }
            finally { CloseHandle(process); }
        }

        private sealed class Entry
        {
            public uint Pid, Parent;
            public long Created;
            public string Name, Image, Package;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            public uint Size, Usage, Pid;
            public IntPtr DefaultHeap;
            public uint Module, Threads, Parent;
            public int Priority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
        }

        [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr process, out long created, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder family);
    }
}
