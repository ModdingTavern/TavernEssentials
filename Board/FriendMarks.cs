using System;
using System.IO;
using System.Reflection;
using Alta.Api.DataTransferModels.Models.Responses;
using HarmonyLib;

namespace TavernEssentials.Board
{
    internal static class FriendMarks
    {
        static FriendServers _store;
        static bool _rowWarned;
        static int _pending;
        static readonly FieldInfo NameText = typeof(ServerElement).GetField("serverNameText",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        internal static bool Start(HarmonyLib.Harmony h)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TheModdingTavern");
            _store = new FriendServers(Path.Combine(root, FriendServers.FileName));
            ServerNames.FriendsWork = Said;
            GameModeManager.ModeChanged += OnModeChanged;
            return Apply(h);
        }

        internal static bool Apply(HarmonyLib.Harmony h)
        {
            MethodInfo setup = AccessTools.Method(typeof(ServerElement), "SetupForServer", new Type[] { typeof(GameServerInfo) });
            if (setup == null || NameText == null || NameText.FieldType != typeof(TextRenderer))
            {
                Log.Warn("marks seam missing: " + (setup == null ? "ServerElement.SetupForServer" : "ServerElement.serverNameText")
                    + ". The info board still says TE friends; the rows show no *.");
                return false;
            }
            h.Patch(setup, null, new HarmonyMethod(typeof(FriendMarks).GetMethod("SetupPostfix", BindingFlags.Public | BindingFlags.Static)));
            return true;
        }

        internal static bool Marked(string address)
        {
            FriendServers store = _store;
            return store != null && store.Marked(address);
        }

        static void Said(bool friendsWork)
        {
            FriendServers store = _store;
            if (store == null) return;
            string address = MyServers.AddressHere();
            if (address == null && Alta.Networking.JoinedServerInfo.Current != null && !(GameModeManager.CurrentMode is JoinedServerGameMode))
            {
                _pending = friendsWork ? 1 : -1;
                return;
            }
            Mark(store, address, friendsWork);
        }

        static void OnModeChanged()
        {
            try
            {
                int pending = _pending;
                _pending = 0;
                FriendServers store = _store;
                if (pending == 0 || store == null || !(GameModeManager.CurrentMode is JoinedServerGameMode)) return;
                Mark(store, MyServers.AddressHere(), pending > 0);
            }
            catch (Exception e)
            {
                Log.Warn("marks, on join: " + e.Message);
            }
        }

        static void Mark(FriendServers store, string address, bool friendsWork)
        {
            string line = store.Said(address, MyServers.NameHere(), friendsWork);
            if (line != null) Log.Msg(line);
        }

        public static void SetupPostfix(ServerElement __instance, GameServerInfo server)
        {
            try
            {
                if (server == null || server.Name == null) return;
                CommunityList.Snapshot snap = CommunityList.Current;
                CommunityList.Entry e = snap == null ? null : snap.ById(server.Identifier);
                string shown = FriendServers.RowName(server.Name, e != null && Marked(e.Address));
                if (shown == server.Name) return;
                TextRenderer text = NameText.GetValue(__instance) as TextRenderer;
                if (text != null) text.Text = shown;
            }
            catch (Exception ex)
            {
                if (!_rowWarned) Log.Warn("marking a board row: " + ex.GetType().Name + ": " + ex.Message);
                _rowWarned = true;
            }
        }
    }
}
