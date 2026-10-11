using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Networking.Scripts.Player;
using HarmonyLib;

namespace TavernEssentials.Names
{
    internal static class NamesFeature
    {
        static readonly FieldInfo ByName = Field("isByUsername"), Id = Field("userIdentifier"), Name = Field("username"),
            Searched = Field("hasSearched"), IdCacheField = Field("idCache"), NameCacheField = Field("nameCache");
        static readonly HashSet<string> Missed = new HashSet<string>();
        static MethodInfo _manager;
        static bool _noAccounts;
        static bool _accountsFailing;
        static DateTime _accountsRetry = DateTime.MinValue;
        const double AccountsCoolDown = 60;

        internal static bool Start()
        {
            if (!NamesConfig.Enabled)
            {
                Log.Msg("switched off (names_local=0): the game asks Alta for player names, and waits.");
                return false;
            }
            HarmonyLib.Harmony h = new HarmonyLib.Harmony("tavernessentials.names");
            if (!Apply(h)) return false;
            Log.Msg("ready: player names come from the players here and TavernLib's accounts, never Alta.");
            return true;
        }

        internal static bool Apply(HarmonyLib.Harmony h)
        {
            MethodInfo target = AccessTools.Method(typeof(UserInfoAccess), "SearchForUserAsync");
            if (target == null || ByName == null || Id == null || Name == null || Searched == null
                || IdCacheField == null || NameCacheField == null)
            {
                Log.Warn("UserInfoAccess changed shape (SearchForUserAsync or its fields not found); names still come from Alta.");
                return false;
            }
            h.Patch(target, new HarmonyMethod(typeof(NamesFeature).GetMethod("SearchPrefix", BindingFlags.Public | BindingFlags.Static)));
            return true;
        }

        public static bool SearchPrefix(UserInfoAccess __instance, ref Task __result)
        {
            try
            {
                string asked;
                if ((bool)ByName.GetValue(__instance))
                {
                    string name = (string)Name.GetValue(__instance);
                    if (name == null || NameCache.ContainsKey(name)) return true;
                    int id;
                    string exact;
                    if (Resolve(name, out id, out exact)) { Remember(id, exact); return true; }
                    asked = "\"" + name + "\"";
                }
                else
                {
                    int id = (int)Id.GetValue(__instance);
                    if (IdCache.ContainsKey(id)) return true;
                    string name;
                    if (Resolve(id, out name)) { Remember(id, name); return true; }
                    asked = "id " + id;
                }
                if (Missed.Add(asked)) Log.Msg("no player " + asked + " is here or in TavernLib's accounts; the game gets \"no such player\".");
                Searched.SetValue(__instance, true);
                __result = Task.FromResult(true);
                return false;
            }
            catch (Exception e)
            {
                Log.Warn("a name lookup went to the game (" + e.GetType().Name + ": " + e.Message + ").");
                return true;
            }
        }

        internal static bool Resolve(int id, out string name)
        {
            name = null;
            UserInfo cached;
            if (IdCacheField != null && IdCache.TryGetValue(id, out cached) && cached != null) { name = cached.Username; return true; }
            foreach (UserInfoAndRole login in Logins())
                if (login.Identifier == id) { name = login.Username; return true; }
            IDictionary accounts = Accounts();
            if (accounts == null) return false;
            foreach (DictionaryEntry a in accounts)
                if (AccountId(a.Value) == id) { name = (string)a.Key; return true; }
            return false;
        }

        internal static bool Resolve(string lowerName, out int id, out string name)
        {
            id = -1;
            name = null;
            UserInfo cached;
            if (NameCacheField != null && NameCache.TryGetValue(lowerName, out cached) && cached != null)
            {
                id = cached.Identifier;
                name = cached.Username;
                return true;
            }
            foreach (UserInfoAndRole login in Logins())
                if (login.Username != null && login.Username.ToLowerInvariant() == lowerName)
                {
                    id = login.Identifier;
                    name = login.Username;
                    return true;
                }
            IDictionary accounts = Accounts();
            if (accounts == null || !accounts.Contains(lowerName)) return false;
            id = AccountId(accounts[lowerName]);
            name = lowerName;
            return id >= 0;
        }

