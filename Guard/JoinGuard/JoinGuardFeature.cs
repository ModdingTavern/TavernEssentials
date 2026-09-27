using System;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Alta.Networking.Servers;
using HarmonyLib;

namespace TavernEssentials.JoinGuard
{
    internal static class JoinGuardFeature
    {
        internal const string Refused = "Your game asked to join as another player. Update TavernEssentials (or TavernBoard) on your PC, or join from the launcher.";

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
            try
            {
                string name;
                int tokenId;
                if (!Read(tokenString, out tokenId, out name)) return;
                if (tokenId == playerId || AlreadyRefused(__result)) return;
                if (JoinGuardConfig.Mode != JoinGuardConfig.ModeRefuse)
                {
                    Log.Warn("\"" + name + "\" (token id " + tokenId + ") joined as id " + playerId
                        + ": another account's id. Let in (joinguard_mode=log); refuse would turn them away.");
                    return;
                }
                object denied = Denied(Refused);
                if (denied == null) return;
                __result = denied;
                Log.Warn("refused \"" + name + "\" (token id " + tokenId + "): the game asked to join as id " + playerId + ".");
            }
            catch (Exception e)
            {
                Log.Warn("checking a join: " + e.GetType().Name + ": " + e.Message);
            }
        }

        internal static bool Read(string tokenString, out int id, out string name)
        {
            id = 0;
            name = null;
            if (string.IsNullOrEmpty(tokenString)) return false;
            JwtSecurityToken token;
            try { token = new JwtSecurityToken(tokenString); }
            catch (Exception) { return false; }
            string uid = null;
            foreach (Claim c in token.Claims)
            {
                if (c.Type == "UserId") uid = c.Value;
                else if (c.Type == "Username") name = c.Value;
            }
            return uid != null && int.TryParse(uid, out id);
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
