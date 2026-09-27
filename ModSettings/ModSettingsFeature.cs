using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TavernEssentials.ModSettings
{
    internal static class ModSettingsFeature
    {
        internal const string Heading = "Mods";

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo Panels = typeof(SettingsPanel).GetField("panels", Any);
        static readonly Type Entry = typeof(SettingsPanel).GetNestedType("SettingsPanelConfiguration", BindingFlags.NonPublic | BindingFlags.Public);
        static readonly FieldInfo EntryBoard = Entry == null ? null : Entry.GetField("board", Any);
        static readonly FieldInfo EntryLever = Entry == null ? null : Entry.GetField("lever", Any);
        static readonly FieldInfo Options = typeof(SelectionLever).GetField("numberOfOptions", Any);
        static readonly FieldInfo MinAngle = typeof(SelectionLever).GetField("minAngle", Any);
        static readonly FieldInfo MaxAngle = typeof(SelectionLever).GetField("maxAngle", Any);
        static readonly FieldInfo Angles = typeof(SelectionLever).GetField("selectionAngles", Any);
        static readonly MethodInfo PerIndex = typeof(SelectionLever).GetMethod("CalculatePerIndex", Any);

        static string _last;
        static ModsBoard _board;

        internal static bool Start()
        {
            if (!ModsConfig.Enabled)
            {
                Log.Msg("switched off (mods_board=0): the fifth Settings Panel lever stays padlocked.");
                return false;
            }
            if (!Apply(new HarmonyLib.Harmony("tavernessentials.modsettings"))) return false;
            Log.Msg("ready: the Settings Panel's padlocked fifth lever opens a Mods board with your mods' settings"
                + (ModsConfig.Hide.Length > 0 ? " (hiding: " + ModsConfig.Hide + ")." : "."));
            return true;
        }

        internal static bool Apply(HarmonyLib.Harmony h)
        {
            string missing = Missing();
            if (missing != null)
            {
                Log.Warn("Settings Panel seam missing: " + missing + ". The fifth lever stays padlocked.");
                return false;
            }
            h.Patch(AccessTools.Method(typeof(SettingsPanel), "Start"),
                new HarmonyMethod(typeof(ModSettingsFeature).GetMethod("StartPrefix", BindingFlags.Public | BindingFlags.Static)));
            return true;
        }

        internal static string Missing()
        {
            MethodInfo target = AccessTools.Method(typeof(SettingsPanel), "Start");
            return target == null ? "SettingsPanel.Start" : Panels == null ? "SettingsPanel.panels"
                : EntryBoard == null || EntryLever == null ? "SettingsPanel.SettingsPanelConfiguration.board/lever"
                : Options == null || MinAngle == null || MaxAngle == null || Angles == null ? "SelectionLever's option fields"
                : PerIndex == null ? "SelectionLever.CalculatePerIndex" : ModsBoard.Missing();
        }

        public static void StartPrefix(SettingsPanel __instance)
        {
            try
            {
                Say(Wire(__instance));
            }
            catch (Exception e)
            {
                Log.Warn("the Mods tab was not added: " + e.GetType().Name + ": " + e.Message);
            }
        }

        internal static string Wire(SettingsPanel panel)
        {
            Array panels = (Array)Panels.GetValue(panel);
            if (panels == null || panels.Length == 0) return "the Settings Panel has no tabs, so no Mods tab";
            SelectionLever last = null;
            Transform lastBoard = null;
            HashSet<SelectionLever> wired = new HashSet<SelectionLever>();
            foreach (object e in panels)
            {
                SelectionLever l = (SelectionLever)EntryLever.GetValue(e);
                if (l == null) continue;
                wired.Add(l);
                last = l;
                lastBoard = (Transform)EntryBoard.GetValue(e);
            }
            if (last == null || lastBoard == null || last.transform.parent == null) return "the Settings Panel's tabs have no lever, so no Mods tab";

            List<SelectionLever> locked = new List<SelectionLever>();
            foreach (SelectionLever l in last.transform.parent.GetComponentsInChildren<SelectionLever>(true))
                if (!wired.Contains(l) && (int)Options.GetValue(l) == 1) locked.Add(l);
            if (locked.Count == 0) return "no padlocked lever beside the tab levers, so no Mods tab";
            if (locked.Count > 1) return locked.Count + " padlocked levers instead of one, so no Mods tab";
            SelectionLever mods = locked[0];

            TextRenderer heading;
            GameObject board = CopyBoard(lastBoard, out heading);
            if (board == null) return "the last board has no heading to copy, so no Mods tab";
            List<Transform> gameBoards = new List<Transform>();
            List<SelectionLever> levers = new List<SelectionLever>();
            foreach (object e in panels)
            {
                gameBoards.Add((Transform)EntryBoard.GetValue(e));
                levers.Add((SelectionLever)EntryLever.GetValue(e));
            }
            string why;
            ModsBoard rows = ModsBoard.Make(board, heading, gameBoards, out why);

            Options.SetValue(mods, Options.GetValue(last));
            MinAngle.SetValue(mods, MinAngle.GetValue(last));
            MaxAngle.SetValue(mods, MaxAngle.GetValue(last));
            Angles.SetValue(mods, ((float[])Angles.GetValue(last)).Clone());
            PerIndex.Invoke(mods, null);

            object entry = Activator.CreateInstance(Entry, true);
            EntryBoard.SetValue(entry, board.transform);
            EntryLever.SetValue(entry, mods);
            Array more = Array.CreateInstance(Entry, panels.Length + 1);
            Array.Copy(panels, more, panels.Length);
            more.SetValue(entry, panels.Length);
            Panels.SetValue(panel, more);

            int hidden = HidePadlock(mods);
            _board = rows;
            if (rows != null) mods.SelectionChanged += Pulled;
            string pad = "";
            try { pad = " " + LeverPad.Dress(mods, levers, board.transform); }
            catch (Exception e) { pad = " The pad's icon and label failed: " + e.GetType().Name + ": " + e.Message; }
            return "the fifth lever opens a Mods board" + (hidden == 0 ? " (its padlock was not found, so it still shows)" : "")
                + (rows == null ? ", empty: " + why : "") + "." + pad;
        }

        static void Pulled(SelectionLever lever, int was, int now)
        {
            if (now != 1 || _board == null) return;
            try
            {
                Say(_board.Open());
            }
            catch (Exception e)
            {
                Log.Warn("the Mods board could not be filled: " + e.GetType().Name + ": " + e.Message);
            }
        }

        static GameObject CopyBoard(Transform original, out TextRenderer heading)
        {
            GameObject src = original.gameObject;
            bool was = src.activeSelf;
            src.SetActive(false);
            GameObject copy;
            try { copy = UnityEngine.Object.Instantiate(src, original.parent); }
            finally { src.SetActive(was); }
            copy.name = "Mods Board";

            heading = null;
            for (int i = copy.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = copy.transform.GetChild(i);
                TextRenderer text = child.GetComponent<TextRenderer>();
                if (heading == null && text != null && child.GetComponent<Collider>() == null) { heading = text; continue; }
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            if (heading == null)
            {
                UnityEngine.Object.DestroyImmediate(copy);
                return null;
            }
            heading.Text = Heading;
            return copy;
        }

        static int HidePadlock(SelectionLever lever)
        {
            Transform tab = LeverPad.TabIcon(lever);
            if (tab == null) return 0;
            int n = 0;
            foreach (Transform icon in tab)
            {
                icon.gameObject.SetActive(false);
                n++;
            }
            return n;
        }

        static void Say(string s)
        {
            if (s == null || s == _last) return;
            _last = s;
            Log.Msg(s);
        }
    }

    internal static class Log
    {
        internal static readonly FeatureLog Out = new FeatureLog();

        internal static void Msg(string s) { Out.Msg(s); }
        internal static void Warn(string s) { Out.Warn(s); }
        internal static void Error(string s) { Out.Error(s); }
    }

    internal static class ModsConfig
    {
        internal static bool Enabled = true;
        internal static string Hide = "";

        internal static readonly string[] Template =
        {
            "# ---- ModSettings: a Mods tab on the main menu's Settings Panel. Game client only.",
            "",
            "# 1 = the padlocked fifth lever opens a Mods board. 0 = it stays padlocked.",
            "mods_board=1",
            "",
            "# Mods whose settings the board leaves out: their names as the board shows them (or their",
            "# MelonPreferences category ids), comma-separated, case ignored. Example: mods_hide=UnityExplorer",
            "mods_hide=",
        };

        internal static void Load(Settings s)
        {
            Enabled = s.Bool("mods_board", Enabled);
            Hide = (s.Text("mods_hide", Hide) ?? "").Trim();
        }
    }
}
