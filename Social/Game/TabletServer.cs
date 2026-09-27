using System;
using System.Reflection;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Console;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using ATT.PlayerStates;
using HarmonyLib;
using AltaStream = Alta.Serialization.Stream;

namespace TavernEssentials.Social
{
    internal static class TabletServer
    {
        static readonly ReportGuard Guard = new ReportGuard();
        static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        static Type _struct;
        static MethodInfo _serialize;
        static FieldInfo _target, _reason, _action;

        internal static bool Apply(HarmonyLib.Harmony h)
        {
            MethodInfo target = AccessTools.Method(typeof(RecentPlayerActionPage), "SyncSocialTabletAction");
            _struct = AccessTools.TypeByName("Alta.UserInterface.SocialTablet.SocialTabletActionSyncStruct");
            if (_struct != null)
            {
                _serialize = AccessTools.Method(_struct, "Serialize");
                _target = AccessTools.Field(_struct, "TargetPlayerId");
                _reason = AccessTools.Field(_struct, "Reason");
                _action = AccessTools.Field(_struct, "ActionType");
            }
            if (target == null || _serialize == null || _target == null || _reason == null || _action == null)
            {
                Log.Warn("tablet reports: seam missing (RecentPlayerActionPage.SyncSocialTabletAction or its message); reports stay with the game.");
                return false;
            }
            try
            {
                h.Patch(target, new HarmonyMethod(typeof(TabletServer).GetMethod("SyncPrefix", BindingFlags.NonPublic | BindingFlags.Static)));
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("tablet reports: could not patch SyncSocialTabletAction: " + e.Message);
                return false;
            }
        }

        static bool SyncPrefix(IPlayer player, AltaStream stream)
        {
            if (!NetworkSceneManager.IsServer || stream == null || !stream.IsReading) return true;
            try
            {
                object data = Activator.CreateInstance(_struct);
                _serialize.Invoke(data, new object[] { stream });
                Handle(player, (int)_target.GetValue(data), _reason.GetValue(data) as string, Convert.ToInt32(_action.GetValue(data)));
            }
            catch (Exception e)
            {
                Log.Error("tablet reports: a tablet message could not be read: " + e.Message);
            }
            return false;
        }

        static void Handle(IPlayer sender, int targetId, string reason, int action)
        {
            UserInfo by = LoginOf(sender as Player);
            if (by == null)
            {
                Log.Warn("tablet reports: a tablet message from a player with no login; dropped.");
                return;
            }
            UserInfo target = Target(targetId);
            if (action != 0)
            {
                Log.Msg("tablet: " + Describe(by) + "'s tablet sent " + (action == 1 ? "a ban" : "an unban") + " of " + Describe(target)
                    + "; dropped (the tablet's ban never reaches Alta; ban with the server's own commands).");
                return;
            }
            if (!ReportGuard.ValidReason(reason))
            {
                Log.Warn("tablet: " + Describe(by) + " reported " + Describe(target) + " for an unknown reason; dropped.");
                return;
            }
            if (!Guard.Allow(by.Identifier, targetId, Clock.Elapsed.TotalSeconds))
            {
                Log.Msg("tablet: " + Describe(by) + " reported " + Describe(target) + " again too soon; held back, no event.");
                return;
            }
            SocialTabletPlayerReportData report = new SocialTabletPlayerReportData();
            report.ReportedBy = by;
            report.ReportedPlayer = target;
            report.Reason = reason;
            ConsoleEvents.SocialTabletPlayerReported.Invoke(delegate { return report; });
            Log.Msg("tablet: " + Describe(by) + " reported " + Describe(target) + " for " + reason + "; SocialTabletPlayerReported fired.");
        }

        static UserInfo LoginOf(Player p)
        {
            if (p == null) return null;
            Connection c = p.ConnectionToRemotePlayer;
            return c == null || c.UserInfo == null ? null : c.UserInfo.UserInfo;
        }

        static UserInfo Target(int id)
        {
            UserInfo login = LoginOf(Player.Get(id) as Player);
            if (login != null) return login;
            string name;
            try
            {
                if (Names.NamesFeature.Resolve(id, out name) && name != null) return new UserInfo(id, name);
            }
            catch (Exception e)
            {
                Log.Detail("tablet reports: name lookup for " + id + ": " + e.Message);
            }
            return new UserInfo(id, "unknown");
        }

        static string Describe(UserInfo u)
        {
            return u == null ? "?" : u.Username + " (" + u.Identifier + ")";
        }
    }
}