        internal static string AccountMismatch(string lowerName, int id)
        {
            IDictionary accounts = lowerName == null ? null : Accounts();
            if (accounts == null) return null;
            if (!accounts.Contains(lowerName)) return "TavernLib has no account \"" + lowerName + "\" any more";
            int own = AccountId(accounts[lowerName]);
            if (own != id)
                return "TavernLib's account \"" + lowerName + "\" is id " + own + ", not " + id + ", the id they proved their key under";
            foreach (DictionaryEntry a in accounts)
            {
                string other = a.Key as string;
                if (AccountId(a.Value) == id && !string.Equals(other, lowerName, StringComparison.OrdinalIgnoreCase))
                    return "TavernLib's account \"" + other + "\" holds id " + id + " too";
            }
            return null;
        }

        internal static void Remember(int id, string name)
        {
            if (name == null || IdCacheField == null) return;
            UserInfo u = new UserInfo(id, name);
            IdCache[id] = u;
            NameCache[name.ToLowerInvariant()] = u;
        }

        static List<UserInfoAndRole> Logins()
        {
            List<UserInfoAndRole> all = new List<UserInfoAndRole>();
            foreach (IPlayer ip in Player.AllPlayers)
            {
                Player p = ip as Player;
                if (p == null) continue;
                try
                {
                    Alta.Networking.Connection c = p.ConnectionToRemotePlayer;
                    if (c != null && c.UserInfo != null) all.Add(c.UserInfo);
                }
                catch { }
            }
            return all;
        }

        static IDictionary Accounts()
        {
            if (_noAccounts || DateTime.UtcNow < _accountsRetry) return null;
            try
            {
                if (_manager == null)
                {
                    Type services = AccessTools.TypeByName("TavernLib.Services.TavernServices");
                    Type manager = AccessTools.TypeByName("TavernLib.Backend.Api.TavernManager");
                    MethodInfo get = services == null ? null : services.GetMethod("GetService", BindingFlags.Public | BindingFlags.Static);
                    if (get == null || manager == null)
                    {
                        _noAccounts = true;
                        Log.Msg("TavernLib's accounts were not found, so only the players here can be named.");
                        return null;
                    }
                    _manager = get.MakeGenericMethod(manager);
                }
                object users = Member(Member(Member(_manager.Invoke(null, null), "UserConfig"), "LastRead"), "Users");
                if (_accountsFailing)
                {
                    _accountsFailing = false;
                    Log.Msg("TavernLib's accounts can be read again.");
                }
                return users as IDictionary;
            }
            catch (Exception e)
            {
                _accountsRetry = DateTime.UtcNow.AddSeconds(AccountsCoolDown);
                if (!_accountsFailing)
                    Log.Warn("TavernLib's accounts could not be read (" + e.GetType().Name + ": " + e.Message + "); only the players here"
                        + " can be named until a read works (tried again after " + (int)AccountsCoolDown + " s).");
                _accountsFailing = true;
                return null;
            }
        }

        static int AccountId(object user)
        {
            object v = Member(user, "UserId");
            if (v == null) return -1;
            ulong id = Convert.ToUInt64(v);
            return id > int.MaxValue ? -1 : (int)id;
        }

        static object Member(object o, string name)
        {
            if (o == null) return null;
            PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null) return p.GetValue(o, null);
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f == null ? null : f.GetValue(o);
        }

        static Dictionary<int, UserInfo> IdCache { get { return (Dictionary<int, UserInfo>)IdCacheField.GetValue(null); } }
        static Dictionary<string, UserInfo> NameCache { get { return (Dictionary<string, UserInfo>)NameCacheField.GetValue(null); } }

        static FieldInfo Field(string name)
        {
            return typeof(UserInfoAccess).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        }
    }

    internal static class Log
    {
        internal static readonly FeatureLog Out = new FeatureLog();

        internal static void Msg(string s) { Out.Msg(s); }
        internal static void Warn(string s) { Out.Warn(s); }
        internal static void Error(string s) { Out.Error(s); }
    }

    internal static class NamesConfig
    {
        internal static bool Enabled = true;

        internal static readonly string[] Template =
        {
            "# ---- Names: player names for the server's own lookups. Server only.",
            "",
            "# 1 = name players from the logins here and TavernLib's accounts (console commands by",
            "#     name, the postbox, the time-trial boards). 0 = ask Alta's web API, which is gone.",
            "names_local=1",
        };

        internal static void Load(Settings s)
        {
            Enabled = s.Bool("names_local", Enabled);
        }
    }
}
