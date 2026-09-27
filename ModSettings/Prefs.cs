using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using MelonLoader;
using MelonLoader.Preferences;

namespace TavernEssentials.ModSettings
{
    internal static class Prefs
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static List<PrefGroup> Read()
        {
            List<PrefGroup> all = new List<PrefGroup>();
            foreach (MelonPreferences_Category c in MelonPreferences.Categories)
            {
                if (c == null) continue;
                PrefGroup g = new PrefGroup
                {
                    Id = c.Identifier,
                    Name = string.IsNullOrEmpty(c.DisplayName) ? c.Identifier : c.DisplayName,
                    Hidden = c.IsHidden,
                };
                foreach (MelonPreferences_Entry e in c.Entries)
                    if (e != null) g.Prefs.Add(Describe(e));
                all.Add(g);
            }
            return all;
        }

        internal static Pref Describe(MelonPreferences_Entry e)
        {
            Type t = e.GetReflectedType();
            Pref p = new Pref
            {
                Id = e.Identifier,
                Name = string.IsNullOrEmpty(e.DisplayName) ? e.Identifier : e.DisplayName,
                Description = e.Description ?? "",
                Hidden = e.IsHidden,
                Kind = KindOf(t),
                Heard = Listeners(e).Count > 0,
                Handle = e,
            };
            if (p.Kind == PrefKind.Enum)
            {
                p.EnumNames = Enum.GetNames(t);
                p.Flags = t.IsDefined(typeof(FlagsAttribute), false);
            }
            IValueRange range = e.Validator as IValueRange;
            if (range != null && (p.Kind == PrefKind.Int || p.Kind == PrefKind.Float || p.Kind == PrefKind.Double))
            {
                double min, max;
                if (Catalog.TryNumber(range.MinValue, out min) && Catalog.TryNumber(range.MaxValue, out max))
                {
                    p.Ranged = true;
                    p.Min = min;
                    p.Max = max;
                }
            }
            return p;
        }

        internal static PrefKind KindOf(Type t)
        {
            if (t == null) return PrefKind.Other;
            if (t == typeof(bool)) return PrefKind.Bool;
            if (t.IsEnum) return Catalog.Keybind(t.FullName) ? PrefKind.Other : PrefKind.Enum;
            if (t == typeof(int)) return PrefKind.Int;
            if (t == typeof(float)) return PrefKind.Float;
            if (t == typeof(double)) return PrefKind.Double;
            if (t == typeof(string)) return PrefKind.Text;
            return PrefKind.Other;
        }

        internal static object Current(Pref p)
        {
            return ((MelonPreferences_Entry)p.Handle).BoxedValue;
        }

        internal static List<Assembly> Listeners(MelonPreferences_Entry e)
        {
            List<Assembly> from = new List<Assembly>();
            Type t = e.GetType();
            foreach (string name in new[] { "OnEntryValueChanged", "OnEntryValueChangedUntyped" })
            {
                FieldInfo f = t.GetField(name, Any);
                object ev = f == null ? null : f.GetValue(e);
                MethodInfo subs = ev == null ? null : ev.GetType().GetMethod("GetSubscribers");
                Array list = subs == null ? null : subs.Invoke(ev, null) as Array;
                if (list == null) continue;
                foreach (object s in list)
                {
                    if (s == null) continue;
                    FieldInfo ma = s.GetType().GetField("melonAssembly", Any);
                    MelonAssembly melon = ma == null ? null : ma.GetValue(s) as MelonAssembly;
                    FieldInfo df = s.GetType().GetField("del", Any);
                    Delegate d = df == null ? null : df.GetValue(s) as Delegate;
                    from.Add(melon != null && melon.Assembly != null ? melon.Assembly : AssemblyOf(d));
                }
            }
            FieldInfo old = t.GetField("OnValueChanged", Any);
            Delegate o = old == null ? null : old.GetValue(e) as Delegate;
            if (o != null)
                foreach (Delegate d in o.GetInvocationList()) from.Add(AssemblyOf(d));
            return from;
        }

        static Assembly AssemblyOf(Delegate d)
        {
            return d == null || d.Method == null || d.Method.DeclaringType == null ? null : d.Method.DeclaringType.Assembly;
        }

        internal static ModInfo MelonOf(PrefGroup g)
        {
            MelonBase found = null;
            foreach (MelonBase m in MelonBase.RegisteredMelons)
                if (m != null && m.Info != null && (Catalog.SameName(g.Id, m.Info.Name) || Catalog.SameName(g.Name, m.Info.Name))) { found = m; break; }
            for (int i = 0; found == null && i < g.Prefs.Count; i++)
                foreach (Assembly a in Listeners((MelonPreferences_Entry)g.Prefs[i].Handle))
                {
                    if (a == null) continue;
                    foreach (MelonBase m in MelonBase.RegisteredMelons)
                        if (m != null && m.MelonAssembly != null && m.MelonAssembly.Assembly == a) { found = m; break; }
                    if (found != null) break;
                }
            if (found == null) return null;
            ModInfo info = new ModInfo();
            if (found.Info != null)
            {
                info.Name = found.Info.Name;
                info.Version = found.Info.Version;
                info.Author = found.Info.Author;
            }
            Assembly asm = found.MelonAssembly == null ? null : found.MelonAssembly.Assembly;
            object[] about = asm == null ? new object[0] : asm.GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false);
            if (about.Length > 0) info.Description = ((AssemblyDescriptionAttribute)about[0]).Description;
            return info;
        }

        internal static bool Set(Pref p, object value, out string unsaved)
        {
            unsaved = null;
            MelonPreferences_Entry e = (MelonPreferences_Entry)p.Handle;
            object was = e.BoxedValue;
            e.BoxedValue = Cast(p, e.GetReflectedType(), value);
            if (Equals(was, e.BoxedValue)) return false;
            try { e.Category.SaveToFile(false); }
            catch (Exception x) { unsaved = x.GetType().Name + ": " + x.Message; }
            return true;
        }

        internal static string Owner(Pref p)
        {
            MelonPreferences_Category c = ((MelonPreferences_Entry)p.Handle).Category;
            return string.IsNullOrEmpty(c.DisplayName) ? c.Identifier : c.DisplayName;
        }

        static object Cast(Pref p, Type t, object value)
        {
            switch (p.Kind)
            {
                case PrefKind.Bool: return (bool)value;
                case PrefKind.Enum: return Enum.Parse(t, (string)value);
                case PrefKind.Int: return (int)Math.Round((double)value);
                case PrefKind.Float: return (float)(double)value;
                case PrefKind.Double: return (double)value;
                default: throw new InvalidOperationException("the board never sets a " + p.Kind + " setting");
            }
        }

        internal static string Show(object o)
        {
            return o == null ? "null" : Convert.ToString(o, CultureInfo.InvariantCulture);
        }
    }
}
