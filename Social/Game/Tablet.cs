using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Meta.UI;
using Alta.Meta.UI.ServerManagement;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using HarmonyLib;

namespace TavernEssentials.Social
{
    internal static class Tablet
    {
        internal const string CardLine = "Pass them a friend card to be friends.";
        internal const string NotHereLine = "They are not here. Reports reach this server for players met this visit.";
        const string MenuCardLine = "Friends are made in-world: pass them a friend card.";
        const string MenuReportLine = "Reports go to the server you are on: use the Social Tablet there.";
        const double SaveEvery = 10;
        internal const int TestId = 424242;
        internal const string TestName = "Tester";

        static string _path;
        static RecentBook _book = new RecentBook();
        static double _nextSave;
        static bool _serverIdLogged, _testListed;
        static readonly Dictionary<TextRenderer, string> Swapped = new Dictionary<TextRenderer, string>();

        static FieldInfo _recentPlayer, _reportReason, _friendSentPage, _reportSentPage;

        internal static bool On { get { return _path != null; } }

        internal static int Apply(HarmonyLib.Harmony h, string socialDir)
        {
            _path = Path.Combine(socialDir, "recent.txt");
            try
            {
                string note;
                _book = RecentBook.Load(_path, out note);
                Log.Msg("recent players: " + _book.Count + " kept (" + _path + ")" + (note == null ? "." : "; " + note + "."));
            }
            catch (Exception e)
            {
                Log.Warn("recent players: could not read " + _path + " (" + e.Message + "); starting an empty list, saved over it.");
                _book = new RecentBook();
            }

            Type page = typeof(RecentPlayerActionPage);
            _recentPlayer = AccessTools.Field(page, "recentPlayer");
            _reportReason = AccessTools.Field(page, "reportReason");
            _friendSentPage = AccessTools.Field(page, "friendRequestSentPage");
            _reportSentPage = AccessTools.Field(page, "reportSentPage");

            int n = 0;
            n += Patch(h, typeof(RecentPlayersClient), "RecordInteractionAsync", "RecordPrefix", false);
            n += Patch(h, typeof(RecentPlayersClient), "GetAllAsync", "RowsPrefix", false);
            n += Patch(h, typeof(PlayerReportsClient), "ReportPlayer", "ReportedPrefix", false);
            n += Patch(h, page, "Setup", "SetupPostfix", true);
            n += Patch(h, page, "MakeFriendRequest", "AddFriendPrefix", false);
            n += Patch(h, page, "ReportPlayer", "ReportPrefix", false);
            n += Patch(h, typeof(RecentPlayersListPanel), "AddPlayerAsFriend", "MenuAddPrefix", false);
            n += Patch(h, typeof(RecentPlayersListPanel), "ReportPlayer", "MenuReportPrefix", false);
            return n;
        }

        internal const int Seams = 8;

        static int Patch(HarmonyLib.Harmony h, Type type, string method, string mine, bool postfix)
        {
            try
            {
                MethodInfo target = AccessTools.Method(type, method);
                MethodInfo patch = typeof(Tablet).GetMethod(mine, BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || patch == null)
                {
                    Log.Warn("tablet seam missing: " + type.Name + "." + method);
                    return 0;
                }
                if (postfix) h.Patch(target, null, new HarmonyMethod(patch));
                else h.Patch(target, new HarmonyMethod(patch));
                return 1;
            }
            catch (Exception e)
            {
                Log.Warn("could not patch " + type.Name + "." + method + ": " + e.Message);
                return 0;
            }
        }

        internal static void NewConnection()
        {
            if (!On) return;
            _book.NewConnection();
            _serverIdLogged = false;
            _testListed = false;
            _nextSave = 0;
        }

        internal static void Tick(double now)
        {
            if (!On || !_book.Dirty || now < _nextSave) return;
            _nextSave = now + SaveEvery;
            _book.Dirty = false;
            string text = _book.Encode(), path = _path;
            Worker.Run("save recent players", delegate { RecentBook.Save(path, text); return path; },
                delegate(object result, Exception error)
                {
                    if (error != null) Log.Warn("recent players: could not save " + path + ": " + error.Message);
                });
        }

        static bool RecordPrefix(UserInfo user, RecentPlayerInteractionType type, ref Task __result)
        {
            __result = Done();
            try
            {
                if (user == null) return false;
                _book.Record(user.Identifier, user.Username, ServerName(), FingerprintHere(user.Identifier), (int)type, UnixNow());
            }
            catch (Exception e)
            {
                Log.Warn("recent players: could not record " + (user == null ? "?" : user.Username) + ": " + e.Message);
            }
            return false;
        }

