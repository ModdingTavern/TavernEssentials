using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Responses.DTOs.Responses;
using Alta.Networking;

namespace TavernEssentials.Social
{
    internal sealed class ListSocialClient : ISocialClient
    {
        internal readonly ISocialClient Inner;

        internal ListSocialClient(ISocialClient inner)
        {
            Inner = inner;
        }

        public Task<IEnumerable<FriendshipInfo>> GetFriends()
        {
            return GameLists.Friends(Inner);
        }

        public Task<IEnumerable<FriendshipInfo>> GetFriendRequests()
        {
            return GameLists.Requests();
        }

        public Task<FriendshipInfo> GetFriendship(int userIdentifier, int friendIdentifier)
        {
            return GameLists.Friendship(Inner, userIdentifier, friendIdentifier);
        }

        public Task RemoveFriend(int friendIdentifier)
        {
            return GameLists.Remove(Inner, friendIdentifier);
        }

        public Task<FriendRequestedType> AddFriend(int friendIdentifier)
        {
            return GameLists.Add(Inner, friendIdentifier);
        }

        public Task<IEnumerable<FriendshipInfo>> GetSentFriendRequests()
        {
            return GameLists.Nothing<FriendshipInfo>();
        }

        public Task<IEnumerable<FriendshipInfo>> GetFriendsForUser(int userIdentifier)
        {
            return Inner == null ? GameLists.Nothing<FriendshipInfo>() : Inner.GetFriendsForUser(userIdentifier);
        }

        public Task<IEnumerable<OculusFriendInfo>> GetOculusFriends(IEnumerable<ulong> oculusIds)
        {
            return Inner == null ? GameLists.Nothing<OculusFriendInfo>() : Inner.GetOculusFriends(oculusIds);
        }

        public Task<FriendRequestResult> CreateFriendshipBetweenUsers(int userIdentifier, int friendIdentifier)
        {
            if (Inner == null) throw new InvalidOperationException("no social client");
            return Inner.CreateFriendshipBetweenUsers(userIdentifier, friendIdentifier);
        }
    }

    internal static class GameLists
    {
        internal const double MaxWait = 30;

        sealed class Waiting
        {
            internal bool Archived;
            internal TaskCompletionSource<IEnumerable<FriendshipInfo>> Done;
            internal double Since;
        }

        sealed class Restoring
        {
            internal string Note;
            internal FriendRecord Record;
            internal string Name;
            internal string Why;
        }

        static readonly FriendIds Ids = new FriendIds();
        static readonly FriendIds RequestIds = new FriendIds(FriendIds.RequestFirst);
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>();
        static readonly List<Waiting> _waiting = new List<Waiting>();
        static readonly HashSet<int> _vouched = new HashSet<int>();
        static readonly HashSet<int> _allowed = new HashSet<int>();
        static readonly HashSet<int> _logged = new HashSet<int>();
        static readonly List<Avatar> _avatars = new List<Avatar>();
        static readonly List<int> _add = new List<int>(), _withdraw = new List<int>();
        static string _dir;
        static bool _loading;
        static double _nextSwap;
        static FriendshipManager _fm;
        static IdList.ListedFn _listed;
        static bool _idListBroken;

        internal static bool RequestsAsking;

        internal static void Boot(string friendsDir)
        {
            _dir = friendsDir;
        }

        internal static bool On { get { return _dir != null; } }

        static bool Usable { get { return _dir != null && !ClientSide.KeyFailed; } }

        internal static void NewConnection()
        {
            _vouched.Clear();
            _logged.Clear();
            _fm = null;
            _listed = null;
        }

        internal static Task<IEnumerable<FriendshipInfo>> Friends(ISocialClient inner)
        {
            if (!Usable) return inner == null ? Nothing<FriendshipInfo>() : inner.GetFriends();
            return Ask(false);
        }

        internal static Task<IEnumerable<FriendshipInfo>> Requests()
        {
            bool asking = RequestsAsking;
            RequestsAsking = false;
            if (!asking || !Usable) return Nothing<FriendshipInfo>();
            return Ask(true);
        }

