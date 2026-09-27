using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TavernEssentials.Social
{
    internal sealed class RecentPlayer
    {
        internal string Fp;
        internal string Name;
        internal string Server;
        internal long Seen;
        internal int How;
        internal bool Live;
        internal int LiveId;
    }

    internal sealed class RecentBook
    {
        internal const int Max = 200;
        internal const int NameMax = 64;
        internal const int ServerMax = 128;

        internal const int OtherFirst = -900000;

        readonly List<RecentPlayer> _rows = new List<RecentPlayer>();
        readonly Dictionary<string, int> _otherIds = new Dictionary<string, int>();

        internal bool Dirty;

        internal int Count { get { return _rows.Count; } }

        internal RecentPlayer Record(int id, string name, string server, string fp, int how, long now)
        {
            name = Clip(name, NameMax);
            server = Clip(server, ServerMax) ?? "";
            RecentPlayer row = fp == null ? null : ByFp(fp);
            RecentPlayer live = ByLive(id);
            if (row == null && live != null && (fp == null || live.Fp == null || live.Fp == fp)) row = live;
            if (row == null && !string.IsNullOrEmpty(name)) row = ByName(name, server);
            if (row == null)
            {
                row = new RecentPlayer();
                _rows.Add(row);
            }
            if (live != null && live != row)
            {
                if (live.Fp == null) _rows.Remove(live);
                else live.Live = false;
            }
            if (fp != null)
            {
                row.Fp = fp;
                RecentPlayer byName = string.IsNullOrEmpty(name) ? null : ByName(name, server);
                if (byName != null && byName != row && byName.Fp == null) _rows.Remove(byName);
            }
            if (!string.IsNullOrEmpty(name)) row.Name = name;
            row.Server = server;
            if (now > row.Seen) row.Seen = now;
            row.How = how;
            row.Live = true;
            row.LiveId = id;
            Trim();
            Dirty = true;
            return row;
        }

        internal void Recognised(int id, string fp)
        {
            RecentPlayer live = ByLive(id);
            if (live == null || fp == null || live.Fp == fp) return;
            RecentPlayer old = ByFp(fp);
            if (old != null && old != live)
            {
                if (live.Name == null) live.Name = old.Name;
                if (old.Seen > live.Seen) live.Seen = old.Seen;
                _rows.Remove(old);
            }
            live.Fp = fp;
            Dirty = true;
        }

        internal void NewConnection()
        {
            foreach (RecentPlayer r in _rows)
            {
                r.Live = false;
                r.LiveId = 0;
            }
        }

        internal List<RecentPlayer> Rows(string server, int limit, Predicate<int> here)
        {
            List<RecentPlayer> pick = new List<RecentPlayer>();
            foreach (RecentPlayer r in _rows)
                if (!string.IsNullOrEmpty(r.Name) && (server == null || r.Server == server)) pick.Add(r);
            Dictionary<RecentPlayer, bool> present = new Dictionary<RecentPlayer, bool>();
            foreach (RecentPlayer r in pick) present[r] = r.Live && here != null && here(r.LiveId);
            pick.Sort(delegate(RecentPlayer a, RecentPlayer b)
            {
                if (present[a] != present[b]) return present[a] ? -1 : 1;
                int c = b.Seen.CompareTo(a.Seen);
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            if (limit >= 0 && pick.Count > limit) pick.RemoveRange(limit, pick.Count - limit);
            return pick;
        }

        internal int IdOf(RecentPlayer r, Func<string, int> friendId)
        {
            if (r.Live) return r.LiveId;
            if (r.Fp != null && friendId != null) return friendId(r.Fp);
            string key = "n:" + (r.Name ?? "").ToLowerInvariant() + "|" + (r.Server ?? "");
            int id;
            if (!_otherIds.TryGetValue(key, out id))
            {
                id = OtherFirst - _otherIds.Count;
                _otherIds[key] = id;
            }
            return id;
        }

        internal static bool IsOtherId(int id)
        {
            return id <= OtherFirst && id > FriendIds.First;
        }

        internal RecentPlayer ByLive(int id)
        {
            foreach (RecentPlayer r in _rows)
                if (r.Live && r.LiveId == id) return r;
            return null;
        }

        RecentPlayer ByFp(string fp)
        {
            foreach (RecentPlayer r in _rows)
                if (r.Fp == fp) return r;
            return null;
        }

        RecentPlayer ByName(string name, string server)
        {
            foreach (RecentPlayer r in _rows)
                if (r.Fp == null && r.Server == server && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                    return r;
            return null;
        }

        void Trim()
        {
            while (_rows.Count > Max)
            {
                RecentPlayer oldest = null;
                foreach (RecentPlayer r in _rows)
                    if (!r.Live && (oldest == null || r.Seen < oldest.Seen)) oldest = r;
                if (oldest == null) return;
                _rows.Remove(oldest);
            }
        }

        static string Clip(string s, int max)
        {
            if (s == null) return null;
            s = s.Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        internal string Encode()
        {
            StringBuilder sb = new StringBuilder();
            foreach (RecentPlayer r in _rows)
            {
                if (string.IsNullOrEmpty(r.Name)) continue;
                Fields f = new Fields().Add("seen", r.Seen).Add("how", r.How).Add("name", r.Name).Add("server", r.Server ?? "");
                if (r.Fp != null) f.Add("fp", r.Fp);
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(f.Encode());
            }
            return sb.ToString();
        }

        internal static RecentBook Decode(string text, out int skipped)
        {
            RecentBook book = new RecentBook();
            skipped = 0;
            if (string.IsNullOrEmpty(text)) return book;
            string[] blocks = text.Replace("\r\n", "\n").Split(new string[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string b in blocks)
            {
                string why;
                Fields f = Fields.Decode(b.EndsWith("\n") ? b : b + "\n", out why);
                long seen;
                int how;
                string name = f == null ? null : f.Get("name");
                if (f == null || string.IsNullOrEmpty(name)
                    || !long.TryParse(f.Get("seen"), NumberStyles.None, CultureInfo.InvariantCulture, out seen)
                    || !f.TryGetInt("how", out how))
                {
                    skipped++;
                    continue;
                }
                string fp = f.Get("fp");
                if (fp != null && book.ByFp(fp) != null) { skipped++; continue; }
                RecentPlayer r = new RecentPlayer();
                r.Name = Clip(name, NameMax);
                r.Server = Clip(f.Get("server"), ServerMax) ?? "";
                r.Seen = seen;
                r.How = how;
                r.Fp = fp;
                book._rows.Add(r);
            }
            book.Trim();
            return book;
        }

        internal static RecentBook Load(string path, out string note)
        {
            note = null;
            if (!File.Exists(path)) return new RecentBook();
            int skipped;
            RecentBook book = Decode(File.ReadAllText(path), out skipped);
            if (skipped > 0) note = skipped + " unreadable entr" + (skipped == 1 ? "y" : "ies") + " skipped";
            return book;
        }

        internal static void Save(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }

    internal sealed class ReportGuard
    {
        internal const int PerTargetSeconds = 60;
        internal const int WindowSeconds = 600;
        internal const int PerWindow = 10;

        internal static readonly string[] Reasons = { "Griefing", "Harrassment", "CheatingExploits" };

        readonly Dictionary<string, double> _last = new Dictionary<string, double>();
        readonly Dictionary<int, List<double>> _recent = new Dictionary<int, List<double>>();

        internal static bool ValidReason(string reason)
        {
            return Array.IndexOf(Reasons, reason) >= 0;
        }

        internal bool Allow(int reporter, int target, double now)
        {
            string key = reporter.ToString(CultureInfo.InvariantCulture) + ">" + target.ToString(CultureInfo.InvariantCulture);
            double last;
            if (_last.TryGetValue(key, out last) && now - last < PerTargetSeconds) return false;
            List<double> times;
            if (!_recent.TryGetValue(reporter, out times))
            {
                times = new List<double>();
                _recent[reporter] = times;
            }
            times.RemoveAll(delegate(double t) { return now - t >= WindowSeconds; });
            if (times.Count >= PerWindow) return false;
            times.Add(now);
            _last[key] = now;
            return true;
        }
    }
}
