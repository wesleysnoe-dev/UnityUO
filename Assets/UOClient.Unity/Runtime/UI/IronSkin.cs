using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// Our own windows' look, the paperdoll's (PaperdollSkin): the iron and knotwork frame on dark leather, and IMGUI's
    /// controls redrawn to match: dark iron buttons with gilt rims and serif words, gilt checkboxes, inset text boxes,
    /// a gold slider knob, iron scrollbars. A window opts in with <see cref="UiWindow.Themed"/>; UiManager draws it
    /// with <see cref="Skin"/> and its stone background (gump 9200, or the popups' 3000) becomes <see cref="Frame"/>.
    /// </summary>
    public static class IronSkin
    {
        public static readonly Color Text = new Color(0.92f, 0.88f, 0.78f), TextHot = new Color(1f, 0.93f, 0.7f),
            ButtonText = new Color(0.93f, 0.84f, 0.62f);

        private static GUISkin _skin;

        /// <summary>The frame's thickness for a window of this size: slimmer on small popups.</summary>
        public static float BorderFor(Rect r) => Mathf.Clamp(Mathf.Min(r.width, r.height) / 9f, 6f, 12f);

        /// <summary>A themed window's frame and background, filling <paramref name="r"/>.</summary>
        public static void Frame(Rect r) => PaperdollSkin.Frame(r, BorderFor(r), 2.4f);

        /// <summary>A small dark panel with a gilt rim (the hover tooltip).</summary>
        public static void Panel(Rect r)
        {
            // Nine-sliced (the rim's corners and edges kept at their own size, only the middle stretched): stretching the
            // whole 16 x 16 picture over the panel made the one-pixel rim a wide, blurred band, wider along the long side.
            if (_panelStyle == null || _panelStyle.normal.background == null)
            {
                _panelStyle = new GUIStyle { border = new RectOffset(4, 4, 4, 4) };
                _panelStyle.normal.background = Rimmed(ref _panel, new Color(0.07f, 0.065f, 0.06f, 0.94f), new Color(0.04f, 0.035f, 0.03f, 0.94f), PaperdollSkin.GoldDim);
            }
            if (Event.current.type == EventType.Repaint)
                _panelStyle.Draw(r, false, false, false, false);
        }

        private static GUIStyle _panelStyle;

        /// <summary>The skin our windows draw with (made on first use, inside OnGUI, from the default one).</summary>
        public static GUISkin Skin
        {
            get
            {
                if (_skin != null)
                    return _skin;
                GUISkin src = GUI.skin;
                _skin = Object.Instantiate(src);
                _skin.hideFlags = HideFlags.HideAndDontSave;
                Font serif = PaperdollSkin.SerifFont;

                Color iron = new Color(0.17f, 0.155f, 0.14f), ironLow = new Color(0.08f, 0.072f, 0.065f);
                Color ironHot = new Color(0.24f, 0.21f, 0.17f), ironHotLow = new Color(0.11f, 0.095f, 0.08f);

                _skin.label.normal.textColor = Text;

                GUIStyle b = _skin.button;
                if (serif != null)
                    b.font = serif;
                b.border = new RectOffset(4, 4, 4, 4);
                b.normal.background = Rimmed(ref _btn, iron, ironLow, PaperdollSkin.GoldDim);
                b.hover.background = Rimmed(ref _btnHot, ironHot, ironHotLow, PaperdollSkin.Gold);
                b.active.background = Rimmed(ref _btnDown, ironLow, iron, PaperdollSkin.Gold);
                b.focused.background = b.normal.background;
                b.onNormal.background = Rimmed(ref _btnOn, new Color(0.3f, 0.24f, 0.14f), new Color(0.16f, 0.12f, 0.07f), PaperdollSkin.Gold);
                b.onHover.background = b.onNormal.background;
                b.onActive.background = b.active.background;
                b.onFocused.background = b.onNormal.background;
                b.normal.textColor = b.focused.textColor = ButtonText;
                b.hover.textColor = b.active.textColor = TextHot;
                b.onNormal.textColor = b.onHover.textColor = b.onActive.textColor = b.onFocused.textColor = TextHot;

                GUIStyle t = _skin.toggle;
                t.border = new RectOffset(14, 0, 14, 0);
                t.normal.background = t.hover.background = t.active.background = t.focused.background = Check(ref _boxOff, false);
                t.onNormal.background = t.onHover.background = t.onActive.background = t.onFocused.background = Check(ref _boxOn, true);
                t.normal.textColor = t.focused.textColor = t.onNormal.textColor = t.onFocused.textColor = Text;
                t.hover.textColor = t.onHover.textColor = t.active.textColor = t.onActive.textColor = TextHot;

                foreach (GUIStyle f in new[] { _skin.textField, _skin.textArea })
                {
                    f.border = new RectOffset(4, 4, 4, 4);
                    f.normal.background = f.hover.background = Rimmed(ref _field, new Color(0.03f, 0.028f, 0.026f), new Color(0.06f, 0.055f, 0.05f), PaperdollSkin.GoldDim);
                    f.focused.background = f.onFocused.background = Rimmed(ref _fieldHot, new Color(0.04f, 0.036f, 0.032f), new Color(0.07f, 0.064f, 0.058f), PaperdollSkin.Gold);
                    f.normal.textColor = f.hover.textColor = f.focused.textColor = f.onFocused.textColor = Text;
                }

                GUIStyle box = _skin.box;
                box.border = new RectOffset(4, 4, 4, 4);
                box.normal.background = Rimmed(ref _box, new Color(0.05f, 0.046f, 0.042f, 0.85f), new Color(0.03f, 0.028f, 0.025f, 0.85f), PaperdollSkin.GoldDim);
                box.normal.textColor = Text;

                _skin.horizontalSlider.normal.background = Rimmed(ref _track, new Color(0.03f, 0.028f, 0.025f), new Color(0.06f, 0.055f, 0.05f), PaperdollSkin.GoldDim);
                _skin.horizontalSlider.border = new RectOffset(3, 3, 3, 3);
                GUIStyle knob = _skin.horizontalSliderThumb;
                knob.normal.background = Knob(ref _knob, PaperdollSkin.Gold);
                knob.hover.background = knob.active.background = knob.focused.background = Knob(ref _knobHot, new Color(1f, 0.86f, 0.55f));
                knob.border = new RectOffset(0, 0, 0, 0);

                foreach (GUIStyle track in new[] { _skin.verticalScrollbar, _skin.horizontalScrollbar })
                {
                    track.normal.background = Rimmed(ref _sbTrack, new Color(0.03f, 0.028f, 0.025f, 0.9f), new Color(0.05f, 0.046f, 0.042f, 0.9f), new Color(0.3f, 0.24f, 0.14f));
                    track.border = new RectOffset(3, 3, 3, 3);
                }
                foreach (GUIStyle thumb in new[] { _skin.verticalScrollbarThumb, _skin.horizontalScrollbarThumb })
                {
                    thumb.normal.background = Rimmed(ref _sbThumb, iron, ironLow, PaperdollSkin.GoldDim);
                    thumb.hover.background = thumb.active.background = Rimmed(ref _sbThumbHot, ironHot, ironHotLow, PaperdollSkin.Gold);
                    thumb.border = new RectOffset(3, 3, 3, 3);
                }

                _skin.settings.selectionColor = new Color(0.72f, 0.56f, 0.25f, 0.55f);
                _skin.settings.cursorColor = PaperdollSkin.Gold;
                return _skin;
            }
        }

        // ---- the controls' pictures, drawn here ----

        private static Texture2D _btn, _btnHot, _btnDown, _btnOn, _field, _fieldHot, _box, _track, _sbTrack, _sbThumb, _sbThumbHot,
            _boxOff, _boxOn, _knob, _knobHot, _panel;

        private static Texture2D New(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

        /// <summary>A 16x16 nine-slice piece: top-to-bottom fill, a one-pixel rim, a faint highlight inside the top edge.</summary>
        private static Texture2D Rimmed(ref Texture2D cache, Color top, Color bottom, Color rim)
        {
            if (cache != null)
                return cache;
            const int s = 16;
            cache = New(s, s);
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    bool edge = x == 0 || y == 0 || x == s - 1 || y == s - 1;
                    Color c = Color.Lerp(bottom, top, y / (float)(s - 1)); // row 0 is the bottom
                    if (edge)
                        c = new Color(rim.r, rim.g, rim.b, Mathf.Max(top.a, 0.95f));
                    else if (y == s - 2)
                        c = Color.Lerp(c, Color.white, 0.06f);
                    px[y * s + x] = c;
                }
            cache.SetPixels(px);
            cache.Apply(false);
            return cache;
        }

        /// <summary>
        /// A checkbox: a dark inset square with a gilt rim; ticked, a gold diamond in it. The toggle style nine-slices
        /// its picture (the box its fixed top-left corner, the rest stretched under the label), so the box sits in the
        /// top-left 14x14 of a larger clear picture.
        /// </summary>
        private static Texture2D Check(ref Texture2D cache, bool on)
        {
            if (cache != null)
                return cache;
            const int s = 14, size = 32;
            cache = New(size, size);
            var px = new Color[size * size]; // clear
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    bool edge = x == 0 || y == 0 || x == s - 1 || y == s - 1;
                    Color c = edge ? PaperdollSkin.Gold : new Color(0.04f, 0.036f, 0.032f);
                    float d = Mathf.Abs(x - (s - 1) / 2f) + Mathf.Abs(y - (s - 1) / 2f);
                    if (on && !edge && d <= 4.2f)
                        c = Color.Lerp(PaperdollSkin.Gold, new Color(1f, 0.9f, 0.6f), 1f - d / 4.2f);
                    px[(size - s + y) * size + x] = c; // texture rows run bottom up: the box at the top
                }
            cache.SetPixels(px);
            cache.Apply(false);
            return cache;
        }

        /// <summary>The slider's knob: a gold disc with a darker rim.</summary>
        private static Texture2D Knob(ref Texture2D cache, Color gold)
        {
            if (cache != null)
                return cache;
            const int s = 16;
            cache = New(s, s);
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                    Color c = d > 0.78f ? PaperdollSkin.GoldDim * 0.8f : Color.Lerp(gold, gold * 0.75f, (s - y) / (float)s);
                    c.a = Mathf.Clamp01((1f - d) * s / 2f);
                    px[y * s + x] = c;
                }
            cache.SetPixels(px);
            cache.Apply(false);
            return cache;
        }
    }
}
