using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TavernEssentials.ModSettings
{
    internal enum PrefKind { Other, Bool, Enum, Int, Float, Double, Text }

    internal sealed class Pref
    {
        internal string Id;
        internal string Name;
        internal string Description = "";
        internal bool Hidden;
        internal PrefKind Kind;
        internal string[] EnumNames;
        internal bool Flags;
        internal bool Ranged;
        internal double Min, Max;
        internal bool Heard;
        internal object Handle { get; set; }
    }

    internal sealed class ModInfo
    {
        internal string Name, Version, Author, Description;
    }

    internal sealed class PrefGroup
    {
        internal string Id;
        internal string Name;
        internal bool Hidden;
        internal readonly List<Pref> Prefs = new List<Pref>();
    }

    internal enum RowKind { Toggle, Drum }

    internal sealed class Row
    {
        internal Pref Pref;
        internal RowKind Kind;
        internal string[] Labels;
        internal object[] Values;
        internal int Column;
        internal float Y;
    }

    internal static class Catalog
    {
        internal const int MaxSteps = 21;

        internal static bool Shows(Pref p)
        {
            if (p == null || p.Hidden) return false;
            switch (p.Kind)
            {
                case PrefKind.Bool: return true;
                case PrefKind.Enum: return !p.Flags && p.EnumNames != null && p.EnumNames.Length > 0;
                case PrefKind.Int:
                case PrefKind.Float:
                case PrefKind.Double: return p.Ranged && p.Max > p.Min;
                default: return false;
            }
        }

        internal static bool Keybind(string typeName)
        {
            return typeName == "UnityEngine.InputSystem.Key" || typeName == "UnityEngine.KeyCode";
        }

        internal static string Describe(Pref p)
        {
            string d = p.Description ?? "";
            if (p.Heard) return d;
            return d.Length == 0 ? "Applies after restart." : d + " (applies after restart)";
        }

        internal static string Header(string category, ModInfo m)
        {
            if (m == null) return category ?? "";
            StringBuilder b = new StringBuilder(string.IsNullOrEmpty(m.Name) ? category : m.Name);
            if (!string.IsNullOrEmpty(m.Version)) b.Append(' ').Append(m.Version);
            if (!string.IsNullOrEmpty(m.Author)) b.Append(" by ").Append(m.Author);
            if (!string.IsNullOrEmpty(m.Description)) b.Append(". ").Append(m.Description);
            return b.ToString();
        }

        internal static bool SameName(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string x = Letters(a);
            return x.Length > 0 && x == Letters(b);
        }

        static string Letters(string s)
        {
            StringBuilder b = new StringBuilder(s.Length);
            foreach (char c in s) if (char.IsLetterOrDigit(c)) b.Append(char.ToLowerInvariant(c));
            return b.ToString();
        }

        internal static List<PrefGroup> Visible(IEnumerable<PrefGroup> all, ICollection<string> hide)
        {
            List<PrefGroup> shown = new List<PrefGroup>();
            foreach (PrefGroup g in all)
            {
                if (g == null || g.Hidden) continue;
                if (hide != null && (hide.Contains(Key(g.Id)) || hide.Contains(Key(g.Name)))) continue;
                foreach (Pref p in g.Prefs)
                {
                    if (!Shows(p)) continue;
                    shown.Add(g);
                    break;
                }
            }
            return shown;
        }

        internal static HashSet<string> HideList(string text)
        {
            HashSet<string> set = new HashSet<string>();
            if (string.IsNullOrEmpty(text)) return set;
            foreach (string part in text.Split(','))
            {
                string k = Key(part);
                if (k.Length > 0) set.Add(k);
            }
            return set;
        }

        static string Key(string s)
        {
            return s == null ? "" : s.Trim().ToLowerInvariant();
        }

        internal static string Words(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            StringBuilder b = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_' || c == ' ')
                {
                    if (b.Length > 0 && b[b.Length - 1] != ' ') b.Append(' ');
                    continue;
                }
                if (i > 0 && b.Length > 0 && b[b.Length - 1] != ' ')
                {
                    char prev = name[i - 1];
                    bool next = i + 1 < name.Length && char.IsLower(name[i + 1]);
                    if ((char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev)))
                        || (char.IsUpper(c) && char.IsUpper(prev) && next)
                        || (char.IsDigit(c) && char.IsLetter(prev))
                        || (char.IsLetter(c) && char.IsDigit(prev)))
                        b.Append(' ');
                }
                b.Append(c);
            }
            return b.ToString().TrimEnd();
        }

        internal static double[] Steps(double min, double max, bool whole)
        {
            List<double> v = new List<double>();
            if (whole)
            {
                double lo = Math.Ceiling(min), hi = Math.Floor(max);
                if (hi < lo) return new double[] { lo };
                if (hi - lo <= MaxSteps - 1)
                {
                    for (double x = lo; x <= hi; x++) v.Add(x);
                    return v.ToArray();
                }
                for (int i = 0; i < MaxSteps; i++) Add(v, Math.Round(lo + (hi - lo) * i / (MaxSteps - 1)));
                return v.ToArray();
            }
            for (int i = 0; i < MaxSteps; i++)
            {
                double x = Math.Round(min + (max - min) * i / (MaxSteps - 1), 2);
                if (x < min) x = min;
                if (x > max) x = max;
                Add(v, x);
            }
            return v.ToArray();
        }

        static void Add(List<double> v, double x)
        {
            if (v.Count == 0 || v[v.Count - 1] != x) v.Add(x);
        }

        internal static string Label(double v, bool whole)
        {
            if (v == 0) v = 0;
            return v.ToString(whole ? "0" : "0.##", CultureInfo.InvariantCulture);
        }

        internal static Row RowFor(Pref p, object current)
        {
            Row r = new Row { Pref = p };
            if (p.Kind == PrefKind.Bool)
            {
                r.Kind = RowKind.Toggle;
                return r;
            }
            r.Kind = RowKind.Drum;
            List<string> labels = new List<string>();
            List<object> values = new List<object>();
            if (p.Kind == PrefKind.Enum)
            {
                foreach (string n in p.EnumNames)
                {
                    labels.Add(Words(n));
                    values.Add(n);
                }
                string now = current == null ? null : current.ToString();
                if (now != null && Array.IndexOf(p.EnumNames, now) < 0)
                {
                    labels.Add(Words(now));
                    values.Add(now);
                }
            }
            else
            {
                bool whole = p.Kind == PrefKind.Int;
                foreach (double x in Steps(p.Min, p.Max, whole))
                {
                    labels.Add(Label(x, whole));
                    values.Add(x);
                }
                double cur;
                if (TryNumber(current, out cur) && !labels.Contains(Label(cur, whole)))
                {
                    int at = 0;
                    while (at < values.Count && (double)values[at] < cur) at++;
                    labels.Insert(at, Label(cur, whole));
                    values.Insert(at, cur);
                }
            }
            r.Labels = labels.ToArray();
            r.Values = values.ToArray();
            return r;
        }

        internal static string LabelOf(Pref p, object current)
        {
            if (current == null) return "";
            if (p.Kind == PrefKind.Enum) return Words(current.ToString());
            double d;
            return TryNumber(current, out d) ? Label(d, p.Kind == PrefKind.Int) : current.ToString();
        }

        internal static bool TryNumber(object o, out double d)
        {
            d = 0;
            if (o == null || o is bool || o is string) return false;
            try
            {
                d = Convert.ToDouble(o, CultureInfo.InvariantCulture);
                return !double.IsNaN(d) && !double.IsInfinity(d);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static string[] Unique(IList<string> names)
        {
            string[] o = new string[names.Count];
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < names.Count; i++)
            {
                string n = string.IsNullOrEmpty(names[i]) ? "?" : names[i];
                string u = n;
                for (int k = 2; seen.Contains(u); k++) u = n + " (" + k + ")";
                seen.Add(u);
                o[i] = u;
            }
            return o;
        }

        internal static string[] PageLabels(int pages)
        {
            string[] o = new string[Math.Max(pages, 1)];
            for (int i = 0; i < o.Length; i++) o[i] = (i + 1) + "/" + o.Length;
            return o;
        }
    }

    internal static class Layout
    {
        internal static readonly float[] ToggleX = { 1.2f, 0.18f };
        internal static readonly float[] DrumX = { 0.731f, -0.257f };
        internal const float PickerY = 0.463f;
        internal const float Top = 0.373f;
        internal const float Floor = -0.29f;
        internal const float ToggleHalf = 0.0935f;
        internal const float DrumAbove = 0.2f;
        internal const float DrumBelow = 0.09f;

        internal static float X(Row r)
        {
            return (r.Kind == RowKind.Toggle ? ToggleX : DrumX)[r.Column];
        }

        internal static List<List<Row>> Pages(IList<Row> rows)
        {
            List<List<Row>> pages = new List<List<Row>>();
            List<Row> page = null;
            int col = 0;
            float cursor = Top;
            foreach (Row r in rows)
            {
                float up = r.Kind == RowKind.Toggle ? ToggleHalf : DrumAbove;
                float down = r.Kind == RowKind.Toggle ? ToggleHalf : DrumBelow;
                if (page == null)
                {
                    page = new List<Row>();
                    pages.Add(page);
                }
                else if (cursor - up - down < Floor - 0.0001f)
                {
                    cursor = Top;
                    if (++col == 2)
                    {
                        col = 0;
                        page = new List<Row>();
                        pages.Add(page);
                    }
                }
                r.Column = col;
                r.Y = cursor - up;
                cursor -= up + down;
                page.Add(r);
            }
            return pages;
        }
    }
}
