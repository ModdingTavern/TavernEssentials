using System;
using System.Collections.Generic;
using System.Globalization;

namespace TavernEssentials.Social
{
    internal sealed class MinuteBudget
    {
        int _count;
        double _start = -1;

        internal bool Take(double now, int perMinute)
        {
            if (_start < 0 || now - _start >= 60)
            {
                _start = now;
                _count = 0;
            }
            if (_count >= perMinute) return false;
            _count++;
            return true;
        }
    }

    internal sealed class Peer
    {
        internal const double Burst = 6;
        internal const double PerSecond = 1;

        internal readonly int Handle;
        internal readonly Reassembler Inbox = new Reassembler();

        double _tokens = Burst;
        double _refilledAt = -1;
        internal int FrameCount;
        internal int Dropped;

        internal string Nonce;
        internal double NonceAt;
        internal int ChallengedId;
        internal string ChallengedName;
        internal long ChallengedEntity;
        readonly MinuteBudget _challenges = new MinuteBudget();
        readonly MinuteBudget _offers = new MinuteBudget();
        readonly MinuteBudget _withdraws = new MinuteBudget();

        internal PublicKey Key;
        internal int SessionId;
        internal string Name;
        internal long Entity;

        internal bool Friends;
        internal bool Revoked;
        internal bool Withdraws;

        internal readonly Dictionary<string, ShownRecord> Shown = new Dictionary<string, ShownRecord>();
        internal readonly HashSet<Peer> FriendsHere = new HashSet<Peer>();
        internal bool BadShowLogged;

        internal readonly HashSet<string> Told = new HashSet<string>();
        internal int Handed;
        internal bool BadWithdrawalLogged;

        internal readonly Dictionary<string, PlayerDirectory.Entry> Away = new Dictionary<string, PlayerDirectory.Entry>();

        internal Peer(int handle)
        {
            Handle = handle;
        }

        internal bool Bound { get { return Key != null; } }

        internal bool TakeToken(double now)
        {
            if (_refilledAt < 0) _refilledAt = now;
            _tokens = Math.Min(Burst, _tokens + (now - _refilledAt) * PerSecond);
            _refilledAt = now;
            if (_tokens < 1)
            {
                Dropped++;
                return false;
            }
            _tokens -= 1;
            FrameCount++;
            return true;
        }

        internal bool TakeChallenge(double now, int perMinute)
        {
            return _challenges.Take(now, perMinute);
        }

        internal bool TakeOffer(double now, int perMinute)
        {
            return _offers.Take(now, perMinute);
        }

        internal bool TakeWithdraw(double now, int perMinute)
        {
            return _withdraws.Take(now, perMinute);
        }
    }

    internal sealed class ShownRecord
    {
        internal readonly long Ts;
        internal readonly string Srv;

        internal ShownRecord(long ts, string srv)
        {
            Ts = ts;
            Srv = srv;
        }
    }

    internal sealed class MadeRecord
    {
        internal FriendRecord Record;
        internal double At;
    }

    internal sealed class Offer
    {
        internal string Id;
        internal string Pair;
        internal Peer PA, PB;
        internal PublicKey A, B;
        internal long Ts;
        internal byte[] Statement;
        internal byte[] ASig, BSig, ServerSig;
        internal double Deadline;
    }

    internal sealed class ServerProtocol
    {
        internal const double ChallengeTtl = 60;
        internal const int ChallengesPerMinute = 6;
        internal const int RosterEntriesPerMessage = 60;
        internal const double OfferTtl = 20;
        internal const int OffersPerMinute = 6;
        internal const int WithdrawsPerMinute = 12;
        internal const double MadeTtl = 600;
        internal const double ExpireEvery = 60;

        internal delegate void SendFn(Peer to, uint kind, Fields body);
        internal delegate void Signed(byte[] signature, Exception error);
        internal delegate void SignFn(byte[] statement, Signed done);
        internal delegate bool ReadyFn();
        internal delegate long ClockFn();

        readonly SendFn _send;
        readonly Dictionary<int, Peer> _peers = new Dictionary<int, Peer>();
        readonly Dictionary<string, Offer> _offers = new Dictionary<string, Offer>();
        readonly Dictionary<string, MadeRecord> _made = new Dictionary<string, MadeRecord>();
        int _nextHandle = 1;
        double _now;
        double _nextExpire;
        string _withdrawalsError;

