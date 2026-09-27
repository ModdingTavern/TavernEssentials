using System;
using System.Reflection;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using HarmonyLib;
using UnityEngine;

namespace TavernEssentials.Discord
{
    internal static class DiscordFeature
    {
        const float Every = 1f;
        const float FlagEvery = 5f;
        const float LookEvery = 5f;

        static FieldInfo _changed, _appId;
        static DiscordController _link;
        static bool _linkSaid, _pending;
        static float _next, _nextFlag, _nextLook;
        static Status _want;
        static JoinedServerInfo _joined;
        static long _since;
        static string _sentSaid, _answerSaid;

        internal static bool Start()
        {
            if (!DiscordConfig.Enabled)
            {
                Log.Msg("switched off (discord_status=0): the game's own status, as it was.");
                return false;
            }
            _changed = AccessTools.Field(typeof(DiscordController), "isActivityChanged");
            _appId = AccessTools.Field(typeof(DiscordController), "applicationId");
            MethodInfo send = AccessTools.Method(typeof(global::Discord.ActivityManager), "UpdateActivity");
            MethodInfo mine = typeof(DiscordFeature).GetMethod("SendPrefix", BindingFlags.NonPublic | BindingFlags.Static);
            if (send == null || mine == null || _changed == null)
            {
                Log.Warn("seam missing (" + (send == null ? "ActivityManager.UpdateActivity" : "DiscordController.isActivityChanged")
                    + "): the game's own status stays.");
                return false;
            }
            new HarmonyLib.Harmony("tavernessentials.discord").Patch(send, new HarmonyMethod(mine));
            Log.Msg("ready: the game's Discord status will name the server as the community list does"
                + (DiscordConfig.Players ? ", with how many are on it" : "") + ", and say \"" + StatusText.Menu + "\" in the menu.");
            return true;
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + Every;
            Status want = Now(null);
            if (!want.Same(_want))
            {
                _want = want;
                _pending = true;
            }
            if (!_pending || now < _nextFlag) return;
            DiscordController link = Link(now);
            if (link == null) return;
            _changed.SetValue(link, true);
            _pending = false;
            _nextFlag = now + FlagEvery;
        }

        static DiscordController Link(float now)
        {
            if (_link != null) return _link;
            if (now < _nextLook) return null;
            _nextLook = now + LookEvery;
            _link = UnityEngine.Object.FindObjectOfType<DiscordController>();
            if (_link == null || _linkSaid) return _link;
            _linkSaid = true;
            string app = _appId == null ? "?" : _appId.GetValue(_link) as string;
            if (_link.isActiveAndEnabled) Log.Msg("the game's link to Discord is up (application " + app + ").");
            else Log.Warn("the game's link to Discord is switched off (application " + app
                + "): it turns itself off when Discord was not running as the game started, so no status goes out.");
            return _link;
        }

        static Status Now(string doing)
        {
            JoinedServerInfo joined = JoinedServerInfo.Current;
            bool inWorld = Player.Current != null && joined != null;
            if (!inWorld)
            {
                _joined = null;
                return StatusText.Build(false, null, null, 0, 0, 0, false);
            }
            if (!ReferenceEquals(joined, _joined))
            {
                _joined = joined;
                _since = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            }
            return StatusText.Build(true, ServerNames.Here(joined.Name), doing, Player.AllPlayers.Count,
                ServerNames.Limit(), _since, DiscordConfig.Players);
        }

        static void SendPrefix(ref global::Discord.Activity activity, ref global::Discord.ActivityManager.UpdateActivityHandler callback)
        {
            try
            {
                string game = "\"" + (activity.Details ?? "") + "\" / \"" + (activity.State ?? "") + "\""
                    + (string.IsNullOrEmpty(activity.Secrets.Join) ? "" : ", join secret \"" + activity.Secrets.Join + "\"");
                Status s = Now(activity.Details);
                activity.State = s.State;
                activity.Details = s.Details;
                activity.Party.Id = s.PartyId;
                activity.Party.Size.CurrentSize = s.PartySize;
                activity.Party.Size.MaxSize = s.PartyMax;
                activity.Secrets.Match = null;
                activity.Secrets.Join = null;
                activity.Secrets.Spectate = null;
                activity.Timestamps.Start = s.Start;
                activity.Timestamps.End = 0;
                string said = s.Describe();
                if (said != _sentSaid)
                {
                    _sentSaid = said;
                    _answerSaid = null;
                    Log.Msg("status: " + said + " (the game had " + game + ").");
                }
                global::Discord.ActivityManager.UpdateActivityHandler theirs = callback;
                callback = delegate(global::Discord.Result r)
                {
                    Answer(r);
                    if (theirs != null) theirs(r);
                };
            }
            catch (Exception e)
            {
                Log.Warn("status: " + e.Message + "; the game's own went out instead.");
            }
        }

        static void Answer(global::Discord.Result r)
        {
            string a = r.ToString();
            if (a == _answerSaid) return;
            _answerSaid = a;
            if (r == global::Discord.Result.Ok) Log.Msg("Discord took it.");
            else Log.Warn("Discord refused it: " + a + ".");
        }

        internal static string LastSent { get { return _sentSaid; } }
    }

    internal static class Log
    {
        internal static readonly FeatureLog Out = new FeatureLog();

        internal static void Msg(string s) { Out.Msg(s); }
        internal static void Warn(string s) { Out.Warn(s); }
    }

    internal static class DiscordConfig
    {
        internal static bool Enabled = true;
        internal static bool Players = true;

        internal static readonly string[] Template =
        {
            "# ---- Discord: your Discord status, through the game's own link to Discord (Alta's",
            "# application: nothing to install or sign up for). Game client only.",
            "",
            "# 1 = in a world your status names the server as the community list does (TavernLib",
            "# servers all call themselves \"Local Server\") and how long you have been there; in the",
            "# main menu it reads \"In the main menu\". No Join button: it asked Alta's dead web API.",
            "# 0 = the game's own status, as it was.",
            "discord_status=1",
            "",
            "# 1 = with how many are on the server, as \"(3 of 10)\", when the list gives its limit.",
            "discord_players=1",
        };

        internal static void Load(Settings s)
        {
            Enabled = s.Bool("discord_status", true);
            Players = s.Bool("discord_players", true);
        }
    }
}
