using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TavernEssentials.Social
{
    internal sealed class WithdrawalStore
    {
        internal const int MaxPerKey = 20;
        internal const int MaxPerName = 20;
        internal const int MaxLines = 1000;
        internal const long KeepSeconds = 90L * 24 * 3600;
        internal const long RefreshEvery = 3600;
        internal const double SaveEvery = 5;
        internal const int NameMax = 64;
        internal const string FileName = "withdrawals.txt";

        internal sealed class Line
        {
            internal string Index;
            internal string Depositor;
            internal string Name;
            internal byte[] Sig;
            internal long Since;
            internal long Created;
            internal long Saved;
        }

        readonly string _path;
        readonly Dictionary<string, Line> _lines = new Dictionary<string, Line>();
        double _nextSave;

        internal bool Dirty;

        internal WithdrawalStore(string path)
        {
            _path = path;
        }

        internal string Path { get { return _path; } }
        internal int Count { get { return _lines.Count; } }

        static string KeyOf(string depositor, string index)
        {
            return depositor + "|" + index;
        }

        internal Line Find(string depositor, string index, long now)
        {
            Line l;
            if (depositor == null || index == null || !_lines.TryGetValue(KeyOf(depositor, index), out l)) return null;
            return now - l.Since > KeepSeconds ? null : l;
        }

        internal int CountOf(string depositor)
        {
            int n = 0;
            foreach (Line l in _lines.Values)
                if (l.Depositor == depositor) n++;
            return n;
        }

        internal int CountByName(string name)
        {
            int n = 0;
            foreach (Line l in _lines.Values)
                if (l.Name == name) n++;
            return n;
        }

        internal Line Deposit(string depositor, string name, string index, byte[] sig, long now)
        {
            if (!Keys.LooksLikeFingerprint(depositor) || !Proto.LooksLikeIndex(index) || !UsableSig(sig)) return null;
            name = CleanName(name);
            if (name == null) return null;

            Line l;
            if (_lines.TryGetValue(KeyOf(depositor, index), out l))
            {
                if (!SameBytes(l.Sig, sig))
                {
                    l.Sig = (byte[])sig.Clone();
                    Dirty = true;
                }
                if (now > l.Since) l.Since = now;
                if (l.Since - l.Saved >= RefreshEvery) Dirty = true;
                return l;
            }
            l = new Line { Index = index, Depositor = depositor, Name = name, Sig = (byte[])sig.Clone(), Since = now, Created = now };
            Keep(l);
            Dirty = true;
            return l;
        }

        void Keep(Line l)
        {
            while (CountOf(l.Depositor) >= MaxPerKey) Remove(Oldest(l.Depositor, null));
            while (CountByName(l.Name) >= MaxPerName) Remove(Oldest(null, l.Name));
            while (_lines.Count >= MaxLines) Remove(Oldest(null, Heaviest()));
            _lines[KeyOf(l.Depositor, l.Index)] = l;
        }

        void Remove(Line l)
        {
            if (l != null) _lines.Remove(KeyOf(l.Depositor, l.Index));
        }

        Line Oldest(string depositor, string name)
        {
            Line pick = null;
            foreach (Line l in _lines.Values)
            {
                if (depositor != null && l.Depositor != depositor) continue;
                if (name != null && l.Name != name) continue;
                if (pick == null || l.Created < pick.Created || (l.Created == pick.Created && string.CompareOrdinal(l.Index, pick.Index) < 0)) pick = l;
            }
            return pick;
        }

        string Heaviest()
        {
            Dictionary<string, int> count = new Dictionary<string, int>();
            Dictionary<string, long> first = new Dictionary<string, long>();
            foreach (Line l in _lines.Values)
            {
                int c;
                count.TryGetValue(l.Name, out c);
                count[l.Name] = c + 1;
                long f;
                if (!first.TryGetValue(l.Name, out f) || l.Created < f) first[l.Name] = l.Created;
            }
            string pick = null;
            foreach (KeyValuePair<string, int> kv in count)
            {
                if (pick == null || kv.Value > count[pick]) { pick = kv.Key; continue; }
                if (kv.Value < count[pick]) continue;
                if (first[kv.Key] > first[pick] || (first[kv.Key] == first[pick] && string.CompareOrdinal(kv.Key, pick) < 0)) pick = kv.Key;
            }
            return pick;
        }

        internal int Expire(long now)
        {
            List<Line> old = new List<Line>();
            foreach (Line l in _lines.Values)
                if (now - l.Since > KeepSeconds) old.Add(l);
            foreach (Line l in old) Remove(l);
            if (old.Count > 0) Dirty = true;
            return old.Count;
        }

        internal int Load(long now, out string keptAside)
        {
            keptAside = null;
            _lines.Clear();
            Dirty = false;
            if (_path == null || !File.Exists(_path)) return 0;
            int skipped = 0;
            List<Line> read = new List<Line>();
            HashSet<string> seen = new HashSet<string>();
            foreach (string text in File.ReadAllLines(_path, Encoding.UTF8))
            {
                if (text.Length == 0 || text[0] == '#') continue;
                Line l = Parse(text);
                if (l == null || !seen.Add(KeyOf(l.Depositor, l.Index))) { skipped++; continue; }
                read.Add(l);
            }
            if (skipped > 0)
            {
                keptAside = _path + ".skipped-" + KeyStore.Stamp();
                File.Copy(_path, keptAside, true);
            }
            read.Sort(delegate(Line x, Line y)
            {
                int c = x.Since.CompareTo(y.Since);
                return c != 0 ? c : string.CompareOrdinal(KeyOf(x.Depositor, x.Index), KeyOf(y.Depositor, y.Index));
            });
            foreach (Line l in read) Keep(l);
            Expire(now);
            if (skipped > 0 || _lines.Count < read.Count) Dirty = true;
            return skipped;
        }

        static Line Parse(string text)
        {
            string[] t = text.Split('\t');
            if (t.Length != 6) return null;
            if (!Proto.LooksLikeIndex(t[0]) || !Keys.LooksLikeFingerprint(t[1])) return null;
            string name = CleanName(t[2]);
            if (name == null || name != t[2]) return null;
            byte[] sig = Keys.FromBase64(t[3]);
            if (!UsableSig(sig)) return null;
            long since, created;
            if (!long.TryParse(t[4], NumberStyles.None, CultureInfo.InvariantCulture, out since)) return null;
            if (!long.TryParse(t[5], NumberStyles.None, CultureInfo.InvariantCulture, out created)) return null;
            return new Line { Index = t[0], Depositor = t[1], Name = name, Sig = sig, Since = since, Created = created, Saved = since };
        }

        static bool UsableSig(byte[] sig)
        {
            return sig != null && sig.Length >= PublicKey.MinBits / 8 && sig.Length <= PublicKey.MaxBits / 8;
        }

        internal static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            StringBuilder sb = new StringBuilder(Math.Min(name.Length, NameMax));
            foreach (char c in name.ToLowerInvariant())
            {
                if (sb.Length >= NameMax) break;
                sb.Append(char.IsControl(c) ? '?' : c);
            }
            return sb.ToString();
        }

        internal string SaveIfDue(double now)
        {
            if (!Dirty || now < _nextSave) return null;
            _nextSave = now + SaveEvery;
            return Save();
        }

        internal string Save()
        {
            List<Line> all = new List<Line>(_lines.Values);
            all.Sort(delegate(Line x, Line y)
            {
                int c = x.Since.CompareTo(y.Since);
                return c != 0 ? c : string.CompareOrdinal(KeyOf(x.Depositor, x.Index), KeyOf(y.Depositor, y.Index));
            });
            StringBuilder sb = new StringBuilder();
            sb.Append("# TavernEssentials: friend removals handed to this server (index, remover's fingerprint, login name, signature, kept since, first kept).\n");
            sb.Append("# Written by the server. It names nobody else; a removal matches only the record it was made for.\n");
            foreach (Line l in all)
                sb.Append(l.Index).Append('\t').Append(l.Depositor).Append('\t').Append(l.Name).Append('\t')
                    .Append(Convert.ToBase64String(l.Sig)).Append('\t').Append(l.Since.ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(l.Created.ToString(CultureInfo.InvariantCulture)).Append('\n');
            string tmp = _path + ".tmp";
            try
            {
                string dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
                foreach (Line l in all) l.Saved = l.Since;
                Dirty = false;
                return null;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        static bool SameBytes(byte[] x, byte[] y)
        {
            if (x == null || y == null || x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
                if (x[i] != y[i]) return false;
            return true;
        }
    }
}