        internal PublicKey Key;
        internal SignFn Signer;
        internal ReadyFn CardsReady;
        internal bool Recognises;
        internal PlayerDirectory Known;
        internal WithdrawalStore Withdrawals;
        internal ClockFn UnixNow = DefaultUnixNow;
        internal const int KnownMax = 400;
        internal const int KnownPerMessage = 200;
        internal int Hellos, Challenges, Confirmed, Refused, Ignored;
        internal int Offered, Witnessed, Declined, Expired;
        internal int Shows, Mutuals, AwayShows;
        internal int Deposits, Splits;
        string _knownError;

        internal ServerProtocol(SendFn send)
        {
            _send = send;
        }

        internal int PeerCount { get { return _peers.Count; } }
        internal int OfferCount { get { return _offers.Count; } }
        internal int MadeCount { get { return _made.Count; } }

        internal int BoundCount
        {
            get
            {
                int n = 0;
                foreach (Peer p in _peers.Values)
                    if (p.Bound) n++;
                return n;
            }
        }

        internal Peer Connect()
        {
            Peer p = new Peer(_nextHandle++);
            _peers[p.Handle] = p;
            return p;
        }

        internal void Disconnect(Peer p)
        {
            if (p == null || !_peers.Remove(p.Handle)) return;
            if (!p.Bound) return;
            string fp = p.Key.Fingerprint;
            Unbind(p, "left");
            NowAway(fp);
        }

        internal void OnMessage(Peer p, uint kind, Fields body, int sessionId, string name, double now, long entity = 0)
        {
            if (body == null) body = new Fields();
            _now = now;
            if (kind == Proto.Hello) OnHello(p, body, sessionId, name, now, entity);
            else if (kind == Proto.Proof) OnProof(p, body, sessionId, now);
            else if (kind == Proto.Consent) OnConsent(p, body);
            else if (kind == Proto.Show) OnShow(p, body);
            else if (kind == Proto.Withdraw) OnWithdraw(p, body, now);
            else Ignored++;
        }

        void OnHello(Peer p, Fields hello, int sessionId, string name, double now, long entity)
        {
            Hellos++;
            if (Key == null || name == null || p.Revoked)
            {
                Ignored++;
                return;
            }
            if (!p.TakeChallenge(now, ChallengesPerMinute))
            {
                Ignored++;
                Log.Detail(Describe(sessionId, name) + ": hello ignored, too many challenges this minute.");
                return;
            }
            p.Friends = hello.Get("friends") == "1";
            p.Withdraws = hello.Get("withdraw") == "1";
            bool again = p.Nonce != null && p.ChallengedId == sessionId && p.ChallengedName == name
                && now - p.NonceAt <= ChallengeTtl;
            if (!again)
            {
                p.Nonce = Keys.NewNonce();
                p.NonceAt = now;
                p.ChallengedId = sessionId;
                p.ChallengedName = name;
            }
            p.ChallengedEntity = entity;
            Challenges++;
            Log.Detail(Describe(sessionId, name) + (again ? ": challenged again (same challenge)." : ": challenged."));
            _send(p, Proto.Challenge, new Fields()
                .Add("proto", Proto.Version)
                .Add("nonce", p.Nonce)
                .Add("srv_n", Key.ModulusBase64)
                .Add("srv_e", Key.ExponentBase64)
                .Add("id", sessionId)
                .Add("name", name)
                .Add("ttl", (long)Math.Ceiling(ChallengeTtl - (now - p.NonceAt))));
        }

        void OnProof(Peer p, Fields proof, int sessionId, double now)
        {
            string why, detail;
            PublicKey key = Check(p, proof, sessionId, now, out why, out detail);
            if (key == null)
            {
                Refused++;
                Log.Msg(Describe(p.ChallengedId, p.ChallengedName) + ": identity refused (" + why
                    + (detail == null ? "" : ": " + detail) + ").");
                _send(p, Proto.Reject, new Fields().Add("why", why));
                return;
            }
            if (p.Bound && !p.Key.SameAs(key))
            {
                Refused++;
                Log.Msg(Describe(p.SessionId, p.Name) + ": identity refused (a second key on one connection).");
                _send(p, Proto.Reject, new Fields().Add("why", "rekey"));
                return;
            }
            Bind(p, key);
        }

