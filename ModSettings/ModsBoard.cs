using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Menu;
using Alta.Networking;
using Alta.Utilities;
using UnityEngine;

namespace TavernEssentials.ModSettings
{
    internal sealed class ModsBoard
    {
        internal const string NoMods = "No mod settings found";

        static readonly Type InitializeOnAwake = typeof(LocalNetworkScene).GetNestedType("InitializeOnAwake", BindingFlags.NonPublic | BindingFlags.Public);

        readonly GameObject _board;
        readonly TextRenderer _heading;
        readonly GameObject _toggle, _drum;
        readonly List<GameObject> _rows = new List<GameObject>();
        LocalNetworkScene _scene;

        List<PrefGroup> _groups = new List<PrefGroup>();
        string[] _modLabels = { "" };
        int _mod, _page;
        List<List<Row>> _pages = new List<List<Row>>();

        MenuGameSetting _modDrum, _pageDrum;
        ModChoiceAccess _modPick, _pagePick;

        ModsBoard(GameObject board, TextRenderer heading, GameObject toggle, GameObject drum)
        {
            _board = board;
            _heading = heading;
            _toggle = toggle;
            _drum = drum;
        }

        internal static string Missing()
        {
            return InitializeOnAwake == null ? "LocalNetworkScene.InitializeOnAwake"
                : Access.DisplayName == null ? "SettingsFieldAccess.displayName" : null;
        }

        internal static ModsBoard Make(GameObject board, TextRenderer heading, IEnumerable<Transform> gameBoards, out string why)
        {
            Transform toggle = null, drum = null;
            foreach (bool asleepOnly in new[] { true, false })
            {
                foreach (Transform b in gameBoards)
                {
                    if (b == null || (asleepOnly && b.gameObject.activeInHierarchy)) continue;
                    foreach (Transform row in b)
                    {
                        if (toggle == null && row.GetComponent<MenuToggleSetting>() != null) toggle = row;
                        if (drum == null && row.GetComponent<ScrollerEnumMenuSetting>() != null) drum = row;
                    }
                }
                if (toggle != null && drum != null) break;
            }
            if (toggle == null || drum == null)
            {
                why = "the game's boards have no " + (toggle == null ? "toggle" : "drum") + " row to copy";
                return null;
            }
            why = null;
            return new ModsBoard(board, heading, Template(toggle, board.transform), Template(drum, board.transform));
        }

        static GameObject Template(Transform row, Transform parent)
        {
            GameObject t = Asleep(row.gameObject, parent);
            foreach (SettingBasedToggler s in t.GetComponentsInChildren<SettingBasedToggler>(true))
                UnityEngine.Object.DestroyImmediate(s);
            t.name = row.GetComponent<MenuToggleSetting>() != null ? "Mods Toggle Template" : "Mods Drum Template";
            return t;
        }

        static GameObject Asleep(GameObject src, Transform parent)
        {
            bool was = src.activeSelf;
            src.SetActive(false);
            GameObject copy;
            try { copy = UnityEngine.Object.Instantiate(src, parent); }
            finally { src.SetActive(was); }
            return copy;
        }

        internal string Open()
        {
            _scene = NetworkSceneManager.Current as LocalNetworkScene;
            string keep = _mod < _groups.Count ? _groups[_mod].Id : null;
            _groups = Catalog.Visible(Prefs.Read(), Catalog.HideList(ModsConfig.Hide));
            if (_modDrum == null) MakePickers();
            if (_groups.Count == 0)
            {
                Clear();
                _heading.Text = NoMods;
                _modDrum.gameObject.SetActive(false);
                _pageDrum.gameObject.SetActive(false);
                return "the Mods board: no mod settings to show.";
            }
            List<string> names = new List<string>();
            _mod = 0;
            for (int i = 0; i < _groups.Count; i++)
            {
                names.Add(_groups[i].Name);
                if (_groups[i].Id == keep) _mod = i;
            }
            _modLabels = Catalog.Unique(names);
            _modPick.Labels = _modLabels;
            _modDrum.gameObject.SetActive(true);
            _modDrum.SetSettings(_modPick);
            ShowMod(_mod);
            return "the Mods board: " + _groups.Count + " mod(s) with settings: " + string.Join(", ", _modLabels) + "."
                + (_scene == null ? " No menu scene found, so the rows' levers were not set up and may not move." : "");
        }

