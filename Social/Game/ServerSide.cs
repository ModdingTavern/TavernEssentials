using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Alta.Networking;
using AltaStream = Alta.Serialization.Stream;

namespace TavernEssentials.Social
{
    internal static class ServerSide
    {
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static readonly Dictionary<Connection, Peer> Peers = new Dictionary<Connection, Peer>();
        static readonly Dictionary<int, Connection> ByHandle = new Dictionary<int, Connection>();

        static ServerProtocol _protocol;
        static KeyPair _key;
        static ISocket _socket;
        static double _nextAttach;
        static double _nextSecond;
        static bool _off;
        static int _badFrames;
        static int _mainThread;
        static bool _offThreadWarned;

        static readonly Dictionary<int, HashSet<int>> Mapped = new Dictionary<int, HashSet<int>>();
        static readonly Dictionary<int, int> Owners = new Dictionary<int, int>();
        static readonly List<ConnectionIds> Conns = new List<ConnectionIds>();
        static readonly HashSet<int> Allowed = new HashSet<int>();
        static readonly List<int> Add = new List<int>(), Withdraw = new List<int>();
        static readonly List<int> Gone = new List<int>();
        static readonly HashSet<long> PairsLogged = new HashSet<long>();
        static bool _mapBroken;

        internal static Action<int> FriendsChanged;
        static readonly HashSet<int> _changed = new HashSet<int>();

        internal static void Boot(string keyPath)
        {
            _protocol = new ServerProtocol(Send);
            _protocol.FriendsChanged = Queue;
            if (Config.Current.Friends)
            {
                string dir = Path.GetDirectoryName(keyPath) ?? ".";
                LoadKnown(Path.Combine(dir, "directory.txt"));
                LoadWithdrawals(Path.Combine(dir, WithdrawalStore.FileName));
            }
            Worker.Run("server key", delegate { return KeyBoot.Run(keyPath, false, null); }, KeyReady);
        }

        static void LoadKnown(string path)
        {
            try
            {
                PlayerDirectory d = new PlayerDirectory(path);
                int skipped = d.Load();
                if (skipped > 0)
                {
                    string copy = path + ".skipped-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                    File.Copy(path, copy, true);
                    Log.Warn("the address book had " + skipped + " line(s) that do not parse; the file as it was is kept in " + copy + ".");
                }
                _protocol.Known = d;
                Log.Msg("address book: " + d.Count + " player(s) proved here before (" + path + ").");
            }
            catch (Exception e)
            {
                Log.Warn("the address book " + path + " could not be read (" + e.GetType().Name + ": " + e.Message
                    + "); the postbox lists only friends who are here until a restart.");
            }
        }

        static void LoadWithdrawals(string path)
        {
            try
            {
                WithdrawalStore w = new WithdrawalStore(path);
                string aside;
                int skipped = w.Load(UnixNow(), out aside);
                if (skipped > 0)
                    Log.Warn("the removals file had " + skipped + " line(s) that do not parse; the file as it was is kept in " + aside + ".");
                _protocol.Withdrawals = w;
                Log.Msg("removals: " + w.Count + " kept (" + path + ").");
            }
            catch (Exception e)
            {
                Log.Warn("the removals file " + path + " could not be read (" + e.GetType().Name + ": " + e.Message
                    + "); friends' removals are not matched on this server until a restart.");
            }
        }

        static void KeyReady(object result, Exception error)
        {
            KeyBoot b = result as KeyBoot;
            if (b == null)
            {
                Log.Error("the server key job failed (" + (error == null ? "?" : error.Message) + "). Social is off.");
                _off = true;
                return;
            }
            if (b.File.Note != null) Log.Msg(b.File.Note);
            if (b.File.Key == null)
            {
                Log.Error(b.File.Error + " Social is off.");
                _off = true;
                return;
            }
            if (!b.Check.Ok)
            {
                Log.Error("the crypto self-check failed: " + b.Check.Why + ". Social is off.");
                _off = true;
                return;
            }
            _key = b.File.Key;
            _protocol.Key = _key.Public;
            _protocol.Signer = Sign;
            if (Config.Current.Friends)
            {
                _protocol.CardsReady = SocialSwap.Server.Ready;
                _protocol.Recognises = true;
            }
            Log.Msg("server key " + _key.Public.Short + (b.File.Created ? " created" : " loaded") + " in "
                + b.LoadMs + " ms; " + b.Check.Describe() + ".");
        }