        internal PublicKey Check(Peer p, Fields proof, int sessionId, double now, out string why, out string detail)
        {
            detail = null;
            string nonce = p.Nonce;
            p.Nonce = null;
            if (nonce == null) { why = "no-challenge"; return null; }
            if (proof.Get("nonce") != nonce) { why = "stale"; return null; }
            if (Key == null) { why = "not-ready"; return null; }
            if (now - p.NonceAt > ChallengeTtl) { why = "expired"; return null; }
            if (sessionId != p.ChallengedId) { why = "session-changed"; return null; }

            PublicKey key = PublicKey.FromBase64(proof.Get("n"), proof.Get("e"), out detail);
            if (key == null) { why = "bad-key"; return null; }
            byte[] sig = Keys.FromBase64(proof.Get("sig"));
            byte[] statement = Proto.Statement(Key.Fingerprint, nonce, p.ChallengedId, p.ChallengedName);
            if (sig == null || !key.Verify(statement, sig)) { why = "bad-signature"; return null; }
            why = null;
            return key;
        }

        void Bind(Peer p, PublicKey key)
        {
            List<Peer> displaced = new List<Peer>();
            foreach (Peer o in BoundOthers(p))
                if (o.Key.SameAs(key)) displaced.Add(o);
            for (int i = 0; i < displaced.Count; i++)
            {
                Peer o = displaced[i];
                Log.Msg(Describe(o.SessionId, o.Name) + ": identity " + key.Short + " is now in use by "
                    + Describe(p.ChallengedId, p.ChallengedName) + ".");
                Unbind(o, "displaced");
                _send(o, Proto.Reject, new Fields().Add("why", "displaced"));
            }
            if (p.Bound) Unbind(p, "proved again");

            p.Key = key;
            p.SessionId = p.ChallengedId;
            p.Name = p.ChallengedName;
            p.Entity = p.ChallengedEntity;
            Confirmed++;
            Log.Msg(Describe(p.SessionId, p.Name) + ": identity " + key.Short + " confirmed.");

            Fields welcome = new Fields().Add("fp", key.Fingerprint);
            if (p.Friends && CardsReady != null)
            {
                if (CardsReady()) welcome.Add("friends", 1);
                else Log.Warn(Describe(p.SessionId, p.Name) + " co-signs friend cards, but none can be witnessed yet"
                    + " (the server's social client is not wrapped): a card passed now stays in hand.");
            }
            if (Recognises) welcome.Add("mutual", 1);
            if (Recognises && Withdrawals != null) welcome.Add("withdraw", 1);
            _send(p, Proto.Welcome, welcome);
            if (!p.Bound) return;
            SendRoster(p, BoundOthers(p));
            if (!p.Bound) return;

            Fields add = AddEntry(new Fields(), p);
            foreach (Peer o in BoundOthers(p)) _send(o, Proto.Roster, add);
            NoteKnown(p);
        }

        List<Peer> BoundOthers(Peer except)
        {
            List<Peer> list = new List<Peer>();
            foreach (Peer o in _peers.Values)
                if (o != except && o.Bound) list.Add(o);
            return list;
        }

        void SendRoster(Peer to, List<Peer> entries)
        {
            int at = 0;
            do
            {
                Fields f = new Fields();
                if (at == 0) f.Add("full", 1);
                for (int i = 0; i < RosterEntriesPerMessage && at < entries.Count; i++, at++)
                    AddEntry(f, entries[at]);
                _send(to, Proto.Roster, f);
            } while (at < entries.Count);
        }

        static Fields AddEntry(Fields f, Peer p)
        {
            string id = p.SessionId.ToString(CultureInfo.InvariantCulture);
            f.Add("add", id + ":" + p.Key.Fingerprint);
            if (p.Entity > 0) f.Add("ent", id + ":" + p.Entity.ToString(CultureInfo.InvariantCulture));
            return f;
        }

        internal Action<int> FriendsChanged;

        void Changed(Peer p)
        {
            Action<int> h = FriendsChanged;
            if (h == null || p == null || !p.Bound) return;
            try { h(p.SessionId); }
            catch (Exception e) { Log.Warn("a friends-changed listener threw: " + e.GetType().Name + ": " + e.Message); }
        }

