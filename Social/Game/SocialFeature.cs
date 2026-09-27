using System;
using System.IO;
using System.Reflection;
using Alta.Meta.UI.ServerManagement;
using Alta.Networking;
using Alta.Networking.Servers;
using HarmonyLib;

namespace TavernEssentials.Social
{
    internal static class SocialFeature
    {
        static bool _server;

        internal static bool Start(string gameDir, bool server)
        {
            Log.Out.Verbose = Config.Current.Verbose;
            _server = server;
            string side = _server ? "server" : "client";
            if (!Config.Current.Enabled)
            {
                Log.Msg("switched off (social_enabled=0).");
                return false;
            }
            if (!Config.Current.Identity)
            {
                Log.Msg("the identity check is off (social_identity=0), so there is nothing to do.");
                return false;
            }

            if (_server)
            {
                string dir = Path.Combine(Path.Combine(gameDir, "UserData"), "TavernSocial");
                ServerSide.Boot(Path.Combine(dir, KeyStore.ServerFile));
                Log.Msg(Mod.Name + " " + Mod.Version + " (" + side + ") starting: loading the server key.");
                if (!Config.Current.Reports) Log.Msg("tablet reports: off (social_reports=0).");
                else if (TabletServer.Apply(new HarmonyLib.Harmony("tavernessentials.tablet")))
                    Log.Msg("tablet reports: ready: a report from a player's tablet fires SocialTabletPlayerReported, the reporter named by their login.");
            }
            else
            {
                int patched = PatchClient();
                if (patched == 0)
                {
                    Log.Error("0/" + ClientSeams() + " seams patched: the game's connection setup was not found. Social is doing nothing.");
                    return false;
                }
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string identity = Path.Combine(Path.Combine(Path.Combine(appData, "TheModdingTavern"), "tavernsocial"), KeyStore.IdentityFile);
                string export = null;
                if (Config.Current.ExportKey)
                    export = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TavernSocial"),
                        "identity-backup.priv.xml");
                ClientSide.Boot(identity, export);
                if (Config.Current.Recent)
                {
                    int t = Tablet.Apply(new HarmonyLib.Harmony("tavernessentials.tablet"), Path.GetDirectoryName(identity));
                    Log.Msg("tablet: " + t + "/" + Tablet.Seams + " seams patched: recent players, the tablet's pages and the main menu's tab answer at once"
                        + (GameLists.On ? "." : "; with social_lists=0 the tablet's first page still waits on Alta."));
                }
                Log.Msg(Mod.Name + " " + Mod.Version + " (" + side + ") starting: " + patched + "/" + ClientSeams()
                    + " seams patched, loading your identity" + (GameLists.On ? "; the game's friends lists come from your records." : "."));
            }
            return true;
        }

        static int ClientSeams()
        {
            return 1 + (Config.Current.Friends ? 1 : 0) + (Config.Current.Friends && Config.Current.Lists ? 1 : 0);
        }

        internal static int PatchClient()
        {
            MethodInfo target = AccessTools.Method(typeof(ConnectToServerUtility), "SetupConnectionToServer", new Type[] { typeof(Connection) });
            if (target == null)
            {
                Log.Warn("seam missing: ConnectToServerUtility.SetupConnectionToServer(Connection)");
                return 0;
            }
            HarmonyLib.Harmony h = new HarmonyLib.Harmony("tavernessentials.social");
            try
            {
                MethodInfo postfix = typeof(SocialFeature).GetMethod("ConnectionPostfix", BindingFlags.NonPublic | BindingFlags.Static);
                h.Patch(target, null, new HarmonyMethod(postfix));
            }
            catch (Exception e)
            {
                Log.Warn("could not patch ConnectToServerUtility.SetupConnectionToServer: " + e.Message);
                return 0;
            }
            int patched = 1;
            if (Config.Current.Friends && NameBoards.Apply(h)) patched++;
            if (!(Config.Current.Friends && Config.Current.Lists)) return patched;

            MethodInfo refresh = AccessTools.Method(typeof(FriendsRequestsPanel), "RefreshData", Type.EmptyTypes);
            if (refresh == null)
            {
                Log.Warn("seam missing: FriendsRequestsPanel.RefreshData(). Archived friends will not show in the main menu's requests list.");
                return patched;
            }
            try
            {
                h.Patch(refresh,
                    new HarmonyMethod(typeof(SocialFeature).GetMethod("RequestsPrefix", BindingFlags.NonPublic | BindingFlags.Static)),
                    new HarmonyMethod(typeof(SocialFeature).GetMethod("RequestsPostfix", BindingFlags.NonPublic | BindingFlags.Static)));
                return patched + 1;
            }
            catch (Exception e)
            {
                Log.Warn("could not patch FriendsRequestsPanel.RefreshData: " + e.Message + ". Archived friends will not show in the main menu's requests list.");
                return patched;
            }
        }

        static void ConnectionPostfix(Connection connection)
        {
            try { ClientSide.OnConnection(connection); }
            catch (Exception e) { Log.Error("new connection: " + e); }
        }

        static void RequestsPrefix()
        {
            GameLists.RequestsAsking = true;
        }

        static void RequestsPostfix()
        {
            GameLists.RequestsAsking = false;
        }

        internal static void Tick()
        {
            Worker.Pump();
            if (_server) ServerSide.Tick();
            else ClientSide.Tick();
        }
    }
}
