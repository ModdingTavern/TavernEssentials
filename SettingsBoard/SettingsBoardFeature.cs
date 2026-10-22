#pragma warning disable 618
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Alta.Menu;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Networking.Servers;
using Alta.QuickAccessActions;
using Alta.Utilities;
using ATT.Character.QuickAccessMenu;
using HarmonyLib;

namespace TavernEssentials.SettingsBoard
{
    internal static class SettingsBoardFeature
    {
        static readonly Dictionary<Connection, ServerSettings> Shown = new Dictionary<Connection, ServerSettings>();
        const int MaxShown = 100;

        static FieldInfo _syncField;
        static MethodInfo _sendToPlayer;
        static MethodInfo _menuSetter;
        static FieldInfo _commandField;
        static PropertyInfo[] _fields;

        internal static bool StartServer()
        {
            Type sync = typeof(ServerSettingsSync);
            MethodInfo save = AccessTools.Method(sync, "SaveServerSettingsFromClient");
            MethodInfo request = AccessTools.Method(sync, "RequestSettings", new Type[] { typeof(IPlayer), typeof(Alta.Serialization.Stream) });
            MethodInfo init = AccessTools.Method(sync, "Initialize");
            MethodInfo update = AccessTools.Method(sync, "UpdateMenuSettingsValue");
            MethodInfo command = AccessTools.Method(typeof(CommandSync), "SyncCommand");
            _syncField = AccessTools.Field(sync, "syncSettings");
            _menuSetter = AccessTools.PropertySetter(sync, "IsMenuAvailable");
            _commandField = AccessTools.Field(typeof(CommandSync), "commandToSync");
            if (save == null)
            {
                Log.Error("seam missing: ServerSettingsSync.SaveServerSettingsFromClient. Any player's game can still change this server's settings.");
                return false;
            }
            HarmonyLib.Harmony h = new HarmonyLib.Harmony("tavernessentials.settingsboard");
            h.Patch(save, new HarmonyMethod(Hook("SavePrefix")));
            Log.Msg(RolesApi() != null
                ? "roles: from TavernLib's roles API; a role given or taken in users.json counts within a second, no rejoin."
                : "roles: from TavernLib's accounts (this TavernLib has no roles API); a role given or taken counts at the player's next join.");
            if (init == null || update == null || _menuSetter == null)
            {
                Log.Warn("seam missing: ServerSettingsSync.Initialize, UpdateMenuSettingsValue or IsMenuAvailable. The guard is on; the board stays off.");
                return true;
            }
            h.Patch(init, null, new HarmonyMethod(Hook("InitPostfix")));
            h.Patch(update, new HarmonyMethod(Hook("UpdatePrefix")));
            if (!SettingsBoardConfig.On)
            {
                Log.Msg("guard ready: only moderators, owners and admins can change this server's settings from a game. Board switched off (settings_board=0).");
                return true;
            }
            if (request == null || _syncField == null)
            {
                SettingsBoardConfig.On = false;
                Log.Warn("seam missing: ServerSettingsSync.RequestSettings or syncSettings. The guard is on; the board stays off.");
                return true;
            }
            h.Patch(request, new HarmonyMethod(Hook("RequestPrefix")));
            if (command != null && _commandField != null) h.Patch(command, null, new HarmonyMethod(Hook("CommandPostfix")));
            Log.Msg("ready: moderators, owners and admins (users.json roles) get the game's server settings board; only they can change this server's settings from a game.");
            return true;
        }

        static MethodInfo Hook(string name)
        {
            return typeof(SettingsBoardFeature).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
        }