        internal static void Tick()
        {
            if (_mainThread == 0) _mainThread = Thread.CurrentThread.ManagedThreadId;
            if (_protocol == null || _off) return;
            double now = Now();
            if (_socket == null)
            {
                if (now < _nextAttach) return;
                _nextAttach = now + 5;
                Attach();
                return;
            }
            if (Config.Current.Friends && !_mapBroken)
            {
                try { MapFriends(); }
                catch (Exception e)
                {
                    _mapBroken = true;
                    Log.Error("the map's friend icons failed and are off until a restart: " + e);
                }
            }
            if (_changed.Count > 0) Drain();
            if (now < _nextSecond) return;
            _nextSecond = now + 1;
            if (Config.Current.Friends && _key != null) SocialSwap.Server.Ensure();
            Sweep();
            _protocol.Tick(now);
        }

        static void Queue(int sessionId)
        {
            lock (_changed) _changed.Add(sessionId);
        }

        static void Drain()
        {
            int[] ids;
            lock (_changed)
            {
                ids = new int[_changed.Count];
                _changed.CopyTo(ids);
                _changed.Clear();
            }
            Action<int> h = FriendsChanged;
            if (h == null) return;
            foreach (int id in ids)
            {
                try { h(id); }
                catch (Exception e) { Log.Warn("after a friends change for player " + id + ": " + e.GetType().Name + ": " + e.Message); }
            }
        }

        static void Attach()
        {
            ISocket s = Socket.Current;
            if (s == null || !s.IsServer || s.TransportSocket == null) return;

            string patch;
            if (!Wire.MessageTypeWide(out patch))
            {
                Log.Error(patch + ", so message type 35 does not fit. Social is off on this server.");
                _off = true;
                return;
            }
            int bytes;
            string wire = Wire.SelfTest(out bytes);
            if (wire != null)
            {
                Log.Error("the wire self-check failed: " + wire + ". Social is off on this server.");
                _off = true;
                return;
            }

            _socket = s;
            s.ConnectionCreated += Register;
            s.SocketDestroyed += OnSocketDestroyed;
            foreach (Connection c in s.Connections) Register(c);
            Log.Msg("listening on message type 35 (type patch: " + patch + "; a full frame is " + bytes + " bytes; "
                + Peers.Count + " connection(s) already up).");
        }

        static void OnSocketDestroyed(ISocket s)
        {
            try
            {
                s.ConnectionCreated -= Register;
                s.SocketDestroyed -= OnSocketDestroyed;
            }
            catch { }
            ServerProtocol fresh = new ServerProtocol(Send);
            fresh.Key = _protocol.Key;
            fresh.Signer = _protocol.Signer;
            fresh.CardsReady = _protocol.CardsReady;
            fresh.Recognises = _protocol.Recognises;
            fresh.Known = _protocol.Known;
            fresh.Withdrawals = _protocol.Withdrawals;
            fresh.FriendsChanged = Queue;
            _protocol = fresh;
            Peers.Clear();
            ByHandle.Clear();
            Mapped.Clear();
            _socket = null;
            Log.Msg("the server socket closed; listening again when one opens.");
        }

        static void Register(Connection c)
        {
            if (c == null || Peers.ContainsKey(c)) return;
            Peer p = _protocol.Connect();
            Peers[c] = p;
            ByHandle[p.Handle] = c;
            c.SetHandler((MessageType)Proto.WireType, Receive);
            c.Disconnected += OnDisconnected;
        }

        static void OnDisconnected(Connection c)
        {
            Peer p;
            if (!Peers.TryGetValue(c, out p)) return;
            Peers.Remove(c);
            ByHandle.Remove(p.Handle);
            Mapped.Remove(p.Handle);
            _protocol.Disconnect(p);
        }

        static void Receive(Connection c, AltaStream stream)
        {
            uint kind, msg, seq;
            bool fin;
            string piece;
            bool framed = Wire.Read(stream, out kind, out msg, out seq, out fin, out piece);

            Peer p;
            if (!Peers.TryGetValue(c, out p)) return;
            if (!framed) { _badFrames++; return; }
            double now = Now();
            if (!p.TakeToken(now)) return;

            string err;
            string whole = p.Inbox.Accept(msg, seq, fin, piece, now, out err);
            if (err != null)
            {
                _badFrames++;
                Log.Detail("connection " + p.Handle + ": frame dropped (" + err + ").");
            }
            if (whole == null) return;
            string why;
            Fields body = Fields.Decode(whole, out why);
            if (body == null)
            {
                _badFrames++;
                Log.Detail("connection " + p.Handle + ": message dropped (" + why + ").");
                return;
            }

            int id = ClientProtocol.NoSession;
            string name = null;
            int login, played;
            string loginName;
            if (Ids(c, out login, out loginName, out played))
            {
                id = login;
                if (played == login) name = loginName;
            }
            _protocol.OnMessage(p, kind, body, id, name, now, EntityOf(c));
        }

        static long EntityOf(Connection c)
        {
            try
            {
                var player = c.Player;
                return player == null ? 0 : (long)player.Identifier;
            }
            catch
            {
                return 0;
            }
        }

