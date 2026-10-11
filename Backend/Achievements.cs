using System;
using System.Reflection;
using Alta.Networking.Scripts.Player;
using HarmonyLib;

namespace TavernEssentials.Backend
{
    internal static class Achievements
    {
        static readonly MethodInfo Board = AccessTools.Method(typeof(AchievementBoard), "UpdatePlayer", new Type[] { typeof(IPlayer) });
        static readonly MethodInfo Festive = AccessTools.Method(typeof(FestiveFeature), "Modify", new Type[] { typeof(bool) });
        static readonly FieldInfo Targets = AccessTools.Field(typeof(FestiveFeature), "gameObjects");
        static readonly MethodInfo Toast = AccessTools.Method(typeof(AchievementEffectHandler), "CallEffect", new Type[] { typeof(GameAchievement) });
        static bool _failLogged;

        internal static string Missing()
        {
            if (Board == null) return "AchievementBoard.UpdatePlayer";
            if (Festive == null) return "FestiveFeature.Modify";
            if (Targets == null || Targets.FieldType != typeof(UnityEngine.GameObject[])) return "FestiveFeature.gameObjects";
            if (Toast == null) return "AchievementEffectHandler.CallEffect";
            return null;
        }

        internal static bool Start(HarmonyLib.Harmony h)
        {
            if (!BackendConfig.HideAchievements)
            {
                Log.Msg("achievements: the town's tutorial boards show (backend_hide_achievements=0).");
                return false;
            }
            bool muted;
            bool hidden = Apply(h, out muted);
            string what = hidden && muted ? "the town's tutorial boards are hidden here, and the achievement popup with them"
                : hidden ? "the town's tutorial boards are hidden here" : muted ? "the achievement popup is muted here" : null;
            if (what != null) Log.Msg("achievements: " + what + " (backend_hide_achievements=1); nothing is saved or sent.");
            return hidden || muted;
        }

        internal static bool Apply(HarmonyLib.Harmony h, out bool muted)
        {
            bool hidden = Patch(h, Board, "AchievementBoard.UpdatePlayer", "HideBoard", true, "the tutorial boards still show");
            bool targets = Targets != null && Targets.FieldType == typeof(UnityEngine.GameObject[]);
            Patch(h, targets ? Festive : null, targets ? "FestiveFeature.Modify" : "FestiveFeature.gameObjects", "HideFestive", true,
                "the Holiday board shows when its festivity turns on");
            muted = Patch(h, Toast, "AchievementEffectHandler.CallEffect", "NoToast", false, "the achievement popup can still play");
            return hidden;
        }

        static bool Patch(HarmonyLib.Harmony h, MethodInfo target, string name, string patch, bool after, string otherwise)
        {
            try
            {
                if (target == null)
                {
                    Log.Warn("achievements: seam missing: " + name + ", so " + otherwise + ".");
                    return false;
                }
                HarmonyMethod m = new HarmonyMethod(typeof(Achievements).GetMethod(patch, BindingFlags.Public | BindingFlags.Static));
                if (after) h.Patch(target, null, m);
                else h.Patch(target, m);
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("achievements: could not patch " + name + ": " + e.Message + ", so " + otherwise + ".");
                return false;
            }
        }

        public static void HideBoard(AchievementBoard __instance)
        {
            try
            {
                if (__instance != null) __instance.gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                Failed(e);
            }
        }

        public static void HideFestive(UnityEngine.GameObject[] ___gameObjects)
        {
            if (___gameObjects == null) return;
            try
            {
                foreach (UnityEngine.GameObject g in ___gameObjects)
                    if (g != null && g.activeSelf && g.GetComponent(typeof(AchievementBoard)) != null) g.SetActive(false);
            }
            catch (Exception e)
            {
                Failed(e);
            }
        }

        static void Failed(Exception e)
        {
            if (_failLogged) return;
            _failLogged = true;
            Log.Warn("achievements: a tutorial board could not be hidden (logged once a run): " + e);
        }

        public static bool NoToast()
        {
            return false;
        }
    }
}