        void MakePickers()
        {
            _modPick = ModChoiceAccess.Make("Mod", "Turn to pick a mod.", _modLabels,
                () => _modLabels[_mod], v => { int i = Array.IndexOf(_modLabels, v); if (i >= 0 && i != _mod) ShowMod(i); });
            _pagePick = ModChoiceAccess.Make("Page", "Turn for more of this mod's settings.", Catalog.PageLabels(1),
                () => _pagePick.Labels[Math.Min(_page, _pagePick.Labels.Length - 1)],
                v => { int i = Array.IndexOf(_pagePick.Labels, v); if (i >= 0 && i != _page) ShowPage(i); });
            _modDrum = Place(_drum, 0, Layout.PickerY, _modPick);
            _pageDrum = Place(_drum, 1, Layout.PickerY, _pagePick);
        }

        void ShowMod(int i)
        {
            _mod = i;
            PrefGroup g = _groups[i];
            _heading.Text = _modLabels[i];
            try { _modPick.Description = Catalog.Header(_modLabels[i], Prefs.MelonOf(g)); }
            catch (Exception e)
            {
                _modPick.Description = _modLabels[i];
                Log.Warn("no header for " + g.Name + ": " + e.GetType().Name + ": " + e.Message);
            }
            List<Row> rows = new List<Row>();
            foreach (Pref p in g.Prefs)
                if (Catalog.Shows(p)) rows.Add(Catalog.RowFor(p, Prefs.Current(p)));
            _pages = Layout.Pages(rows);
            _page = 0;
            _pagePick.Labels = Catalog.PageLabels(_pages.Count);
            _pageDrum.gameObject.SetActive(_pages.Count > 1);
            _pageDrum.SetSettings(_pagePick);
            ShowPage(0);
        }

        void ShowPage(int page)
        {
            Clear();
            _page = page;
            if (page >= _pages.Count) return;
            foreach (Row r in _pages[page])
            {
                Row row = r;
                Pref p = r.Pref;
                if (r.Kind == RowKind.Toggle)
                {
                    ModToggleAccess a = ModToggleAccess.Make(p.Name, Catalog.Describe(p),
                        () => (bool)Prefs.Current(p), v => Changed(p, v));
                    _rows.Add(Place(_toggle, r.Column, r.Y, a).gameObject);
                }
                else
                {
                    ModChoiceAccess a = ModChoiceAccess.Make(p.Name, Catalog.Describe(p), r.Labels,
                        () => Catalog.LabelOf(p, Prefs.Current(p)),
                        v => { int i = Array.IndexOf(row.Labels, v); if (i >= 0) Changed(p, row.Values[i]); });
                    _rows.Add(Place(_drum, r.Column, r.Y, a).gameObject);
                }
            }
        }

        static readonly HashSet<string> _unsaved = new HashSet<string>();

        static void Changed(Pref p, object value)
        {
            string why;
            if (!Prefs.Set(p, value, out why)) return;
            string mod = Prefs.Owner(p);
            string set = "set " + p.Id + " = " + Prefs.Show(Prefs.Current(p));
            if (why == null)
            {
                _unsaved.Remove(mod);
                Log.Msg(set + " (saved).");
            }
            else if (_unsaved.Add(mod)) Log.Warn(set + ", but " + mod + "'s file could not be saved: " + why);
            else Log.Msg(set + " (changed, but not saved).");
        }

        void Clear()
        {
            foreach (GameObject g in _rows) if (g != null) UnityEngine.Object.Destroy(g);
            _rows.Clear();
        }

        MenuGameSetting Place(GameObject template, int column, float y, ISettingsFieldAccess access)
        {
            GameObject g = UnityEngine.Object.Instantiate(template, template.transform.parent);
            g.name = "Mods Row " + access.DisplayName;
            MenuGameSetting row = g.GetComponent<MenuGameSetting>();
            row.SetSettings(access);
            Localize(g);
            Transform t = g.transform;
            Vector3 p = template.transform.localPosition;
            float x = (row is MenuToggleSetting ? Layout.ToggleX : Layout.DrumX)[column];
            t.localPosition = new Vector3(x, y, p.z);
            t.localRotation = template.transform.localRotation;
            g.SetActive(true);
            return row;
        }

        void Localize(GameObject g)
        {
            foreach (Interactable i in g.GetComponentsInChildren<Interactable>(true)) i.IsNetworked = false;
            if (_scene == null) _scene = NetworkSceneManager.Current as LocalNetworkScene;
            if (_scene == null) return;
            foreach (NetworkEntity e in g.GetComponentsInChildren<NetworkEntity>(true))
                if (!e.IsInitialized && e.GetComponent(InitializeOnAwake) == null) _scene.InitializeEntity(e, true);
        }
    }
}
