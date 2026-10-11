using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Alta.Api.Client.HighLevel;
using Alta.Api.Client.LowLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Shared;
using Alta.Customization;
using HarmonyLib;

namespace TavernEssentials.Backend
{
    internal static class BackendFeature
    {
        sealed class Seam
        {
            internal readonly string Name;
            internal readonly Type Type;
            internal readonly string Method;
            internal readonly Type[] Args;
            internal readonly string Prefix;

            internal Seam(string name, Type type, string method, Type[] args, string prefix)
            {
                Name = name;
                Type = type;
                Method = method;
                Args = args;
                Prefix = prefix;
            }
        }

        static readonly Seam[] Seams =
        {
            new Seam("login refresh", typeof(UserApiClient), "RefreshLoginAsync", new Type[0], "Done"),
            new Seam("customizer", typeof(PurchasedList), "UpdateCurrentRotation", new Type[0], "NoRotation"),
            new Seam("public IP", typeof(ServicesApiClient), "GetPublicIpAddressAsync", new Type[0], "NoPublicIp"),
            new Seam("email check", typeof(UserApiClient), "IsUserVerifiedAsync", new Type[0], "Verified"),
            new Seam("analytics", typeof(LowLevelApiClient), "EnqueueAnalyticsEvent", new Type[] { typeof(AnalyticsEventData) }, "Done"),
            new Seam("inbox statuses", typeof(StatusAPI), "GetStatus", new Type[0], "Statuses"),
            new Seam("inbox reports", typeof(PlayerReportsClient), "ViewAllReports",
                new Type[] { typeof(ReportStatus?), typeof(int?) }, "NoReports"),
            new Seam("wrist settings", typeof(ServerApiClient), "HasPermissionsInServer",
                new Type[] { typeof(int), typeof(int), typeof(GroupPermissions[]) }, "NoPermission"),
        };

        internal static int Total { get { return Seams.Length; } }

        static string _marker;
        static bool _welcome;
        static Status _status;
        static bool _marked;

        internal static bool Start(string game)
        {
            if (!BackendConfig.Enabled)
            {
                Log.Msg("switched off (backend_enabled=0): dead Alta calls are left to fail slowly.");
                return false;
            }
            PickWelcome(Path.Combine(Path.Combine(Path.Combine(game, "UserData"), "TavernEssentials"), "welcome-seen.txt"));
            HarmonyLib.Harmony h = new HarmonyLib.Harmony("tavernessentials.backend");
            List<string> missing = new List<string>();
            int applied = Apply(h, missing);
            Log.Msg("ready: " + applied + "/" + Seams.Length + " stand-ins for Alta's dead web API"
                + (missing.Count == 0 ? "." : "; missing: " + string.Join(", ", missing.ToArray()) + "."));
            bool hidden = Achievements.Start(h);
            return applied > 0 || hidden;
        }

        internal static void PickWelcome(string marker)
        {
            _marker = marker;
            _status = null;
            _marked = false;
            _welcome = BackendConfig.ShowWelcome && !File.Exists(marker);
            if (!BackendConfig.ShowWelcome) Log.Msg("welcome: switched off (backend_welcome=0).");
            else if (_welcome) Log.Msg("welcome: not handed out here yet (no " + marker + "); the main menu's inbox shows it.");
        }

        internal static Status[] Inbox()
        {
            if (!_welcome) return new Status[0];
            try
            {
                if (_status == null) _status = WelcomeStatus();
                if (!_marked)
                {
                    _marked = true;
                    Welcome.Mark(_marker, Mod.Version);
                    Log.Msg("welcome: handed to the inbox; " + _marker + " is written (" + Mod.Version
                        + "), so it shows this game only.");
                }
            }
            catch (Exception e)
            {
                Log.Warn("welcome: " + e.GetType().Name + ": " + e.Message);
            }
            return _status == null ? new Status[0] : new Status[] { _status };
        }

