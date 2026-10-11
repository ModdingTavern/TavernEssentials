using System;
using System.Reflection;
using Alta.Networking.Servers;
using HarmonyLib;

namespace TavernEssentials.Board
{
    internal static class JoinRefusals
    {
        static readonly RefusalCount Count = new RefusalCount();
        static readonly DateTime Epoch = DateTime.UtcNow;

        internal static bool Apply(HarmonyLib.Harmony h)
        {
            try
            {
                MethodInfo line = AccessTools.PropertyGetter(typeof(JoinResult), "ErrorMessage");
                MethodInfo again = AccessTools.PropertyGetter(typeof(JoinResult), "IsRecoverableFailure");
                if (line == null || again == null)
                {
                    Log.Warn("join reasons: JoinResult.ErrorMessage or IsRecoverableFailure is gone; the game's own line stays.");
                    return false;
                }
                h.Patch(line, null, new HarmonyMethod(typeof(JoinRefusals).GetMethod("Line", BindingFlags.Public | BindingFlags.Static)));
                h.Patch(again, null, new HarmonyMethod(typeof(JoinRefusals).GetMethod("Again", BindingFlags.Public | BindingFlags.Static)));
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("join reasons: " + e.GetType().Name + ": " + e.Message + "; the game's own line stays.");
                return false;
            }
        }

        internal static string Reason(JoinResult r)
        {
            if (r == null || r.WasSuccessful || r.Result != ClientJoinResult.JoinAccepted) return null;
            return Refusals.Clean(r.Message);
        }

        public static void Line(JoinResult __instance, ref string __result)
        {
            try
            {
                string reason = Reason(__instance);
                if (reason != null) __result = Refusals.Prefix + reason;
            }
            catch (Exception) { }
        }

        public static void Again(JoinResult __instance, ref bool __result)
        {
            try
            {
                string reason = Reason(__instance);
                if (reason == null || !__result) return;
                bool known = Count.Seen(__instance);
                bool stop = Count.GiveUp(__instance, reason, (DateTime.UtcNow - Epoch).TotalSeconds);
                if (!known)
                {
                    if (stop) Log.Msg("join: refused again for the same reason, so back to the main menu: \"" + reason + "\"");
                    else if (Count.Times == 1) Log.Msg("join: the server turned it away: \"" + reason + "\""
                        + (Refusals.WaitingHelps(reason) ? " (the game tries again: waiting can clear this)." : "."));
                }
                if (stop) __result = false;
            }
            catch (Exception) { }
        }
    }
}
