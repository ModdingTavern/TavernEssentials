using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TavernEssentials
{
    internal sealed class Settings
    {
        internal sealed class OldFile
        {
            internal readonly string Path;
            internal readonly string Prefix;

            internal OldFile(string path, string prefix)
            {
                Path = path;
                Prefix = prefix;
            }
        }

        readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        readonly List<string> _notes;

        Settings(List<string> notes)
        {
            _notes = notes;
        }

        internal static Settings Load(string path, string[] template, OldFile[] carry, List<string> notes)
        {
            Settings s = new Settings(notes);
            try
            {
                if (!File.Exists(path)) Write(path, template, carry, notes);
                else AddMissing(path, template, notes);
                s.Read(File.ReadAllLines(path), KeysOf(template));
            }
            catch (Exception e)
            {
                notes.Add("config unreadable, defaults used: " + e.Message);
                s._values.Clear();
            }
            return s;
        }

        internal bool Bool(string key, bool def)
        {
            string v;
            if (!_values.TryGetValue(key, out v)) return def;
            switch (v.ToLowerInvariant())
            {
                case "1": case "true": case "on": case "yes": return true;
                case "0": case "false": case "off": case "no": return false;
            }
            _notes.Add("config " + key + "=" + v + " is not 0 or 1; the default stays.");
            return def;
        }

        internal string Text(string key, string def)
        {
            string v;
            if (!_values.TryGetValue(key, out v) || v.Length == 0) return def;
            return v;
        }

        internal int Number(string key, int def)
        {
            string v;
            if (!_values.TryGetValue(key, out v)) return def;
            int n;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            _notes.Add("config " + key + "=" + v + " is not a whole number; the default stays.");
            return def;
        }

        void Read(string[] lines, HashSet<string> known)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string key, value;
                if (!Split(lines[i], out key, out value))
                {
                    string t = lines[i].Trim();
                    if (t.Length > 0 && t[0] != '#') _notes.Add("config line " + (i + 1) + " is not key=value; ignored.");
                    continue;
                }
                if (!known.Contains(key))
                {
                    _notes.Add("config key \"" + key + "\" is unknown; ignored.");
                    continue;
                }
                _values[key] = value;
            }
        }

        static void Write(string path, string[] template, OldFile[] carry, List<string> notes)
        {
            string[] lines = (string[])template.Clone();
            List<string> carried = new List<string>();
            if (carry != null)
                foreach (OldFile old in carry)
                {
                    string said = CarryFrom(old, lines, notes);
                    if (said != null) carried.Add(said);
                }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
            File.Move(tmp, path);
            if (carried.Count == 0) notes.Add("wrote a default config to " + path + ".");
            else notes.Add("wrote a config to " + path + ", carrying over " + string.Join(" and ", carried.ToArray()) + ".");
        }

        static void AddMissing(string path, string[] template, List<string> notes)
        {
            HashSet<string> have = new HashSet<string>();
            foreach (string line in File.ReadAllLines(path))
            {
                string k, v;
                if (Split(line, out k, out v)) have.Add(k);
            }
            List<string> added = new List<string>();
            string text = Missing(template, have, added);
            if (added.Count == 0) return;
            string which = added.Count + (added.Count == 1 ? " setting" : " settings") + " (" + string.Join(", ", added.ToArray()) + ")";
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                int bom;
                Encoding enc = EncodingOf(bytes, out bom);
                string had = enc.GetString(bytes, bom, bytes.Length - bom);
                string lead = had.Length > 0 && !had.EndsWith("\n") ? "\r\n" : "";
                byte[] add = enc.GetBytes(lead + text);
                using (FileStream f = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    f.Write(add, 0, add.Length);
                notes.Add("config: added " + which + " to " + path + ", each at its default.");
            }
            catch (Exception e)
            {
                notes.Add("config: could not add " + which + " to " + path + " (" + e.Message + "); they keep their defaults.");
            }
        }

        internal static Encoding EncodingOf(byte[] b, out int bom)
        {
            bom = 4;
            if (Starts(b, 0xFF, 0xFE, 0x00, 0x00)) return new UTF32Encoding(false, false);
            if (Starts(b, 0x00, 0x00, 0xFE, 0xFF)) return new UTF32Encoding(true, false);
            bom = 2;
            if (Starts(b, 0xFF, 0xFE)) return new UnicodeEncoding(false, false);
            if (Starts(b, 0xFE, 0xFF)) return new UnicodeEncoding(true, false);
            bom = Starts(b, 0xEF, 0xBB, 0xBF) ? 3 : 0;
            return new UTF8Encoding(false);
        }

        static bool Starts(byte[] b, params byte[] mark)
        {
            if (b.Length < mark.Length) return false;
            for (int i = 0; i < mark.Length; i++)
                if (b[i] != mark[i]) return false;
            return true;
        }

        internal static string Missing(string[] template, HashSet<string> have, List<string> added)
        {
            StringBuilder b = new StringBuilder();
            int heading = -1, shown = -1;
            for (int i = 0; i < template.Length; i++)
            {
                if (IsHeading(template[i]))
                {
                    heading = i;
                    continue;
                }
                string k, v;
                if (!Split(template[i], out k, out v) || have.Contains(k) || added.Contains(k)) continue;
                if (added.Count == 0)
                    b.Append("\r\n# Added by ").Append(Mod.Name).Append(' ').Append(Mod.Version)
                        .Append(": settings this file did not have yet, each at its default.\r\n");
                int from = i;
                while (from > 0 && IsComment(template[from - 1]) && !IsHeading(template[from - 1])) from--;
                if (heading >= 0 && heading != shown)
                {
                    b.Append("\r\n");
                    for (int h = heading; h < from && IsComment(template[h]); h++) b.Append(template[h]).Append("\r\n");
                    shown = heading;
                }
                b.Append("\r\n");
                for (int c = from; c <= i; c++) b.Append(template[c]).Append("\r\n");
                added.Add(k);
            }
            return b.ToString();
        }

        static bool IsComment(string line)
        {
            return line.TrimStart().StartsWith("#");
        }

        static bool IsHeading(string line)
        {
            return line.TrimStart().StartsWith("# ----");
        }

        static string CarryFrom(OldFile old, string[] lines, List<string> notes)
        {
            if (old == null || !File.Exists(old.Path)) return null;
            string[] oldLines;
            try
            {
                oldLines = File.ReadAllLines(old.Path);
            }
            catch (Exception e)
            {
                notes.Add("could not read " + old.Path + " to carry its settings over: " + e.Message);
                return null;
            }
            HashSet<string> carried = new HashSet<string>();
            foreach (string raw in oldLines)
            {
                string key, value;
                if (!Split(raw, out key, out value)) continue;
                string here = old.Prefix + key;
                int at = IndexOfKey(lines, here);
                if (at < 0)
                {
                    notes.Add(old.Path + ": \"" + key + "\" is not a setting any more; not carried over.");
                    continue;
                }
                lines[at] = here + "=" + value;
                carried.Add(here);
            }
            if (carried.Count == 0) return null;
            return carried.Count + (carried.Count == 1 ? " setting from " : " settings from ") + old.Path;
        }

        static bool Split(string line, out string key, out string value)
        {
            key = null;
            value = null;
            string t = line.Trim();
            if (t.Length == 0 || t[0] == '#') return false;
            int eq = t.IndexOf('=');
            if (eq <= 0) return false;
            key = t.Substring(0, eq).Trim().ToLowerInvariant();
            value = t.Substring(eq + 1).Trim();
            return true;
        }

        static int IndexOfKey(string[] lines, string key)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string k, v;
                if (Split(lines[i], out k, out v) && k == key) return i;
            }
            return -1;
        }

        static HashSet<string> KeysOf(string[] template)
        {
            HashSet<string> keys = new HashSet<string>();
            foreach (string line in template)
            {
                string k, v;
                if (Split(line, out k, out v)) keys.Add(k);
            }
            return keys;
        }
    }
}
