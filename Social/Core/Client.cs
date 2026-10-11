using System;
using System.Collections.Generic;
using System.Globalization;

namespace TavernEssentials.Social
{
    internal sealed class AnswerPlan
    {
        internal byte[] Statement;
        internal PublicKey Server;
        internal string Nonce;
        internal string Name;
        internal int SessionId;
        internal bool NameUnchecked;
    }

    internal sealed class OfferPlan
    {
        internal string Id;
        internal long Ts;
        internal byte[] Statement;
        internal PublicKey Other;
        internal int OtherSession;
    }

    internal sealed class SessionRoster
    {
        readonly Dictionary<int, string> _fp = new Dictionary<int, string>();
        readonly Dictionary<int, long> _entity = new Dictionary<int, long>();

        internal int Count { get { return _fp.Count; } }

        internal void Clear()
        {
            _fp.Clear();
            _entity.Clear();
        }

        internal string FingerprintOf(int sessionId)
        {
            string fp;
            return _fp.TryGetValue(sessionId, out fp) ? fp : null;
        }

        internal bool TryGetSession(string fingerprint, out int sessionId)
        {
            foreach (KeyValuePair<int, string> e in _fp)
                if (e.Value == fingerprint) { sessionId = e.Key; return true; }
            sessionId = 0;
            return false;
        }

        internal long EntityOf(int sessionId)
        {
            long e;
            return _entity.TryGetValue(sessionId, out e) ? e : 0;
        }

        internal List<KeyValuePair<int, string>> Entries()
        {
            return new List<KeyValuePair<int, string>>(_fp);
        }

        internal void Apply(Fields f, out int added, out int removed)
        {
            added = 0;
            removed = 0;
            if (f.Get("full") == "1") Clear();
            foreach (string a in f.GetAll("add"))
            {
                int id;
                string fp;
                if (!Split(a, out id, out fp) || !Keys.LooksLikeFingerprint(fp)) continue;
                _fp[id] = fp;
                _entity.Remove(id);
                added++;
            }
            foreach (string en in f.GetAll("ent"))
            {
                int id;
                string rest;
                long entity;
                if (!Split(en, out id, out rest) || !_fp.ContainsKey(id)) continue;
                if (!long.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out entity) || entity <= 0) continue;
                _entity[id] = entity;
            }
            foreach (string d in f.GetAll("del"))
            {
                int id;
                if (!int.TryParse(d, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id)) continue;
                _entity.Remove(id);
                if (_fp.Remove(id)) removed++;
            }
        }

