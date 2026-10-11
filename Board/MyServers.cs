using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;

namespace TavernEssentials.Board
{
    internal static class MyServers
    {
        static JoinedServers _store;
        static readonly object Gate = new object();
        static string _pending;
        static string _hiddenSaid;
        static bool _counted;

        internal static JoinedServers Store { get { return _store; } }

        internal static void Start()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TheModdingTavern");
            _store = new JoinedServers(Path.Combine(root, JoinedServers.FileName));
            GameModeManager.ModeChanged += OnModeChanged;
        }

        internal static bool TryReplace(ref Task<IEnumerable<GameServerInfo>> result)
        {
            CommunityList.Snapshot snap = CommunityList.Current;
            if (snap == null || _store == null) return false;
            List<GameServerInfo> mine = new List<GameServerInfo>();
            foreach (CommunityList.Entry e in Joined(snap)) mine.Add(e.Info);
            result = Task.FromResult<IEnumerable<GameServerInfo>>(mine);
            return true;
        }

        internal static List<CommunityList.Entry> Joined(CommunityList.Snapshot snap)
        {
            List<CommunityList.Entry> mine = new List<CommunityList.Entry>();
            if (snap == null || _store == null) return mine;
            HashSet<string> keys = new HashSet<string>();
            foreach (string a in _store.Snapshot()) keys.Add(JoinedServers.Key(a));
            foreach (CommunityList.Entry e in snap.Entries)
                if (keys.Contains(JoinedServers.Key(e.Address))) mine.Add(e);
            return mine;
        }

        internal static string Leave(CommunityList.Entry e)
        {
            string error;
            if (_store.Remove(e.Address, out error))
                return "left \"" + e.Info.Name + "\" (" + e.Address + "): it is off My Servers; joining it again puts it back.";
            if (error != null) return "could not take \"" + e.Info.Name + "\" off My Servers: " + error;
            return "\"" + e.Info.Name + "\" was not on My Servers.";
        }

        internal static void OnList(CommunityList.Snapshot snap)
        {
            if (_store == null || !BoardConfig.MyServers) return;
            try
            {
                if (!_store.Exists) Seed(snap);

                string pending;
                lock (Gate) { pending = _pending; _pending = null; }
                if (pending != null) Record(snap, pending);

                SayHidden(snap);
            }
            catch (Exception e)
            {
                Log.Warn("my servers: " + e.Message);
            }
        }

        static void Seed(CommunityList.Snapshot snap)
        {
            string tokens = Path.Combine(Path.GetDirectoryName(_store.Path), "tokens");
            int unreadable;
            List<string> hosts = JoinedServers.TokenCacheHosts(tokens, out unreadable);
            List<KeyValuePair<string, string>> listed = new List<KeyValuePair<string, string>>();
            foreach (CommunityList.Entry e in snap.Entries)
                listed.Add(new KeyValuePair<string, string>(e.Address, e.Ip));
            List<string> seeded = JoinedServers.MatchSeed(hosts, listed);

            string error;
            if (!_store.Seed(seeded, out error))
            {
                if (error != null) Log.Warn("My Servers: " + error + " It stays empty and tries again with the next list.");
                return;
            }

            HashSet<string> matchedIps = new HashSet<string>();
            List<string> names = new List<string>();
            HashSet<string> seededKeys = new HashSet<string>();
            foreach (string a in seeded) seededKeys.Add(JoinedServers.Key(a));
            foreach (CommunityList.Entry e in snap.Entries)
            {
                if (!seededKeys.Contains(JoinedServers.Key(e.Address))) continue;
                names.Add(e.Info.Name);
                matchedIps.Add(e.Ip);
            }
            int left = 0;
            foreach (string h in hosts) if (!matchedIps.Contains(h)) left++;

            Log.Msg("My Servers: first start, so it was filled once from the launcher's token cache: "
                + (names.Count == 0 ? "nothing matched" : names.Count + " server(s) (" + string.Join(", ", names.ToArray()) + ")")
                + "." + (left > 0 ? " " + left + " cached server(s) are not on today's list and were left out." : "")
                + (unreadable > 0 ? " " + unreadable + " cache file(s) could not be read." : "")
                + " From now on a server is added when you join it (" + _store.Path + ").");
        }

