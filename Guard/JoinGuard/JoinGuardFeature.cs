using System;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Converters;
using Alta.Networking.Servers;
using HarmonyLib;

namespace TavernEssentials.JoinGuard
{
    internal static class JoinGuardFeature
    {
        internal const string Refused = "Your game asked to join as another player. Update TavernEssentials (or TavernBoard) on your PC, or join from the launcher.";
        internal const string Unreadable = "This server could not read your login token. Join from the launcher.";

        internal static bool Start()
        {
            if (JoinGuardConfig.Mode == JoinGuardConfig.ModeOff)
            {
                Log.Msg("switched off (joinguard_mode=off): a player's id is not checked against their token.");
                return false;
            }
            MethodInfo target = Target();
            if (target == null)
            {
                Log.Warn("join check seam missing: ServerPlayerConnectionHandlerOld.CheckIfPlayerIsAllowedCustom - ids are not checked.");
                return false;
            }
            HarmonyLib.Harmony h = new HarmonyLib.Harmony("tavernessentials.joinguard");
            h.Patch(target, null, new HarmonyMethod(typeof(JoinGuardFeature).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
            Log.Msg("ready (joinguard_mode=" + JoinGuardConfig.Mode + "): "
                + (JoinGuardConfig.Mode == JoinGuardConfig.ModeRefuse
                    ? "a join whose id is not its token's is refused."
                    : "a join whose id is not its token's is logged, not refused. joinguard_mode=refuse turns it away."));
            return true;
        }

        internal static MethodInfo Target()
        {
            return AccessTools.Method(typeof(ServerPlayerConnectionHandlerOld), "CheckIfPlayerIsAllowedCustom");
        }

        public static void Postfix(int playerId, string tokenString, ref object __result)
        {
            string verdict;
            try
            {
                verdict = Check(playerId, tokenString, ref __result);
            }
            catch (Exception e)
            {
                verdict = "not checked: " + e.GetType().Name;
                Log.Warn("checking a join: " + e.GetType().Name + ": " + e.Message);
            }
            if (Interlocked.Exchange(ref _firstChecked, 1) == 0)
                Log.Msg("checked the first join (id " + playerId + ": " + verdict + ").");
        }

        static int _firstChecked;

        static string Check(int playerId, string tokenString, ref object __result)
        {
            if (AlreadyRefused(__result)) return "already refused before the guard";
            string name;
            int tokenId;
            string why = Read(tokenString, out tokenId, out name);
            bool refuse = JoinGuardConfig.Mode == JoinGuardConfig.ModeRefuse;
            if (why != null)
            {
                if (!refuse)
                {
                    Log.Warn("a join as id " + playerId + " with a token " + why
                        + ": let in (joinguard_mode=log); refuse would turn it away.");
                    return "logged: token " + why;
                }
                if (!Deny(Unreadable, ref __result)) return "let in: no refusal could be built";
                Log.Warn("refused a join as id " + playerId + ": its token " + why + ".");
                return "refused: token " + why;
            }
            if (tokenId == playerId) return "its token's own id";
            if (!refuse)
            {
                Log.Warn("\"" + name + "\" (token id " + tokenId + ") joined as id " + playerId
                    + ": another account's id. Let in (joinguard_mode=log); refuse would turn them away.");
                return "logged: another account's id";
            }
            if (!Deny(Refused, ref __result)) return "let in: no refusal could be built";
            Log.Warn("refused \"" + name + "\" (token id " + tokenId + "): the game asked to join as id " + playerId + ".");
            return "refused: another account's id";
        }

        static bool Deny(string why, ref object __result)
        {
            object denied = Denied(why);
            if (denied == null) return false;
            __result = denied;
            return true;
        }

        internal static string Read(string tokenString, out int id, out string name)
        {
            id = 0;
            name = null;
            if (string.IsNullOrEmpty(tokenString)) return "that is empty";
            string uid = null;
            int uids = 0, names = 0;
            try
            {
                foreach (Claim c in JWTUtility.CreateFromString(tokenString, false).Claims)
                {
                    if (c.Type == "UserId") { uids++; uid = c.Value; }
                    else if (c.Type == "Username") { names++; name = c.Value; }
                }
            }
            catch (Exception) { return "that cannot be read"; }
            if (uids != 1) return "that has " + uids + " UserId claims";
            if (names != 1) return "that has " + names + " Username claims";
            if (!int.TryParse(uid, NumberStyles.None, CultureInfo.InvariantCulture, out id)) return "whose UserId is not a number";
            return null;
        }

        static bool AlreadyRefused(object task)
        {
            Task t = task as Task;
            if (t == null || !t.IsCompleted || t.IsFaulted || t.IsCanceled) return false;
            PropertyInfo result = t.GetType().GetProperty("Result");
            object r = result == null ? null : result.GetValue(t, null);
            PropertyInfo error = r == null ? null : r.GetType().GetProperty("Error");
            return error != null && error.GetValue(r, null) != null;
        }

        static Type _result;
        static MethodInfo _deny;
        static MethodInfo _fromResult;

        internal static object Denied(string why)
        {
            if (_deny == null)
            {
                _result = typeof(ServerPlayerConnectionHandlerOld).GetNestedType("PlayerJoinResult", BindingFlags.NonPublic | BindingFlags.Public);
                _deny = _result == null ? null : _result.GetMethod("CreateDeniedResult", BindingFlags.Public | BindingFlags.Static);
                MethodInfo from = typeof(Task).GetMethod("FromResult");
                _fromResult = _result == null || from == null ? null : from.MakeGenericMethod(_result);
                if (_deny == null || _fromResult == null)
                {
                    Log.Error("could not build a refusal (PlayerJoinResult.CreateDeniedResult not found); the join goes through.");
                    return null;
                }
            }
            return _fromResult.Invoke(null, new object[] { _deny.Invoke(null, new object[] { why }) });
        }
    }

    internal static class Log
    {
        internal static readonly FeatureLog Out = new FeatureLog();

        internal static void Msg(string s) { Out.Msg(s); }
        internal static void Warn(string s) { Out.Warn(s); }
        internal static void Error(string s) { Out.Error(s); }
    }

    internal static class JoinGuardConfig
    {
        internal const string ModeOff = "off";
        internal const string ModeLog = "log";
        internal const string ModeRefuse = "refuse";

        internal static string Mode = ModeLog;

        internal static readonly string[] Template =
        {
            "# ---- JoinGuard: a player plays under the id their login token carries. Server only.",
            "",
            "# refuse = turn away a join that asks for an id its token is not for (TavernLib 1.5.x lets",
            "#          it in, as another player). log = let it in and log it. off = no check.",
            "# Players on TavernEssentials 0.6 or older, or on TavernBoard, send the wrong id when they",
            "# join from the main menu's boards; with refuse they are told to update.",
            "joinguard_mode=log",
        };

        internal static void Load(Settings s, System.Collections.Generic.List<string> notes)
        {
            string v = s.Text("joinguard_mode", ModeLog).ToLowerInvariant();
            if (v == ModeOff || v == ModeLog || v == ModeRefuse) Mode = v;
            else
            {
                Mode = ModeLog;
                notes.Add("config joinguard_mode=" + v + " is not refuse, log or off; log stays.");
            }
        }
    }
}