        static bool Ids(Connection c, out int login, out string loginName, out int played)
        {
            login = played = ClientProtocol.NoSession;
            loginName = null;
            try
            {
                UserInfoAndRole auth = c.UserInfo;
                var player = c.Player;
                if (auth == null || player == null || player.UserInfo == null) return false;
                login = auth.Identifier;
                loginName = auth.Username;
                played = player.UserInfo.Identifier;
                return true;
            }
            catch
            {
                return false;
            }
        }

        static void Sweep()
        {
            List<KeyValuePair<Connection, Peer>> all = new List<KeyValuePair<Connection, Peer>>(Peers);
            foreach (KeyValuePair<Connection, Peer> kv in all)
            {
                if (kv.Value.Revoked) continue;
                int login, played;
                string loginName;
                if (!Ids(kv.Key, out login, out loginName, out played) || played == login) continue;
                Log.Warn((loginName ?? "?") + " (" + login + "): the game client changed its player id in-game to "
                    + played + "; Social will not identify this connection again.");
                _protocol.Revoke(kv.Value, "player id changed in-game");
            }
        }

        static void MapFriends()
        {
            bool any = Mapped.Count > 0;
            if (!any)
                foreach (Peer p in Peers.Values)
                    if (p.FriendsHere.Count > 0) { any = true; break; }
            if (!any) return;

            Conns.Clear();
            foreach (KeyValuePair<Connection, Peer> kv in Peers)
            {
                int login, played;
                string loginName;
                if (Ids(kv.Key, out login, out loginName, out played)) Conns.Add(new ConnectionIds(kv.Value.Handle, login, played));
            }
            IdOwners.Build(Conns, Owners);

            foreach (KeyValuePair<Connection, Peer> kv in Peers)
            {
                Peer p = kv.Value;
                HashSet<int> vouched;
                Mapped.TryGetValue(p.Handle, out vouched);
                if (p.FriendsHere.Count == 0 && vouched == null) continue;

                Allowed.Clear();
                int owner;
                if (p.Bound && Owners.TryGetValue(p.SessionId, out owner) && owner == p.Handle)
                    foreach (Peer q in p.FriendsHere)
                        if (q.Bound && Owners.TryGetValue(q.SessionId, out owner) && owner == q.Handle) Allowed.Add(q.SessionId);

                FriendshipManager fm = null;
                try
                {
                    var player = kv.Key.Player;
                    if (player != null) fm = player.FriendshipManager;
                }
                catch { }
                if (fm == null) continue;
                if (vouched == null) Mapped[p.Handle] = vouched = new HashSet<int>();

                IdList.Plan(Allowed, vouched, fm.IsFriendsWith, Add, Withdraw);
                foreach (int id in Withdraw)
                {
                    fm.UpdateStatusWithPlayer(false, id);
                    if (!HasFriend(p, id)) vouched.Remove(id);
                    else Log.Warn(Who(p.SessionId) + " and " + Who(id) + ": friend icons withdrawn, because another player here"
                        + " goes by one of their ids. They come back once it stops.");
                }
                foreach (int id in Add)
                {
                    fm.UpdateStatusWithPlayer(true, id);
                    int other;
                    if (!Owners.TryGetValue(id, out other) || p.Handle > other) continue;
                    if (PairsLogged.Add(p.Handle * 4294967296L + other))
                        Log.Msg(Who(p.SessionId) + " and " + Who(id) + ": each other's friend icon on the map.");
                }
                foreach (int id in Allowed) vouched.Add(id);
            }

            Gone.Clear();
            foreach (int handle in Mapped.Keys)
                if (!ByHandle.ContainsKey(handle)) Gone.Add(handle);
            foreach (int handle in Gone) Mapped.Remove(handle);
        }

        internal static List<KeyValuePair<int, string>> FriendsHereOf(int sessionId, out string why)
        {
            if (_mainThread != 0 && Thread.CurrentThread.ManagedThreadId != _mainThread) { why = "asked off the main thread"; return null; }
            if (_protocol == null || _off || _socket == null || !Config.Current.Friends) { why = "Social's friends are not running on this server"; return null; }
            Peer p = PeerFor(sessionId, out why);
            if (p == null) return null;

            Conns.Clear();
            foreach (KeyValuePair<Connection, Peer> kv in Peers)
            {
                int login, played;
                string loginName;
                if (Ids(kv.Key, out login, out loginName, out played)) Conns.Add(new ConnectionIds(kv.Value.Handle, login, played));
            }
            IdOwners.Build(Conns, Owners);

            List<KeyValuePair<int, string>> friends = new List<KeyValuePair<int, string>>();
            int owner;
            if (!Owners.TryGetValue(p.SessionId, out owner) || owner != p.Handle) { why = "another connection goes by their id"; return null; }
            foreach (Peer q in p.FriendsHere)
            {
                Connection c;
                int login, played;
                string loginName;
                if (!q.Bound || !Owners.TryGetValue(q.SessionId, out owner) || owner != q.Handle) continue;
                if (!ByHandle.TryGetValue(q.Handle, out c) || !Ids(c, out login, out loginName, out played)) continue;
                friends.Add(new KeyValuePair<int, string>(q.SessionId, loginName));
            }
            why = null;
            return friends;
        }