        static void SayHidden(CommunityList.Snapshot snap)
        {
            string[] mine = _store.Snapshot();
            HashSet<string> listed = new HashSet<string>();
            foreach (CommunityList.Entry e in snap.Entries) listed.Add(JoinedServers.Key(e.Address));
            List<string> hidden = new List<string>();
            foreach (string a in mine) if (!listed.Contains(JoinedServers.Key(a))) hidden.Add(a);

            string said = string.Join(", ", hidden.ToArray());
            if (!_counted)
            {
                _counted = true;
                _hiddenSaid = said;
                Log.Msg("My Servers: " + (mine.Length - hidden.Count) + " server(s)"
                    + (hidden.Count > 0 ? "; not on today's list, so hidden until they are back: " + said : "") + ".");
                return;
            }
            if (said == _hiddenSaid) return;
            _hiddenSaid = said;
            if (hidden.Count == 0) Log.Msg("My Servers: every server you joined is on the list again.");
            else Log.Msg("My Servers: not on today's list, so hidden until they are back: " + said + ".");
        }

        static void OnModeChanged()
        {
            try
            {
                if (_store == null || !BoardConfig.MyServers) return;
                if (!(GameModeManager.CurrentMode is JoinedServerGameMode)) return;
                DevGameServerInfo dev = GameModeManager.CurrentGameServerInfo as DevGameServerInfo;
                if (dev == null || dev.ConnectionInfo == null || dev.ConnectionInfo.Address == null) return;
                string endpoint = Endpoint(dev.ConnectionInfo.Address, dev.ConnectionInfo.GamePort);

                CommunityList.Snapshot snap = CommunityList.Current;
                if (snap == null)
                {
                    lock (Gate) { _pending = endpoint; }
                    Log.Detail("My Servers: joined " + endpoint + " before the community list arrived; it is added when the list is in.");
                    return;
                }
                Record(snap, endpoint);
            }
            catch (Exception e)
            {
                Log.Warn("my servers, on join: " + e.Message);
            }
        }

        static void Record(CommunityList.Snapshot snap, string endpoint)
        {
            int colon = endpoint.LastIndexOf(':');
            int port;
            if (colon <= 0 || !int.TryParse(endpoint.Substring(colon + 1), out port)) return;
            CommunityList.Entry e = snap.ByEndpoint(endpoint.Substring(0, colon), port);
            if (e == null)
            {
                Log.Msg("My Servers: joined " + endpoint + ", which is not on the community list, so it was not added.");
                return;
            }
            string error;
            if (_store.Add(e.Address, out error))
                Log.Msg("My Servers: added \"" + e.Info.Name + "\" (" + e.Address + "): you joined it.");
            else if (error != null)
                Log.Warn("My Servers: could not add \"" + e.Info.Name + "\": " + error);
            else
                Log.Detail("My Servers: \"" + e.Info.Name + "\" is on it already.");
        }

        internal static string NameHere()
        {
            DevGameServerInfo info = ListedHere();
            return info == null ? null : info.Name;
        }

        internal static int LimitHere()
        {
            DevGameServerInfo info = ListedHere();
            return info == null || info.PlayerLimit == null ? 0 : info.PlayerLimit.Value;
        }

        internal static string AddressHere()
        {
            DevGameServerInfo dev = JoinedHere();
            CommunityList.Entry e = dev == null ? null : EntryFor(dev);
            return e == null ? null : e.Address;
        }

        static CommunityList.Entry EntryFor(DevGameServerInfo dev)
        {
            CommunityList.Snapshot snap = CommunityList.Current;
            return snap == null ? null : snap.ByEndpoint(dev.ConnectionInfo.Address.ToString(), dev.ConnectionInfo.GamePort);
        }

        static DevGameServerInfo JoinedHere()
        {
            DevGameServerInfo dev;
            if (GameModeManager.CurrentMode is JoinedServerGameMode)
                dev = GameModeManager.CurrentGameServerInfo as DevGameServerInfo;
            else if (GameModeManager.CurrentMode == null && Alta.Networking.JoinedServerInfo.Current != null)
                dev = JoinPatch.Joined;
            else
                return null;
            if (dev == null || dev.ConnectionInfo == null || dev.ConnectionInfo.Address == null) return null;
            return dev;
        }

        static DevGameServerInfo ListedHere()
        {
            DevGameServerInfo dev = JoinedHere();
            if (dev == null) return null;
            CommunityList.Entry e = EntryFor(dev);
            if (e != null) return e.Info;
            return dev.Identifier <= -2 ? dev : null;
        }

        static string Endpoint(IPAddress ip, int port)
        {
            return ip.ToString() + ":" + port;
        }
    }
}
