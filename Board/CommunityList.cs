using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TavernEssentials.Board
{
    public static class CommunityList
    {
        static readonly object Gate = new object();
        static Snapshot _published;

        internal sealed class Snapshot
        {
            internal readonly Task<IEnumerable<GameServerInfo>> All;
            internal readonly List<Entry> Entries;

            internal Snapshot(List<Entry> entries)
            {
                Entries = entries;
                List<GameServerInfo> infos = new List<GameServerInfo>(entries.Count);
                foreach (Entry e in entries) infos.Add(e.Info);
                All = Task.FromResult<IEnumerable<GameServerInfo>>(infos);
            }

            internal Entry ByEndpoint(string ip, int port)
            {
                foreach (Entry e in Entries)
                    if (e.Port == port && e.Ip == ip) return e;
                return null;
            }

            internal Entry ById(int id)
            {
                foreach (Entry e in Entries)
                    if (e.Info.Identifier == id) return e;
                return null;
            }
        }

        internal sealed class Entry
        {
            internal DevGameServerInfo Info;
            internal string Address;
            internal string Ip;
            internal int Port;
            internal bool HasPassword;
        }

        static readonly Dictionary<string, int> Ids = new Dictionary<string, int>();

        static readonly HashSet<string> WarnedHosts = new HashSet<string>();
        static readonly HashSet<string> WarnedEntries = new HashSet<string>();

        static readonly object HostGate = new object();
        static readonly Dictionary<string, string> HostByEndpoint = new Dictionary<string, string>();

        static Thread _thread;
        static bool _announced;
        static int _failStreak;

        public static bool TryReplace(ref Task<IEnumerable<GameServerInfo>> result)
        {
            Snapshot s = Current;
            if (s == null) return false;
            result = s.All;
            return true;
        }

        internal static Snapshot Current
        {
            get { lock (Gate) { return _published; } }
        }

        public static string OriginalHost(string ip, int port)
        {
            lock (HostGate)
            {
                string v;
                if (HostByEndpoint.TryGetValue(ip + ":" + port, out v)) return v;
                return null;
            }
        }

        public static void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "TavernEssentials board fetch";
            _thread.Start();
        }

        static void Loop()
        {
            while (true)
            {
                try
                {
                    FetchOnce();
                    _failStreak = 0;
                }
                catch (Exception e)
                {
                    _failStreak++;
                    string msg = "fetch failed (" + e.Message + ") - last good list stands.";
                    if (_failStreak == 1) Log.Warn(msg);
                    else Log.Detail(msg + " (streak " + _failStreak + ")");
                }
                Thread.Sleep(BoardConfig.RefreshSeconds * 1000);
            }
        }

        static void FetchOnce()
        {
            string json = Download(BoardConfig.Url);
            JsonSerializerSettings asText = new JsonSerializerSettings();
            asText.DateParseHandling = DateParseHandling.None;
            List<JToken> raw = JsonConvert.DeserializeObject<List<JToken>>(json, asText);
            if (raw == null) throw new Exception("list deserialized to null");

            List<Entry> mapped = new List<Entry>();
            int skipped = 0;
            foreach (JToken t in raw)
            {
                Entry e = null;
                try
                {
                    e = Map(t == null || t.Type != JTokenType.Object ? null : t.ToObject<CommunityServer>());
                }
                catch (Exception ex)
                {
                    if (WarnedEntries.Add(ex.GetType().Name + ": " + ex.Message))
                        Log.Warn("a community list entry could not be read (" + ex.GetType().Name + ": " + ex.Message + ") - skipping it.");
                }
                if (e != null) mapped.Add(e);
                else skipped++;
            }

            List<Entry> ordered = Order(mapped);

            Snapshot snap = new Snapshot(ordered);
            lock (Gate) { _published = snap; }
            MyServers.OnList(snap);

            if (!_announced)
            {
                _announced = true;
                Log.Msg("community list live: " + ordered.Count + " servers"
                    + (skipped > 0 ? (", " + skipped + " skipped") : "") + ".");
            }
            Log.Detail("refresh: " + raw.Count + " listed, " + ordered.Count
                + " mapped, " + skipped + " skipped.");
        }

        internal static List<Entry> Order(List<Entry> mapped)
        {
            return mapped
                .OrderByDescending(delegate(Entry e) { return e.Info.CurrentPlayerCount; })
                .ThenBy(delegate(Entry e) { return e.Info.Name; }, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static string Download(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 10000;
            req.ReadWriteTimeout = 10000;
            req.UserAgent = Mod.Name + "/" + Mod.Version;
            using (WebResponse resp = req.GetResponse())
            using (Stream stream = resp.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        static Entry Map(CommunityServer entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Address)) return null;

            string host = entry.Address;
            int port = 1757;
            int colon = host.LastIndexOf(':');
            if (colon > 0)
            {
                int parsed;
                if (int.TryParse(host.Substring(colon + 1), out parsed)) port = parsed;
                host = host.Substring(0, colon);
            }

            IPAddress addr = ResolveIPv4(host);
            if (addr == null)
            {
                if (WarnedHosts.Add(host))
                    Log.Warn("cannot resolve '" + host + "' - skipping \"" + entry.Name + "\".");
                return null;
            }

            string ipStr = addr.ToString();
            lock (HostGate) { HostByEndpoint[ipStr + ":" + port] = host; }

            DevGameServerInfo dev = DevGameServerInfo.GetDevServer(ipStr, port, 0);

            dev.Name = string.IsNullOrEmpty(entry.Name) ? entry.Address : entry.Name;
            dev.Identifier = IdFor(entry.Address);
            dev.CurrentPlayerCount = entry.PlayerCount;
            dev.PlayerLimit = entry.PlayerLimit;
            dev.Playability = 1f;
            dev.Target = 0;
            dev.LaunchRegion = entry.Region;
            dev.LastStartedVersion = entry.Version;
            dev.Description = BuildDescription(entry);

            int count = entry.PlayerCount;
            if (count < 0) count = 0;
            if (count > 1000) count = 1000;
            List<UserInfo> players = new List<UserInfo>(count);
            for (int i = 0; i < count; i++) players.Add(new UserInfo());
            dev.OnlinePlayers = players;

            Entry e = new Entry();
            e.Info = dev;
            e.Address = entry.Address;
            e.Ip = ipStr;
            e.Port = port;
            e.HasPassword = entry.HasPassword;
            return e;
        }

        static string BuildDescription(CommunityServer entry)
        {
            string region = string.IsNullOrEmpty(entry.Region) ? "unknown" : FriendServers.Unmarked(entry.Region);
            string version = string.IsNullOrEmpty(entry.Version) ? "?" : FriendServers.Unmarked(entry.Version);
            return region + " - " + version
                + " - " + (entry.HasPassword ? "locked" : "open")
                + (BoardConfig.Marks && FriendMarks.Marked(entry.Address) ? " - " + FriendServers.InfoMark : "")
                + " - " + entry.Address;
        }

        static IPAddress ResolveIPv4(string host)
        {
            IPAddress literal;
            if (IPAddress.TryParse(host, out literal))
                return literal.AddressFamily == AddressFamily.InterNetwork ? literal : null;
            try
            {
                IPAddress[] all = Dns.GetHostAddresses(host);
                for (int i = 0; i < all.Length; i++)
                    if (all[i].AddressFamily == AddressFamily.InterNetwork) return all[i];
            }
            catch (Exception) { }
            return null;
        }

        static int IdFor(string address)
        {
            int id;
            if (Ids.TryGetValue(address, out id)) return id;
            id = -2 - Ids.Count;
            Ids[address] = id;
            return id;
        }

        class CommunityServer
        {
            [JsonProperty("address")] public string Address { get; set; }
            [JsonProperty("has_password")] public bool HasPassword { get; set; }
            [JsonProperty("kind")] public string Kind { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("player_count")] public int PlayerCount { get; set; }
            [JsonProperty("player_limit")] public int PlayerLimit { get; set; }
            [JsonProperty("region")] public string Region { get; set; }
            [JsonProperty("version")] public string Version { get; set; }
        }
    }
}