        void Unbind(Peer p, string reason)
        {
            p.Key = null;
            p.Shown.Clear();
            p.Away.Clear();
            foreach (Peer o in p.FriendsHere) { o.FriendsHere.Remove(p); Changed(o); }
            p.FriendsHere.Clear();
            DropOffersOf(p, reason);
            Fields del = new Fields().Add("del", p.SessionId);
            foreach (Peer o in BoundOthers(p)) _send(o, Proto.Roster, del);
            Log.Detail(Describe(p.SessionId, p.Name) + ": identity dropped (" + reason + ").");
        }

        internal void Revoke(Peer p, string why)
        {
            if (p == null || p.Revoked) return;
            p.Revoked = true;
            p.Nonce = null;
            if (!p.Bound) return;
            Log.Warn(Describe(p.SessionId, p.Name) + ": no longer identified (" + why + ").");
            Unbind(p, "revoked");
            _send(p, Proto.Reject, new Fields().Add("why", "revoked"));
        }

        internal bool Witness(Peer x, Peer y, long unixNow, double now, out string why)
        {
            if (x == null || y == null || !x.Bound || !y.Bound) { why = "a player is not identified"; return false; }
            if (x == y || x.Key.SameAs(y.Key)) { why = "the same key on both sides"; return false; }
            if (!x.Friends || !y.Friends)
            {
                Peer old = x.Friends ? y : x;
                why = Describe(old.SessionId, old.Name) + "'s game does not co-sign friend cards (TavernSocial 0.1.0, or friends off)";
                return false;
            }
            if (Signer == null || Key == null) { why = "the server cannot sign"; return false; }

            Peer pa = x, pb = y;
            if (!FriendRecord.InOrder(pa.Key, pb.Key)) { pa = y; pb = x; }
            string pair = pa.Key.Fingerprint + "|" + pb.Key.Fingerprint;

            _now = now;
            MadeRecord made;
            if (_made.TryGetValue(pair, out made))
            {
                if (now - made.At <= MadeTtl)
                {
                    SendRecord(pa, made.Record);
                    SendRecord(pb, made.Record);
                    Log.Detail(Describe(pa.SessionId, pa.Name) + " and " + Describe(pb.SessionId, pb.Name)
                        + ": friends already; their record went out again.");
                    why = null;
                    return true;
                }
                _made.Remove(pair);
            }
            foreach (Offer pending in _offers.Values)
                if (pending.Pair == pair) { why = null; return true; }
            if (!pa.TakeOffer(now, OffersPerMinute) || !pb.TakeOffer(now, OffersPerMinute))
            {
                why = "too many friend cards this minute";
                return false;
            }

            Offer o = new Offer();
            o.Id = Keys.NewNonce();
            o.Pair = pair;
            o.PA = pa;
            o.PB = pb;
            o.A = pa.Key;
            o.B = pb.Key;
            o.Ts = unixNow;
            o.Statement = Proto.FriendStatement(o.A.Fingerprint, o.B.Fingerprint, o.Ts, Key.Fingerprint);
            o.Deadline = now + OfferTtl;
            _offers[o.Id] = o;
            Offered++;
            Log.Detail(Describe(pa.SessionId, pa.Name) + " and " + Describe(pb.SessionId, pb.Name)
                + " passed a friend card; asking both to co-sign.");

            _send(pa, Proto.Offer, OfferBody(o, pb.SessionId));
            _send(pb, Proto.Offer, OfferBody(o, pa.SessionId));

            Signer(o.Statement, delegate(byte[] sig, Exception error)
            {
                Offer live;
                if (!_offers.TryGetValue(o.Id, out live) || live != o) return;
                if (sig == null)
                {
                    Drop(o);
                    Log.Error("the server could not sign a friendship record: "
                        + (error == null ? "no signature" : error.GetType().Name + ": " + error.Message));
                    return;
                }
                o.ServerSig = sig;
                TryFinish(o);
            });
            why = null;
            return true;
        }

        static Fields OfferBody(Offer o, int otherSession)
        {
            return new Fields()
                .Add("offer", o.Id)
                .Add("ts", o.Ts)
                .Add("a_n", o.A.ModulusBase64)
                .Add("a_e", o.A.ExponentBase64)
                .Add("b_n", o.B.ModulusBase64)
                .Add("b_e", o.B.ExponentBase64)
                .Add("other", otherSession);
        }