        internal static Task<FriendshipInfo> Friendship(ISocialClient inner, int user, int other)
        {
            if (!Usable) return inner == null ? Task.FromResult<FriendshipInfo>(null) : inner.GetFriendship(user, other);
            FriendshipInfo f = null;
            try
            {
                ClientProtocol protocol = ClientSide.Protocol;
                string fp = protocol == null ? null : protocol.FingerprintHere(other);
                if (fp != null && Allowed().Contains(other))
                {
                    _vouched.Add(other);
                    FriendBook.Entry e = protocol.Book == null ? null : protocol.Book.Find(fp);
                    f = Info(other, e == null || string.IsNullOrEmpty(e.Name) ? "friend " + Keys.Short(fp) : e.Name,
                        e == null ? 0 : e.Record.Ts, FriendshipType.Accepted);
                }
            }
            catch (Exception e)
            {
                Log.Warn("answering whether " + other + " is a friend: " + e.Message);
            }
            return Task.FromResult(f);
        }

        internal static Task Remove(ISocialClient inner, int id)
        {
            string fp = Usable ? FingerprintFor(id) : null;
            if (fp == null)
            {
                if (inner == null) throw new InvalidOperationException("no social client");
                return inner.RemoveFriend(id);
            }
            TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
            string dir = _dir, who = Label(fp);
            ClientProtocol protocol = ClientSide.Protocol;
            KeyPair me = protocol == null ? null : protocol.Key;
            if (me == null)
            {
                Log.Warn("friend " + who + ": not removed yet, your identity is still loading. Try again in a moment.");
                done.TrySetException(new InvalidOperationException("the identity is still loading"));
                return done.Task;
            }
            long now = ClientSide.UnixNow();
            Worker.Run("remove friend", delegate { return FriendStore.Remove(dir, me, fp, now); },
                delegate(object result, Exception error)
                {
                    Removal r = result as Removal;
                    if (r == null)
                    {
                        Log.Error("could not remove friend " + who + ": " + (error == null ? "?" : error.GetType().Name + ": " + Keys.Shorten(error.Message)));
                        done.TrySetException(error ?? new IOException("remove failed"));
                        return;
                    }
                    string name = Names.ContainsKey(fp) ? Names[fp] : r.Name;
                    if (r.Moved > 0)
                    {
                        Log.Msg("friend " + who + ": " + r.Moved + " record file(s) moved to friends\\removed\\, nothing deleted."
                            + " Only a friend card passed again makes you friends.");
                        ClientSide.Removed(fp, name, r.Made);
                    }
                    else if (StillCounted(fp))
                        ClientSide.Removed(fp, name, r.Made);
                    else
                        Log.Msg("friend " + who + ": nothing to remove (removed already, or never a friend).");
                    done.TrySetResult(true);
                });
            return done.Task;
        }

        internal static Task<FriendRequestedType> Add(ISocialClient inner, int id)
        {
            string fp = Usable ? FingerprintFor(id) : null;
            if (fp == null)
            {
                Log.Msg("a friend request for player " + id + " was not sent: friend cards make friends.");
                return Task.FromResult(FriendRequestedType.AlreadyRequested);
            }
            TaskCompletionSource<FriendRequestedType> done = new TaskCompletionSource<FriendRequestedType>();
            string dir = _dir, who = Label(fp);
            Worker.Run("restore friend", delegate
                {
                    Restoring r = new Restoring();
                    r.Note = FriendStore.Restore(dir, fp);
                    string path = FriendStore.PathFor(dir, fp);
                    if (File.Exists(path)) r.Record = FriendStore.Load(path, out r.Name, out r.Why);
                    return r;
                },
                delegate(object result, Exception error)
                {
                    Restoring r = result as Restoring;
                    if (r == null)
                    {
                        Log.Error("could not restore friend " + who + ": " + (error == null ? "?" : error.GetType().Name + ": " + Keys.Shorten(error.Message)));
                        done.TrySetException(error ?? new IOException("restore failed"));
                        return;
                    }
                    if (r.Note == null && r.Record == null)
                    {
                        Log.Warn("friend " + who + ": nothing archived to restore.");
                        done.TrySetException(new InvalidOperationException("nothing archived for that friend"));
                        return;
                    }
                    if (r.Note == null) Log.Msg("friend " + who + " is a friend already.");
                    else Log.Msg("friend " + who + ": " + r.Note + ".");
                    if (r.Record == null) Log.Warn("friend " + who + ": the record is back in friends\\ but is not usable (" + r.Why + ").");
                    else ClientSide.Restored(r.Record, r.Name);
                    done.TrySetResult(r.Note == null ? FriendRequestedType.AlreadyFriends : FriendRequestedType.Befriended);
                });
            return done.Task;
        }