        public static bool SavePrefix(IPlayer player, ServerSettings settings)
        {
            string name = "(unknown login)";
            try
            {
                Connection c;
                bool staff = Staff(player, out name, out c);
                if (!staff)
                {
                    RefusedWrites.Warn("refused a server settings change from " + name + ": not moderator, owner or admin in users.json.");
                    return false;
                }
                if (settings == null) return false;
                ServerSettings now = ServerHandler.Current.ServerConfig.Settings;
                ServerSettings sent;
                if (c == null || !Shown.TryGetValue(c, out sent)) sent = Travel(now);
                else Shown.Remove(c);
                List<string> refused = new List<string>();
                List<Change> changes = BoardRules.Changes(Values(sent), Values(settings), Values(now), refused);
                foreach (string f in refused)
                    Log.Warn(name + " tried to change " + f + " from the board; that one is not the board's, so it stays as it is.");
                ServerSettings next = (ServerSettings)((ICloneable)now).Clone();
                List<Change> applied = new List<Change>();
                foreach (Change ch in changes)
                {
                    PropertyInfo p = Field(ch.Field);
                    object v = p == null ? null : Filtered(p, ch.Now);
                    if (v == null)
                    {
                        Log.Warn(name + "'s " + ch.Field + " = " + BoardRules.Show(ch.Now) + " could not be checked, so it stays " + BoardRules.Show(ch.Was) + ".");
                        continue;
                    }
                    p.SetValue(next, v, null);
                    applied.Add(new Change(ch.Field, ch.Was, v));
                }
                if (applied.Count == 0)
                {
                    Log.Msg(name + " closed the settings board; nothing changed.");
                    return false;
                }
                ServerSettingsChangeLogger.LogServerSettingsChange(now, next);
                ServerHandler.Current.ServerConfig.SetAllSettings(next);
                foreach (Change ch in applied)
                    Log.Msg(name + " changed " + ch.Field + ": " + BoardRules.Show(ch.Was) + " -> " + BoardRules.Show(ch.Now) + ".");
            }
            catch (Exception e)
            {
                Log.Warn("a server settings change from " + name + " was not applied: " + e.GetType().Name + ": " + e.Message);
            }
            return false;
        }