        void OnConsent(Peer p, Fields c)
        {
            Offer o;
            string id = c.Get("offer");
            if (id == null || !_offers.TryGetValue(id, out o)) { Ignored++; return; }
            bool isA = p == o.PA;
            if (!isA && p != o.PB) { Ignored++; return; }
            PublicKey key = isA ? o.A : o.B;
            if (!p.Bound || !p.Key.SameAs(key)) { Ignored++; return; }
            Peer other = isA ? o.PB : o.PA;

            string no = c.Get("no");
            if (no != null)
            {
                Drop(o);
                Declined++;
                Log.Msg(Describe(p.SessionId, p.Name) + " did not co-sign the friendship with "
                    + Describe(other.SessionId, other.Name) + " (" + DeclineReason(no) + "), so there is no record.");
                return;
            }
            byte[] sig = Keys.FromBase64(c.Get("sig"));
            if (sig == null || !key.Verify(o.Statement, sig))
            {
                Drop(o);
                Log.Warn(Describe(p.SessionId, p.Name) + "'s co-signature for the friendship with "
                    + Describe(other.SessionId, other.Name) + " does not verify, so there is no record.");
                return;
            }
            if (isA) { if (o.ASig == null) o.ASig = sig; }
            else if (o.BSig == null) o.BSig = sig;
            TryFinish(o);
        }

        void TryFinish(Offer o)
        {
            if (o.ASig == null || o.BSig == null || o.ServerSig == null) return;
            _offers.Remove(o.Id);

            FriendRecord r = new FriendRecord();
            r.A = o.A;
            r.B = o.B;
            r.Ts = o.Ts;
            r.Server = Key;
            r.ASig = o.ASig;
            r.BSig = o.BSig;
            r.ServerSig = o.ServerSig;
            _made[o.Pair] = new MadeRecord { Record = r, At = _now };
            Witnessed++;
            int sent = (SendRecord(o.PA, r) ? 1 : 0) + (SendRecord(o.PB, r) ? 1 : 0);
            Log.Msg("witnessed " + o.PA.Name + " (" + o.A.Short + ") and " + o.PB.Name + " (" + o.B.Short
                + ") becoming friends; " + (sent == 2 ? "both have the record." : sent + " of 2 still here to get the record."));
        }

        bool SendRecord(Peer to, FriendRecord r)
        {
            if (!_peers.ContainsKey(to.Handle) || !to.Bound || (!to.Key.SameAs(r.A) && !to.Key.SameAs(r.B))) return false;
            _send(to, Proto.Record, r.ToFields());
            return true;
        }

        void Drop(Offer o)
        {
            _offers.Remove(o.Id);
        }

        void DropOffersOf(Peer p, string reason)
        {
            List<Offer> gone = new List<Offer>();
            foreach (Offer o in _offers.Values)
                if (o.PA == p || o.PB == p) gone.Add(o);
            foreach (Offer o in gone)
            {
                Drop(o);
                Log.Detail("the friendship offer for " + Describe(o.PA.SessionId, o.PA.Name) + " and "
                    + Describe(o.PB.SessionId, o.PB.Name) + " is off (" + reason + ").");
            }
        }

        internal void Tick(double now)
        {
            _now = now;
            TickRemovals(now);
            List<string> stale = null;
            foreach (KeyValuePair<string, MadeRecord> kv in _made)
                if (now - kv.Value.At > MadeTtl)
                {
                    if (stale == null) stale = new List<string>();
                    stale.Add(kv.Key);
                }
            if (stale != null) foreach (string pair in stale) _made.Remove(pair);

            List<Offer> late = null;
            foreach (Offer o in _offers.Values)
                if (now > o.Deadline)
                {
                    if (late == null) late = new List<Offer>();
                    late.Add(o);
                }
            if (late == null) return;
            foreach (Offer o in late)
            {
                Drop(o);
                Expired++;
                string missing;
                if (o.ServerSig == null) missing = "the server's own signature";
                else if (o.ASig == null && o.BSig == null) missing = "both players";
                else
                {
                    Peer w = o.ASig == null ? o.PA : o.PB;
                    missing = Describe(w.SessionId, w.Name);
                }
                Log.Msg("the friendship record for " + Describe(o.PA.SessionId, o.PA.Name) + " and "
                    + Describe(o.PB.SessionId, o.PB.Name) + " was not finished in time (waiting on " + missing + ").");
            }
        }

