using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Inventory;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using HarmonyLib;

namespace TavernEssentials.Postbox
{
    internal static class PostboxFeature
    {
        internal delegate List<KeyValuePair<int, string>> FriendsQuery(int sessionId, out string why);

        internal static FriendsQuery FriendsOf = Social.ServerSide.FriendsHereOf;
        internal static FriendsQuery AwayOf = Social.ServerSide.AwayFriendsOf;

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo Selector = typeof(PostboxSender).GetField("friendSelector", Any);
        static readonly FieldInfo Docks = typeof(PostboxSender).GetField("sendingDocks", Any);
        static readonly FieldInfo Sending = typeof(PostboxSender).GetField("isSending", Any);
        static readonly MethodInfo Effect = typeof(PostboxSender).GetMethod("PlayEffect", Any);
        static readonly FieldInfo WheelList = typeof(FriendSelector).GetField("friends", Any);
        static readonly FieldInfo WheelSync = typeof(FriendSelector).GetField("syncFilter", Any);
        static readonly FieldInfo Holder = typeof(PostboxSender).GetField("currentPlayerIdentifier", Any);
        static readonly PropertyInfo AllPlayers = typeof(Player).GetProperty("AllPlayers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        internal static bool Start()
        {
            if (!PostboxConfig.Enabled)
            {
                Log.Msg("switched off (postbox_enabled=0): the game's own postbox, which loses what it sends.");
                return false;
            }
            HarmonyLib.Harmony h = new HarmonyLib.Harmony("tavernessentials.postbox");
            if (!Apply(h)) return false;
            Social.ServerSide.FriendsChanged += Refresh;
            Log.Msg("ready: the friend wheel lists your mutual friends here and your friends away, and a send is checked before anything is taken"
                + (Social.Config.Current.Enabled && Social.Config.Current.Identity && Social.Config.Current.Friends
                    ? "." : ". Social's friends are off here, so every send is refused."));
            return true;
        }

        internal static bool StartClient()
        {
            if (!PostboxConfig.Enabled)
            {
                Log.Msg("switched off (postbox_enabled=0): the wheel lists only friends who are here.");
                return false;
            }
            Social.Config social = Social.Config.Current;
            if (!social.Enabled || !social.Identity || !social.Friends)
            {
                Log.Msg("Social's friends are off (" + (!social.Enabled ? "social_enabled=0" : !social.Identity ? "social_identity=0" : "social_friends=0")
                    + "), so there are no friends away to list: Postbox stays off here.");
                return false;
            }
            HarmonyLib.Harmony h = new HarmonyLib.Harmony("tavernessentials.postbox");
            if (!ApplyClient(h)) return false;
            Log.Msg("ready: the friend wheel also lists your friends who are away (on a server with TavernEssentials 0.9.0 or later).");
            return true;
        }

        internal static bool ApplyClient(HarmonyLib.Harmony h)
        {
            MethodInfo read = AccessTools.Method(typeof(FriendSelector), "SyncFilter");
            if (read == null)
            {
                Log.Warn("postbox seam missing: FriendSelector.SyncFilter. The wheel lists only friends who are here.");
                return false;
            }
            h.Patch(read, new HarmonyMethod(typeof(PostboxFeature).GetMethod("ReadPrefix", BindingFlags.Public | BindingFlags.Static)),
                null, null, new HarmonyMethod(typeof(PostboxFeature).GetMethod("ReadFinalizer", BindingFlags.Public | BindingFlags.Static)), null);
            return true;
        }

        public static void ReadPrefix(Alta.Serialization.Stream stream, out List<UserInfo> __state)
        {
            __state = null;
            try
            {
                if (stream == null || stream.IsWriting) return;
                Player me = Player.Current as Player;
                Social.ClientProtocol social = Social.ClientSide.Protocol;
                if (me == null || me.FriendshipManager == null || social == null) return;
                __state = AddAway(me.FriendshipManager.Friends, social.AwayFriends());
            }
            catch (Exception e)
            {
                __state = null;
                if (!_readWarned) Log.Warn("adding friends away to the wheel: " + e.GetType().Name + ": " + e.Message);
                _readWarned = true;
            }
        }

        public static void ReadFinalizer(List<UserInfo> __state)
        {
            if (__state == null || __state.Count == 0) return;
            try
            {
                Player me = Player.Current as Player;
                if (me != null && me.FriendshipManager != null) RemoveAdded(me.FriendshipManager.Friends, __state);
                List<string> names = new List<string>();
                foreach (UserInfo u in __state) names.Add(u.Username);
                Log.Msg("the wheel can list your friends away: " + string.Join(", ", names.ToArray()) + ".");
            }
            catch (Exception e)
            {
                if (!_readWarned) Log.Warn("taking friends away off the list again: " + e.GetType().Name + ": " + e.Message);
                _readWarned = true;
            }
        }

        static bool _readWarned;

        internal static List<UserInfo> AddAway(List<UserInfo> friends, List<Social.AwayFriend> away)
        {
            List<UserInfo> added = new List<UserInfo>();
            if (friends == null || away == null) return added;
            foreach (Social.AwayFriend a in away)
            {
                if (a.Id < 0 || Listed(friends, a.Id)) continue;
                UserInfo u = new UserInfo(a.Id, a.Name);
                friends.Add(u);
                added.Add(u);
            }
            return added;
        }

        internal static void RemoveAdded(List<UserInfo> friends, List<UserInfo> added)
        {
            if (friends == null) return;
            foreach (UserInfo u in added)
                for (int i = friends.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(friends[i], u)) { friends.RemoveAt(i); break; }
        }

        static bool Listed(List<UserInfo> list, int id)
        {
            foreach (UserInfo u in list)
                if (u != null && u.Identifier == id) return true;
            return false;
        }

        internal static bool Apply(HarmonyLib.Harmony h)
        {
            MethodInfo send = AccessTools.Method(typeof(PostboxSender), "Send", new Type[] { typeof(int), typeof(int) });
            MethodInfo setup = AccessTools.Method(typeof(PostboxSender), "SetupForPlayer", new Type[] { typeof(IPlayer) });
            string missing = send == null ? "PostboxSender.Send" : setup == null ? "PostboxSender.SetupForPlayer"
                : Selector == null ? "PostboxSender.friendSelector" : Docks == null ? "PostboxSender.sendingDocks"
                : Sending == null ? "PostboxSender.isSending" : WheelList == null ? "FriendSelector.friends"
                : WheelSync == null ? "FriendSelector.syncFilter" : Holder == null ? "PostboxSender.currentPlayerIdentifier"
                : AllPlayers == null ? "Player.AllPlayers" : null;
            if (missing != null)
            {
                Log.Warn("postbox seam missing: " + missing + ". The postbox is the game's own (it loses what it sends).");
                return false;
            }
            h.Patch(send, new HarmonyMethod(typeof(PostboxFeature).GetMethod("SendPrefix", BindingFlags.Public | BindingFlags.Static)));
            h.Patch(setup, null, new HarmonyMethod(typeof(PostboxFeature).GetMethod("SetupPostfix", BindingFlags.Public | BindingFlags.Static)));
            return true;
        }

        internal static void Refresh(int loginId)
        {
            IPlayer player = PlayerOf(loginId);
            if (player == null) return;
            int boxes = 0;
            string last = null;
            foreach (PostboxSender box in UnityEngine.Object.FindObjectsOfType<PostboxSender>())
            {
                int holder = (int)Holder.GetValue(box);
                if (holder > 0 && holder != loginId) continue;
                FriendSelector selector = Selector.GetValue(box) as FriendSelector;
                if (selector == null) continue;
                last = FillWheel(selector, loginId, player);
                boxes++;
            }
            if (boxes > 0) Log.Msg(NameOf(player) + "'s wheel on " + boxes + " postbox(es): " + last);
        }

        static IPlayer PlayerOf(int loginId)
        {
            IEnumerable all = AllPlayers.GetValue(null, null) as IEnumerable;
            if (all == null) return null;
            foreach (object o in all)
            {
                IPlayer p = o as IPlayer;
                if (p != null && LoginOf(p) == loginId) return p;
            }
            return null;
        }

        static string NameOf(IPlayer player)
        {
            return player == null || player.UserInfo == null ? "?" : player.UserInfo.Username;
        }

        public static void SetupPostfix(PostboxSender __instance, IPlayer player)
        {
            try
            {
                string who = player == null || player.UserInfo == null ? "?" : player.UserInfo.Username;
                Log.Msg(who + " opened a postbox: " + FillWheel((FriendSelector)Selector.GetValue(__instance), LoginOf(player), player));
            }
            catch (Exception e)
            {
                Log.Warn("filling the friend wheel: " + e.GetType().Name + ": " + e.Message);
            }
        }

        internal static string FillWheel(FriendSelector selector, int loginId, IPlayer player)
        {
            List<UserInfo> wheel = (List<UserInfo>)WheelList.GetValue(selector);
            if (wheel == null) WheelList.SetValue(selector, wheel = new List<UserInfo>());
            wheel.Clear();

            string why = "their in-game id is not their login";
            List<KeyValuePair<int, string>> friends = loginId < 0 ? null : FriendsOf(loginId, out why);
            List<string> names = new List<string>();
            if (friends != null)
                foreach (KeyValuePair<int, string> f in friends)
                {
                    if (f.Key < 0 || f.Value == null) continue;
                    wheel.Add(new UserInfo(f.Key, f.Value));
                    names.Add(f.Value);
                }
            string whyAway;
            List<KeyValuePair<int, string>> away = friends == null || AwayOf == null ? null : AwayOf(loginId, out whyAway);
            if (away != null)
                foreach (KeyValuePair<int, string> f in away)
                {
                    if (f.Key < 0 || f.Value == null || Listed(wheel, f.Key)) continue;
                    wheel.Add(new UserInfo(f.Key, f.Value));
                    names.Add(f.Value + " (away)");
                }

            MethodSync sync = WheelSync.GetValue(selector) as MethodSync;
            if (sync != null && player != null) sync.SendToPlayer(player);

            if (friends == null) return "no friends to list (" + why + ").";
            if (names.Count == 0) return "no friends of theirs are here or away.";
            return "the wheel lists " + string.Join(", ", names.ToArray()) + "." + (sync == null || player == null ? " (not sent: no syncFilter or player)" : "");
        }

        public static bool SendPrefix(PostboxSender __instance, int from, int to)
        {
            try
            {
                if ((bool)Sending.GetValue(__instance)) return true;
                string fromName, toName;
                bool away;
                string why = Check(from, to, out fromName, out toName, out away);
                int items = Items(__instance);
                if (why != null)
                {
                    if (fromName == null && !Names.NamesFeature.Resolve(from, out fromName)) fromName = "id " + from;
                    Log.Msg("refused a send from " + fromName + " to id " + to + " (" + items
                        + " item(s)): " + why + ". Nothing was taken.");
                    Reject(__instance);
                    return false;
                }
                int left = PostBoxSenderManager.instance == null ? -1 : PostBoxSenderManager.instance.GetAvailableQuantity(from, to);
                if (left == 0)
                    Log.Msg(fromName + " has no sends left to " + toName + " (the game allows "
                        + PostBoxSenderManager.instance.GiftsPerPlayer + " per friend); the game keeps the items in the box.");
                else
                    Log.Msg(fromName + " sends " + Math.Min(items, left < 0 ? items : left) + " item(s) to " + toName
                        + (away ? ", who is away (it waits in their postbox)." : "."));
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("a send was stopped: the check failed (" + e.GetType().Name + ": " + e.Message + "). Nothing was taken.");
                return false;
            }
        }

        internal static string Check(int from, int to, out string fromName, out string toName, out bool away)
        {
            fromName = toName = null;
            away = false;
            if (to < 0 || to == from) return "no friend was picked";
            string why;
            List<KeyValuePair<int, string>> friends = FriendsOf(from, out why);
            if (friends == null) return "the sender " + why;
            foreach (KeyValuePair<int, string> f in friends)
                if (f.Key == to) toName = f.Value;
            if (toName == null)
            {
                List<KeyValuePair<int, string>> gone = AwayOf == null ? null : AwayOf(from, out why);
                if (gone != null)
                    foreach (KeyValuePair<int, string> f in gone)
                        if (f.Key == to) toName = f.Value;
                if (toName == null) return "id " + to + " is not one of their friends here or away";
                away = true;
                string mismatch = Names.NamesFeature.AccountMismatch(toName.ToLowerInvariant(), to);
                if (mismatch != null) return mismatch;
            }
            if (!Names.NamesFeature.Resolve(from, out fromName) || fromName == null) return "the sender's name is not known";
            Names.NamesFeature.Remember(from, fromName);
            Names.NamesFeature.Remember(to, toName);
            return null;
        }

        static int Items(PostboxSender box)
        {
            PickupDock[] docks = Docks.GetValue(box) as PickupDock[];
            int n = 0;
            if (docks != null)
                foreach (PickupDock d in docks)
                    if (d != null && d.QuantityStored > 0) n++;
            return n;
        }

        static void Reject(PostboxSender box)
        {
            try
            {
                if (Effect == null) return;
                Type kind = Effect.GetParameters()[0].ParameterType;
                Effect.Invoke(box, new object[] { Enum.Parse(kind, "Reject") });
            }
            catch { }
        }

        static int LoginOf(IPlayer player)
        {
            Player p = player as Player;
            if (p == null || p.UserInfo == null) return -1;
            Connection c = p.ConnectionToRemotePlayer;
            if (c == null || c.UserInfo == null || c.UserInfo.Identifier != p.UserInfo.Identifier) return -1;
            return c.UserInfo.Identifier;
        }
    }

    internal static class Log
    {
        internal static readonly FeatureLog Out = new FeatureLog();

        internal static void Msg(string s) { Out.Msg(s); }
        internal static void Warn(string s) { Out.Warn(s); }
        internal static void Error(string s) { Out.Error(s); }
    }

    internal static class PostboxConfig
    {
        internal static bool Enabled = true;

        internal static readonly string[] Template =
        {
            "# ---- Postbox: send your friends items by mail. Needs social_friends=1.",
            "",
            "# 1 = the friend wheel lists your mutual friends on this server and your friends away",
            "#     (players this server has seen before), and a send is checked before the box takes",
            "#     anything. On a game client: the wheel can list your friends away. 0 = the game's",
            "#     own postbox, which loses what it sends.",
            "postbox_enabled=1",
        };

        internal static void Load(Settings s)
        {
            Enabled = s.Bool("postbox_enabled", Enabled);
        }
    }
}
