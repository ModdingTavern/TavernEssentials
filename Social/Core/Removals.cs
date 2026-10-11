using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TavernEssentials.Social
{
    internal sealed class Withdrawal
    {
        internal string Friend;
        internal long Ts;
        internal string Server;
        internal string Index;
        internal byte[] Sig;
        internal long Removed;
    }

    internal sealed class RecordCopy
    {
        internal string Path;
        internal PublicKey Friend;
        internal long Ts;
        internal string Server;
        internal string Name;
        internal bool Removed;
        internal bool Live;
        internal byte[] Sig;
        internal long RemovedAt;
        internal FriendRecord Record;

        internal string FriendFp { get { return Friend.Fingerprint; } }

        internal bool Is(string friendFp, long ts, string server)
        {
            return FriendFp == friendFp && Ts == ts && Server == server;
        }
    }

    internal sealed class RecordIndex
    {
        readonly List<RecordCopy> _copies = new List<RecordCopy>();

        internal int Count { get { return _copies.Count; } }

        internal static RecordIndex Load(string dir, PublicKey me)
        {
            RecordIndex x = new RecordIndex();
            if (dir == null || me == null) return x;
            x.Read(dir, me, true, false);
            x.Read(FriendStore.ArchiveDir(dir), me, false, false);
            x.Read(FriendStore.RemovedDir(dir), me, false, true);
            return x;
        }

        void Read(string folder, PublicKey me, bool friends, bool removed)
        {
            if (!Directory.Exists(folder)) return;
            foreach (string path in Directory.GetFiles(folder, "*" + FriendStore.Extension))
            {
                RecordCopy c = FriendStore.ReadCopy(path, me);
                if (c == null) continue;
                c.Live = friends && FriendStore.IsLiveName(path);
                c.Removed = removed;
                if (!removed) c.Sig = null;
                else if (c.Sig != null && !me.Verify(Proto.UnfriendStatement(me.Fingerprint, c.FriendFp, c.Ts, c.Server), c.Sig)) c.Sig = null;
                _copies.Add(c);
            }
        }

        internal void Add(RecordCopy c)
        {
            if (c != null) _copies.Add(c);
        }

        internal RecordCopy Held(string friendFp, long ts, string server)
        {
            foreach (RecordCopy c in _copies)
                if (!c.Removed && c.Is(friendFp, ts, server)) return c;
            return null;
        }

        internal bool IsRemoved(string friendFp, long ts, string server)
        {
            foreach (RecordCopy c in _copies)
                if (c.Removed && c.Is(friendFp, ts, server)) return true;
            return false;
        }

        internal string RemovedName(string friendFp, long ts, string server)
        {
            foreach (RecordCopy c in _copies)
                if (c.Removed && c.Is(friendFp, ts, server) && !string.IsNullOrEmpty(c.Name)) return c.Name;
            return null;
        }

        internal static RecordCopy LiveCopy(FriendRecord r, PublicKey friend, string name)
        {
            RecordCopy c = new RecordCopy();
            c.Record = r;
            c.Friend = friend;
            c.Ts = r.Ts;
            c.Server = r.Server.Fingerprint;
            c.Name = name;
            c.Live = true;
            return c;
        }

        internal List<KeyValuePair<long, string>> RemoveAll(string friendFp)
        {
            return Mark(delegate(RecordCopy c) { return c.FriendFp == friendFp; });
        }

        internal List<KeyValuePair<long, string>> Remove(string friendFp, long ts, string server)
        {
            return Mark(delegate(RecordCopy c) { return c.Is(friendFp, ts, server); });
        }

        List<KeyValuePair<long, string>> Mark(Predicate<RecordCopy> which)
        {
            List<KeyValuePair<long, string>> records = new List<KeyValuePair<long, string>>();
            foreach (RecordCopy c in _copies)
            {
                if (c.Removed || !which(c)) continue;
                c.Removed = true;
                c.Live = false;
                KeyValuePair<long, string> r = new KeyValuePair<long, string>(c.Ts, c.Server);
                if (!records.Contains(r)) records.Add(r);
            }
            return records;
        }

        internal List<Withdrawal> Pending(PublicKey me, FriendBook book, int max)
        {
            List<Withdrawal> list = new List<Withdrawal>();
            HashSet<string> seen = new HashSet<string>();
            foreach (RecordCopy c in _copies)
            {
                if (!c.Removed || c.Sig == null || (book != null && book.Find(c.FriendFp) != null)) continue;
                Withdrawal w = Of(me, c);
                if (!seen.Add(w.Index)) continue;
                list.Add(w);
            }
            list.Sort(delegate(Withdrawal x, Withdrawal y)
            {
                int byTime = y.Removed.CompareTo(x.Removed);
                return byTime != 0 ? byTime : string.CompareOrdinal(x.Index, y.Index);
            });
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        internal static Withdrawal Withdrawn(PublicKey me, RecordCopy c)
        {
            if (c.Sig == null || !me.Verify(Proto.UnfriendStatement(me.Fingerprint, c.FriendFp, c.Ts, c.Server), c.Sig)) return null;
            return Of(me, c);
        }

        static Withdrawal Of(PublicKey me, RecordCopy c)
        {
            Withdrawal w = new Withdrawal();
            w.Friend = c.FriendFp;
            w.Ts = c.Ts;
            w.Server = c.Server;
            w.Index = Proto.UnfriendIndex(me.Fingerprint, c.FriendFp, c.Ts, c.Server);
            w.Sig = c.Sig;
            w.Removed = c.RemovedAt;
            return w;
        }
    }

    internal sealed class Removal
    {
        internal int Moved;
        internal string Name;
        internal readonly List<Withdrawal> Made = new List<Withdrawal>();
    }
}
