using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TavernEssentials.Social
{
    internal sealed class FriendRecord
    {
        internal const int Version = 1;

        internal PublicKey A;
        internal PublicKey B;
        internal long Ts;
        internal PublicKey Server;
        internal byte[] ASig;
        internal byte[] BSig;
        internal byte[] ServerSig;

        internal byte[] Statement()
        {
            return Proto.FriendStatement(A.Fingerprint, B.Fingerprint, Ts, Server.Fingerprint);
        }

        internal static bool InOrder(PublicKey a, PublicKey b)
        {
            return string.CompareOrdinal(a.Fingerprint, b.Fingerprint) < 0;
        }

        internal static byte[] StatementFor(string x, string y, long ts, string serverFingerprint)
        {
            if (string.CompareOrdinal(x, y) > 0) { string t = x; x = y; y = t; }
            return Proto.FriendStatement(x, y, ts, serverFingerprint);
        }

        internal byte[] SignatureOf(PublicKey player)
        {
            if (A.SameAs(player)) return ASig;
            if (B.SameAs(player)) return BSig;
            return null;
        }

        internal PublicKey Other(PublicKey me)
        {
            if (me == null) return null;
            if (A.SameAs(me)) return B;
            if (B.SameAs(me)) return A;
            return null;
        }

        internal Fields ToFields()
        {
            return new Fields()
                .Add("v", Version)
                .Add("a_n", A.ModulusBase64)
                .Add("a_e", A.ExponentBase64)
                .Add("b_n", B.ModulusBase64)
                .Add("b_e", B.ExponentBase64)
                .Add("ts", Ts)
                .Add("srv_n", Server.ModulusBase64)
                .Add("srv_e", Server.ExponentBase64)
                .Add("a_sig", Convert.ToBase64String(ASig))
                .Add("b_sig", Convert.ToBase64String(BSig))
                .Add("srv_sig", Convert.ToBase64String(ServerSig));
        }

        internal static FriendRecord FromFields(Fields f, out string why)
        {
            if (f == null) { why = "empty"; return null; }
            string v = f.Get("v");
            if (v != "1") { why = "record version " + (v ?? "?"); return null; }

            FriendRecord r = new FriendRecord();
            string keyWhy;
            r.A = PublicKey.FromBase64(f.Get("a_n"), f.Get("a_e"), out keyWhy);
            if (r.A == null) { why = "the first key is unusable (" + keyWhy + ")"; return null; }
            r.B = PublicKey.FromBase64(f.Get("b_n"), f.Get("b_e"), out keyWhy);
            if (r.B == null) { why = "the second key is unusable (" + keyWhy + ")"; return null; }
            r.Server = PublicKey.FromBase64(f.Get("srv_n"), f.Get("srv_e"), out keyWhy);
            if (r.Server == null) { why = "the server's key is unusable (" + keyWhy + ")"; return null; }
            if (r.A.SameAs(r.B)) { why = "one key on both sides"; return null; }
            if (!InOrder(r.A, r.B)) { why = "the keys are out of order"; return null; }
            if (!long.TryParse(f.Get("ts"), NumberStyles.None, CultureInfo.InvariantCulture, out r.Ts) || r.Ts <= 0)
            {
                why = "no usable time";
                return null;
            }
            r.ASig = Keys.FromBase64(f.Get("a_sig"));
            r.BSig = Keys.FromBase64(f.Get("b_sig"));
            r.ServerSig = Keys.FromBase64(f.Get("srv_sig"));
            if (r.ASig == null || r.BSig == null || r.ServerSig == null) { why = "a signature is missing"; return null; }
            why = null;
            return r;
        }

        internal bool Verify(out string why)
        {
            byte[] s = Statement();
            if (!Server.Verify(s, ServerSig)) { why = "the server's signature does not verify"; return false; }
            if (!A.Verify(s, ASig)) { why = "the first player's signature does not verify"; return false; }
            if (!B.Verify(s, BSig)) { why = "the second player's signature does not verify"; return false; }
            why = null;
            return true;
        }

        internal static FriendRecord Accept(Fields f, PublicKey me, PublicKey server, out string why)
        {
            FriendRecord r = FromFields(f, out why);
            if (r == null) return null;
            if (server == null || !r.Server.SameAs(server)) { why = "it names a different server"; return null; }
            PublicKey friend = r.Other(me);
            if (friend == null) { why = "neither key is yours"; return null; }

            byte[] s = r.Statement();
            bool meIsA = r.A.SameAs(me);
            if (!r.Server.Verify(s, r.ServerSig)) { why = "the server's signature does not verify"; return null; }
            if (!me.Verify(s, meIsA ? r.ASig : r.BSig)) { why = "it does not carry your own signature"; return null; }
            if (!friend.Verify(s, meIsA ? r.BSig : r.ASig)) { why = "your friend's signature does not verify"; return null; }
            why = null;
            return r;
        }

        internal bool SameAs(FriendRecord o)
        {
            return o != null && A.SameAs(o.A) && B.SameAs(o.B) && Ts == o.Ts && Server.SameAs(o.Server)
                && SameBytes(ASig, o.ASig) && SameBytes(BSig, o.BSig) && SameBytes(ServerSig, o.ServerSig);
        }

        static bool SameBytes(byte[] x, byte[] y)
        {
            if (x == null || y == null || x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
                if (x[i] != y[i]) return false;
            return true;
        }
    }

    internal static class FriendStore
    {
        internal const string Folder = "friends";
        internal const string ArchiveFolder = "archived";
        internal const string Extension = ".txt";

        static readonly object Gate = new object();

        internal static string PathFor(string dir, PublicKey friend)
        {
            return PathFor(dir, friend.Fingerprint);
        }

        internal static string PathFor(string dir, string friendFingerprint)
        {
            return Path.Combine(dir, friendFingerprint + Extension);
        }

        internal static string ArchiveDir(string dir)
        {
            return Path.Combine(dir, ArchiveFolder);
        }

        internal static string Archive(string dir, string friendFingerprint)
        {
            lock (Gate)
            {
                string from = PathFor(dir, friendFingerprint);
                if (!File.Exists(from)) return null;
                string archive = ArchiveDir(dir);
                Directory.CreateDirectory(archive);
                string to = PathFor(archive, friendFingerprint);
                string note = "archived";
                if (File.Exists(to))
                {
                    string kept = FreeName(archive, friendFingerprint + ".replaced-" + KeyStore.Stamp(), Extension);
                    File.Move(to, kept);
                    note = "archived; the one archived before is kept as " + Path.GetFileName(kept);
                }
                File.Move(from, to);
                return note;
            }
        }

        internal static string Restore(string dir, string friendFingerprint)
        {
            lock (Gate)
            {
                string from = PathFor(ArchiveDir(dir), friendFingerprint);
                if (!File.Exists(from)) return null;
                Directory.CreateDirectory(dir);
                string to = PathFor(dir, friendFingerprint);
                if (File.Exists(to))
                {
                    string kept = FreeName(dir, friendFingerprint + ".replaced-" + KeyStore.Stamp(), Extension);
                    File.Move(from, kept);
                    return "a newer record was already there, so it stays; the archived one is kept as " + Path.GetFileName(kept);
                }
                File.Move(from, to);
                return "restored";
            }
        }

        internal static bool Delete(string dir, string friendFingerprint)
        {
            lock (Gate)
            {
                string path = PathFor(ArchiveDir(dir), friendFingerprint);
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
        }

        internal static string Save(string dir, FriendRecord r, PublicKey friend, string name)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(dir);
                string path = PathFor(dir, friend);
                Fields f = r.ToFields();
                if (!string.IsNullOrEmpty(name)) f.Add("name", name);
                f.Add("share", 0);
                string text = f.Encode();

                if (!File.Exists(path))
                {
                    KeyStore.WriteNew(path, text);
                    return "saved";
                }
                string ignored, why;
                FriendRecord old = Parse(File.ReadAllText(path), out ignored, out why);
                if (old != null && old.SameAs(r)) return "already saved";
                string kept = FreeName(dir, friend.Fingerprint + ".replaced-" + KeyStore.Stamp(), Extension);
                KeyStore.WriteReplace(path, text, kept);
                return "saved; the different record that was there is kept as " + Path.GetFileName(kept);
            }
        }

        internal static FriendRecord Load(string path, out string name, out string why)
        {
            name = null;
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception e)
            {
                why = "unreadable (" + e.GetType().Name + ")";
                return null;
            }
            FriendRecord r = Parse(text, out name, out why);
            if (r == null || !r.Verify(out why)) return null;
            return r;
        }

        static FriendRecord Parse(string text, out string name, out string why)
        {
            name = null;
            Fields f = Fields.Decode((text ?? "").Replace("\r\n", "\n"), out why);
            if (f == null) return null;
            name = f.Get("name");
            return FriendRecord.FromFields(f, out why);
        }

        static string FreeName(string dir, string stem, string ext)
        {
            string path = Path.Combine(dir, stem + ext);
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(dir, stem + "-" + i.ToString(CultureInfo.InvariantCulture) + ext);
            return path;
        }
    }

    internal sealed class FriendBook
    {
        internal sealed class Entry
        {
            internal FriendRecord Record;
            internal PublicKey Friend;
            internal string Name;
        }

        readonly Dictionary<string, Entry> _byFriend = new Dictionary<string, Entry>();

        internal int Count { get { return _byFriend.Count; } }

        internal Entry Find(string friendFingerprint)
        {
            Entry e;
            return friendFingerprint != null && _byFriend.TryGetValue(friendFingerprint, out e) ? e : null;
        }

        internal void Put(FriendRecord r, PublicKey me, string name)
        {
            PublicKey friend = r.Other(me);
            if (friend == null) return;
            Entry old = Find(friend.Fingerprint);
            Entry e = new Entry();
            e.Record = r;
            e.Friend = friend;
            e.Name = !string.IsNullOrEmpty(name) ? name : old != null ? old.Name : null;
            _byFriend[friend.Fingerprint] = e;
        }

        internal bool Remove(string friendFingerprint)
        {
            return friendFingerprint != null && _byFriend.Remove(friendFingerprint);
        }

        internal List<Entry> Entries()
        {
            return new List<Entry>(_byFriend.Values);
        }

        internal static FriendBook Load(string dir, PublicKey me, List<string> notes)
        {
            return Load(dir, me, notes, "friends\\");
        }

        internal static FriendBook Load(string dir, PublicKey me, List<string> notes, string where)
        {
            FriendBook book = new FriendBook();
            if (me == null || dir == null || !Directory.Exists(dir)) return book;
            foreach (string path in Directory.GetFiles(dir, "*" + FriendStore.Extension))
            {
                string stem = Path.GetFileNameWithoutExtension(path);
                if (!Keys.LooksLikeFingerprint(stem)) continue;
                string file = where + Keys.Short(stem) + "..." + FriendStore.Extension;
                string name, why;
                FriendRecord r = FriendStore.Load(path, out name, out why);
                if (r == null)
                {
                    notes.Add(file + " is not used: " + why + ".");
                    continue;
                }
                PublicKey friend = r.Other(me);
                if (friend == null)
                {
                    notes.Add(file + " is not used: it is about another key than yours.");
                    continue;
                }
                Entry have = book.Find(friend.Fingerprint);
                if (have != null && have.Record.Ts >= r.Ts) continue;
                Entry e = new Entry();
                e.Record = r;
                e.Friend = friend;
                e.Name = name;
                book._byFriend[friend.Fingerprint] = e;
            }
            return book;
        }
    }
}
