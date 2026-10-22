using System;

namespace TavernEssentials.Board
{
    internal static class BoardFeature
    {
        internal static bool Start()
        {
            if (!BoardConfig.Enabled)
            {
                Log.Msg("switched off (board_enabled=0).");
                return false;
            }

            HarmonyLib.Harmony h = new HarmonyLib.Harmony("tavernessentials.board");
            int patched = BoardPatches.Apply(h);
            if (patched == 0)
            {
                Log.Warn("no board seams found - game code moved? Board inert, fetch not started.");
                return false;
            }

            bool joinPatched = BoardConfig.JoinAuth && JoinPatch.Apply(h);

            bool leaveFixed = BoardConfig.LeaveOrb && LeaveFix.Apply(h);

            bool reasons = BoardConfig.JoinReasons && JoinRefusals.Apply(h);

            bool pagePatched = false;
            if (BoardConfig.MyServers)
            {
                MyServers.Start();
                pagePatched = ServersPanel.Apply(h);
            }

            bool marked = BoardConfig.Marks && FriendMarks.Start(h);

            CommunityList.Start();
            ServerNames.Listed = MyServers.NameHere;
            ServerNames.ListedLimit = MyServers.LimitHere;
            Log.Msg("ready: " + patched + "/8 list seams patched, join-auth "
                + (BoardConfig.JoinAuth ? (joinPatched ? "on" : "FAILED") : "off") + ", leave-fix "
                + (BoardConfig.LeaveOrb ? (leaveFixed ? "on" : "FAILED") : "off") + ", join reasons "
                + (BoardConfig.JoinReasons ? (reasons ? "on" : "FAILED") : "off") + ", my-servers "
                + (BoardConfig.MyServers ? (pagePatched ? "on" : "on (Servers page FAILED)") : "off") + ", marks "
                + (BoardConfig.Marks ? (marked ? "on" : "on (rows FAILED)") : "off") + ", list = "
                + BoardConfig.Url + " every " + BoardConfig.RefreshSeconds + "s.");
            return true;
        }

        static float _next;

        internal static void Tick()
        {
            if (!BoardConfig.MyServers) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 1f;
            ServersPanel.Ensure();
        }
    }

    internal static class Log
    {
        internal static readonly FeatureLog Out = new FeatureLog();

        internal static void Msg(string s) { Out.Msg(s); }
        internal static void Warn(string s) { Out.Warn(s); }
        internal static void Error(string s) { Out.Error(s); }
        internal static void Detail(string s) { Out.Detail(s); }
    }

    public static class BoardConfig
    {
        const string DefaultUrl = "http://themoddingtavern.com:1763/servers";

        public static bool Enabled = true;
        public static string Url = DefaultUrl;
        public static int RefreshSeconds = 30;
        public static bool Verbose = false;
        public static bool JoinAuth = true;
        public static bool LeaveOrb = true;
        public static bool QuestScene = true;
        public static bool MyServers = true;
        public static bool JoinReasons = true;
        public static bool Marks = true;

        internal const string Prefix = "board_";

        internal static readonly string[] Template =
        {
            "# ---- Board: the main-menu server boards. Game client only; a server keeps it off.",
            "",
            "# Master switch for Board. 0 = it loads and does nothing.",
            "board_enabled=1",
            "",
            "# The community server list endpoint.",
            "board_url=" + DefaultUrl,
            "",
            "# Seconds between list refreshes (minimum 10; the boards poll every 30).",
            "board_refresh_seconds=30",
            "",
            "# 1 = log every refresh and skip decision.",
            "board_verbose=0",
            "",
            "# Authenticate board joins per-server (port-1762 handshake, mints",
            "# the identity JWT the server expects). 0 = joins stay vanilla.",
            "board_join_auth=1",
            "",
            "# Make the wheel's native leave orb return to the MAIN MENU (boards",
            "# refill, join again from there). 0 = launcher behaviour: eating the",
            "# leave orb quits the game.",
            "board_leave_orb=1",
            "",
            "# Load the scene a server runs: the quest overworld or the tutorial scene",
            "# on servers that ask for one (the launcher does this with /questScene or",
            "# /tutorialScene; we read the same auth fields at join). Needs board_join_auth=1.",
            "# 0 = leave the scene alone: the standard overworld, unless the launcher",
            "# started this game for a quest or tutorial server.",
            "board_quest_scene=1",
            "",
            "# My Servers board and the main menu's Servers page = the servers you have",
            "# joined (%APPDATA%\\TheModdingTavern\\joined_servers.txt); Leave server on the",
            "# Servers page takes one off. 0 = My Servers shows every server, like Discover.",
            "board_my_servers=1",
            "",
            "# 1 = when a server turns your join away, the loading screen shows its reason instead of",
            "# \"Successfully connected to server.\", and the second refusal in a row for a reason that",
            "# waiting will not clear takes you back to the main menu, not 24 tries over two minutes.",
            "# 0 = the game's own line and tries.",
            "board_join_reasons=1",
            "",
            "# 1 = mark the servers where your game found TavernEssentials' friends working: a * after",
            "# the name on the boards and \"TE friends\" on the info board, from your own visits",
            "# (%APPDATA%\\TheModdingTavern\\friend_servers.txt). Nothing is sent anywhere. 0 = no marks.",
            "board_marks=1",
        };

        internal static void Load(Settings s)
        {
            Enabled = s.Bool("board_enabled", true);
            Url = s.Text("board_url", DefaultUrl);
            RefreshSeconds = s.Number("board_refresh_seconds", 30);
            if (RefreshSeconds < 10) RefreshSeconds = 10;
            Verbose = s.Bool("board_verbose", false);
            JoinAuth = s.Bool("board_join_auth", true);
            LeaveOrb = s.Bool("board_leave_orb", true);
            QuestScene = s.Bool("board_quest_scene", true);
            MyServers = s.Bool("board_my_servers", true);
            JoinReasons = s.Bool("board_join_reasons", true);
            Marks = s.Bool("board_marks", true);
            Log.Out.Verbose = Verbose;
        }
    }
}