        internal static int MadeUpId(string fp)
        {
            return Ids.IdOf(fp);
        }

        internal static bool CanRestore(int id)
        {
            string fp = Usable ? FingerprintFor(id) : null;
            return fp != null && File.Exists(FriendStore.PathFor(FriendStore.ArchiveDir(_dir), fp));
        }

        static bool StillCounted(string fp)
        {
            ClientProtocol protocol = ClientSide.Protocol;
            if (protocol == null) return false;
            if (protocol.Book != null && protocol.Book.Find(fp) != null) return true;
            int session;
            return protocol.SessionOf(fp, out session) && protocol.FingerprintHere(session) != null;
        }

        static string FingerprintFor(int id)
        {
            if (FriendIds.IsRequest(id)) return RequestIds.FingerprintOf(id);
            if (FriendIds.IsMadeUp(id)) return Ids.FingerprintOf(id);
            ClientProtocol protocol = ClientSide.Protocol;
            return protocol == null ? null : protocol.FingerprintAt(id);
        }

        static Task<IEnumerable<FriendshipInfo>> Ask(bool archived)
        {
            Waiting w = new Waiting();
            w.Archived = archived;
            w.Done = new TaskCompletionSource<IEnumerable<FriendshipInfo>>();
            w.Since = ClientSide.Now();
            _waiting.Add(w);
            return w.Done.Task;
        }

        static void Load(PublicKey me)
        {
            List<Waiting> batch = new List<Waiting>(_waiting);
            _waiting.Clear();
            _loading = true;
            string dir = _dir;
            List<string> notes = new List<string>();
            Worker.Run("read friends lists", delegate { return FriendLists.Load(dir, me, notes); },
                delegate(object result, Exception error)
                {
                    _loading = false;
                    FriendLists lists = result as FriendLists;
                    if (lists == null)
                        Log.Warn("could not read your friends folder for the game's lists: " + (error == null ? "?" : error.GetType().Name + ": " + Keys.Shorten(error.Message)));
                    else
                        foreach (string n in notes) Log.Detail(n);
                    foreach (Waiting w in batch)
                    {
                        IEnumerable<FriendshipInfo> rows = new FriendshipInfo[0];
                        try
                        {
                            if (lists != null) rows = w.Archived ? ArchivedRows(lists.Archived) : FriendRows(lists.Friends);
                        }
                        catch (Exception e)
                        {
                            Log.Warn("building a friends list: " + e.Message);
                        }
                        w.Done.TrySetResult(rows);
                    }
                });
        }

        static IEnumerable<FriendshipInfo> FriendRows(FriendBook book)
        {
            ClientProtocol protocol = ClientSide.Protocol;
            Allowed();
            List<FriendshipInfo> rows = new List<FriendshipInfo>();
            foreach (ListRow r in ListRows.From(book, Ids.IdOf))
            {
                Names[r.Fingerprint] = r.Name;
                int id = r.Id, session;
                if (protocol != null && protocol.SessionOf(r.Fingerprint, out session) && _allowed.Contains(session))
                {
                    id = session;
                    _vouched.Add(session);
                }
                rows.Add(Info(id, r.Name, r.Ts, FriendshipType.Accepted));
            }
            return rows;
        }