        void OnShow(Peer p, Fields s)
        {
            if (!Recognises || !p.Bound) { Ignored++; return; }
            string fp = s.Get("fp"), srv = s.Get("srv");
            byte[] sig = Keys.FromBase64(s.Get("sig"));
            long ts;
            if (!Keys.LooksLikeFingerprint(fp) || !Keys.LooksLikeFingerprint(srv) || sig == null
                || !long.TryParse(s.Get("ts"), NumberStyles.None, CultureInfo.InvariantCulture, out ts) || ts <= 0)
            {
                Ignored++;
                return;
            }
            Peer friend = BoundWith(fp);
            if (friend == p) { Ignored++; return; }
            if (friend == null) { ShowAway(p, fp, ts, srv, sig); return; }
            Shows++;
            if (!friend.Key.Verify(FriendRecord.StatementFor(p.Key.Fingerprint, fp, ts, srv), sig))
            {
                if (!p.BadShowLogged)
                    Log.Warn(Describe(p.SessionId, p.Name) + " showed a friendship with " + Describe(friend.SessionId, friend.Name)
                        + " that " + (friend.Name ?? "they") + " never signed; ignored.");
                p.BadShowLogged = true;
                return;
            }
            if (ShowWithdrawn(p, fp, friend.Key, ts, srv)) return;
            p.Shown[fp] = new ShownRecord(ts, srv);
            if (!friend.Shown.ContainsKey(p.Key.Fingerprint) || p.FriendsHere.Contains(friend)) return;

            p.FriendsHere.Add(friend);
            friend.FriendsHere.Add(p);
            Mutuals++;
            Log.Msg(Describe(p.SessionId, p.Name) + " and " + Describe(friend.SessionId, friend.Name)
                + " are friends here: both showed their record.");
            _send(p, Proto.Mutual, MutualBody(friend));
            if (!p.Bound || !friend.Bound) return;
            _send(friend, Proto.Mutual, MutualBody(p));
            Changed(p);
            Changed(friend);
        }

        void NoteKnown(Peer p)
        {
            if (Known == null) return;
            if (Known.Note(p.Key, p.SessionId, p.Name, UnixNow())) SaveKnown();
            if (!Recognises) return;
            List<string> away = Known.Recent(KnownMax, delegate(string fp) { return BoundWith(fp) != null; });
            int at = 0;
            while (at < away.Count)
            {
                Fields f = new Fields();
                for (int i = 0; i < KnownPerMessage && at < away.Count; i++, at++)
                    f.Add("tag", Proto.KnownTag(Key.Fingerprint, away[at]));
                _send(p, Proto.Known, f);
            }
        }

        void SaveKnown()
        {
            string error = Known.Save();
            if (error != null && error != _knownError)
                Log.Warn("could not save the address book " + Known.Path + " (" + error + "); it is kept in memory.");
            else if (error == null && _knownError != null)
                Log.Msg("the address book saved again.");
            _knownError = error;
        }

        void ShowAway(Peer p, string fp, long ts, string srv, byte[] sig)
        {
            PlayerDirectory.Entry e = Known == null ? null : Known.Find(fp);
            if (e == null) { Ignored++; return; }
            Shows++;
            if (!e.Key.Verify(FriendRecord.StatementFor(p.Key.Fingerprint, fp, ts, srv), sig))
            {
                if (!p.BadShowLogged)
                    Log.Warn(Describe(p.SessionId, p.Name) + " showed a friendship with " + Describe(e.Id, e.Name)
                        + " that " + e.Name + " never signed; ignored.");
                p.BadShowLogged = true;
                return;
            }
            if (ShowWithdrawn(p, fp, e.Key, ts, srv)) return;
            p.Shown[fp] = new ShownRecord(ts, srv);
            if (p.Away.ContainsKey(fp)) return;
            p.Away[fp] = e;
            AwayShows++;
            Log.Detail(Describe(p.SessionId, p.Name) + " is friends with " + Describe(e.Id, e.Name) + ", who is away.");
            _send(p, Proto.Away, AwayBody(fp, e));
            Changed(p);
        }

