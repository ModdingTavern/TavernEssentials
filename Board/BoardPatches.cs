using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using HarmonyLib;

namespace TavernEssentials.Board
{
    public static class BoardPatches
    {
        const string Joined = "GetJoinedServersAsync";

        static readonly string[] Methods =
        {
            "GetPublicServersAsync",
            Joined,
            "GetOpenServersAsync",
            "DiscoverServers",
        };

        public static int Apply(HarmonyLib.Harmony h)
        {
            MethodInfo all = typeof(BoardPatches).GetMethod("AllPrefix", BindingFlags.Public | BindingFlags.Static);
            MethodInfo joined = typeof(BoardPatches).GetMethod("JoinedPrefix", BindingFlags.Public | BindingFlags.Static);
            Type[] targets = { typeof(OfflineServerApiClient), typeof(ServerApiClient) };
            int patched = 0;
            foreach (Type type in targets)
            {
                foreach (string name in Methods)
                {
                    MethodInfo target = AccessTools.Method(type, name, new Type[0]);
                    if (target == null)
                    {
                        Log.Warn("seam missing: " + type.Name + "." + name);
                        continue;
                    }
                    try
                    {
                        h.Patch(target, new HarmonyMethod(name == Joined ? joined : all));
                        patched++;
                    }
                    catch (Exception e)
                    {
                        Log.Warn("patch failed on " + type.Name + "." + name + ": " + e.Message);
                    }
                }
            }
            return patched;
        }

        public static bool AllPrefix(ref Task<IEnumerable<GameServerInfo>> __result)
        {
            try
            {
                return !CommunityList.TryReplace(ref __result);
            }
            catch (Exception e)
            {
                Log.Warn("board list: " + e.Message);
                return true;
            }
        }

        public static bool JoinedPrefix(ref Task<IEnumerable<GameServerInfo>> __result)
        {
            try
            {
                if (!BoardConfig.MyServers) return !CommunityList.TryReplace(ref __result);
                return !MyServers.TryReplace(ref __result);
            }
            catch (Exception e)
            {
                Log.Warn("my servers list: " + e.Message);
                return true;
            }
        }
    }
}
