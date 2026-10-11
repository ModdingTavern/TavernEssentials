using System;
using System.Collections.Generic;
using System.Globalization;

namespace TavernEssentials.Social
{
    internal sealed class FriendIds
    {
        internal const int First = -1000000;
        internal const int RequestFirst = -500000000;

        readonly int _first;
        readonly Dictionary<string, int> _byFp = new Dictionary<string, int>();
        readonly Dictionary<int, string> _byId = new Dictionary<int, string>();

        internal FriendIds() : this(First) { }

        internal FriendIds(int first)
        {
            _first = first;
        }

        internal int IdOf(string fingerprint)
        {
            int id;
            if (_byFp.TryGetValue(fingerprint, out id)) return id;
            id = _first - _byFp.Count;
            _byFp[fingerprint] = id;
            _byId[id] = fingerprint;
            return id;
        }

        internal string FingerprintOf(int id)
        {
            string fp;
            return _byId.TryGetValue(id, out fp) ? fp : null;
        }

        internal static bool IsMadeUp(int id)
        {
            return id <= First;
        }

        internal static bool IsRequest(int id)
        {
            return id <= RequestFirst;
        }
    }

    internal sealed class ListRow
    {
        internal int Id;
        internal string Name;
        internal string Fingerprint;
        internal long Ts;
    }

    internal static class ListRows
    {
        internal delegate int IdFn(string fingerprint);

        internal static List<ListRow> From(FriendBook book, IdFn idOf)
        {
            List<ListRow> rows = new List<ListRow>();
            if (book == null) return rows;
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<FriendBook.Entry> entries = book.Entries();
            foreach (FriendBook.Entry e in entries)
            {
                string n = e.Name ?? "";
                int c;
                seen.TryGetValue(n, out c);
                seen[n] = c + 1;
            }
            foreach (FriendBook.Entry e in entries)
            {
                ListRow r = new ListRow();
                r.Fingerprint = e.Friend.Fingerprint;
                r.Id = idOf(r.Fingerprint);
                r.Ts = e.Record.Ts;
                if (string.IsNullOrEmpty(e.Name)) r.Name = "friend " + e.Friend.Short;
                else if (seen[e.Name] > 1) r.Name = e.Name + " (" + e.Friend.Short + ")";
                else r.Name = e.Name;
                rows.Add(r);
            }
            rows.Sort(delegate(ListRow x, ListRow y)
            {
                int byName = string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : string.CompareOrdinal(x.Fingerprint, y.Fingerprint);
            });
            return rows;
        }

        internal static DateTime When(long unixSeconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds);
        }
    }

    internal sealed class FriendLists
    {
        internal FriendBook Friends;
        internal FriendBook Archived;

        internal static FriendLists Load(string dir, PublicKey me, List<string> notes)
        {
            FriendLists l = new FriendLists();
            l.Friends = FriendBook.Load(dir, me, notes);
            l.Archived = dir == null ? new FriendBook()
                : FriendBook.Load(FriendStore.ArchiveDir(dir), me, notes, "friends\\" + FriendStore.ArchiveFolder + "\\");
            foreach (FriendBook.Entry e in l.Friends.Entries()) l.Archived.Remove(e.Friend.Fingerprint);
            return l;
        }
    }

    internal struct Avatar
    {
        internal int Id;
        internal long Entity;

        internal Avatar(int id, long entity)
        {
            Id = id;
            Entity = entity;
        }

        public override string ToString()
        {
            return Id.ToString(CultureInfo.InvariantCulture) + "@" + Entity.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal static class IdList
    {
        internal delegate bool ListedFn(int id);

        internal static void Plan(HashSet<int> allowed, HashSet<int> vouched, ListedFn listed, List<int> add, List<int> withdraw)
        {
            add.Clear();
            withdraw.Clear();
            foreach (int id in allowed)
                if (!listed(id)) add.Add(id);
            foreach (int id in vouched)
                if (!allowed.Contains(id) && listed(id)) withdraw.Add(id);
        }
    }

    internal struct ConnectionIds
    {
        internal int Handle;
        internal int Login;
        internal int Played;

        internal ConnectionIds(int handle, int login, int played)
        {
            Handle = handle;
            Login = login;
            Played = played;
        }
    }

    internal static class IdOwners
    {
        internal static void Build(IList<ConnectionIds> connections, Dictionary<int, int> into)
        {
            into.Clear();
            List<int> spoiled = new List<int>();
            for (int i = 0; i < connections.Count; i++)
            {
                ConnectionIds c = connections[i];
                if (c.Login != c.Played)
                {
                    spoiled.Add(c.Login);
                    spoiled.Add(c.Played);
                    continue;
                }
                if (into.ContainsKey(c.Login)) spoiled.Add(c.Login);
                else into[c.Login] = c.Handle;
            }
            foreach (int id in spoiled) into.Remove(id);
        }
    }
}