        void NowAway(string fp)
        {
            PlayerDirectory.Entry e = Known == null ? null : Known.Find(fp);
            if (e == null || BoundWith(fp) != null) return;
            foreach (Peer o in BoundOthers(null))
            {
                if (!o.Shown.ContainsKey(fp) || o.Away.ContainsKey(fp)) continue;
                o.Away[fp] = e;
                _send(o, Proto.Away, AwayBody(fp, e));
                Changed(o);
            }
        }

        static Fields AwayBody(string fp, PlayerDirectory.Entry e)
        {
            return new Fields()
                .Add("id", e.Id)
                .Add("fp", fp)
                .Add("name", e.Name);
        }

        internal List<PlayerDirectory.Entry> AwayOf(Peer p)
        {
            List<PlayerDirectory.Entry> list = new List<PlayerDirectory.Entry>();
            if (p == null || !p.Bound) return list;
            foreach (KeyValuePair<string, PlayerDirectory.Entry> kv in p.Away)
                if (BoundWith(kv.Key) == null) list.Add(kv.Value);
            return list;
        }

        void OnWithdraw(Peer p, Fields w, double now)
        {
            if (Withdrawals == null || !p.Bound) { Ignored++; return; }
            if (!p.TakeWithdraw(now, WithdrawsPerMinute))
            {
                Ignored++;
                Log.Detail(Describe(p.SessionId, p.Name) + ": a removal ignored, too many this minute.");
                return;
            }
            string mine = p.Key.Fingerprint;
            WithdrawalStore.Line line = Withdrawals.Deposit(mine, p.Name, w.Get("idx"), Keys.FromBase64(w.Get("wsig")), UnixNow());
            if (line == null) { Ignored++; return; }
            Deposits++;
            p.Handed++;
            Log.Detail(Describe(p.SessionId, p.Name) + " handed over " + p.Handed + " removal(s).");

            foreach (Peer o in BoundOthers(p))
            {
                ShownRecord r;
                if (!p.Bound) return;
                if (!o.Bound || !o.Shown.TryGetValue(mine, out r)) continue;
                WithdrawalStore.Line found = Withdrawn(mine, p.Key, o.Key.Fingerprint, r.Ts, r.Srv, p);
                if (found != null) Split(mine, p, o.Key.Fingerprint, o, found, r.Ts, r.Srv);
            }
            if (!p.Bound) return;
            foreach (KeyValuePair<string, ShownRecord> kv in new List<KeyValuePair<string, ShownRecord>>(p.Shown))
            {
                if (!p.Bound) return;
                WithdrawalStore.Line found = Withdrawn(mine, p.Key, kv.Key, kv.Value.Ts, kv.Value.Srv, p);
                if (found != null) Split(mine, p, kv.Key, null, found, kv.Value.Ts, kv.Value.Srv);
            }
        }

        bool ShowWithdrawn(Peer p, string fp, PublicKey key, long ts, string srv)
        {
            WithdrawalStore.Line line = Withdrawn(fp, key, p.Key.Fingerprint, ts, srv, p);
            if (line == null) return false;
            Split(fp, null, p.Key.Fingerprint, p, line, ts, srv);
            return true;
        }

        WithdrawalStore.Line Withdrawn(string remover, PublicKey removerKey, string friend, long ts, string srv, Peer logFor)
        {
            if (Withdrawals == null || removerKey == null) return null;
            WithdrawalStore.Line line = Withdrawals.Find(remover, Proto.UnfriendIndex(remover, friend, ts, srv), UnixNow());
            if (line == null) return null;
            if (removerKey.Verify(Proto.UnfriendStatement(remover, friend, ts, srv), line.Sig)) return line;
            if (logFor != null && !logFor.BadWithdrawalLogged)
                Log.Warn("a removal kept for " + Keys.Short(remover) + " does not verify against their key; ignored.");
            if (logFor != null) logFor.BadWithdrawalLogged = true;
            return null;
        }

