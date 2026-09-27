using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TavernEssentials.ModSettings
{
    internal static class LeverPad
    {
        internal const string Gear = "settings_gear_TWELVE";
        internal const string Label = "Mods";
        static readonly Color Red = new Color(0.72f, 0.07f, 0.06f);

        internal static string Dress(SelectionLever mods, IList<SelectionLever> others, Transform board)
        {
            Renderer pad = Pad(mods);
            Transform icon = TabIcon(mods);
            if (pad == null || icon == null) return "the Mods pad was left bare (no pad or tab icon found).";

            Vector3 c = pad.bounds.center;
            Vector3 n = icon.up;
            if (n.y < 0) n = -n;
            Vector3 front = Vector3.ProjectOnPlane(Vector3.down, n);
            if (front.sqrMagnitude < 0.0001f) front = Vector3.ProjectOnPlane(-board.forward, n);
            front.Normalize();
            Vector3 right = Vector3.Cross(n, front).normalized;
            float width = Span(pad.bounds, right), depth = Span(pad.bounds, front);
            if (width <= 0 || depth <= 0) return "the Mods pad was left bare (its size read as zero).";

            float w = 0;
            Vector3 off = Vector3.zero;
            Material look = null;
            int count = 0;
            foreach (SelectionLever l in others)
            {
                Renderer p = Pad(l);
                Transform t = TabIcon(l);
                Bounds b;
                if (p == null || t == null || !Union(t, out b)) continue;
                Vector3 d = b.center - p.bounds.center;
                off += new Vector3(Vector3.Dot(d, right), Vector3.Dot(d, front), Vector3.Dot(d, n));
                w += Span(b, right);
                if (look == null) look = Look(t);
                count++;
            }
            if (count > 0) { w /= count; off /= count; }

            string gear = count == 0 ? "no gear icon (the other levers show no icon to size it by)"
                : PlaceGear(mods, icon, c + right * off.x + front * off.y + n * off.z, n, 0.9f * w, look);
            string label = PlaceLabel(pad, others, mods.transform.root, right, front, n);
            return "the Mods pad: " + gear + "; " + label + " (pad " + F(width) + " x " + F(depth) + ", face "
                + F(Vector3.Angle(n, Vector3.up)) + " deg from level).";
        }

        static string PlaceGear(SelectionLever mods, Transform icon, Vector3 want, Vector3 n, float w, Material look)
        {
            MeshFilter found = null;
            Transform root = mods.transform.root;
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null && f.sharedMesh.name.StartsWith(Gear)) { found = f; break; }
            if (found == null) return "no gear icon (the " + Gear + " mesh is not in the menu)";

            GameObject g = Object.Instantiate(found.gameObject, icon);
            g.name = "Mods Icon";
            g.SetActive(true);
            Vector3 size = found.sharedMesh.bounds.size;
            Vector3 thin = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
            float across = thin == Vector3.right ? Mathf.Max(size.y, size.z) : thin == Vector3.up ? Mathf.Max(size.x, size.z) : Mathf.Max(size.x, size.y);
            g.transform.rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.FromToRotation(thin, Vector3.up);
            g.transform.localScale = Vector3.one;
            float scale = g.transform.lossyScale.x;
            if (across <= 0 || scale <= 0) { Object.Destroy(g); return "no gear icon (its mesh reads as flat)"; }
            g.transform.localScale = Vector3.one * (w / (across * scale));
            string wear = " in its own material (no other icon has one to copy)";
            if (look != null)
            {
                foreach (Renderer x in g.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] ms = x.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) ms[i] = look;
                    x.sharedMaterials = ms;
                }
                wear = " in the other icons' material " + look.name + (look.HasProperty("_MainTex") && look.mainTexture != null ? " (textured)" : "");
            }
            Renderer r = g.GetComponentInChildren<Renderer>();
            g.transform.position += want - (r != null ? r.bounds.center : g.transform.position);
            return "gear icon " + F(w) + " across" + wear;
        }

        static string PlaceLabel(Renderer pad, IList<SelectionLever> others, Transform root, Vector3 right, Vector3 front, Vector3 n)
        {
            TextRenderer[] texts = root.GetComponentsInChildren<TextRenderer>();
            TextRenderer model = null;
            Vector3 off = Vector3.zero;
            int count = 0;
            foreach (SelectionLever l in others)
            {
                Renderer p = Pad(l);
                TextRenderer t = p == null ? null : NameOn(p, texts);
                if (t == null) continue;
                off += t.transform.position - p.bounds.center;
                if (model == null) model = t;
                count++;
            }
            if (model == null) return "no label (no other pad has a name to copy)";
            off /= count;
            GameObject g = Object.Instantiate(model.gameObject, model.transform.parent);
            g.name = "Mods Label";
            g.SetActive(true);
            g.GetComponent<TextRenderer>().Text = Label;
            g.transform.position = pad.bounds.center + off;
            return "label \"" + Label + "\", a copy of \"" + model.Text + "\" (" + count + " named pad(s)) at "
                + F(Vector3.Dot(off, front)) + " towards the front edge, " + F(Vector3.Dot(off, n)) + " along the face and "
                + F(Vector3.Dot(off, right)) + " across, from the pad's centre";
        }

        internal static TextRenderer NameOn(Renderer pad, TextRenderer[] texts)
        {
            Bounds reach = pad.bounds;
            reach.Expand(0.1f);
            TextRenderer best = null;
            float bestD = float.MaxValue;
            foreach (TextRenderer t in texts)
            {
                if (t == null || t.gameObject.name == "Mods Label" || !reach.Contains(t.transform.position)) continue;
                float d = (t.transform.position - pad.bounds.center).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        static Material Look(Transform icon)
        {
            foreach (Renderer r in icon.GetComponentsInChildren<Renderer>())
                if (r.enabled && r.sharedMaterial != null) return r.sharedMaterial;
            return null;
        }

        internal static Renderer Pad(SelectionLever l)
        {
            foreach (Transform child in l.transform)
            {
                Renderer r = child.GetComponent<Renderer>();
                if (r != null) return r;
            }
            return null;
        }

        internal static Transform TabIcon(SelectionLever l)
        {
            foreach (Transform child in l.transform)
                if (child.GetComponents<Component>().Length == 1) return child;
            return null;
        }

        static bool Union(Transform t, out Bounds b)
        {
            b = new Bounds();
            bool any = false;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (any) b.Encapsulate(r.bounds);
                else { b = r.bounds; any = true; }
            }
            return any;
        }

        static float Span(Bounds b, Vector3 dir)
        {
            Vector3 e = b.extents;
            return 2 * (Mathf.Abs(dir.x) * e.x + Mathf.Abs(dir.y) * e.y + Mathf.Abs(dir.z) * e.z);
        }

        static string F(float v)
        {
            return v.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}