        static bool RowsPrefix(int limit, ref Task<IEnumerable<RecentPlayerInteractionInfo>> __result)
        {
            List<RecentPlayerInteractionInfo> list = new List<RecentPlayerInteractionInfo>();
            try
            {
                JoinedServerInfo joined = JoinedServerInfo.Current;
                bool inWorld = Player.Current != null && joined != null;
                string here = inWorld ? ServerName() : null;
                if (inWorld && !_serverIdLogged)
                {
                    _serverIdLogged = true;
                    Log.Msg("tablet: this server is \"" + here + "\"" + (here == joined.Name ? "" : " (it calls itself \"" + joined.Name + "\")")
                        + ", id " + joined.Identifier + ".");
                }
                if (inWorld && Config.Current.RecentTest && !_testListed)
                {
                    _testListed = true;
                    _book.Record(TestId, TestName, here, null, 3, UnixNow());
                    Log.Msg("tablet: social_recent_test=1, so " + TestName + " (" + TestId + ") is listed as met here; a report on them reaches this server as \"unknown\".");
                }
                foreach (RecentPlayer r in _book.Rows(null, -1, null))
                    if (r.Live && r.Fp == null)
                    {
                        string fp = FingerprintHere(r.LiveId);
                        if (fp != null) _book.Recognised(r.LiveId, fp);
                    }
                foreach (RecentPlayer r in _book.Rows(here, limit, Here))
                {
                    RecentPlayerInteractionInfo info = new RecentPlayerInteractionInfo();
                    info.InteractionTime = Epoch.AddSeconds(r.Seen);
                    info.Type = (RecentPlayerInteractionType)r.How;
                    info.UserInfo = new UserInfo(_book.IdOf(r, GameLists.MadeUpId), r.Name);
                    GameServerInfoMinimal server = new GameServerInfoMinimal();
                    server.Identifier = inWorld && r.Server == here ? joined.Identifier : -1;
                    server.Name = r.Server;
                    info.Server = server;
                    list.Add(info);
                }
            }
            catch (Exception e)
            {
                Log.Warn("recent players: could not list them: " + e.Message);
            }
            __result = Task.FromResult<IEnumerable<RecentPlayerInteractionInfo>>(list);
            return false;
        }

        static bool ReportedPrefix(ref Task<PlayerReportInfo> __result)
        {
            __result = Task.FromResult<PlayerReportInfo>(null);
            return false;
        }

        static void SetupPostfix()
        {
            foreach (KeyValuePair<TextRenderer, string> s in Swapped)
                if (s.Key != null) s.Key.Text = s.Value;
            Swapped.Clear();
        }

        static bool AddFriendPrefix(RecentPlayerActionPage __instance)
        {
            try
            {
                UserInfo who = Who(__instance);
                if (who == null || GameLists.CanRestore(who.Identifier)) return true;
                Log.Msg("tablet: Add Friend on " + who.Username + ": friend cards make friends, so nothing was sent.");
                Say(__instance, _friendSentPage, "Text_FriendSent", CardLine);
                return false;
            }
            catch (Exception e)
            {
                Log.Warn("tablet: Add Friend: " + e.Message);
                return true;
            }
        }

        static bool ReportPrefix(RecentPlayerActionPage __instance)
        {
            try
            {
                UserInfo who = Who(__instance);
                if (who == null) return true;
                if (FriendIds.IsMadeUp(who.Identifier) || RecentBook.IsOtherId(who.Identifier))
                {
                    Log.Msg("tablet: Report on " + who.Username + ": not met on this server this visit, so nothing was sent.");
                    Say(__instance, _reportSentPage, "Text_ReportSent", NotHereLine);
                    return false;
                }
                object reason = _reportReason == null ? null : _reportReason.GetValue(__instance);
                Log.Msg("tablet: reporting " + who.Username + " (" + who.Identifier + ") for " + reason + " to this server.");
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("tablet: Report: " + e.Message);
                return true;
            }
        }

        static UserInfo Who(RecentPlayerActionPage page)
        {
            RecentPlayerInteractionInfo info = _recentPlayer == null ? null : _recentPlayer.GetValue(page) as RecentPlayerInteractionInfo;
            return info == null ? null : info.UserInfo;
        }

        static void Say(RecentPlayerActionPage page, FieldInfo pageField, string textName, string line)
        {
            Panel target = pageField == null ? null : pageField.GetValue(page) as Panel;
            if (target == null) throw new InvalidOperationException("the page is missing");
            TextRenderer text = null;
            foreach (TextRenderer t in target.GetComponentsInChildren<TextRenderer>(true))
                if (t.gameObject.name == textName) { text = t; break; }
            if (text != null)
            {
                if (!Swapped.ContainsKey(text)) Swapped[text] = text.Text;
                text.Text = line;
            }
            else Log.Warn("tablet: no " + textName + " on " + target.name + "; it shows the game's own words.");
            target.SwitchTo();
        }

        static bool MenuAddPrefix(RecentPlayersListPanel __instance, RecentPlayerInteractionInfo player)
        {
            try
            {
                if (player != null && player.UserInfo != null && GameLists.CanRestore(player.UserInfo.Identifier)) return true;
                Popup(__instance, MenuCardLine);
                return false;
            }
            catch (Exception e)
            {
                Log.Warn("recent players tab: Add as Friend: " + e.Message);
                return true;
            }
        }

        static bool MenuReportPrefix(RecentPlayersListPanel __instance)
        {
            try
            {
                Popup(__instance, MenuReportLine);
                return false;
            }
            catch (Exception e)
            {
                Log.Warn("recent players tab: Report: " + e.Message);
                return true;
            }
        }

        static void Popup(RecentPlayersListPanel panel, string line)
        {
            panel.Panel.Window.ShowConfirmPopup(line, "Ok", null, null, null);
        }

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static long UnixNow()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalSeconds;
        }

        static string ServerName()
        {
            JoinedServerInfo j = JoinedServerInfo.Current;
            return ServerNames.Here(j == null ? null : j.Name);
        }

        static string FingerprintHere(int id)
        {
            ClientProtocol p = ClientSide.Protocol;
            return p == null ? null : p.FingerprintHere(id);
        }

        static bool Here(int id)
        {
            return Player.Get(id) != null;
        }

        static Task Done()
        {
            return Task.FromResult(true);
        }
    }
}
