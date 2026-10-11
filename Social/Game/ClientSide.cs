using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Alta.Networking;
using AltaStream = Alta.Serialization.Stream;

namespace TavernEssentials.Social
{
    internal static class ClientSide
    {
        static readonly Stopwatch Clock = Stopwatch.StartNew();

        static ClientProtocol _protocol;
        static Connection _server;
        static Reassembler _inbox = new Reassembler();
        static bool _checked;
        static bool _usable;
        static string _friendsDir;
        static int _connections;
        static bool _reading;
        static double _nextMark;
        static bool _keyFailed;
        static readonly HashSet<uint> _flagged = new HashSet<uint>();
        static readonly HashSet<uint> _markLogged = new HashSet<uint>();
        static readonly Dictionary<int, double> _missingSince = new Dictionary<int, double>();
        static readonly HashSet<int> _missingLogged = new HashSet<int>();
        internal const double AvatarWait = 15;

        internal static void Boot(string identityPath, string exportPath)
        {
            _protocol = new ClientProtocol(Send, Sign);
            _protocol.ServerSaid = ServerNames.SayFriendsWork;
            if (Config.Current.Friends)
            {
                _friendsDir = Path.Combine(Path.GetDirectoryName(identityPath), FriendStore.Folder);
                _protocol.Consented = Consented;
                _protocol.Keep = Keep;
                _protocol.StillHeld = delegate(string fp) { return File.Exists(FriendStore.PathFor(_friendsDir, fp)); };
                _protocol.TheyRemoved = TheyRemoved;
                if (Config.Current.Lists) GameLists.Boot(_friendsDir);
            }
            Worker.Run("identity", delegate { return KeyBoot.Run(identityPath, true, exportPath); }, KeyReady);
        }

        static void KeyReady(object result, Exception error)
        {
            KeyBoot b = result as KeyBoot;
            if (b == null)
            {
                _keyFailed = true;
                Log.Error("the identity job failed (" + (error == null ? "?" : error.Message) + "). Social is off.");
                return;
            }
            if (b.File.Note != null) Log.Msg(b.File.Note);
            if (b.File.Key == null)
            {
                _keyFailed = true;
                Log.Error(b.File.Error + " Social is off.");
                return;
            }
            if (!b.Check.Ok)
            {
                _keyFailed = true;
                Log.Error("the crypto self-check failed: " + b.Check.Why + ". Social is off.");
                return;
            }
            if (b.ExportNote != null) Log.Msg(b.ExportNote);
            if (b.ExportError != null) Log.Warn("could not keep the backup copy of your key: " + b.ExportError);
            _protocol.Key = b.File.Key;
            Log.Msg("identity " + b.File.Key.Public.Short + (b.File.Created ? " created" : " loaded") + " in " + b.LoadMs
                + " ms (%APPDATA%\\TheModdingTavern\\tavernsocial\\" + KeyStore.IdentityFile + "); " + b.Check.Describe() + ".");
        }

        internal static void OnConnection(Connection connection)
        {
            if (_protocol == null || connection == null) return;
            if (!_checked)
            {
                _checked = true;
                string patch;
                if (!Wire.MessageTypeWide(out patch))
                    Log.Error(patch + ", so message type 35 does not fit. Social stays quiet on every server.");
                else
                {
                    int bytes;
                    string wire = Wire.SelfTest(out bytes);
                    if (wire != null)
                        Log.Error("the wire self-check failed: " + wire + ". Social stays quiet on every server.");
                    else
                    {
                        _usable = true;
                        Log.Msg("message type 35 ready (type patch: " + patch + "; a full frame is " + bytes + " bytes).");
                    }
                }
            }
            if (!_usable) return;

            _server = connection;
            _inbox = new Reassembler();
            connection.SetHandler((MessageType)Proto.WireType, Receive);
            connection.Disconnected += OnDisconnected;
            _protocol.Reset();
            _connections++;
            ForgetAvatars();
            GameLists.NewConnection();
            Tablet.NewConnection();
            Log.Detail("new server connection; saying hello once the join is approved.");
        }

        static void OnDisconnected(Connection c)
        {
            if (c != _server) return;
            _server = null;
            _protocol.Disconnected();
            ForgetAvatars();
        }

        static void ForgetAvatars()
        {
            _flagged.Clear();
            _markLogged.Clear();
            _missingSince.Clear();
            _missingLogged.Clear();
        }

        internal static void Tick()
        {
            if (_protocol == null) return;
            bool ready = false;
            if (_server != null)
            {
                try { ready = _server.IsApproved && Player.Current != null; }
                catch { }
            }
            double now = Now();
            ReadBook();
            _protocol.Tick(now, ready);
            if (_friendsDir != null && now >= _nextMark)
            {
                _nextMark = now + 1;
                Mark(now);
            }
            GameLists.Tick(now, _server != null);
            Tablet.Tick(now);
        }