        internal static List<KeyValuePair<int, string>> AwayFriendsOf(int sessionId, out string why)
        {
            if (FriendsHereOf(sessionId, out why) == null) return null;
            Peer p = PeerFor(sessionId, out why);
            if (p == null) return null;
            HashSet<int> taken = new HashSet<int>();
            foreach (Connection c in Peers.Keys)
            {
                int login, played;
                string loginName;
                if (Ids(c, out login, out loginName, out played)) { taken.Add(login); taken.Add(played); }
            }
            List<KeyValuePair<int, string>> away = new List<KeyValuePair<int, string>>();
            foreach (PlayerDirectory.Entry e in _protocol.AwayOf(p))
                if (!taken.Contains(e.Id)) away.Add(new KeyValuePair<int, string>(e.Id, e.Name));
            why = null;
            return away;
        }

        static bool HasFriend(Peer p, int sessionId)
        {
            foreach (Peer q in p.FriendsHere)
                if (q.SessionId == sessionId) return true;
            return false;
        }

        internal static bool Witness(int owner, int friend)
        {
            if (_mainThread != 0 && Thread.CurrentThread.ManagedThreadId != _mainThread)
            {
                if (!_offThreadWarned) Log.Warn("a friend card reached Social off the main thread; it gets no record.");
                _offThreadWarned = true;
                return false;
            }
            if (_protocol == null || _off || _socket == null || _key == null)
            {
                Log.Msg("friend card between " + Who(owner) + " and " + Who(friend) + ": Social is not listening yet, so no record.");
                return false;
            }
            string why;
            Peer a = PeerFor(owner, out why);
            if (a == null)
            {
                Log.Msg("friend card between " + Who(owner) + " and " + Who(friend) + ": no record, " + Who(owner) + " " + why + ".");
                return false;
            }
            Peer b = PeerFor(friend, out why);
            if (b == null)
            {
                Log.Msg("friend card between " + Who(owner) + " and " + Who(friend) + ": no record, " + Who(friend) + " " + why + ".");
                return false;
            }
            if (_protocol.Witness(a, b, UnixNow(), Now(), out why)) return true;
            Log.Msg("friend card between " + Who(owner) + " and " + Who(friend) + ": no record (" + why + ").");
            return false;
        }

        static Peer PeerFor(int sessionId, out string why)
        {
            Connection found = null;
            foreach (KeyValuePair<Connection, Peer> kv in Peers)
            {
                int login, played;
                string loginName;
                if (!Ids(kv.Key, out login, out loginName, out played)) continue;
                if (login != sessionId && played != sessionId) continue;
                if (login != played) { why = "is claimed by a player whose in-game id does not match its login"; return null; }
                if (found != null) { why = "is on two connections"; return null; }
                found = kv.Key;
            }
            if (found == null) { why = "is not connected"; return null; }
            Peer p = Peers[found];
            if (!p.Bound) { why = "is not identified (no TavernEssentials or TavernSocial, or not yet)"; return null; }
            if (p.SessionId != sessionId) { why = "was identified under another id"; return null; }
            why = null;
            return p;
        }

        static string Who(int sessionId)
        {
            foreach (Connection c in Peers.Keys)
            {
                int login, played;
                string loginName;
                if (Ids(c, out login, out loginName, out played) && login == sessionId)
                    return ServerProtocol.Describe(login, loginName);
            }
            return ServerProtocol.Describe(sessionId, null);
        }

        static void Sign(byte[] statement, ServerProtocol.Signed done)
        {
            KeyPair key = _key;
            Worker.Run("witness", delegate { return key.Sign(statement); },
                delegate(object result, Exception error) { done(result as byte[], error); });
        }

        static void Send(Peer to, uint kind, Fields body)
        {
            Connection c;
            if (ByHandle.TryGetValue(to.Handle, out c)) Wire.Send(c, kind, body);
        }

        static double Now()
        {
            return Clock.Elapsed.TotalSeconds;
        }

        static long UnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        internal static string Status()
        {
            if (_protocol == null) return "not started";
            if (_off) return "off (see the start-up log)";
            return _protocol.Status() + ", bad frames " + _badFrames;
        }
    }
}