        public static bool RequestPrefix(ServerSettingsSync __instance, IPlayer player)
        {
            string name = "(unknown login)";
            try
            {
                Connection c;
                if (!Staff(player, out name, out c))
                {
                    RefusedRequests.Warn("did not send the server settings to " + name + ": not moderator, owner or admin in users.json.");
                    return false;
                }
                ServerSettings now = ServerHandler.Current.ServerConfig.Settings;
                object sync = _syncField.GetValue(__instance);
                if (_sendToPlayer == null && sync != null)
                    _sendToPlayer = sync.GetType().GetMethod("SendToPlayer", new Type[] { typeof(IPlayer), typeof(ServerSettings) });
                if (_sendToPlayer == null)
                {
                    Log.Warn("could not send the server settings to " + name + ": the sync's SendToPlayer was not found.");
                    return false;
                }
                if (c != null)
                {
                    if (Shown.Count >= MaxShown) Shown.Clear();
                    Shown[c] = Travel(now);
                }
                _sendToPlayer.Invoke(sync, new object[] { player, now });
                if (!_firstAnswer)
                {
                    _firstAnswer = true;
                    Log.Msg("sent the server settings to " + name + " (first board this run; " + CommandRows() + ").");
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not send the server settings to " + name + ": " + e.GetType().Name + ": " + e.Message);
            }
            return false;
        }

        static bool _firstAnswer;
        static readonly Quiet RefusedWrites = new Quiet();
        static readonly Quiet RefusedRequests = new Quiet();

        public static void InitPostfix(ServerSettingsSync __instance)
        {
            try
            {
                _menuSetter.Invoke(__instance, new object[] { SettingsBoardConfig.On });
            }
            catch (Exception e)
            {
                Log.Warn("could not set the board's availability: " + e.GetType().Name + ": " + e.Message);
            }
        }

        public static bool UpdatePrefix()
        {
            return false;
        }

        static double _lastCommand;

        public static void CommandPostfix(CommandSync __instance, IPlayer player)
        {
            try
            {
                double t = Quiet.Now;
                if (t - _lastCommand < 2) return;
                _lastCommand = t;
                string name;
                Connection c;
                Staff(player, out name, out c);
                Log.Msg(name + " pressed a board command row (" + _commandField.GetValue(__instance)
                    + "); this version runs none of them.");
            }
            catch (Exception) { }
        }

        static string CommandRows()
        {
            try
            {
                int n = UnityEngine.Resources.FindObjectsOfTypeAll<ServerCommandDefinitionBase>().Length;
                return n + " command row assets loaded here";
            }
            catch (Exception e)
            {
                return "command rows not counted: " + e.GetType().Name;
            }
        }

        static ServerSettings Travel(ServerSettings s)
        {
            return SerializableServerSettings.FromSettings(s).ToSettings();
        }

        static PropertyInfo[] Fields()
        {
            if (_fields == null)
            {
                List<PropertyInfo> all = new List<PropertyInfo>();
                foreach (PropertyInfo p in typeof(ServerSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0) all.Add(p);
                _fields = all.ToArray();
            }
            return _fields;
        }

        static PropertyInfo Field(string name)
        {
            foreach (PropertyInfo p in Fields())
                if (p.Name == name) return p;
            return null;
        }

        static Dictionary<string, object> Values(ServerSettings s)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            foreach (PropertyInfo p in Fields()) d[p.Name] = p.GetValue(s, null);
            return d;
        }

        static object Filtered(PropertyInfo p, object v)
        {
            try
            {
                object[] attrs = p.GetCustomAttributes(typeof(JsonPropertyFilterAttribute), true);
                if (attrs.Length == 0) return v;
                JsonPropertyFilterAttribute f = (JsonPropertyFilterAttribute)attrs[0];
                if (v is float) return f.FilterValue<float>((float)v);
                if (v is int) return f.FilterValue<int>((int)v);
                return v;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static bool Staff(IPlayer player, out string name, out Connection c)
        {
            name = "(unknown login)";
            c = null;
            Player p = player as Player;
            if (p == null) return false;
            c = p.ConnectionToRemotePlayer;
            if (c == null || c.UserInfo == null || string.IsNullOrEmpty(c.UserInfo.Username)) return false;
            name = "\"" + c.UserInfo.Username + "\"";
            return BoardRules.IsStaff(ServerRoles(c.UserInfo.Username));
        }

        static MethodInfo _manager;
        static MethodInfo _rolesApi;
        static bool _rolesApiChecked;

        internal static MethodInfo RolesApi()
        {
            if (!_rolesApiChecked)
            {
                _rolesApiChecked = true;
                Type roles = AccessTools.TypeByName("TavernLib.ModdingApi.Roles");
                _rolesApi = roles == null ? null : roles.GetMethod("GetRoles", BindingFlags.Public | BindingFlags.Static,
                    null, new Type[] { typeof(string) }, null);
            }
            return _rolesApi;
        }

        static IEnumerable<string> ServerRoles(string login)
        {
            try
            {
                MethodInfo api = RolesApi();
                if (api != null) return Strings(api.Invoke(null, new object[] { login }));
                if (_manager == null)
                {
                    Type services = AccessTools.TypeByName("TavernLib.Services.TavernServices");
                    Type manager = AccessTools.TypeByName("TavernLib.Backend.Api.TavernManager");
                    MethodInfo get = services == null ? null : services.GetMethod("GetService", BindingFlags.Public | BindingFlags.Static);
                    if (get == null || manager == null)
                    {
                        Log.Warn("TavernLib's accounts were not found, so nobody counts as staff.");
                        return null;
                    }
                    _manager = get.MakeGenericMethod(manager);
                }
                object users = Member(_manager.Invoke(null, null), "UserConfig");
                if (users == null) return null;
                MethodInfo getUser = users.GetType().GetMethod("GetUser", new Type[] { typeof(string) });
                object user = getUser == null ? null : getUser.Invoke(users, new object[] { login });
                return Strings(Member(user, "Roles"));
            }
            catch (Exception e)
            {
                Exception x = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                Log.Warn("TavernLib's roles for \"" + login + "\" could not be read: " + x.GetType().Name + ": " + x.Message);
                return null;
            }
        }

        static FieldInfo _ignoredForGood;
        static MethodInfo _handler;
        static bool _handlerMissing;
        static int _shown = -1;
        static bool _rowsLogged;

        internal static bool StartClient()
        {
            if (!SettingsBoardConfig.On)
            {
                Log.Msg("switched off (settings_board=0): the server settings bubble stays hidden.");
                return false;
            }
            MethodInfo ignored = AccessTools.PropertyGetter(typeof(SettingsQuickAccessMenu), "IsIgnored");
            MethodInfo run = AccessTools.Method(typeof(SettingsQuickAccessMenu), "Run");
            _ignoredForGood = AccessTools.Field(typeof(SettingsQuickAccessMenu), "isIgnored");
            if (ignored == null)
            {
                Log.Warn("seam missing: SettingsQuickAccessMenu.IsIgnored. The server settings bubble stays hidden.");
                return false;
            }
            HarmonyLib.Harmony h = new HarmonyLib.Harmony("tavernessentials.settingsboard");
            h.Patch(ignored, null, new HarmonyMethod(Hook("IgnoredPostfix")));
            if (run != null) h.Patch(run, null, new HarmonyMethod(Hook("RunPostfix")));
            Log.Msg("ready: the game's server settings bubble shows on the wheel for moderators, owners and admins, on a server that runs the board.");
            return true;
        }

        public static void IgnoredPostfix(SettingsQuickAccessMenu __instance, ref bool __result)
        {
            try
            {
                if (!__instance.IsServerSettings) return;
                if (_ignoredForGood != null && (bool)_ignoredForGood.GetValue(__instance)) return;
                ServerSettingsSync sync = ServerSettingsSync.Instance;
                bool available = sync != null && sync.IsMenuAvailable;
                bool staff = BoardRules.IsStaff(ClientRoles());
                __result = !(available && staff);
                int shown = __result ? 0 : 1;
                if (shown != _shown)
                {
                    _shown = shown;
                    Log.Msg(shown == 1 ? "the server settings bubble is on the wheel."
                        : "the server settings bubble is hidden (" + (staff ? "this server has no board" : "no staff role here") + ").");
                }
            }
            catch (Exception e)
            {
                if (_shown != 2)
                {
                    _shown = 2;
                    Log.Warn("could not decide the server settings bubble: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        public static void RunPostfix(SettingsQuickAccessMenu __instance)
        {
            if (_rowsLogged) return;
            try
            {
                if (!__instance.IsServerSettings) return;
                _rowsLogged = true;
                SettingsFieldAccess[] rows = __instance.SettingsToDisplay;
                Log.Msg("the server settings board has " + (rows == null ? 0 : rows.Length) + " rows:");
                if (rows == null) return;
                for (int i = 0; i < rows.Length; i++)
                {
                    SettingsFieldAccess r = rows[i];
                    if (r == null) { Log.Msg("  " + i + ": (empty)"); continue; }
                    Log.Msg("  " + i + ": " + r.GetType().Name + " " + r.SettingType + " \"" + r.DisplayName + "\""
                        + Private(r, "fieldName", " field ") + Private(r, "command", " command "));
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not list the board's rows: " + e.GetType().Name + ": " + e.Message);
            }
        }

        static string Private(object o, string field, string label)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    object v = f.GetValue(o);
                    return v == null ? "" : label + "\"" + v + "\"";
                }
            }
            return "";
        }

        static IEnumerable<string> ClientRoles()
        {
            if (_handlerMissing) return null;
            if (_handler == null)
            {
                Type services = AccessTools.TypeByName("TavernLib.Services.TavernServices");
                Type handler = AccessTools.TypeByName("TavernLib.Backend.EntranceMessageHandler");
                MethodInfo get = services == null ? null : services.GetMethod("GetService", BindingFlags.Public | BindingFlags.Static);
                if (get == null || handler == null)
                {
                    _handlerMissing = true;
                    Log.Warn("TavernLib's role message handler was not found, so the server settings bubble stays hidden.");
                    return null;
                }
                _handler = get.MakeGenericMethod(handler);
            }
            return Strings(Member(Member(_handler.Invoke(null, null), "LocalUser"), "Roles"));
        }

        static IEnumerable<string> Strings(object list)
        {
            IEnumerable e = list as IEnumerable;
            if (e == null) return null;
            List<string> all = new List<string>();
            foreach (object o in e)
                if (o != null) all.Add(o.ToString());
            return all;
        }

        static object Member(object o, string name)
        {
            if (o == null) return null;
            PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null) return p.GetValue(o, null);
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f == null ? null : f.GetValue(o);
        }
    }

    internal sealed class Quiet
    {
        static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        double _at = -100;
        int _held;

        internal static double Now { get { return Clock.Elapsed.TotalSeconds; } }

        internal void Warn(string line)
        {
            double t = Now;
            if (t - _at < 5)
            {
                _held++;
                return;
            }
            _at = t;
            if (_held > 0) line += " (" + _held + " more held back since the last line)";
            _held = 0;
            Log.Warn(line);
        }
    }

    internal static class Log
    {
        internal static readonly FeatureLog Out = new FeatureLog();

        internal static void Msg(string s) { Out.Msg(s); }
        internal static void Warn(string s) { Out.Warn(s); }
        internal static void Error(string s) { Out.Error(s); }
    }

    internal static class SettingsBoardConfig
    {
        internal static bool On = true;

        internal static readonly string[] Template =
        {
            "# ---- Settings board: the game's in-VR server settings board, for a server's staff. Server and game client.",
            "",
            "# 1 = players whose users.json roles hold moderator, owner or admin get the server settings",
            "#     bubble on the wheel, with the server's real settings. 0 = no board. Either way only",
            "#     those players can change the server's settings from a game (the game lets anyone).",
            "settings_board=1",
        };

        internal static void Load(Settings s)
        {
            On = s.Bool("settings_board", true);
        }
    }
}
