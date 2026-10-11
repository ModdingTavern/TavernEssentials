using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TavernEssentials.Board
{
    internal sealed class JoinedServers
    {
        internal const string FileName = "joined_servers.txt";

        static readonly string[] Header =
        {
            "# My Servers: the servers you have joined, one community-list address per line.",
            "# TavernEssentials adds a server when you join it. To take one off, use Leave server",
            "# on the main menu's Servers page, or delete its line here.",
        };

        readonly object _gate = new object();
        readonly string _path;
        readonly string[] _header;
        List<string> _addresses = new List<string>();
        DateTime _stamp = DateTime.MinValue;
        bool _exists;

        internal JoinedServers(string path) : this(path, Header) { }

        internal JoinedServers(string path, string[] header)
        {
            _path = path;
            _header = header;
        }

        internal string Path { get { return _path; } }

        internal bool Exists
        {
            get { lock (_gate) { Refresh(); return _exists; } }
        }

        internal string[] Snapshot()
        {
            lock (_gate)
            {
                Refresh();
                return _addresses.ToArray();
            }
        }

        internal bool Contains(string address)
        {
            string key = Key(address);
            if (key.Length == 0) return false;
            lock (_gate)
            {
                Refresh();
                foreach (string a in _addresses)
                    if (Key(a) == key) return true;
                return false;
            }
        }

        internal bool Add(string address, out string error)
        {
            error = null;
            string clean = Clean(address);
            if (clean == null) { error = "not an address: \"" + address + "\""; return false; }
            lock (_gate)
            {
                List<string> lines;
                if (!ReadLines(out lines, out error)) return false;
                string key = Key(clean);
                foreach (string line in lines)
                    if (Key(AddressOf(line)) == key) return false;
                if (lines.Count == 0) lines.AddRange(_header);
                lines.Add(clean);
                return Write(lines, out error);
            }
        }

        internal bool Remove(string address, out string error)
        {
            error = null;
            string key = Key(address);
            if (key.Length == 0) return false;
            lock (_gate)
            {
                List<string> lines;
                if (!ReadLines(out lines, out error)) return false;
                List<string> kept = new List<string>(lines.Count);
                bool found = false;
                foreach (string line in lines)
                {
                    if (Key(AddressOf(line)) == key) found = true;
                    else kept.Add(line);
                }
                if (!found) return false;
                return Write(kept, out error);
            }
        }

        internal bool Seed(IEnumerable<string> addresses, out string error)
        {
            error = null;
            lock (_gate)
            {
                Refresh();
                if (_exists) return false;
                List<string> lines = new List<string>(_header);
                HashSet<string> seen = new HashSet<string>();
                foreach (string a in addresses)
                {
                    string clean = Clean(a);
                    if (clean != null && seen.Add(Key(clean))) lines.Add(clean);
                }
                return Write(lines, out error);
            }
        }

        internal static List<string> MatchSeed(IEnumerable<string> cachedHosts,
            IEnumerable<KeyValuePair<string, string>> listAddressToIp)
        {
            HashSet<string> hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string h in cachedHosts)
                if (!string.IsNullOrEmpty(h)) hosts.Add(h.Trim());
            List<string> matched = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            foreach (KeyValuePair<string, string> e in listAddressToIp)
            {
                if (e.Value == null || !hosts.Contains(e.Value.Trim())) continue;
                string clean = Clean(e.Key);
                if (clean != null && seen.Add(Key(clean))) matched.Add(clean);
            }
            return matched;
        }

        internal static List<string> TokenCacheHosts(string dir, out int unreadable)
        {
            unreadable = 0;
            List<string> hosts = new List<string>();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return hosts;
            string[] files;
            try { files = Directory.GetFiles(dir, ".token_*.json"); }
            catch (Exception) { return hosts; }
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                string host = null;
                try
                {
                    Match m = HostField.Match(File.ReadAllText(f));
                    if (m.Success) host = m.Groups[1].Value.Trim();
                }
                catch (Exception) { }
                if (string.IsNullOrEmpty(host)) { unreadable++; continue; }
                if (seen.Add(host)) hosts.Add(host);
            }
            return hosts;
        }

        static readonly Regex HostField = new Regex("\"host\"\\s*:\\s*\"([^\"]*)\"");

        void Refresh()
        {
            DateTime stamp;
            try
            {
                if (!File.Exists(_path))
                {
                    _exists = false;
                    _addresses = new List<string>();
                    _stamp = DateTime.MinValue;
                    return;
                }
                stamp = File.GetLastWriteTimeUtc(_path);
            }
            catch (Exception) { return; }
            if (_exists && stamp == _stamp) return;

            List<string> lines;
            string error;
            if (!ReadLines(out lines, out error)) return;
            List<string> addresses = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            foreach (string line in lines)
            {
                string a = AddressOf(line);
                if (a != null && seen.Add(Key(a))) addresses.Add(a);
            }
            _addresses = addresses;
            _stamp = stamp;
            _exists = true;
        }

        bool ReadLines(out List<string> lines, out string error)
        {
            lines = new List<string>();
            error = null;
            try
            {
                if (!File.Exists(_path)) return true;
                foreach (string raw in File.ReadAllLines(_path))
                    lines.Add(raw.TrimEnd('\r'));
                return true;
            }
            catch (Exception e)
            {
                error = "could not read " + _path + ": " + e.Message;
                return false;
            }
        }

        bool Write(List<string> lines, out string error)
        {
            error = null;
            string tmp = _path + ".tmp";
            try
            {
                string dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, string.Join("\r\n", lines.ToArray()) + "\r\n", new UTF8Encoding(false));
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
            }
            catch (Exception e)
            {
                error = "could not write " + _path + ": " + e.Message;
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return false;
            }
            _stamp = DateTime.MinValue;
            _exists = false;
            Refresh();
            return true;
        }

        static string AddressOf(string line)
        {
            if (line == null) return null;
            string t = line.Trim();
            if (t.Length == 0 || t[0] == '#') return null;
            return Clean(t);
        }

        static string Clean(string address)
        {
            if (address == null) return null;
            string t = address.Trim();
            if (t.Length == 0 || t[0] == '#') return null;
            for (int i = 0; i < t.Length; i++)
                if (char.IsWhiteSpace(t[i])) return null;
            return t;
        }

        internal static string Key(string address)
        {
            return address == null ? "" : address.Trim().ToLowerInvariant();
        }
    }
}
