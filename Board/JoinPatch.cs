using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Threading;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using HarmonyLib;

namespace TavernEssentials.Board
{
    public static class JoinPatch
    {
        static readonly object JoinedGate = new object();
        static DevGameServerInfo _joined;

        internal static DevGameServerInfo Joined
        {
            get { lock (JoinedGate) { return _joined; } }
        }

        static void SetJoined(DevGameServerInfo dev)
        {
            lock (JoinedGate) { _joined = dev; }
        }

        const int DefaultSceneIndex = 0;
        const int QuestSceneIndex = 4;

        public static bool Apply(HarmonyLib.Harmony h)
        {
            MethodInfoWrap target = Find();
            if (target.Method == null)
            {
                Log.Warn("join seam missing: GameServerInfoExtensions.JoinServerAsync - join stays vanilla.");
                return false;
            }
            var postfix = new HarmonyMethod(typeof(JoinPatch).GetMethod("Postfix",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
            System.Reflection.MethodInfo approved = ApprovedTarget();
            if (approved == null)
            {
                Log.Warn("join seam missing: JoinServerPipeline.ConnectionApprovedByServer - join stays vanilla"
                    + " (a board join would play under the wrong id).");
                return false;
            }
            try
            {
                h.Patch(approved, new HarmonyMethod(typeof(JoinPatch).GetMethod("ApprovedPrefix",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)));
                h.Patch(target.Method, null, postfix);
                GameModeManager.ModeChanged += delegate { SetJoined(null); };
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("join patch failed: " + e.Message);
                return false;
            }
        }

        struct MethodInfoWrap { public System.Reflection.MethodInfo Method; }

        internal static System.Reflection.MethodInfo ApprovedTarget()
        {
            return AccessTools.Method(typeof(JoinServerPipeline), "ConnectionApprovedByServer");
        }

        public static void ApprovedPrefix(ServerJoinResult ___joinResult)
        {
            try
            {
                JwtSecurityToken wire = ___joinResult != null && ___joinResult.Token != null
                    ? ___joinResult.Token : ApiAccess.ApiClient.UserCredentials.IdentityToken;
                int id;
                if (!UserId(wire, out id))
                {
                    Log.Warn("join: the token has no readable UserId; the game joins as it is set.");
                    return;
                }
                UserInfo me = ApiAccess.ApiClient.UserClient.LoggedInUserInfo;
                if (me == null || me.Identifier == id) return;
                int was = me.Identifier;
                me.Identifier = id;
                Log.Msg("join: playing as id " + id + ", the id this server has for you (the game had " + was + ").");
            }
            catch (Exception e)
            {
                Log.Warn("join: could not set the id to join as: " + e.Message);
            }
        }

        internal static bool UserId(JwtSecurityToken token, out int id)
        {
            id = 0;
            if (token == null) return false;
            foreach (System.Security.Claims.Claim c in token.Claims)
                if (c.Type == "UserId") return int.TryParse(c.Value, out id);
            return false;
        }

        static MethodInfoWrap Find()
        {
            MethodInfoWrap w = new MethodInfoWrap();
            Type ext = AccessTools.TypeByName("GameServerInfoExtensions");
            if (ext == null) return w;
            w.Method = AccessTools.Method(ext, "JoinServerAsync", new Type[] { typeof(GameServerInfo) });
            return w;
        }

        public static void Postfix(GameServerInfo gameServer, ref Task<ServerJoinResult> __result)
        {
            try
            {
                if (!BoardConfig.JoinAuth) return;
                DevGameServerInfo dev = gameServer as DevGameServerInfo;
                if (dev == null) return;
                if (gameServer.Identifier > -2) return;
                if (dev.ConnectionInfo == null) return;
                __result = Wrap(__result, dev);
            }
            catch (Exception e)
            {
                Log.Warn("join postfix: " + e.Message);
            }
        }

        static Task<ServerJoinResult> Wrap(Task<ServerJoinResult> orig, DevGameServerInfo dev)
        {
            TaskCompletionSource<ServerJoinResult> tcs = new TaskCompletionSource<ServerJoinResult>();
            string ip = dev.ConnectionInfo.Address.ToString();
            int port = dev.ConnectionInfo.GamePort;

            Thread t = new Thread(delegate()
            {
                ServerJoinResult r;
                try { r = orig.GetAwaiter().GetResult(); }
                catch (Exception e) { SetJoined(null); tcs.SetException(e); return; }

                bool ok = false;
                try
                {
                    if (r != null && r.IsAllowed && r.ConnectionInfo != null)
                    {
                        string host = CommunityList.OriginalHost(ip, port);
                        if (string.IsNullOrEmpty(host)) host = ip;

                        AuthResult a = Authenticator.AuthenticateFor(host, ip);
                        if (a.Ok)
                        {
                            r.Token = new JwtSecurityToken(a.Jwt);
                            ApplySceneIndex(dev, a.QuestScene, host);
                            Log.Msg("join: authenticated on \"" + host + "\".");
                            ok = true;
                        }
                        else if (a.CleanFail)
                        {
                            r.IsAllowed = false;
                            r.WasRejected = true;
                            r.FailReason = ServerJoinFailResult.CouldntVerifyUser;
                            r.Message = a.Message;
                            Log.Warn("join refused: " + a.Message);
                        }
                        else
                        {
                            Log.Warn("join: no 1762 auth on \"" + host + "\" ("
                                + a.Message + ") - trying launch credentials.");
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Warn("join auth error: " + e.Message);
                }
                SetJoined(ok ? dev : null);
                tcs.SetResult(r);
            });
            t.IsBackground = true;
            t.Name = "TavernEssentials board join-auth";
            t.Start();
            return tcs.Task;
        }

        static void ApplySceneIndex(DevGameServerInfo dev, bool questScene, string host)
        {
            if (!BoardConfig.QuestScene) return;
            int want = questScene ? QuestSceneIndex : DefaultSceneIndex;
            if (dev.SceneIndex == want) return;
            dev.SceneIndex = want;
            Log.Msg("join: \"" + host + "\" wants scene " + want
                + (questScene ? " (quest overworld)." : " (standard overworld)."));
        }
    }
}