        void Split(string aFp, Peer a, string bFp, Peer b, WithdrawalStore.Line line, long ts, string srv)
        {
            if (a == null) a = BoundWith(aFp);
            if (b == null) b = BoundWith(bFp);
            bool aChanged = false, bChanged = false;
            if (b != null)
            {
                bChanged |= b.Shown.Remove(aFp);
                bChanged |= b.Away.Remove(aFp);
                if (a != null) bChanged |= b.FriendsHere.Remove(a);
            }
            if (a != null)
            {
                aChanged |= a.Shown.Remove(bFp);
                aChanged |= a.Away.Remove(bFp);
                if (b != null) aChanged |= a.FriendsHere.Remove(b);
            }
            _made.Remove(string.CompareOrdinal(aFp, bFp) < 0 ? aFp + "|" + bFp : bFp + "|" + aFp);
            if (aChanged) Changed(a);
            if (bChanged) Changed(b);
            if (b == null || !b.Told.Add(line.Depositor + "|" + line.Index)) return;

            Splits++;
            string remover = a != null ? Describe(a.SessionId, a.Name) : AwayName(aFp);
            string friend = Describe(b.SessionId, b.Name);
            if (b.Withdraws)
            {
                _send(b, Proto.Withdrawn, new Fields()
                    .Add("fp", aFp)
                    .Add("ts", ts)
                    .Add("srv", srv)
                    .Add("wsig", Convert.ToBase64String(line.Sig)));
                Log.Msg(remover + " removed " + friend + " as a friend; " + (b.Name ?? "?") + "'s game was told.");
            }
            else if (a != null && a.Bound)
            {
                _send(b, Proto.Roster, new Fields().Add("del", a.SessionId));
                Log.Msg(remover + " removed " + friend + " as a friend; " + (b.Name ?? "?") + "'s game is older: dropped from friends here.");
            }
            else
                Log.Msg(remover + " removed " + friend + " as a friend; " + (b.Name ?? "?") + "'s game is older and is not told; their show of it is refused.");
        }

        string AwayName(string fp)
        {
            PlayerDirectory.Entry e = Known == null ? null : Known.Find(fp);
            return e == null ? "a player away (" + Keys.Short(fp) + ")" : Describe(e.Id, e.Name);
        }

        void TickRemovals(double now)
        {
            if (Withdrawals == null) return;
            if (now >= _nextExpire)
            {
                _nextExpire = now + ExpireEvery;
                Withdrawals.Expire(UnixNow());
            }
            string error = Withdrawals.SaveIfDue(now);
            if (error != null)
            {
                if (error != _withdrawalsError)
                    Log.Warn("could not save the removals file " + Withdrawals.Path + " (" + error + "); they are kept in memory.");
                _withdrawalsError = error;
            }
            else if (_withdrawalsError != null && !Withdrawals.Dirty)
            {
                Log.Msg("the removals file saved again.");
                _withdrawalsError = null;
            }
        }

        static long DefaultUnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        Peer BoundWith(string fingerprint)
        {
            foreach (Peer o in _peers.Values)
                if (o.Bound && o.Key.Fingerprint == fingerprint) return o;
            return null;
        }

        static Fields MutualBody(Peer friend)
        {
            return new Fields()
                .Add("id", friend.SessionId)
                .Add("fp", friend.Key.Fingerprint)
                .Add("name", friend.Name ?? "");
        }

        static string DeclineReason(string code)
        {
            switch (code)
            {
                case "no-card": return "their game saw no friend card between them";
                case "not-mine": return "the offer was not about them";
                case "mismatch": return "the offer's key for the other player was not the one proved here";
                case "malformed": return "the offer did not parse";
                case "off": return "friend cards are off in their config";
                case "busy": return "too many friend cards at once";
                case "error": return "signing failed";
                default: return "reason unknown";
            }
        }

        internal static string Describe(int sessionId, string name)
        {
            return (name ?? "?") + " (" + sessionId.ToString(CultureInfo.InvariantCulture) + ")";
        }

        internal string Status()
        {
            return PeerCount + " connection(s), " + BoundCount + " identified; hellos " + Hellos
                + ", challenges " + Challenges + ", confirmed " + Confirmed + ", refused " + Refused
                + ", ignored " + Ignored + "; friend cards: offered " + Offered + ", witnessed " + Witnessed
                + ", declined " + Declined + ", expired " + Expired + "; friends here: shown " + Shows + ", mutual " + Mutuals
                + (Known == null ? "" : "; address book " + Known.Count + ", friends away shown " + AwayShows)
                + (Withdrawals == null ? "" : "; removals kept " + Withdrawals.Count + ", handed over " + Deposits + ", split " + Splits);
        }
    }
}