        internal static ClientProtocol Protocol { get { return _protocol; } }

        internal static bool KeyFailed { get { return _keyFailed; } }

        internal static void Removed(string friendFingerprint, string name, List<Withdrawal> made)
        {
            if (_protocol == null) return;
            _protocol.Removed(friendFingerprint, name, made);
            Unflag(friendFingerprint);
            if (_friendsDir != null) Mark(Now());
        }

        static void Unflag(string friendFingerprint)
        {
            List<int> sessions = new List<int>();
            int s;
            if (_protocol.Roster.TryGetSession(friendFingerprint, out s)) sessions.Add(s);
            if (_protocol.SessionOf(friendFingerprint, out s) && !sessions.Contains(s)) sessions.Add(s);
            foreach (int session in sessions)
            {
                try
                {
                    Player p = Avatar(session, false);
                    if (p != null && p.FriendshipManager != null && p.FriendshipManager.IsFriend) p.FriendshipManager.UpdateStatus(false);
                }
                catch (Exception e)
                {
                    Log.Warn("taking the friend mark off a removed friend: " + e.GetType().Name + ": " + Keys.Shorten(e.Message));
                }
            }
        }

        static void TheyRemoved(string friendFingerprint, List<KeyValuePair<long, string>> records, string name)
        {
            Unflag(friendFingerprint);
            if (_friendsDir != null) Mark(Now());
            KeyPair key = _protocol.Key;
            if (_friendsDir == null || key == null) return;
            string dir = _friendsDir;
            PublicKey me = key.Public;
            long now = UnixNow();
            string who = (string.IsNullOrEmpty(name) ? "?" : name) + " (" + Keys.Short(friendFingerprint) + ")";
            Worker.Run("friend removed", delegate { return FriendStore.RemoveCopies(dir, me, friendFingerprint, records, now); },
                delegate(object result, Exception error)
                {
                    Removal r = result as Removal;
                    if (r == null)
                        Log.Error("could not set aside the records " + who + " removed: " + (error == null ? "?" : error.GetType().Name + ": " + Keys.Shorten(error.Message)));
                    else
                        Log.Detail(r.Moved + " record file(s) with " + who + " moved to friends\\removed\\.");
                });
        }

        internal static void Restored(FriendRecord record, string name)
        {
            if (_protocol == null) return;
            _protocol.Restored(record, name);
            _nextMark = 0;
        }

        static void ReadBook()
        {
            if (_friendsDir == null || _reading || _server == null || _protocol.Book != null || _protocol.Key == null) return;
            _reading = true;
            int connection = _connections;
            string dir = _friendsDir;
            PublicKey me = _protocol.Key.Public;
            List<string> notes = new List<string>();
            RecordIndex[] copies = new RecordIndex[1];
            Worker.Run("read friends", delegate
                {
                    FriendBook b = FriendBook.Load(dir, me, notes);
                    copies[0] = RecordIndex.Load(dir, me);
                    return b;
                },
                delegate(object result, Exception error)
                {
                    _reading = false;
                    if (connection != _connections) return;
                    FriendBook book = result as FriendBook;
                    if (book == null)
                    {
                        Log.Error("could not read your friends folder: " + (error == null ? "?" : error.GetType().Name + ": " + Keys.Shorten(error.Message)));
                        _protocol.UseBook(new FriendBook(), new RecordIndex());
                        return;
                    }
                    foreach (string n in notes) Log.Warn(n);
                    _protocol.UseBook(book, copies[0]);
                    Log.Detail(book.Count + " friendship record(s) read.");
                });
        }

        static void Mark(double now)
        {
            HashSet<uint> want = new HashSet<uint>();
            foreach (int session in _protocol.FriendsHere())
            {
                Player p = Avatar(session, true);
                if (p == null || p.FriendshipManager == null)
                {
                    double since;
                    if (!_missingSince.TryGetValue(session, out since)) _missingSince[session] = now;
                    else if (now - since >= AvatarWait && _missingLogged.Add(session)) LogMissing(session);
                    continue;
                }
                _missingSince.Remove(session);
                uint entity = ((INetworkEntity)p).Identifier;
                want.Add(entity);
                _flagged.Add(entity);
                if (p.FriendshipManager.IsFriend) continue;
                p.FriendshipManager.UpdateStatus(true);
                if (_markLogged.Add(entity))
                    Log.Msg("friend " + NameOf(session) + ": nameplate marked as a friend.");
            }
            foreach (uint entity in new List<uint>(_flagged))
            {
                if (want.Contains(entity)) continue;
                _flagged.Remove(entity);
                Player p = ByEntity(entity);
                if (p != null && p.FriendshipManager != null && p.FriendshipManager.IsFriend) p.FriendshipManager.UpdateStatus(false);
            }
        }

