namespace TavernEssentials.Social
{
    internal sealed class Config
    {
        internal bool Enabled = true;
        internal bool Identity = true;
        internal bool Friends = true;
        internal bool Lists = true;
        internal bool Recent = true;
        internal bool Reports = true;
        internal bool RecentTest = false;
        internal bool ExportKey = false;
        internal bool Verbose = false;

        internal static Config Current = new Config();

        internal const string Prefix = "social_";

        internal static readonly string[] Template =
        {
            "# ---- Social: cross-server friends. The same lines work on a game client and a server.",
            "",
            "# Master switch for Social. 0 = it loads and does nothing.",
            "social_enabled=1",
            "",
            "# The identity check: your game proves its key to each server running Social, and a server",
            "# checks the key of each player who joins with Social. Everything else in Social needs it,",
            "# so 0 = Social does nothing, the same as social_enabled=0.",
            "social_identity=1",
            "",
            "# Friends. On a server: witness friend cards, hand both players a signed record, and",
            "# recognise friends who show theirs. On a game client: co-sign the cards you pass,",
            "# keep the records, and show them to servers you join. 0 = none of it.",
            "social_friends=1",
            "",
            "# Game client only, with friends on. Your friends in the game's own friends lists: the",
            "# main menu's list shows them. Unfriend (and the tablet's Remove, and Decline in the requests",
            "# list) removes a friend: the record moves to friends\\removed\\ (nothing is deleted), and",
            "# their game drops you on servers you both visit that run TavernEssentials 0.13.0 or later.",
            "# The requests list shows friends an older version archived, and Accept brings one back. A",
            "# friend recognised on the server you are on also counts for friends-only voice. (The map's",
            "# friend icons are the server's, under its social_friends.) 0 = the lists stay empty and",
            "# friends-only voice counts nobody, as the",
            "# game has them offline.",
            "social_lists=1",
            "",
            "# Game client only. The Social Tablet's Recent Players page and the main menu's Recent",
            "# Players tab: the players you meet are kept in %APPDATA%\\TheModdingTavern\\tavernsocial\\recent.txt",
            "# (names, the server's name, when; never player ids) and both lists fill at once. Reports",
            "# from the tablet go to the server you are on. 0 = the game asks Alta's dead web API.",
            "social_recent=1",
            "",
            "# Game client only, for trying a report alone: 1 = the Recent Players page also lists a",
            "# pretend player, Tester, as met on each server you join. Reporting them reaches the",
            "# server, which logs it against \"unknown\". Leave it at 0.",
            "social_recent_test=0",
            "",
            "# Server only. A report from a player's Social Tablet fires the console event",
            "# SocialTabletPlayerReported, the reporter named by their login. 0 = left to the game,",
            "# which waits on Alta's dead web API and never fires it.",
            "social_reports=1",
            "",
            "# Game client only. 1 = keep a copy of your key in Documents\\TavernSocial\\.",
            "# Your key is who you are to your friends: lose it and they lose you. If your",
            "# Documents folder syncs to a cloud drive, the copy goes there too.",
            "social_export_key=0",
            "",
            "# 1 = log every step of the identity check.",
            "social_verbose=0",
        };

        internal static Config From(Settings s)
        {
            Config c = new Config();
            c.Enabled = s.Bool("social_enabled", c.Enabled);
            c.Identity = s.Bool("social_identity", c.Identity);
            c.Friends = s.Bool("social_friends", c.Friends);
            c.Lists = s.Bool("social_lists", c.Lists);
            c.Recent = s.Bool("social_recent", c.Recent);
            c.Reports = s.Bool("social_reports", c.Reports);
            c.RecentTest = s.Bool("social_recent_test", c.RecentTest);
            c.ExportKey = s.Bool("social_export_key", c.ExportKey);
            c.Verbose = s.Bool("social_verbose", c.Verbose);
            return c;
        }
    }
}