        static IEnumerable<FriendshipInfo> ArchivedRows(FriendBook book)
        {
            List<FriendshipInfo> rows = new List<FriendshipInfo>();
            foreach (ListRow r in ListRows.From(book, RequestIds.IdOf))
            {
                Names[r.Fingerprint] = r.Name;
                rows.Add(Info(r.Id, r.Name, r.Ts, FriendshipType.Requested));
            }
            return rows;
        }

        static FriendshipInfo Info(int id, string name, long ts, FriendshipType type)
        {
            FriendshipInfo f = new FriendshipInfo();
            f.Identifier = id;
            f.Username = name;
            f.CreatedAt = ListRows.When(ts);
            f.IconIndex = 0;
            f.Type = type;
            return f;
        }

        static string Label(string fp)
        {
            string name;
            return (Names.TryGetValue(fp, out name) ? name : "?") + " (" + Keys.Short(fp) + ")";
        }

        internal static Task<IEnumerable<T>> Nothing<T>()
        {
            return Task.FromResult<IEnumerable<T>>(new T[0]);
        }

        internal static void Tick(double now, bool connected)
        {
            if (_dir == null) return;
            RequestsAsking = false;
            if (now >= _nextSwap)
                _nextSwap = SocialSwap.Client.Ready() || SocialSwap.Client.Broken ? now + 1 : now;

            if (_waiting.Count > 0) Answer(now);
            if (!connected || _idListBroken) return;
            try { KeepIdList(); }
            catch (Exception e)
            {
                _idListBroken = true;
                Log.Error("keeping the friend id list failed, so friends-only voice will not count friends until a restart: " + e);
            }
        }

        static void Answer(double now)
        {
            ClientProtocol protocol = ClientSide.Protocol;
            if (!ClientSide.KeyFailed && protocol != null && protocol.Key != null)
            {
                if (!_loading) Load(protocol.Key.Public);
                return;
            }
            List<Waiting> expired = new List<Waiting>();
            for (int i = _waiting.Count - 1; i >= 0; i--)
            {
                if (!ClientSide.KeyFailed && now - _waiting[i].Since < MaxWait) continue;
                expired.Add(_waiting[i]);
                _waiting.RemoveAt(i);
            }
            foreach (Waiting w in expired) w.Done.TrySetResult(new FriendshipInfo[0]);
        }

        static void KeepIdList()
        {
            ClientProtocol protocol = ClientSide.Protocol;
            if (protocol == null || (protocol.FriendsHereCount == 0 && _vouched.Count == 0)) return;
            FriendshipManager fm = null;
            try
            {
                var me = Player.Current;
                if (me != null) fm = me.FriendshipManager;
            }
            catch { }
            if (fm == null) return;
            if (fm != _fm)
            {
                _fm = fm;
                _listed = fm.IsFriendsWith;
            }

            Allowed();
            IdList.Plan(_allowed, _vouched, _listed, _add, _withdraw);
            foreach (int id in _withdraw)
            {
                fm.UpdateStatusWithPlayer(false, id);
                string fp = protocol.FingerprintHere(id);
                if (fp == null) { _vouched.Remove(id); continue; }
                Log.Warn("friend " + protocol.NameHere(id) + ": another avatar here goes by their player id, so friends-only voice"
                    + " stops counting that id until it is theirs alone again.");
            }
            foreach (int id in _add)
            {
                fm.UpdateStatusWithPlayer(true, id);
                if (_logged.Add(id)) Log.Msg("friend " + protocol.NameHere(id) + ": counted as a friend for friends-only voice.");
            }
            foreach (int id in _allowed) _vouched.Add(id);
        }

        static HashSet<int> Allowed()
        {
            _allowed.Clear();
            ClientProtocol protocol = ClientSide.Protocol;
            if (protocol == null || protocol.FriendsHereCount == 0) return _allowed;
            _avatars.Clear();
            foreach (var ip in Player.AllPlayers)
            {
                Player p = ip as Player;
                if (p == null || p.UserInfo == null) continue;
                _avatars.Add(new Avatar(p.UserInfo.Identifier, ((INetworkEntity)p).Identifier));
            }
            protocol.Vouchable(_avatars, _allowed);
            return _allowed;
        }
    }
}
