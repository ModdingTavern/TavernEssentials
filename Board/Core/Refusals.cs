using System;
using System.Text;

namespace TavernEssentials.Board
{
    internal static class Refusals
    {
        internal const string Prefix = "Server refused the join: ";
        internal const int MaxReason = 130;

        internal const double Window = 30;

        static readonly string[] Waits =
        {
            "Already connected to this server.",
            "is already on the server",
            "Reached player limit of",
            ", check Discord",
            "Failed join prerequisites.",
        };

        internal static string Line(string reason)
        {
            string r = Clean(reason);
            return r == null ? null : Prefix + r;
        }

        internal static string Clean(string reason)
        {
            if (reason == null) return null;
            StringBuilder b = new StringBuilder(reason.Length);
            bool inTag = false, space = false;
            foreach (char c in reason)
            {
                if (c == '<') { inTag = true; continue; }
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (char.IsWhiteSpace(c) || char.IsControl(c)) { space = b.Length > 0; continue; }
                if (space) { b.Append(' '); space = false; }
                b.Append(c);
            }
            if (b.Length == 0) return null;
            if (b.Length > MaxReason) return b.ToString(0, MaxReason - 3).TrimEnd() + "...";
            return b.ToString();
        }

        internal static bool WaitingHelps(string reason)
        {
            string r = Clean(reason);
            if (r == null) return true;
            foreach (string w in Waits)
                if (r.IndexOf(w, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }

    internal sealed class RefusalCount
    {
        readonly object _lock = new object();
        object _try;
        string _reason;
        int _times;
        double _at = double.NegativeInfinity;
        bool _giveUp;

        internal bool GiveUp(object attempt, string reason, double now)
        {
            lock (_lock)
            {
                if (ReferenceEquals(attempt, _try)) return _giveUp;
                string r = Refusals.Clean(reason);
                if (r != null && r == _reason && now - _at <= Refusals.Window) _times++;
                else _times = 1;
                _try = attempt;
                _reason = r;
                _at = now;
                _giveUp = r != null && _times >= 2 && !Refusals.WaitingHelps(r);
                return _giveUp;
            }
        }

        internal bool Seen(object attempt)
        {
            lock (_lock) return ReferenceEquals(attempt, _try);
        }

        internal int Times
        {
            get { lock (_lock) return _times; }
        }
    }
}
