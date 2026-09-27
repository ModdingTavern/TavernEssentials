using System;
using System.Reflection;
using System.Threading.Tasks;
using Alta.Utilities;
using UnityEngine;

namespace TavernEssentials.ModSettings
{
    public sealed class ModToggleAccess : ToggleGameSettingAccess
    {
        internal Func<bool> Read;
        internal Action<bool> Write;
        internal string Description = "";

        internal static ModToggleAccess Make(string name, string description, Func<bool> read, Action<bool> write)
        {
            ModToggleAccess a = ScriptableObject.CreateInstance<ModToggleAccess>();
            Access.Name(a, name);
            a.Description = description ?? "";
            a.Read = read;
            a.Write = write;
            return a;
        }

        public override bool IsValid() { return true; }
        public override string SettingDescription { get { return Description; } }

        public override bool GetValue()
        {
            try { return Read(); }
            catch (Exception e) { Log.Warn("reading " + DisplayName + ": " + e.Message); return false; }
        }

        public override void SetValue(bool value)
        {
            try { Write(value); }
            catch (Exception e) { Log.Warn("setting " + DisplayName + ": " + e.GetType().Name + ": " + e.Message); }
        }
    }

    public sealed class ModChoiceAccess : EnumSettingAccess
    {
        internal string[] Labels = { "" };
        internal Func<string> Read;
        internal Action<string> Write;
        internal string Description = "";

        internal static ModChoiceAccess Make(string name, string description, string[] labels, Func<string> read, Action<string> write)
        {
            ModChoiceAccess a = ScriptableObject.CreateInstance<ModChoiceAccess>();
            Access.Name(a, name);
            a.Description = description ?? "";
            a.Labels = labels;
            a.Read = read;
            a.Write = write;
            return a;
        }

        public override bool IsValid() { return true; }
        public override string SettingDescription { get { return Description; } }

        public override Task<string[]> GetPossibleValue()
        {
            return Task.FromResult(Labels);
        }

        public override string GetValue()
        {
            try { return Read(); }
            catch (Exception e) { Log.Warn("reading " + DisplayName + ": " + e.Message); return Labels[0]; }
        }

        public override void SetValue(string value)
        {
            try { Write(value); }
            catch (Exception e) { Log.Warn("setting " + DisplayName + ": " + e.GetType().Name + ": " + e.Message); }
        }
    }

    internal static class Access
    {
        internal static readonly FieldInfo DisplayName = typeof(SettingsFieldAccess).GetField("displayName", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static void Name(SettingsFieldAccess a, string name)
        {
            DisplayName.SetValue(a, name ?? "");
        }
    }
}
