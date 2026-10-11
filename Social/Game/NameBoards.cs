using System;
using System.Reflection;
using HarmonyLib;

namespace TavernEssentials.Social
{
    internal static class NameBoards
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly Type ShowNames = Type.GetType("Alta.QuickAccessActions.ShowNames, Root.Township");
        static readonly Type Handler = ShowNames == null ? null : ShowNames.GetNestedType("NameBoardHandler", BindingFlags.NonPublic | BindingFlags.Public);
        static readonly MethodInfo Activate = Handler == null ? null : Handler.GetMethod("Activate", Any);
        static readonly MethodInfo Redraw = Handler == null ? null : Handler.GetMethod("UpdateFriendship", Any);
        static readonly FieldInfo PlayerField = Handler == null ? null : Handler.GetField("player", Any);
        static bool _failLogged;

        const string Else = " A friend rejoining beside you may stay white until their name board is made again.";

        internal static string Missing()
        {
            return ShowNames == null ? "Alta.QuickAccessActions.ShowNames" : Handler == null ? "ShowNames.NameBoardHandler"
                : Activate == null ? "NameBoardHandler.Activate" : Redraw == null ? "NameBoardHandler.UpdateFriendship"
                : PlayerField == null ? "NameBoardHandler.player" : null;
        }

        internal static bool Apply(HarmonyLib.Harmony h)
        {
            string missing = Missing();
            if (missing != null)
            {
                Log.Warn("seam missing: " + missing + "." + Else);
                return false;
            }
            try
            {
                h.Patch(Activate, null, new HarmonyMethod(typeof(NameBoards).GetMethod("ActivatePostfix", BindingFlags.Public | BindingFlags.Static)));
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("could not patch NameBoardHandler.Activate: " + e.Message + "." + Else);
                return false;
            }
        }

        public static void ActivatePostfix(object __instance)
        {
            try
            {
                UnityEngine.Object player = PlayerField.GetValue(__instance) as UnityEngine.Object;
                if (player == null) return;
                Redraw.Invoke(__instance, null);
            }
            catch (Exception e)
            {
                if (_failLogged) return;
                _failLogged = true;
                Exception real = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                Log.Warn("a name board could not read the friend flag again (logged once a run): " + real);
            }
        }
    }
}