        static Status WelcomeStatus()
        {
            DateTime now = DateTime.UtcNow;
            Status s = new Status();
            s.Identifier = Welcome.Id;
            s.Title = Welcome.Title;
            s.Message = Welcome.Message;
            s.CreatedAt = now;
            s.PublishedAt = now;
            s.UpdatedAt = now;
            s.Region = Status.StatusRegion.Global;
            s.Type = Status.StatusType.OnGoing;
            s.Platform = Status.StatusPlatform.Pc;
            s.Severity = Status.StatusSeverity.Low;
            return s;
        }

        internal static int Apply(HarmonyLib.Harmony h, List<string> missing)
        {
            int applied = 0;
            foreach (Seam s in Seams)
            {
                try
                {
                    MethodInfo target = AccessTools.Method(s.Type, s.Method, s.Args);
                    MethodInfo prefix = typeof(StandIns).GetMethod(s.Prefix, BindingFlags.Public | BindingFlags.Static);
                    if (target == null || prefix == null)
                    {
                        missing.Add(s.Name + " (" + s.Type.Name + "." + s.Method + ")");
                        continue;
                    }
                    h.Patch(target, new HarmonyMethod(prefix));
                    applied++;
                }
                catch (Exception e)
                {
                    missing.Add(s.Name + " (" + e.GetType().Name + ": " + e.Message + ")");
                }
            }
            return applied;
        }
    }

    public static class StandIns
    {
        public static bool Done(ref Task __result)
        {
            __result = Task.CompletedTask;
            return false;
        }

        public static bool NoRotation(ref Task __result)
        {
            PurchasedList.CurrentOnStore.Clear();
            PurchasedList.CurrentRotationToSets.Clear();
            __result = Task.CompletedTask;
            return false;
        }

        public static bool NoPublicIp(ref Task<IPAddress> __result)
        {
            __result = Task.FromResult(new IPAddress(0L));
            return false;
        }

        public static bool Verified(ref Task<bool> __result)
        {
            __result = Task.FromResult(true);
            return false;
        }

        public static bool Statuses(ref Task<IEnumerable<Status>> __result)
        {
            __result = Task.FromResult<IEnumerable<Status>>(BackendFeature.Inbox());
            return false;
        }

        public static bool NoReports(ref Task<IEnumerable<PlayerReportAdmin>> __result)
        {
            __result = Task.FromResult<IEnumerable<PlayerReportAdmin>>(new PlayerReportAdmin[0]);
            return false;
        }

        public static bool NoPermission(ref Task<bool> __result)
        {
            __result = Task.FromResult(false);
            return false;
        }
    }

    internal static class Log
    {
        internal static readonly FeatureLog Out = new FeatureLog();

        internal static void Msg(string s) { Out.Msg(s); }
        internal static void Warn(string s) { Out.Warn(s); }
    }

    internal static class BackendConfig
    {
        internal static bool Enabled = true;
        internal static bool ShowWelcome = true;
        internal static bool HideAchievements = true;

        internal static readonly string[] Template =
        {
            "# ---- Backend: quick answers to calls that still go to Alta's dead web API. Game client only.",
            "",
            "# 1 = answer them at once: leaving a server lands in the main menu (not a black screen), the",
            "# customizer and the inbox open without a wait, the first join is ~7 s faster, the",
            "# analytics queue stops growing, and the wrist menu stops asking Alta on every spawn.",
            "# 0 = the game waits on each and fails, as before.",
            "backend_enabled=1",
            "",
            "# 1 = a one-line welcome in the main menu's inbox, where Alta's news was, once",
            "# (UserData\\TavernEssentials\\welcome-seen.txt marks it done). Nothing is fetched. Needs",
            "# backend_enabled=1. 0 = the inbox stays empty.",
            "backend_welcome=1",
            "",
            "# 1 = hide the town's tutorial boards (Alta's achievement boards). Achievements never count on",
            "# community servers, so every task on them stays open for good. Your view only: nothing is",
            "# saved or sent. Needs backend_enabled=1. 0 = the boards show, as before.",
            "backend_hide_achievements=1",
        };

        internal static void Load(Settings s)
        {
            Enabled = s.Bool("backend_enabled", true);
            ShowWelcome = s.Bool("backend_welcome", true);
            HideAchievements = s.Bool("backend_hide_achievements", true);
        }
    }
}
