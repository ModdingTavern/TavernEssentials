using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TavernEssentials.Social
{
    internal sealed class PlayerDirectory
    {
        internal const int MaxEntries = 2000;
        internal const int MaxKeysPerLogin = 3;
        internal const long SeenEvery = 3600;

        internal sealed class Entry
        {
            internal PublicKey Key;
            internal int Id;
            internal string Name;
            internal long Seen;
        }

        readonly string _path;
        readonly Dictionary<string, Entry> _byFp = new Dictionary<string, Entry>();

        internal PlayerDirectory(string path)
        {
            _path = path;
        }

        internal string Path { get { return _path; } }
        internal int Count { get { return _byFp.Count; } }

        internal Entry Find(string fingerprint)
        {
            Entry e;
            return fingerprint != null && _byFp.TryGetValue(fingerprint, out e) ? e : null;
        }

        internal int Load()
        {
            _byFp.Clear();
            if (_path == null || !File.Exists(_path)) return 0;
            int skipped = 0;
            foreach (string line in File.ReadAllLines(_path, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                Entry e;
                string fp;
                if (Parse(line, out fp, out e) && !_byFp.ContainsKey(fp)) _byFp[fp] = e;
                else skipped++;
            }
            return skipped;
        }

        static bool Parse(string line, out string fp, out Entry e)
        {
            fp = null;
            e = null;
            string[] t = line.Split('\t');
            if (t.Length != 6 || !Keys.LooksLikeFingerprint(t[0])) return false;
            string why;
            PublicKey key = PublicKey.FromBase64(t[1], t[2], out why);
            if (key == null || key.Fingerprint != t[0]) return false;
            int id;
            long seen;
            if (!int.TryParse(t[3], NumberStyles.None, CultureInfo.InvariantCulture, out id)) return false;
            if (!long.TryParse(t[4], NumberStyles.None, CultureInfo.InvariantCulture, out seen)) return false;
            if (t[5].Length == 0) return false;
            fp = t[0];
            e = new Entry { Key = key, Id = id, Name = t[5], Seen = seen };
            return true;
        }

        internal bool Note(PublicKey key, int id, string name, long seen)
        {
            if (key == null || id < 0 || string.IsNullOrEmpty(name)) return false;
            name = Clean(name);
            string fp = key.Fingerprint;
            Entry e = Find(fp);
            if (e != null && e.Id == id && e.Name == name && seen - e.Seen < SeenEvery) return false;
            if (e == null || e.Name != name)
            {
                _byFp.Remove(fp);
                MakeRoomFor(name);
                if (_byFp.Count >= MaxEntries) Evict();
                if (e == null) e = new Entry { Key = key };
                _byFp[fp] = e;
            }
            e.Id = id;
            e.Name = name;
            e.Seen = seen;
            return true;
        }

        void MakeRoomFor(string name)
        {
            while (true)
            {
                string oldest = null;
                long at = long.MaxValue;
                int held = 0;
                foreach (KeyValuePair<string, Entry> kv in _byFp)
                {
                    if (kv.Value.Name != name) continue;
                    held++;
                    if (oldest == null || kv.Value.Seen < at) { at = kv.Value.Seen; oldest = kv.Key; }
                }
                if (held < MaxKeysPerLogin) return;
                _byFp.Remove(oldest);
            }
        }

        void Evict()
        {
            string oldest = null;
            long at = long.MaxValue;
            foreach (KeyValuePair<string, Entry> kv in _byFp)
                if (kv.Value.Seen < at) { at = kv.Value.Seen; oldest = kv.Key; }
            if (oldest != null) _byFp.Remove(oldest);
        }

        static string Clean(string name)
        {
            StringBuilder sb = new StringBuilder(name.Length);
            foreach (char c in name) sb.Append(char.IsControl(c) ? '?' : c);
            return sb.ToString();
        }

        internal string Save()
        {
            List<string> lines = new List<string>();
            lines.Add("# TavernEssentials: players whose key was proved on this server (fingerprint, key, id, last seen, name).");
            lines.Add("# Written by the server. The postbox reads it to send to a friend who is away.");
            foreach (KeyValuePair<string, Entry> kv in _byFp)
            {
                Entry e = kv.Value;
                lines.Add(kv.Key + "\t" + e.Key.ModulusBase64 + "\t" + e.Key.ExponentBase64 + "\t"
                    + e.Id.ToString(CultureInfo.InvariantCulture) + "\t" + e.Seen.ToString(CultureInfo.InvariantCulture) + "\t" + e.Name);
            }
            string tmp = _path + ".tmp";
            try
            {
                string dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, string.Join("\n", lines.ToArray()) + "\n", new UTF8Encoding(false));
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
                return null;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        internal List<string> Recent(int max, Predicate<string> skip)
        {
            List<KeyValuePair<string, Entry>> all = new List<KeyValuePair<string, Entry>>(_byFp);
            all.Sort(delegate(KeyValuePair<string, Entry> a, KeyValuePair<string, Entry> b) { return b.Value.Seen.CompareTo(a.Value.Seen); });
            List<string> list = new List<string>();
            foreach (KeyValuePair<string, Entry> kv in all)
            {
                if (list.Count >= max) break;
                if (skip == null || !skip(kv.Key)) list.Add(kv.Key);
            }
            return list;
        }
    }
}
