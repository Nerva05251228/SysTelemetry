using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using SysTelemetry.Models;

namespace SysTelemetry.Services
{
    internal sealed class SingBoxTrafficRoutes
    {
        private const int MostRecentSampleLimitSeconds = 5;
        private Dictionary<string, Connection> _previous = new Dictionary<string, Connection>();
        private DateTime _lastSampleUtc;
        private string _configPath;
        private DateTime _configWriteUtc;
        private DateTime _resolvedUtc;
        private DateTime _lastProbeUtc;
        private RoutingConfig _config;
        private uint _corePid;
        private long _coreStartedUtc;
        private string _manualPeers;
        private string _manualController;
        private static readonly HashSet<string> NodeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AnyTLS", "Http", "Hysteria", "Hysteria2", "Mieru", "Shadowsocks", "ShadowsocksR",
            "Snell", "Socks5", "Ssh", "Trojan", "Tuic", "Vless", "Vmess", "WireGuard"
        };

        public OutboundMap Outbounds { get; private set; }
        public bool ControllerAvailable { get; private set; }

        public void Refresh(string manualPeers, string manualController)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if ((nowUtc - _lastProbeUtc).TotalSeconds < 2) return;
            _lastProbeUtc = nowUtc;
            uint corePid;
            long coreStartedUtc;
            string configPath = FindV2rayNConfig(out corePid, out coreStartedUtc);
            try
            {
                bool sameCore = _configPath == configPath && _corePid == corePid && _coreStartedUtc == coreStartedUtc;
                bool configExists = configPath != null && File.Exists(configPath);
                DateTime updatedUtc = configExists ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;
                bool configChanged = configExists && (!sameCore || _config == null || _configWriteUtc != updatedUtc);
                if (configChanged || !sameCore || _manualPeers != manualPeers || _manualController != manualController)
                {
                    Outbounds = null;
                    Reset();
                    if (!sameCore) _config = null;
                    if (configChanged)
                    {
                        using (var stream = File.Open(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            _config = ReadJson<RoutingConfig>(stream);
                        SaveRunningCoreConfig(configPath, corePid, coreStartedUtc, _config);
                    }
                    else if (!configExists && configPath != null && _config == null)
                    {
                        _config = ReadRunningCoreConfig(configPath, corePid, coreStartedUtc);
                    }
                    _configPath = configPath;
                    _configWriteUtc = updatedUtc;
                    _corePid = corePid;
                    _coreStartedUtc = coreStartedUtc;
                    _manualPeers = manualPeers;
                    _manualController = manualController;
                    _resolvedUtc = DateTime.MinValue;
                }

                if ((nowUtc - _resolvedUtc).TotalSeconds < 30) return;
                _resolvedUtc = nowUtc;

                var map = new OutboundMap { CorePid = corePid, FullyResolved = true,
                    ControllerUsesProxyTypes = _config == null || !string.IsNullOrWhiteSpace(manualController) };
                foreach (var outbound in (_config == null ? null : _config.Outbounds) ?? new Outbound[0])
                {
                    if (outbound == null || string.IsNullOrWhiteSpace(outbound.Tag)) continue;
                    if (outbound.Type == "direct")
                    {
                        map.DirectTags.Add(outbound.Tag);
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(outbound.Server) || outbound.Port < 1 || outbound.Port > 65535)
                        continue;
                    map.NodeTags.Add(outbound.Tag);
                    if (string.IsNullOrWhiteSpace(manualPeers))
                        AddPeer(map, outbound.Server, outbound.Port);
                }
                foreach (string item in (manualPeers ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Uri endpoint;
                    if (TryParseEndpoint(item, out endpoint))
                        AddPeer(map, endpoint.DnsSafeHost.Trim('[', ']'), endpoint.Port);
                    else
                        map.FullyResolved = false;
                }
                map.FullyResolved = map.FullyResolved && map.Peers.Count > 0;
                var api = _config == null || _config.Experimental == null ? null : _config.Experimental.Clash;
                string address = string.IsNullOrWhiteSpace(manualController)
                    ? (api == null ? null : api.Controller) : manualController;
                Uri controller;
                if (TryParseEndpoint(address, out controller) && controller.IsLoopback)
                {
                    map.Controller = controller;
                    map.Secret = string.IsNullOrWhiteSpace(manualController) ? api.Secret : null;
                    if (string.IsNullOrEmpty(map.Secret))
                        map.Secret = Environment.GetEnvironmentVariable("SYSTELEMETRY_PROXY_CONTROLLER_TOKEN");
                }
                Outbounds = map;
            }
            catch (IOException) { Outbounds = null; }
            catch (UnauthorizedAccessException) { Outbounds = null; }
            catch (SerializationException) { Outbounds = null; }
        }

        private static bool TryParseEndpoint(string endpoint, out Uri uri)
        {
            endpoint = (endpoint ?? string.Empty).Trim();
            return Uri.TryCreate("http://" + endpoint, UriKind.Absolute, out uri) && uri.Port > 0 &&
                uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
                endpoint.LastIndexOf(':') > endpoint.LastIndexOf(']');
        }

        private static void AddPeer(OutboundMap map, string server, int port)
        {
            var addresses = Resolve(server);
            if (addresses.Length == 0) map.FullyResolved = false;
            foreach (var address in addresses)
                map.Peers.Add(new IPEndPoint(Normalize(address), port));
        }

        public void Collect(Dictionary<string, ApplicationTraffic> applications, ApplicationProcessCatalog catalog)
        {
            var map = Outbounds;
            if (map == null || map.Controller == null)
            {
                ControllerAvailable = false;
                Reset();
                return;
            }

            try
            {
                if (map.ControllerUsesProxyTypes && map.ProxyTypes.Count == 0)
                {
                    var types = ReadController<ProxyCatalogue>(map, "/proxies");
                    if (types != null && types.Proxies != null)
                    {
                        foreach (var proxy in types.Proxies)
                        {
                            if (proxy.Value == null) continue;
                            if (NodeTypes.Contains(proxy.Value.Type)) map.ProxyTypes.Add(proxy.Key);
                            if (string.Equals(proxy.Value.Type, "Direct", StringComparison.OrdinalIgnoreCase))
                                map.DirectTypes.Add(proxy.Key);
                        }
                    }
                }
                var response = ReadController<ConnectionSnapshot>(map, "/connections");
                var connections = response == null ? null : response.Connections;

                DateTime nowUtc = DateTime.UtcNow;
                var current = new Dictionary<string, Connection>();
                DateTime previousSample = _lastSampleUtc;
                if ((nowUtc - previousSample).TotalSeconds > MostRecentSampleLimitSeconds)
                    previousSample = DateTime.MinValue;

                foreach (var connection in connections ?? new Connection[0])
                {
                    if (connection == null || string.IsNullOrEmpty(connection.Id) ||
                        connection.Upload < 0 || connection.Download < 0 || current.ContainsKey(connection.Id))
                        continue;
                    current.Add(connection.Id, connection);

                    string name = connection.Metadata == null ? null :
                        string.IsNullOrWhiteSpace(connection.Metadata.ProcessPath)
                            ? connection.Metadata.ProcessName : connection.Metadata.ProcessPath;
                    if (string.IsNullOrEmpty(name) || connection.Chains == null || previousSample == DateTime.MinValue)
                        continue;
                    var nodeTags = map.ControllerUsesProxyTypes ? map.ProxyTypes : map.NodeTags;
                    var directTags = map.ControllerUsesProxyTypes ? map.DirectTypes : map.DirectTags;
                    bool viaNode = Array.Exists(connection.Chains, nodeTags.Contains);
                    if (!viaNode && !Array.Exists(connection.Chains, directTags.Contains)) continue;

                    Connection previous;
                    long upload = connection.Upload;
                    long download = connection.Download;
                    if (_previous.TryGetValue(connection.Id, out previous))
                    {
                        upload = Math.Max(0, upload - previous.Upload);
                        download = Math.Max(0, download - previous.Download);
                    }
                    else if (connection.Start < previousSample || connection.Start > nowUtc)
                    {
                        continue;
                    }
                    const long maxSampleBytes = 50L * 1024 * 1024 * 1024;
                    if (upload > maxSampleBytes || download > maxSampleBytes) continue;
                    long bytes = upload + download;
                    if (bytes <= 0) continue;

                    var process = catalog.FindByImage(name);
                    var traffic = ApplicationTraffic.GetAppTraffic(applications, process);
                    var detail = traffic.GetProcessTraffic(process);
                    if (viaNode)
                    {
                        traffic.NodeBytes += bytes;
                        detail.NodeBytes += bytes;
                    }
                    else
                    {
                        traffic.DirectBytes += bytes;
                        detail.DirectBytes += bytes;
                    }
                }

                _previous = current;
                _lastSampleUtc = nowUtc;
                ControllerAvailable = true;
            }
            catch (WebException) { ControllerAvailable = false; Reset(); }
            catch (IOException) { ControllerAvailable = false; Reset(); }
            catch (SerializationException) { ControllerAvailable = false; Reset(); }
            catch (ArgumentException) { ControllerAvailable = false; Reset(); }
        }

        private static T ReadController<T>(OutboundMap map, string path)
        {
            var request = (HttpWebRequest)WebRequest.Create(new Uri(map.Controller, path));
            request.Proxy = null;
            request.AllowAutoRedirect = false;
            request.Timeout = 500;
            request.ReadWriteTimeout = 500;
            if (!string.IsNullOrEmpty(map.Secret))
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + map.Secret;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
                return ReadJson<T>(stream);
        }

        public void Reset()
        {
            _previous.Clear();
            _lastSampleUtc = DateTime.MinValue;
        }

        private static T ReadJson<T>(Stream stream)
        {
            var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
            return (T)new DataContractJsonSerializer(typeof(T), settings).ReadObject(stream);
        }

        private static string FindV2rayNConfig(out uint corePid, out long startedUtc)
        {
            corePid = 0;
            startedUtc = 0;
            foreach (var process in Process.GetProcessesByName("sing-box"))
            {
                using (process)
                {
                    try
                    {
                        string coreFolder = Path.GetDirectoryName(process.MainModule.FileName);
                        string folder = Path.GetFullPath(Path.Combine(coreFolder, "..", ".."));
                        if (!File.Exists(Path.Combine(folder, "v2rayN.exe"))) continue;
                        startedUtc = process.StartTime.ToUniversalTime().Ticks;
                        corePid = (uint)process.Id;
                        return Path.Combine(folder, "binConfigs", "config.json");
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                }
            }
            return null;
        }

        private static string RunningCoreConfigPath
        {
            get { return Path.Combine(Path.GetDirectoryName(TrafficMonitorService.TrafficFilePath), "running-proxy.json"); }
        }

        private static RoutingConfig ReadRunningCoreConfig(string path, uint pid, long startedUtc)
        {
            try
            {
                using (var stream = File.OpenRead(RunningCoreConfigPath))
                {
                    var cached = ReadJson<RunningCoreConfig>(stream);
                    if (cached != null && cached.Path == path && cached.Pid == pid && cached.StartedUtc == startedUtc)
                        return cached.Config;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SerializationException) { }
            return null;
        }

        private static void SaveRunningCoreConfig(string path, uint pid, long startedUtc, RoutingConfig config)
        {
            if (config == null) return;
            var api = config.Experimental == null ? null : config.Experimental.Clash;
            if (api != null && !string.IsNullOrEmpty(api.Secret)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RunningCoreConfigPath));
                using (var stream = File.Create(RunningCoreConfigPath))
                {
                    new DataContractJsonSerializer(typeof(RunningCoreConfig)).WriteObject(stream,
                        new RunningCoreConfig { Path = path, Pid = pid, StartedUtc = startedUtc, Config = config });
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SerializationException) { }
        }

        private static IPAddress[] Resolve(string server)
        {
            IPAddress address;
            if (IPAddress.TryParse(server, out address)) return new[] { address };
            try
            {
                var lookup = Dns.GetHostAddressesAsync(server);
                return lookup.Wait(500) ? lookup.Result : new IPAddress[0];
            }
            catch (AggregateException) { return new IPAddress[0]; }
            catch (ArgumentException) { return new IPAddress[0]; }
        }

        private static IPAddress Normalize(IPAddress address)
        {
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }

        internal sealed class OutboundMap
        {
            public readonly HashSet<IPEndPoint> Peers = new HashSet<IPEndPoint>();
            public readonly HashSet<string> NodeTags = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> DirectTags = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> ProxyTypes = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> DirectTypes = new HashSet<string>(StringComparer.Ordinal);
            public Uri Controller;
            public string Secret;
            public uint CorePid;
            public bool FullyResolved;
            public bool ControllerUsesProxyTypes;

            public bool IsNode(IPAddress address, int port)
            {
                return Peers.Contains(new IPEndPoint(Normalize(address), port));
            }
        }

        [DataContract]
        private sealed class RunningCoreConfig
        {
            [DataMember] public string Path { get; set; }
            [DataMember] public uint Pid { get; set; }
            [DataMember] public long StartedUtc { get; set; }
            [DataMember] public RoutingConfig Config { get; set; }
        }

        [DataContract]
        private sealed class RoutingConfig
        {
            [DataMember(Name = "outbounds")] public Outbound[] Outbounds { get; set; }
            [DataMember(Name = "experimental")] public ExperimentalConfig Experimental { get; set; }
        }

        [DataContract]
        private sealed class ExperimentalConfig
        {
            [DataMember(Name = "clash_api")] public ClashApi Clash { get; set; }
        }

        [DataContract]
        private sealed class ClashApi
        {
            [DataMember(Name = "external_controller")] public string Controller { get; set; }
            [DataMember(Name = "secret")] public string Secret { get; set; }
        }

        [DataContract]
        private sealed class Outbound
        {
            [DataMember(Name = "tag")] public string Tag { get; set; }
            [DataMember(Name = "type")] public string Type { get; set; }
            [DataMember(Name = "server")] public string Server { get; set; }
            [DataMember(Name = "server_port")] public int Port { get; set; }
        }

        [DataContract]
        private sealed class ConnectionSnapshot
        {
            [DataMember(Name = "connections")] public Connection[] Connections { get; set; }
        }

        [DataContract]
        private sealed class Connection
        {
            [DataMember(Name = "id")] public string Id { get; set; }
            [DataMember(Name = "start")] public string StartedAt { get; set; }
            [DataMember(Name = "upload")] public long Upload { get; set; }
            [DataMember(Name = "download")] public long Download { get; set; }
            [DataMember(Name = "chains")] public string[] Chains { get; set; }
            [DataMember(Name = "metadata")] public ProcessMetadata Metadata { get; set; }

            public DateTime Start
            {
                get
                {
                    DateTimeOffset time;
                    return DateTimeOffset.TryParse(StartedAt, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out time) ? time.UtcDateTime : DateTime.MinValue;
                }
            }
        }

        [DataContract]
        private sealed class ProcessMetadata
        {
            [DataMember(Name = "processPath")] public string ProcessPath { get; set; }
            [DataMember(Name = "process")] public string ProcessName { get; set; }
        }

        [DataContract]
        private sealed class ProxyCatalogue
        {
            [DataMember(Name = "proxies")] public Dictionary<string, ProxyType> Proxies { get; set; }
        }

        [DataContract]
        private sealed class ProxyType
        {
            [DataMember(Name = "type")] public string Type { get; set; }
        }
    }
}
