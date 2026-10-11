using System;
using System.Globalization;

namespace TavernEssentials.Discord
{
    internal sealed class Status
    {
        internal string State;
        internal string Details;
        internal string PartyId;
        internal int PartySize;
        internal int PartyMax;
        internal long Start;

        internal bool Same(Status o)
        {
            return o != null && o.State == State && o.Details == Details && o.PartyId == PartyId
                && o.PartySize == PartySize && o.PartyMax == PartyMax && o.Start == Start;
        }

        internal string Describe()
        {
            return "\"" + (Details ?? "") + "\" / \"" + (State ?? "") + "\""
                + (PartyId == null ? "" : " (" + PartySize + " of " + PartyMax + ")")
                + (Start == 0 ? "" : ", since " + Start);
        }
    }

    internal static class StatusText
    {
        internal const string Menu = "In the main menu";
        internal const string NoName = "On a server";

        internal const string GameDefault = "Local Server";

        internal const int MaxText = 127;

        internal static Status Build(bool inWorld, string server, string doing, int players, int limit, long since, bool showCount)
        {
            Status s = new Status();
            if (!inWorld)
            {
                s.State = Menu;
                return s;
            }
            s.State = Named(server) ?? NoName;
            s.Details = Text(doing);
            if (showCount && limit > 0 && players > 0)
            {
                s.PartyId = PartyKey(s.State);
                s.PartySize = players;
                s.PartyMax = players > limit ? players : limit;
            }
            if (since > 0) s.Start = since;
            return s;
        }

        internal static string PartyKey(string server)
        {
            string t = (server ?? "").Trim().ToLowerInvariant();
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < t.Length; i++)
            {
                h ^= t[i];
                h *= 1099511628211UL;
            }
            return "att-" + h.ToString("x16", CultureInfo.InvariantCulture);
        }

        internal static bool NewPlace(Status was, Status now)
        {
            return was == null || now == null || was.State != now.State;
        }

        internal static string Named(string server)
        {
            string t = Text(server);
            return t != null && string.Equals(t, GameDefault, StringComparison.OrdinalIgnoreCase) ? null : t;
        }

        internal static string Text(string s)
        {
            if (s == null) return null;
            string t = s.Trim();
            if (t.Length > MaxText) t = t.Substring(0, MaxText).TrimEnd();
            return t.Length < 2 ? null : t;
        }
    }
}