        static bool Split(string s, out int id, out string rest)
        {
            id = 0;
            rest = null;
            int colon = s.IndexOf(':');
            if (colon <= 0) return false;
            if (!int.TryParse(s.Substring(0, colon), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id)) return false;
            rest = s.Substring(colon + 1);
            return true;
        }
    }

    internal sealed class AwayFriend
    {
        internal int Id;
        internal string Name;
        internal string Fingerprint;
    }

    internal sealed class ClientProtocol
    {
        internal static readonly double[] HelloWaits = { 2.5, 2.5, 5, 10, 15 };
        internal static int HelloTries { get { return HelloWaits.Length; } }
        internal const int FreshRetries = 2;
        internal const double ProofWait = 15;
        internal const int NoSession = -1;
        internal const int MaxCoSigning = 4;
        internal const double RefusalLogEvery = 10;
        internal const double ShowEvery = 1.5;
        internal const double RecognitionWait = 30;
        internal const double WithdrawEvery = 7;
        internal const int MaxPending = 20;
        internal const int MaxEarly = 8;

        internal delegate void SendFn(uint kind, Fields body);
        internal delegate void Signed(byte[] signature, Exception error);
        internal delegate void SignFn(byte[] statement, Signed done);
        internal delegate bool ConsentFn(int sessionId, out string name);
        internal delegate void KeepFn(FriendRecord record, PublicKey friend, string name);
        internal delegate bool HeldFn(string friendFingerprint);
        internal delegate void SaidFn(bool friendsWork);
        internal delegate void TheyRemovedFn(string friendFingerprint, List<KeyValuePair<long, string>> records, string name);

        internal enum Stage { Idle, Hello, Signing, Proved, Confirmed, Silent, Refused }

        readonly SendFn _send;
        readonly SignFn _sign;
        internal readonly SessionRoster Roster = new SessionRoster();

        internal KeyPair Key;
        internal ConsentFn Consented;
        internal KeepFn Keep;
        internal HeldFn StillHeld;
        internal SaidFn ServerSaid;
        internal TheyRemovedFn TheyRemoved;
        internal FriendBook Book;
        internal RecordIndex Copies;
        Stage _stage;
        int _tries;
        int _retries;
        bool _retryHello;
        double _nextHello;
        double _proofAt;
        double _now;
        int _epoch;
        string _serverFp;
        PublicKey _server;
        int _coSigning;
        double _refusalLoggedAt = -1000;
        readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        internal string LastProblem;
        internal bool ServerWitnesses;
        internal bool ServerRecognises;

        readonly Dictionary<string, double> _shownAt = new Dictionary<string, double>();
        readonly Dictionary<int, double> _arrivedAt = new Dictionary<int, double>();
        readonly HashSet<string> _quiet = new HashSet<string>();
        readonly Dictionary<int, string> _friendsHere = new Dictionary<int, string>();
        readonly Dictionary<string, string> _archived = new Dictionary<string, string>();
        double _nextShow;
        bool _noCheckLogged;
        readonly HashSet<string> _known = new HashSet<string>();
        readonly Dictionary<string, bool> _isKnown = new Dictionary<string, bool>();
        readonly Dictionary<string, AwayFriend> _away = new Dictionary<string, AwayFriend>();
        internal const int MaxKnown = 2000;

        internal bool ServerWithdraws;
        readonly List<Withdrawal> _fresh = new List<Withdrawal>();
        List<Withdrawal> _backlog;
        readonly HashSet<string> _handed = new HashSet<string>();
        double _nextWithdraw;
        readonly HashSet<string> _withdrawn = new HashSet<string>();
        readonly List<Fields> _early = new List<Fields>();

        internal ClientProtocol(SendFn send, SignFn sign)
        {
            _send = send;
            _sign = sign;
        }

        internal Stage Current { get { return _stage; } }
        internal string ServerFingerprint { get { return _serverFp; } }
        internal bool Friends { get { return Consented != null && Keep != null; } }

        internal bool Withdraws { get { return Friends && TheyRemoved != null; } }

        internal List<int> FriendsHere()
        {
            List<int> l = new List<int>();
            foreach (KeyValuePair<int, string> f in _friendsHere)
                if (!_archived.ContainsKey(f.Value)) l.Add(f.Key);
            return l;
        }

        internal int FriendsHereCount
        {
            get
            {
                int n = 0;
                foreach (KeyValuePair<int, string> f in _friendsHere)
                    if (!_archived.ContainsKey(f.Value)) n++;
                return n;
            }
        }

        internal bool IsFriendHere(int sessionId)
        {
            return FingerprintHere(sessionId) != null;
        }

        internal string FingerprintHere(int sessionId)
        {
            string fp;
            return _friendsHere.TryGetValue(sessionId, out fp) && !_archived.ContainsKey(fp) ? fp : null;
        }

        internal string FingerprintAt(int sessionId)
        {
            string fp;
            return _friendsHere.TryGetValue(sessionId, out fp) ? fp : null;
        }

        internal string NameHere(int sessionId)
        {
            string fp = FingerprintAt(sessionId);
            if (fp == null) return "session " + sessionId.ToString(CultureInfo.InvariantCulture);
            string archivedName;
            if (_archived.TryGetValue(fp, out archivedName)) return Named(archivedName, fp);
            return Named(Book == null ? null : Book.Find(fp), fp);
        }

        internal bool SessionOf(string friendFingerprint, out int sessionId)
        {
            foreach (KeyValuePair<int, string> f in _friendsHere)
                if (f.Value == friendFingerprint) { sessionId = f.Key; return true; }
            sessionId = NoSession;
            return false;
        }

        internal void Vouchable(IList<Avatar> avatars, HashSet<int> into)
        {
            into.Clear();
            foreach (KeyValuePair<int, string> f in _friendsHere)
            {
                if (_archived.ContainsKey(f.Value)) continue;
                long entity = Roster.EntityOf(f.Key);
                if (entity <= 0) continue;
                int wearing = 0;
                bool atEntity = false;
                for (int i = 0; i < avatars.Count; i++)
                {
                    if (avatars[i].Id != f.Key) continue;
                    wearing++;
                    if (avatars[i].Entity == entity) atEntity = true;
                }
                if (wearing == 1 && atEntity) into.Add(f.Key);
            }
        }

        internal void UseBook(FriendBook book, RecordIndex copies = null)
        {
            if (book != null)
                foreach (string fp in _archived.Keys) book.Remove(fp);
            if (copies != null)
                foreach (string fp in _archived.Keys) copies.RemoveAll(fp);
            Copies = copies;
            Book = book;
        }

        internal void Removed(string friendFingerprint, string name, List<Withdrawal> made)
        {
            FriendBook.Entry e = Book == null ? null : Book.Find(friendFingerprint);
            if (string.IsNullOrEmpty(name) && e != null) name = e.Name;
            _archived[friendFingerprint] = name;
            _away.Remove(friendFingerprint);
            if (Book != null) Book.Remove(friendFingerprint);
            _shownAt.Remove(friendFingerprint);
            _quiet.Remove(friendFingerprint);
            if (Copies != null) Copies.RemoveAll(friendFingerprint);
            if (made != null)
                foreach (Withdrawal w in made)
                    if (!_fresh.Exists(delegate(Withdrawal x) { return x.Index == w.Index; })) _fresh.Add(w);
            if (made != null && made.Count > 0)
                Log.Msg("friend " + Named(name, friendFingerprint) + " removed. Their game is told on the TE servers you both visit.");
            else
                Log.Msg("friend " + Named(name, friendFingerprint) + " removed here. You held no record with them to withdraw, so their game is not told.");
        }

        internal int EarlyCount { get { return _early.Count; } }

        internal void Restored(FriendRecord record, string name)
        {
            if (Key == null || record == null) return;
            PublicKey friend = record.Other(Key.Public);
            if (friend == null) return;
            if (Book != null) Book.Put(record, Key.Public, name);
        }

        internal void Reset()
        {
            _epoch++;
            _stage = Stage.Hello;
            _tries = 0;
            _retries = 0;
            _retryHello = false;
            _nextHello = 0;
            Forget();
            Book = null;
            Copies = null;
            LastProblem = null;
        }

        internal void Disconnected()
        {
            _epoch++;
            _stage = Stage.Idle;
            Forget();
        }

        void Forget()
        {
            _serverFp = null;
            _server = null;
            ServerWitnesses = false;
            ServerRecognises = false;
            ServerWithdraws = false;
            _coSigning = 0;
            _names.Clear();
            _archived.Clear();
            Roster.Clear();
            ForgetFriendsHere();
            _fresh.Clear();
            _backlog = null;
            _handed.Clear();
            _nextWithdraw = 0;
            _withdrawn.Clear();
            _early.Clear();
        }

        void ForgetFriendsHere()
        {
            _shownAt.Clear();
            _arrivedAt.Clear();
            _quiet.Clear();
            _friendsHere.Clear();
            _nextShow = 0;
            _noCheckLogged = false;
            _known.Clear();
            _isKnown.Clear();
            _away.Clear();
        }

        internal List<AwayFriend> AwayFriends()
        {
            List<AwayFriend> l = new List<AwayFriend>();
            foreach (AwayFriend a in _away.Values)
            {
                int session;
                if (_archived.ContainsKey(a.Fingerprint) || Roster.TryGetSession(a.Fingerprint, out session)) continue;
                l.Add(a);
            }
            return l;
        }

        internal void Tick(double now, bool ready)
        {
            _now = now;
            if (_stage == Stage.Proved && now - _proofAt > ProofWait)
            {
                Log.Warn("the server never answered the identity proof.");
                Retry("no answer to the proof");
            }
            if (_stage == Stage.Confirmed)
            {
                TakeEarly();
                HandOver(now);
                ShowRecords(now);
            }
            if (_stage != Stage.Hello || !ready || Key == null || now < _nextHello) return;
            if (_tries >= HelloTries && !_retryHello)
            {
                _stage = Stage.Silent;
                if (_serverFp == null)
                {
                    Log.Msg("no TavernEssentials or TavernSocial on this server (" + HelloTries + " hellos, no answer).");
                    Said(false);
                }
                else
                    Log.Warn("gave up on this server's identity check (" + (LastProblem ?? "?") + ").");
                return;
            }
            if (_retryHello)
            {
                _retryHello = false;
                Log.Detail("hello again for a fresh challenge (" + _retries + "/" + FreshRetries + ").");
            }
            else
            {
                _tries++;
                Log.Detail("hello " + _tries + "/" + HelloTries + ".");
            }
            _nextHello = now + HelloWaits[Math.Max(_tries, 1) - 1];
            Fields hello = new Fields().Add("proto", Proto.Version).Add("mod", Mod.Version);
            if (Friends) hello.Add("friends", 1);
            if (Withdraws) hello.Add("withdraw", 1);
            _send(Proto.Hello, hello);
        }

        internal void OnMessage(uint kind, Fields body, string ownName, int ownSessionId)
        {
            if (body == null) return;
            if (kind == Proto.Challenge) OnChallenge(body, ownName, ownSessionId);
            else if (kind == Proto.Welcome) OnWelcome(body);
            else if (kind == Proto.Roster) OnRoster(body);
            else if (kind == Proto.Reject) OnReject(body);
            else if (kind == Proto.Offer) OnOffer(body);
            else if (kind == Proto.Record) OnRecord(body);
            else if (kind == Proto.Mutual) OnMutual(body);
            else if (kind == Proto.Known) OnKnown(body);
            else if (kind == Proto.Away) OnAway(body);
            else if (kind == Proto.Withdrawn) OnWithdrawn(body);
        }

        void OnChallenge(Fields c, string ownName, int ownSessionId)
        {
            if (_stage != Stage.Hello || _tries == 0 || Key == null) return;
            string why;
            AnswerPlan plan = Answer(c, ownName, ownSessionId, out why);
            if (plan == null)
            {
                _stage = Stage.Refused;
                LastProblem = why;
                Log.Warn("not answering this server's identity challenge: " + why + ".");
                return;
            }
            if (plan.NameUnchecked)
                Log.Warn("could not read your own username, so the server's name for you (" + plan.Name + ") went unchecked.");

            _serverFp = plan.Server.Fingerprint;
            _server = plan.Server;
            _stage = Stage.Signing;
            int epoch = _epoch;
            KeyPair key = Key;
            string nonce = plan.Nonce;
            Log.Detail("challenged by server " + plan.Server.Short + "; signing.");
            _sign(plan.Statement, delegate(byte[] sig, Exception error)
            {
                if (epoch != _epoch || _stage != Stage.Signing) return;
                if (sig == null)
                {
                    _stage = Stage.Refused;
                    LastProblem = "signing failed";
                    Log.Error("signing failed: " + (error == null ? "no signature" : error.GetType().Name + ": " + error.Message));
                    return;
                }
                _stage = Stage.Proved;
                _proofAt = _now;
                _send(Proto.Proof, new Fields()
                    .Add("nonce", nonce)
                    .Add("n", key.Public.ModulusBase64)
                    .Add("e", key.Public.ExponentBase64)
                    .Add("sig", Convert.ToBase64String(sig)));
            });
        }

        void Said(bool friendsWork)
        {
            SaidFn said = ServerSaid;
            if (said == null) return;
            try { said(friendsWork); }
            catch (Exception e) { Log.Warn("marking this server on the boards: " + e.Message); }
        }

        void OnWelcome(Fields w)
        {
            if (_stage != Stage.Proved) return;
            string fp = w.Get("fp");
            if (Key == null || fp != Key.Public.Fingerprint)
            {
                _stage = Stage.Refused;
                LastProblem = "the server confirmed a different key";
                Log.Error("the server confirmed key " + Keys.Short(fp) + ", but yours is "
                    + (Key == null ? "?" : Key.Public.Short) + ".");
                return;
            }
            _stage = Stage.Confirmed;
            ServerWitnesses = w.Get("friends") == "1";
            ServerRecognises = w.Get("mutual") == "1";
            ServerWithdraws = Withdraws && w.Get("withdraw") == "1";
            ForgetFriendsHere();
            Log.Msg("identity " + Key.Public.Short + " confirmed by server " + Keys.Short(_serverFp) + ".");
            if (Friends)
                Log.Msg(ServerWitnesses ? "friend cards passed here make records."
                    : "this server did not say it witnesses friend cards (TavernSocial before 0.2.1, friends off, or not ready): a card passed here may make no record.");
            if (!ServerRecognises) Said(false);
            else if (ServerWitnesses) Said(true);
        }

        void OnRoster(Fields r)
        {
            if (_stage != Stage.Confirmed) return;
            bool full = r.Get("full") == "1";
            int added, removed;
            Dictionary<int, string> before = new Dictionary<int, string>();
            foreach (KeyValuePair<int, string> e in Roster.Entries()) before[e.Key] = e.Value;
            Roster.Apply(r, out added, out removed);
            foreach (KeyValuePair<int, string> e in Roster.Entries())
            {
                string was;
                if (!before.TryGetValue(e.Key, out was) || was != e.Value) _arrivedAt[e.Key] = _now;
            }
            foreach (int session in new List<int>(_arrivedAt.Keys))
                if (Roster.FingerprintOf(session) == null) _arrivedAt.Remove(session);
            if (full)
                Log.Msg(Roster.Count + " other identified player(s) here.");
            else if (added + removed > 0)
                Log.Msg("roster " + (added > 0 ? "+" + added : "-" + removed) + ", now " + Roster.Count + " other identified player(s) here.");

            foreach (KeyValuePair<int, string> f in new List<KeyValuePair<int, string>>(_friendsHere))
            {
                if (Roster.FingerprintOf(f.Key) == f.Value) continue;
                _friendsHere.Remove(f.Key);
                FriendBook.Entry e = Book == null ? null : Book.Find(f.Value);
                Log.Msg("friend " + Named(e, f.Value) + " left.");
            }
        }

        void OnReject(Fields r)
        {
            string why = r.Get("why") ?? "unknown";
            if (why == "displaced" || why == "revoked")
            {
                if (_stage != Stage.Confirmed) return;
                _stage = Stage.Refused;
                LastProblem = why;
                Roster.Clear();
                ForgetFriendsHere();
                if (why == "displaced")
                    Log.Warn("another game using your key joined this server, so this one is no longer identified here.");
                else
                    Log.Warn("this server saw your player change its id in-game and stopped identifying you.");
                return;
            }
            if (_stage != Stage.Proved) return;
            if (why == "no-challenge" || why == "stale" || why == "expired" || why == "session-changed" || why == "not-ready")
            {
                Log.Detail("the server asked for a fresh challenge (" + why + ").");
                Retry(why);
                return;
            }
            _stage = Stage.Refused;
            LastProblem = why;
            Log.Warn("the server refused the identity proof (" + why + ").");
        }

        void OnOffer(Fields o)
        {
            string id = o.Get("offer");
            if (!Keys.LooksLikeNonce(id)) return;
            if (_stage != Stage.Confirmed || Key == null || _server == null) return;
            if (!Friends) { Decline(id, "off"); return; }
            if (_coSigning >= MaxCoSigning) { Decline(id, "busy"); return; }

            string code, why;
            OfferPlan plan = Consider(o, Key.Public, _server.Fingerprint, Roster, out code, out why);
            if (plan == null)
            {
                Decline(id, code);
                RefusalLog("not co-signing a friendship record: " + why + ".");
                return;
            }
            string name, refusal;
            if (!ConsentsTo(plan.OtherSession, plan.Other.Fingerprint, out name, out refusal))
            {
                Decline(id, "no-card");
                RefusalLog("not co-signing a friendship with " + Label(name, plan) + ": " + refusal + ".");
                return;
            }
            if (!string.IsNullOrEmpty(name)) _names[plan.Other.Fingerprint] = name;

            _coSigning++;
            int epoch = _epoch;
            Log.Detail("co-signing a friendship with " + Label(name, plan) + ".");
            _sign(plan.Statement, delegate(byte[] sig, Exception error)
            {
                if (epoch != _epoch) return;
                _coSigning--;
                if (sig == null)
                {
                    Log.Error("co-signing failed: " + (error == null ? "no signature" : error.GetType().Name + ": " + error.Message));
                    Decline(id, "error");
                    return;
                }
                _send(Proto.Consent, new Fields().Add("offer", id).Add("sig", Convert.ToBase64String(sig)));
            });
        }

        void OnRecord(Fields f)
        {
            if (_stage != Stage.Confirmed || Key == null || _server == null || !Friends) return;
            string why;
            FriendRecord r = FriendRecord.Accept(f, Key.Public, _server, out why);
            if (r == null)
            {
                Log.Warn("refused a friendship record from this server: " + why + ".");
                return;
            }
            PublicKey friend = r.Other(Key.Public);
            FriendBook.Entry known = Book == null ? null : Book.Find(friend.Fingerprint);
            int session;
            string name, refusal;
            if (Copies != null && Copies.IsRemoved(friend.Fingerprint, r.Ts, r.Server.Fingerprint))
            {
                if (!_archived.TryGetValue(friend.Fingerprint, out name)) name = Copies.RemovedName(friend.Fingerprint, r.Ts, r.Server.Fingerprint);
                RefusalLog("this server sent back an old friend record with " + Named(name, friend.Fingerprint) + " that was removed; it is not kept."
                    + " Make a new card on a server with TavernEssentials 0.13.0.");
                return;
            }
            if (!Roster.TryGetSession(friend.Fingerprint, out session))
                refusal = "they are not identified here";
            else
                ConsentsTo(session, friend.Fingerprint, out name, out refusal);
            if (refusal != null)
            {
                RefusalLog("not keeping a friendship record with " + Named(known, friend.Fingerprint) + ": " + refusal + ".");
                return;
            }
            _names.TryGetValue(friend.Fingerprint, out name);
            if (string.IsNullOrEmpty(name) && known != null) name = known.Name;
            if (Book != null) Book.Put(r, Key.Public, name);
            if (Copies != null) Copies.Add(RecordIndex.LiveCopy(r, friend, name));
            if (_archived.Remove(friend.Fingerprint))
            {
                foreach (KeyValuePair<int, string> h in new List<KeyValuePair<int, string>>(_friendsHere))
                    if (h.Value == friend.Fingerprint) _friendsHere.Remove(h.Key);
                _shownAt.Remove(friend.Fingerprint);
                _quiet.Remove(friend.Fingerprint);
            }
            Keep(r, friend, name);
        }

        bool ConsentsTo(int session, string friendFp, out string name, out string refusal)
        {
            refusal = null;
            if (!Consented(session, out name))
                refusal = "your game saw no friend card passed between you";
            else if (_friendsHere.ContainsKey(session) && !_archived.ContainsKey(friendFp) && (StillHeld == null || !StillHeld(friendFp)))
                refusal = "you removed your record of them";
            return refusal == null;
        }

        void ShowRecords(double now)
        {
            if (!Friends || Book == null || Key == null || Book.Count == 0) return;
            foreach (KeyValuePair<int, string> e in Roster.Entries())
            {
                FriendBook.Entry f = Book.Find(e.Value);
                if (f == null) continue;
                double at;
                if (!_shownAt.TryGetValue(e.Value, out at))
                {
                    if (!ServerRecognises)
                    {
                        if (!_noCheckLogged)
                            Log.Msg("friend " + Named(f, e.Value) + " is here, but this server does not recognise friends"
                                + " (TavernSocial before 0.3.0, or friends off there).");
                        _noCheckLogged = true;
                        continue;
                    }
                    if (now < _nextShow) continue;
                    _nextShow = now + ShowEvery;
                    _shownAt[e.Value] = now;
                    Log.Detail("showing the server our record with " + Named(f, e.Value) + ".");
                    _send(Proto.Show, new Fields()
                        .Add("fp", e.Value)
                        .Add("ts", f.Record.Ts)
                        .Add("srv", f.Record.Server.Fingerprint)
                        .Add("sig", Convert.ToBase64String(f.Record.SignatureOf(f.Friend))));
                    continue;
                }
                double arrived;
                if (_arrivedAt.TryGetValue(e.Key, out arrived) && arrived > at) at = arrived;
                if (now - at >= RecognitionWait && !_friendsHere.ContainsKey(e.Key) && _quiet.Add(e.Value))
                    Log.Msg("friend " + Named(f, e.Value) + " is here, but has not shown their copy of your friendship:"
                        + " they removed it, or run TavernSocial before 0.3.0.");
            }
            if (_known.Count == 0 || !ServerRecognises || _serverFp == null) return;
            foreach (FriendBook.Entry f in Book.Entries())
            {
                string fp = f.Friend.Fingerprint;
                int session;
                if (_shownAt.ContainsKey(fp) || Roster.TryGetSession(fp, out session) || !IsKnown(fp)) continue;
                if (now < _nextShow) return;
                _nextShow = now + ShowEvery;
                _shownAt[fp] = now;
                Log.Detail("showing the server our record with " + Named(f, fp) + ", who has been here before.");
                _send(Proto.Show, new Fields()
                    .Add("fp", fp)
                    .Add("ts", f.Record.Ts)
                    .Add("srv", f.Record.Server.Fingerprint)
                    .Add("sig", Convert.ToBase64String(f.Record.SignatureOf(f.Friend))));
            }
        }

        bool IsKnown(string fp)
        {
            bool known;
            if (!_isKnown.TryGetValue(fp, out known))
                _isKnown[fp] = known = _known.Contains(Proto.KnownTag(_serverFp, fp));
            return known;
        }

        void OnKnown(Fields k)
        {
            if (_stage != Stage.Confirmed || !Friends) return;
            int before = _known.Count;
            foreach (string tag in k.GetAll("tag"))
                if (tag.Length == 12 && _known.Count < MaxKnown) _known.Add(tag);
            _isKnown.Clear();
            Log.Detail("the server's address book: " + _known.Count + " player(s) who are not here (+" + (_known.Count - before) + ").");
        }

        void OnAway(Fields a)
        {
            if (_stage != Stage.Confirmed || !Friends) return;
            int id;
            string fp = a.Get("fp");
            if (!a.TryGetInt("id", out id) || id < 0 || fp == null) return;
            FriendBook.Entry f = Book == null ? null : Book.Find(fp);
            if (f == null || !_shownAt.ContainsKey(fp))
            {
                if (f == null && _archived.ContainsKey(fp)) return;
                Log.Warn("the server says " + Keys.Short(fp) + " is your friend away, but you showed it no record with them; ignored.");
                return;
            }
            bool first = !_away.ContainsKey(fp);
            string name = string.IsNullOrEmpty(f.Name) ? a.Get("name") : f.Name;
            _away[fp] = new AwayFriend { Id = id, Name = string.IsNullOrEmpty(name) ? "friend " + Keys.Short(fp) : name, Fingerprint = fp };
            if (first) Log.Msg("friend " + Named(f, fp) + " is away; the postbox can send to them.");
        }

        void OnMutual(Fields m)
        {
            if (_stage != Stage.Confirmed || !Friends) return;
            int id;
            string fp = m.Get("fp");
            if (!m.TryGetInt("id", out id) || fp == null || Roster.FingerprintOf(id) != fp) return;
            FriendBook.Entry f = Book == null ? null : Book.Find(fp);
            string archivedName;
            if (_archived.TryGetValue(fp, out archivedName))
            {
                if (_friendsHere.ContainsKey(id)) return;
                _friendsHere[id] = fp;
                Log.Msg("friend " + Named(archivedName, fp) + " is here, but removed: not counted as a friend.");
                return;
            }
            if (f == null || !_shownAt.ContainsKey(fp))
            {
                Log.Warn("the server says " + Keys.Short(fp) + " is your friend here, but you hold no record with them; ignored.");
                return;
            }
            if (_friendsHere.ContainsKey(id)) return;
            _friendsHere[id] = fp;
            string now = m.Get("name");
            bool renamed = !string.IsNullOrEmpty(now) && !string.Equals(now, f.Name, StringComparison.OrdinalIgnoreCase);
            Log.Msg("friend " + Named(f, fp) + " is here" + (renamed ? " as " + now : "")
                + ": recognised by server " + Keys.Short(_serverFp) + ".");
        }

        void HandOver(double now)
        {
            if (!ServerWithdraws || Key == null || now < _nextShow || now < _nextWithdraw) return;
            Withdrawal w = NextWithdrawal();
            if (w == null) return;
            _handed.Add(w.Index);
            _nextShow = now + ShowEvery;
            _nextWithdraw = now + WithdrawEvery;
            Log.Detail("handing the server a removal (" + _handed.Count + " this visit).");
            _send(Proto.Withdraw, new Fields().Add("idx", w.Index).Add("wsig", Convert.ToBase64String(w.Sig)));
        }

        Withdrawal NextWithdrawal()
        {
            foreach (Withdrawal w in _fresh)
                if (StillWithdrawn(w)) return w;
            if (_backlog == null)
            {
                if (Copies == null || Book == null) return null;
                _backlog = Copies.Pending(Key.Public, Book, MaxPending);
                _backlog.Reverse();
            }
            int extra = 0;
            foreach (Withdrawal f in _fresh)
                if (_handed.Contains(f.Index) && !_backlog.Exists(delegate(Withdrawal b) { return b.Index == f.Index; })) extra++;
            int skip = Math.Max(0, extra + _backlog.Count - MaxPending);
            foreach (Withdrawal w in _backlog)
            {
                if (skip > 0) { skip--; continue; }
                if (StillWithdrawn(w)) return w;
            }
            return null;
        }

        bool StillWithdrawn(Withdrawal w)
        {
            return !_handed.Contains(w.Index) && (Book == null || Book.Find(w.Friend) == null);
        }

        void OnWithdrawn(Fields w)
        {
            if (_stage != Stage.Confirmed || !Withdraws || Key == null) return;
            string fp = w.Get("fp"), srv = w.Get("srv");
            byte[] sig = Keys.FromBase64(w.Get("wsig"));
            long ts;
            if (!Keys.LooksLikeFingerprint(fp) || !Keys.LooksLikeFingerprint(srv) || sig == null
                || !long.TryParse(w.Get("ts"), NumberStyles.None, CultureInfo.InvariantCulture, out ts) || ts <= 0)
            {
                RefusalLog("the server passed on a friend's removal that does not parse; ignored.");
                return;
            }
            if (Book == null || Copies == null)
            {
                if (_early.Count < MaxEarly) _early.Add(w);
                return;
            }
            string record = fp + "|" + ts.ToString(CultureInfo.InvariantCulture) + "|" + srv;
            if (_withdrawn.Contains(record)) return;

            FriendBook.Entry live = Book.Find(fp);
            bool isLive = live != null && live.Record.Ts == ts && live.Record.Server.Fingerprint == srv;
            RecordCopy held = isLive ? null : Copies.Held(fp, ts, srv);
            PublicKey friend = isLive ? live.Friend : held == null ? null : held.Friend;
            string name = isLive ? live.Name : held == null ? null : held.Name;
            if (friend == null)
            {
                Log.Detail("the server passed on a friend's removal of a record you do not hold; ignored.");
                return;
            }
            if (!friend.Verify(Proto.UnfriendStatement(fp, Key.Public.Fingerprint, ts, srv), sig))
            {
                RefusalLog("the server passed on a removal by " + Named(name, fp) + " that they never signed; ignored.");
                return;
            }
            _withdrawn.Add(record);

            foreach (KeyValuePair<int, string> h in new List<KeyValuePair<int, string>>(_friendsHere))
                if (h.Value == fp) _friendsHere.Remove(h.Key);
            _away.Remove(fp);
            List<KeyValuePair<long, string>> records;
            if (isLive)
            {
                records = Copies.RemoveAll(fp);
                _archived[fp] = name;
                Book.Remove(fp);
                _shownAt.Remove(fp);
                _quiet.Remove(fp);
                Log.Msg(Named(name, fp) + " removed you as a friend.");
            }
            else
            {
                records = Copies.Remove(fp, ts, srv);
                _quiet.Add(fp);
                Log.Msg(Named(name, fp) + " removed an older friendship record with you; it is set aside."
                    + " The one you hold now counts again from your next visit.");
            }
            if (!records.Exists(delegate(KeyValuePair<long, string> r) { return r.Key == ts && r.Value == srv; }))
                records.Add(new KeyValuePair<long, string>(ts, srv));
            TheyRemovedFn moved = TheyRemoved;
            try { moved(fp, records, name); }
            catch (Exception e) { Log.Warn("setting aside the records a friend removed: " + e.Message); }
        }

        void TakeEarly()
        {
            if (_early.Count == 0 || Book == null || Copies == null) return;
            List<Fields> early = new List<Fields>(_early);
            _early.Clear();
            foreach (Fields w in early) OnWithdrawn(w);
        }

        static string Named(FriendBook.Entry f, string fp)
        {
            return Named(f == null ? null : f.Name, fp);
        }

        static string Named(string name, string fp)
        {
            return (string.IsNullOrEmpty(name) ? "?" : name) + " (" + Keys.Short(fp) + ")";
        }

        void Decline(string offer, string code)
        {
            _send(Proto.Consent, new Fields().Add("offer", offer).Add("no", code));
        }

        void RefusalLog(string line)
        {
            if (_now - _refusalLoggedAt < RefusalLogEvery) return;
            _refusalLoggedAt = _now;
            Log.Warn(line);
        }

        static string Label(string name, OfferPlan plan)
        {
            return (string.IsNullOrEmpty(name) ? "session " + plan.OtherSession.ToString(CultureInfo.InvariantCulture) : name)
                + " (" + plan.Other.Short + ")";
        }

        internal static OfferPlan Consider(Fields o, PublicKey me, string serverFp, SessionRoster roster, out string code, out string why)
        {
            code = "malformed";
            string id = o.Get("offer");
            if (!Keys.LooksLikeNonce(id)) { why = "the offer has no usable id"; return null; }
            long ts;
            if (!long.TryParse(o.Get("ts"), NumberStyles.None, CultureInfo.InvariantCulture, out ts) || ts <= 0)
            {
                why = "the offer has no usable time";
                return null;
            }
            string keyWhy;
            PublicKey a = PublicKey.FromBase64(o.Get("a_n"), o.Get("a_e"), out keyWhy);
            PublicKey b = PublicKey.FromBase64(o.Get("b_n"), o.Get("b_e"), out keyWhy);
            if (a == null || b == null) { why = "a key in the offer is unusable"; return null; }
            if (!FriendRecord.InOrder(a, b)) { why = "the offer's keys are out of order"; return null; }
            int other;
            if (!o.TryGetInt("other", out other)) { why = "the offer names no player"; return null; }

            PublicKey them = a.SameAs(me) ? b : b.SameAs(me) ? a : null;
            if (them == null) { code = "not-mine"; why = "neither key in the offer is yours"; return null; }
            if (roster.FingerprintOf(other) != them.Fingerprint)
            {
                code = "mismatch";
                why = "the offer's key for session " + other.ToString(CultureInfo.InvariantCulture)
                    + " is not the one that player proved here";
                return null;
            }

            OfferPlan p = new OfferPlan();
            p.Id = id;
            p.Ts = ts;
            p.Other = them;
            p.OtherSession = other;
            p.Statement = Proto.FriendStatement(a.Fingerprint, b.Fingerprint, ts, serverFp);
            code = null;
            why = null;
            return p;
        }

        void Retry(string why)
        {
            LastProblem = why;
            if (_retries >= FreshRetries)
            {
                _stage = Stage.Refused;
                Log.Warn("gave up on this server's identity check (" + why + ", after " + FreshRetries + " fresh challenges).");
                return;
            }
            _retries++;
            _retryHello = true;
            _stage = Stage.Hello;
            _nextHello = _now;
        }

        internal static AnswerPlan Answer(Fields c, string ownName, int ownSessionId, out string why)
        {
            string proto = c.Get("proto");
            if (proto != Proto.Version.ToString(CultureInfo.InvariantCulture))
            {
                why = "it speaks protocol " + (proto ?? "?");
                return null;
            }
            string nonce = c.Get("nonce");
            byte[] nb = Keys.FromBase64Url(nonce);
            if (nb == null || nb.Length != Keys.NonceBytes) { why = "its challenge is malformed"; return null; }
            string keyWhy;
            PublicKey server = PublicKey.FromBase64(c.Get("srv_n"), c.Get("srv_e"), out keyWhy);
            if (server == null) { why = "its key is unusable (" + keyWhy + ")"; return null; }
            int id;
            if (!c.TryGetInt("id", out id)) { why = "it gave no session id"; return null; }
            string name = c.Get("name");
            if (string.IsNullOrEmpty(name)) { why = "it gave no name"; return null; }
            if (ownSessionId != NoSession && id != ownSessionId)
            {
                why = "it says you are session " + id + " but the game says " + ownSessionId;
                return null;
            }
            bool nameUnchecked = string.IsNullOrEmpty(ownName);
            if (!nameUnchecked && !string.Equals(name, ownName, StringComparison.OrdinalIgnoreCase))
            {
                why = "it calls you \"" + name + "\" but you are \"" + ownName + "\"";
                return null;
            }
            AnswerPlan plan = new AnswerPlan();
            plan.Server = server;
            plan.Nonce = nonce;
            plan.Name = name;
            plan.SessionId = id;
            plan.NameUnchecked = nameUnchecked;
            plan.Statement = Proto.Statement(server.Fingerprint, nonce, id, name);
            why = null;
            return plan;
        }

        internal string StateText()
        {
            switch (_stage)
            {
                case Stage.Idle: return "not connected";
                case Stage.Hello: return Key == null ? "waiting for the identity key" : "saying hello (" + _tries + "/" + HelloTries + ")";
                case Stage.Signing: return "signing the challenge";
                case Stage.Proved: return "waiting for the server";
                case Stage.Confirmed: return "identified as " + Key.Public.Short + ", " + Roster.Count + " other identified player(s) here";
                case Stage.Silent: return "no TavernEssentials or TavernSocial on this server";
                default: return "not identified (" + (LastProblem ?? "?") + ")";
            }
        }
    }
}