        static Player Avatar(int session, bool strict)
        {
            long entity = _protocol.Roster.EntityOf(session);
            if (entity <= 0 || entity > uint.MaxValue) return strict ? null : Player.GetPlayer(session);
            Player p = ByEntity((uint)entity);
            return p != null && p.UserInfo != null && p.UserInfo.Identifier == session ? p : null;
        }

        static Player ByEntity(uint entity)
        {
            foreach (var ip in Player.AllPlayers)
            {
                Player p = ip as Player;
                if (p != null && ((INetworkEntity)p).Identifier == entity) return p;
            }
            return null;
        }

        static void LogMissing(int session)
        {
            long entity = _protocol.Roster.EntityOf(session);
            string byEntity = "no avatar", byId = "none";
            try
            {
                Player e = entity > 0 && entity <= uint.MaxValue ? ByEntity((uint)entity) : null;
                if (e != null) byEntity = "an avatar going by id " + (e.UserInfo == null ? "?" : e.UserInfo.Identifier.ToString());
                Player s = Player.GetPlayer(session);
                if (s != null) byId = "entity " + ((INetworkEntity)s).Identifier;
            }
            catch (Exception ex)
            {
                byEntity = ex.GetType().Name;
            }
            Log.Warn("friend " + NameOf(session) + " is recognised here, but their avatar was not found: the server says entity "
                + entity + ", where this game has " + byEntity + "; the avatar going by session " + session + " is " + byId
                + ". Their nameplate stays as it is.");
        }

        static string NameOf(int session)
        {
            string fp = _protocol.Roster.FingerprintOf(session);
            FriendBook.Entry e = _protocol.Book == null ? null : _protocol.Book.Find(fp);
            string name = e == null ? null : e.Name;
            return (string.IsNullOrEmpty(name) ? "?" : name) + " (" + Keys.Short(fp) + ")";
        }

        static void Receive(Connection c, AltaStream stream)
        {
            uint kind, msg, seq;
            bool fin;
            string piece;
            if (!Wire.Read(stream, out kind, out msg, out seq, out fin, out piece)) return;
            if (c != _server) return;

            string err;
            string whole = _inbox.Accept(msg, seq, fin, piece, Now(), out err);
            if (err != null) Log.Detail("frame dropped (" + err + ").");
            if (whole == null) return;
            string why;
            Fields body = Fields.Decode(whole, out why);
            if (body == null)
            {
                Log.Detail("message dropped (" + why + ").");
                return;
            }

            string ownName;
            int ownSession;
            WhoAmI(out ownName, out ownSession);
            _protocol.OnMessage(kind, body, ownName, ownSession);
        }

        static void WhoAmI(out string name, out int session)
        {
            name = null;
            session = ClientProtocol.NoSession;
            try
            {
                var p = Player.Current;
                if (p != null && p.UserInfo != null) session = p.UserInfo.Identifier;
            }
            catch { }
            try
            {
                var api = ApiAccess.ApiClient;
                if (api != null && api.UserClient != null && api.UserClient.LoggedInUserInfo != null)
                    name = api.UserClient.LoggedInUserInfo.Username;
            }
            catch { }
        }

        static bool Consented(int sessionId, out string name)
        {
            name = null;
            try
            {
                Player p = Avatar(sessionId, false);
                if (p == null) return false;
                if (p.UserInfo != null) name = p.UserInfo.Username;
                return p.FriendshipManager != null && p.FriendshipManager.IsFriend;
            }
            catch
            {
                return false;
            }
        }

        static void Keep(FriendRecord record, PublicKey friend, string name)
        {
            string dir = _friendsDir;
            string who = (string.IsNullOrEmpty(name) ? "?" : name) + " (" + friend.Short + ")";
            string server = record.Server.Short;
            Worker.Run("save friend", delegate { return FriendStore.Save(dir, record, friend, name); },
                delegate(object result, Exception error)
                {
                    if (result == null)
                        Log.Error("could not save the friendship record with " + who + ": "
                            + (error == null ? "?" : error.GetType().Name + ": " + Keys.Shorten(error.Message)));
                    else
                        Log.Msg("friend " + who + ": record " + result + " (witnessed by server " + server + ").");
                });
        }

        static void Send(uint kind, Fields body)
        {
            if (_server != null) Wire.Send(_server, kind, body);
        }

        static void Sign(byte[] statement, ClientProtocol.Signed done)
        {
            KeyPair key = _protocol.Key;
            Worker.Run("sign", delegate { return key.Sign(statement); },
                delegate(object result, Exception error) { done(result as byte[], error); });
        }

        internal static double Now()
        {
            return Clock.Elapsed.TotalSeconds;
        }

        internal static long UnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        internal static string Status()
        {
            return _protocol == null ? "not started" : _protocol.StateText();
        }
    }
}
