using System;
using System.Collections.Generic;
using System.Globalization;

namespace TavernEssentials.SettingsBoard
{
    internal sealed class Change
    {
        internal readonly string Field;
        internal readonly object Was;
        internal readonly object Now;

        internal Change(string field, object was, object now)
        {
            Field = field;
            Was = was;
            Now = now;
        }
    }

    internal static class BoardRules
    {
        internal static readonly string[] StaffRoles = { "moderator", "owner", "admin" };

        internal static readonly string[] Locked =
        {
            "IsSaving", "IsAutoSaving", "MinutesBetweenAutoSaving", "EmptyServerShutdownTimeInSeconds",
            "FPSForMaximumFixedFrameCount", "FPSForMinimumFixedFrameCount",
            "MaximumPhysicsFixedFramesPerSecond", "MinimumPhysicsFixedFramesPerSecond",
            "IsUpdatingPhysicsFixedFrameCount", "IsCustomizationEnabled", "PlayerLimit",
            "HasInGameSettings", "WipesWhenEmpty", "HasRunRepairBoxFix",
        };

        internal static bool IsStaff(IEnumerable<string> roles)
        {
            if (roles == null) return false;
            foreach (string r in roles)
            {
                if (r == null) continue;
                string t = r.Trim();
                foreach (string s in StaffRoles)
                    if (string.Equals(t, s, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static bool IsLocked(string field)
        {
            foreach (string l in Locked)
                if (l == field) return true;
            return false;
        }

        internal static List<Change> Changes(IDictionary<string, object> sent, IDictionary<string, object> mine,
            IDictionary<string, object> now, List<string> refused)
        {
            List<Change> changes = new List<Change>();
            foreach (KeyValuePair<string, object> kv in mine)
            {
                object shown, current;
                if (!sent.TryGetValue(kv.Key, out shown) || !now.TryGetValue(kv.Key, out current)) continue;
                if (Equals(kv.Value, shown)) continue;
                if (IsLocked(kv.Key))
                {
                    if (refused != null) refused.Add(kv.Key);
                    continue;
                }
                if (Equals(kv.Value, current)) continue;
                changes.Add(new Change(kv.Key, current, kv.Value));
            }
            changes.Sort(delegate(Change a, Change b) { return string.CompareOrdinal(a.Field, b.Field); });
            return changes;
        }

        internal static string Show(object v)
        {
            if (v == null) return "(none)";
            if (v is float) return ((float)v).ToString("0.####", CultureInfo.InvariantCulture);
            if (v is bool) return (bool)v ? "on" : "off";
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }
}
