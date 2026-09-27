using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MelonLoader;
using Alta.QuickAccessActions;

namespace TavernEssentials.Board
{
    public static class LeaveFix
    {
        static HarmonyLib.Harmony _h;
        static bool _stripped;
        static bool _leaving;

        public static bool Apply(HarmonyLib.Harmony h)
        {
            _h = h;
            MethodInfo target = Target();
            if (target == null)
            {
                Log.Warn("leave: ReturnToMainMenuAction.LetGoValid not found - game code moved; orb untouched.");
                return false;
            }
            try
            {
                h.Patch(target, new HarmonyMethod(typeof(LeaveFix).GetMethod(
                    "LetGoValidPrefix", BindingFlags.Public | BindingFlags.Static)), null, null);
            }
            catch (Exception e)
            {
                Log.Warn("leave: prefix failed: " + e.Message);
                return false;
            }
            EnsureStripped();
            GameModeManager.ModeChanged += OnModeChanged;
            return true;
        }

        static void OnModeChanged()
        {
            EnsureStripped();
            if (GameModeManager.CurrentMode != null) _leaving = false;
        }

        internal static MethodInfo Target()
        {
            return AccessTools.Method(typeof(ReturnToMainMenuAction), "LetGoValid");
        }

        static void EnsureStripped()
        {
            if (_stripped) return;
            try
            {
                MethodInfo target = Target();
                if (target == null) return;
                int removed = Strip(target, _h);
                if (removed > 0)
                {
                    _stripped = true;
                    Log.Msg("leave: removed the launcher's quit-on-menu-return patch (" + removed + ").");
                }
            }
            catch (Exception e) { Log.Warn("leave: strip failed: " + e.Message); }
        }

        internal static int Strip(MethodInfo target, HarmonyLib.Harmony h)
        {
            HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(target);
            if (info == null) return 0;
            List<MethodInfo> strip = new List<MethodInfo>();
            foreach (HarmonyLib.Patch p in info.Prefixes)
            {
                MethodInfo pm = p.PatchMethod;
                if (pm == null) continue;
                if (pm.Name == "QuitOnMenuReturn" || IsTavernLib(pm)) strip.Add(pm);
            }
            for (int i = 0; i < strip.Count; i++) h.Unpatch(target, strip[i]);
            return strip.Count;
        }

        static bool IsTavernLib(MethodInfo pm)
        {
            try
            {
                return pm.DeclaringType != null
                    && pm.DeclaringType.Assembly.GetName().Name == "TavernLib";
            }
            catch (Exception) { return false; }
        }

        public static bool LetGoValidPrefix(ReturnToMainMenuAction __instance)
        {
            try
            {
                ClearOrb(__instance);
                if (_leaving) { Log.Detail("leave: already leaving; ignored."); return false; }
                if (GameModeManager.CurrentMode == null) { Log.Detail("leave: no active mode; ignored."); return false; }
                _leaving = true;
                Log.Msg("leave: orb eaten - disconnecting, then loading the main menu.");
                MelonCoroutines.Start(LeaveRoutine());
            }
            catch (Exception e)
            {
                _leaving = false;
                Log.Warn("leave: failed to start: " + e.Message);
            }
            return false;
        }

        static IEnumerator LeaveRoutine()
        {
            Task stop = StartTeardown();
            if (stop == null) { _leaving = false; yield break; }
            while (!stop.IsCompleted) yield return null;
            if (stop.IsFaulted)
            {
                Log.Warn("leave: teardown faulted (" + Flat(stop.Exception) + ") - staying in place.");
                _leaving = false;
                yield break;
            }
            Log.Detail("leave: mode stopped; swapping to the menu scene.");

            TryStartReload();
            Task menu = StartMenuLoad();
            if (menu != null)
            {
                while (!menu.IsCompleted) yield return null;
                if (menu.IsFaulted) Log.Warn("leave: menu load faulted: " + Flat(menu.Exception));
                else Log.Msg("leave: back at the main menu.");
            }
            TryFinishReload();
            TryFadeBackIn();
            _leaving = false;
        }

        static Task StartTeardown()
        {
            try { return GameModeManager.StopCurrentModeAsync("return to menu", false); }
            catch (Exception e)
            {
                Log.Warn("leave: teardown failed to start: " + e.Message);
                return null;
            }
        }

        static Task StartMenuLoad()
        {
            try { return AltaSceneManager.ReturnToMainMenuAsync(); }
            catch (Exception e)
            {
                Log.Warn("leave: menu load failed to start: " + e.Message);
                return null;
            }
        }

        static void TryStartReload()
        {
            try { ApplicationManager.StartReload(); }
            catch (Exception e) { Log.Warn("leave: StartReload failed (continuing): " + e.Message); }
        }

        static void TryFinishReload()
        {
            try { ApplicationManager.FinishReload(); }
            catch (Exception e) { Log.Detail("leave: FinishReload failed: " + e.Message); }
        }

        static void ClearOrb(ReturnToMainMenuAction action)
        {
            try
            {
                MethodInfo clear = AccessTools.Method(typeof(ReturnToMainMenuAction), "ClearOrb");
                if (clear != null) clear.Invoke(action, null);
            }
            catch (Exception e) { Log.Detail("leave: ClearOrb failed (continuing): " + e.Message); }
        }

        static void TryFadeBackIn()
        {
            try
            {
                PlayerController pc = PlayerController.Current;
                if (pc == null || pc.ScreenFader == null) return;
                MethodInfo fade = AccessTools.Method(pc.ScreenFader.GetType(), "FadeBackIn");
                if (fade == null) return;
                ParameterInfo[] ps = fade.GetParameters();
                object[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                    args[i] = ps[i].ParameterType == typeof(float) ? (object)0f
                        : (ps[i].IsOptional ? ps[i].DefaultValue : null);
                fade.Invoke(pc.ScreenFader, args);
            }
            catch (Exception e) { Log.Detail("leave: fade-back-in skipped: " + e.Message); }
        }

        static string Flat(Exception e)
        {
            if (e == null) return "no exception";
            AggregateException ag = e as AggregateException;
            if (ag != null) e = ag.GetBaseException();
            return e.GetType().Name + ": " + e.Message;
        }
    }
}
