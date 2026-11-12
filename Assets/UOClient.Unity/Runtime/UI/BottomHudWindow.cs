using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The bottom HUD: a health orb and a mana orb at the ends of a panel along the bottom of the screen, the stamina
    /// bar across its top, a row of action slots (a spell, a skill or an item each: click to use, right-click to choose)
    /// and the menu buttons with the war / peace switch. It sizes itself to the screen (BottomHudLayout) and stays put:
    /// it is neither closed nor moved; the options switch it off. The orbs' levels glide to their value, a lost chunk
    /// lingers pale for a moment, and the health orb flashes when hit; poison turns it green.
    /// </summary>
    public sealed class BottomHudWindow : UiWindow
    {
        public const string WindowKey = "bottomhud";

        /// <summary>A line for the player (the journal's system text); set by the game.</summary>
        public static Action<string> Say;

        /// <summary>The options' switch (GameOptions.More.cs bottomHud).</summary>
        public static bool Enabled { get; set; } = true;

        public override bool Themed => true;
        public override string Key => WindowKey;
        public override bool CanClose => false;
        public override bool ClosesOnEsc => false;
        public override bool Movable => false;
        public override bool StepsAsideOnOpen => false;

        [Serializable]
        private sealed class Saved
        {
            public List<HudSlot> slots = new List<HudSlot>(); // the first bar (and the whole file before there were more)
            public List<Bar> more = new List<Bar>();          // the second bar on
            public int page;                                  // the bar on show
        }

        [Serializable]
        private sealed class Bar
        {
            public List<HudSlot> slots = new List<HudSlot>();
        }

        /// <summary>A bar's slots (made if it has none yet).</summary>
        private List<HudSlot> BarSlots(int page)
        {
            if (page <= 0)
                return _saved.slots;
            while (_saved.more.Count < page)
                _saved.more.Add(new Bar());
            return _saved.more[page - 1].slots ??= new List<HudSlot>();
        }

        /// <summary>The bar on show, 0-based.</summary>
        private int Page => Mathf.Clamp(_saved.page, 0, BottomHudLayout.Pages - 1);

        public override void OnClosed()
        {
            if (Hotkeys.SlotPressed == _slotKey)
                Hotkeys.SlotPressed = null;
            if (Hotkeys.BarTurned == _barKey)
                Hotkeys.BarTurned = null;
        }

        private void TurnPage(int step)
        {
            _saved.page = BottomHudLayout.NextPage(Page, step);
            Save();
        }

        private static string FilePath => Path.Combine(ClientPaths.ProjectRoot, "UserSettings", "unity-uo-hudbar.json");

        private readonly Saved _saved;
        private float _hits = -1f, _mana = -1f, _stam = -1f, _hitsTrail, _manaTrail, _flash, _lastHits = -1f;
        private GUIStyle _number, _small, _menu, _slotText, _pager, _pageNumber;

        private static readonly (string Label, string Command)[] Menu =
        {
            ("Paperdoll", "paperdoll"), ("Pack", "backpack"), ("Skills", "skills"), ("Journal", "journal"),
            ("Map", "worldmap"), ("Options", "options"), ("Assistant", "assistant")
        };

        private readonly Action<int> _slotKey, _barKey;
        private readonly Dictionary<string, (float Start, float End)> _cool = new Dictionary<string, (float, float)>();
        private float _width = BottomHudLayout.MinWidth;

        public BottomHudWindow()
        {
            _saved = Load();
            // The number row (Hotkeys): the slots of the bar on show.
            _slotKey = i =>
            {
                if (i < BottomHudLayout.SlotCount(_width))
                    Use(Page, i, BottomHudLayout.Fit(BarSlots(Page), BottomHudLayout.SlotCount(_width))[i]);
            };
            _barKey = TurnPage;
            Hotkeys.SlotPressed = _slotKey;
            Hotkeys.BarTurned = _barKey;
            Rect = new Rect(0, 0, BottomHudLayout.MinWidth, BottomHudLayout.Height);
        }

        private static Saved Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonUtility.FromJson<Saved>(File.ReadAllText(FilePath)) ?? new Saved();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient hud: could not read the action bar ({e.Message}); starting empty.");
            }
            return new Saved();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(_saved, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient hud: could not save the action bar ({e.Message}).");
            }
        }

        /// <summary>Sets a slot of a bar (the picker's answer) and saves.</summary>
        public void SetSlot(int page, int index, HudSlot slot)
        {
            List<HudSlot> slots = BarSlots(page);
            while (slots.Count <= index)
                slots.Add(new HudSlot());
            slots[index] = slot ?? new HudSlot();
            Save();
        }

        public override void Draw()
        {
            EnsureStyles();
            float sw = Screen.width / Ui.Scale, sh = Screen.height / Ui.Scale;
            float width = BottomHudLayout.Width(sw), height = BottomHudLayout.Height;
            _width = width;
            Rect = new Rect(Mathf.Round((sw - width) / 2f), sh - height - 4f, width, height);

            Mobile me = Session.World.Player;
            if (me == null)
                return;
            float hits = BottomHudLayout.Ratio(me.Hits, me.HitsMax), mana = BottomHudLayout.Ratio(me.Mana, me.ManaMax),
                  stam = BottomHudLayout.Ratio(me.Stamina, me.StaminaMax);
            if (Event.current.type == EventType.Repaint)
                Animate(hits, mana, stam);

            (float px, float pw) = BottomHudLayout.Panel(width);
            float panelY = height - BottomHudLayout.PanelHeight;

            // The panel, with the stamina bar along its top edge.
            DrawPanel(new Rect(px, panelY, pw, BottomHudLayout.PanelHeight));
            var bar = new Rect(px + BottomHudLayout.Overlap + 6f, panelY - BottomHudLayout.StaminaHeight + 3f,
                               pw - 2f * (BottomHudLayout.Overlap + 6f), BottomHudLayout.StaminaHeight);
            Fill(bar, PaperdollSkin.GoldDim);
            Fill(new Rect(bar.x + 1, bar.y + 1, bar.width - 2, bar.height - 2), new Color(0.05f, 0.045f, 0.04f));
            var filled = new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * Mathf.Clamp01(_stam), bar.height - 4);
            float low = _stam < 0.25f ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f) : 0f; // nearly spent: it pulses
            Fill(filled, Color.Lerp(new Color(0.85f, 0.66f, 0.2f), new Color(1f, 0.35f, 0.2f), low));
            if (filled.width > 4f)
            {
                // A band of light running along it, so the bar reads as alive even when it is not changing.
                const float band = 70f;
                float at = Mathf.Repeat(Time.unscaledTime * 90f, filled.width + band) - band;
                GUI.BeginGroup(filled);
                Tint(new Rect(at, 0, band, filled.height), Orbs.Sheen, new Color(1f, 1f, 1f, 0.5f));
                GUI.EndGroup();
            }
            GUI.Label(bar, $"{me.Stamina} / {me.StaminaMax}", _small);

            // The tooltip is whatever the pointer is over this frame: without this the last slot's stayed up while
            // the pointer sat on a menu button, as if the slot were still the thing pointed at.
            if (Event.current.type == EventType.Repaint)
                HoveredText = null;
            DrawSlots(width, panelY);
            DrawMenu(px, pw, panelY);

            // The orbs over the panel's ends.
            bool poisoned = (me.Flags & 0x04) != 0;
            DrawOrb(width, true, _hitsOrb, _hits, _hitsTrail, poisoned ? new Color(0.25f, 0.75f, 0.2f) : new Color(0.82f, 0.1f, 0.08f),
                    $"{me.Hits}", $"/ {me.HitsMax}", _flash, poisoned ? null : "orb_red");
            DrawOrb(width, false, _manaOrb, _mana, _manaTrail, new Color(0.14f, 0.32f, 0.9f), $"{me.Mana}", $"/ {me.ManaMax}", 0f, "orb_blue");
            // Sparkles where something just happened (a slot used, health coming back).
            if (_healed)
            {
                _healed = false;
                (float hx, float hy) = BottomHudLayout.OrbCentre(width, true);
                float o = BottomHudLayout.Orb * 1.2f;
                _bursts.Play("sparkle", new Rect(hx - o / 2f, hy - o / 2f, o, o));
            }
            _bursts.Draw();
        }

        /// <summary>Only the panel and the holders take the pointer; the rest of the HUD's box is the world's.</summary>
        public override bool Covers(Vector2 local) => BottomHudLayout.Covers(Rect.width, local.x, local.y);

        // ---- the art (Resources/Hud, made from the generated pictures by tools/hud_art_prepare.py) ----

        private static Texture2D _holderArt, _panelArt, _slotArt;
        private static bool _artLooked;

        private static void LoadArt()
        {
            if (_artLooked)
                return;
            _artLooked = true;
            _holderArt = Resources.Load<Texture2D>("Hud/orb_holder");
            _panelArt = Resources.Load<Texture2D>("Hud/hud_panel");
            _slotArt = Resources.Load<Texture2D>("Hud/slot_frame");
        }

        /// <summary>
        /// The panel: the art's two ends at their own shape (scaled to the panel's height) and its plain middle stretched
        /// between them, so the knotwork never squashes whatever the screen's width. Without the art, the iron frame.
        /// </summary>
        private static void DrawPanel(Rect r)
        {
            LoadArt();
            if (_panelArt == null)
            {
                IronSkin.Frame(r);
                return;
            }
            const float capShare = 0.18f;
            float k = r.height / _panelArt.height;
            float cap = Mathf.Min(_panelArt.width * capShare * k, r.width * 0.3f);
            GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y, cap, r.height), _panelArt, new Rect(0, 0, capShare, 1));
            GUI.DrawTextureWithTexCoords(new Rect(r.x + cap, r.y, r.width - 2f * cap, r.height), _panelArt, new Rect(capShare, 0, 1f - 2f * capShare, 1));
            GUI.DrawTextureWithTexCoords(new Rect(r.xMax - cap, r.y, cap, r.height), _panelArt, new Rect(1f - capShare, 0, capShare, 1));
        }

        private void Animate(float hits, float mana, float stam)
        {
            float dt = Mathf.Min(0.1f, Time.unscaledDeltaTime);
            if (_hits < 0f)
            {
                _hits = _hitsTrail = hits;
                _mana = _manaTrail = mana;
                _stam = stam;
                _lastHits = hits;
            }
            if (hits < _lastHits - 0.004f)
                _flash = 1f; // struck
            if (hits > _lastHits + 0.03f && _lastHits >= 0f)
                _healed = true; // a heal: sparkles over the orb
            _lastHits = hits;
            _flash = Mathf.Max(0f, _flash - dt * 2.2f);
            _hits = BottomHudLayout.Ease(_hits, hits, dt);
            _mana = BottomHudLayout.Ease(_mana, mana, dt);
            _stam = BottomHudLayout.Ease(_stam, stam, dt);
            // What was lost lingers pale, then drains after the level; a gain takes the trail with it at once.
            _hitsTrail = _hitsTrail < _hits ? _hits : BottomHudLayout.Ease(_hitsTrail, _hits, dt, 2.2f);
            _manaTrail = _manaTrail < _mana ? _mana : BottomHudLayout.Ease(_manaTrail, _mana, dt, 2.2f);
        }

        // ---- the orbs ----

        private readonly FlipBursts _bursts = new FlipBursts();
        private bool _healed;
        private readonly LiquidOrb _hitsOrb = new LiquidOrb(0.0f), _manaOrb = new LiquidOrb(3.7f);

        private void DrawOrb(float width, bool left, LiquidOrb orb, float level, float trail, Color liquid, string value, string max, float flash,
                             string flip = null)
        {
            LoadArt();
            (float cx, float cy) = BottomHudLayout.OrbCentre(width, left);
            float size = BottomHudLayout.Orb;
            if (_holderArt != null)
                size = BottomHudLayout.OrbArt * BottomHudLayout.OrbOpening * 1.04f / 0.93f; // the liquid fills the opening, under its rim
            var r = new Rect(cx - size / 2f, cy - size / 2f, size, size);
            // The liquid is alive: its surface rolls, its body swirls and bubbles rise (LiquidOrb paints it as it goes).
            if (Event.current.type == EventType.Repaint)
                orb.Paint(level, trail, liquid, Time.unscaledTime);
            GUI.DrawTexture(r, orb.Texture);
            // The generated liquid (a looping video of it swirling, Resources/Hud/anim) under the painted surface line:
            // shown from just below the surface down, so the rolling surface and what was lost stay the painted ones.
            FlipBook swirl = flip != null ? FlipBook.Get(flip) : null;
            if (swirl != null && level > 0.01f)
            {
                float baseY = -0.93f + 1.86f * Mathf.Clamp01(level);
                float surface = r.y + r.height * (1f - (baseY + 1f) / 2f) + r.height * 0.03f;
                float inner = r.width * 0.93f;
                var disc = new Rect(r.center.x - inner / 2f, r.center.y - inner / 2f, inner, inner);
                if (surface < disc.yMax)
                {
                    GUI.BeginGroup(new Rect(disc.x, surface, disc.width, disc.yMax - surface));
                    Color was = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, 0.92f);
                    GUI.DrawTexture(new Rect(0, disc.y - surface, disc.width, disc.height), swirl.At(Time.unscaledTime, true));
                    GUI.color = was;
                    GUI.EndGroup();
                }
            }
            Tint(r, Orbs.Glass, Color.white);
            if (flash > 0.01f)
                Tint(r, Orbs.Disc, new Color(1f, 0.25f, 0.2f, 0.45f * flash));
            if (_holderArt != null)
            {
                // The holder over the liquid; the mana side's is the health side's in a mirror.
                float a = BottomHudLayout.OrbArt;
                var hr = new Rect(cx - a / 2f, cy - a / 2f, a, a);
                GUI.DrawTextureWithTexCoords(hr, _holderArt, left ? new Rect(0, 0, 1, 1) : new Rect(1, 0, -1, 1));
            }
            else
            {
                Tint(r, Orbs.Rim, Color.white);
            }
            var text = new Rect(r.x, r.y + r.height * 0.34f, r.width, 26f);
            Shadowed(text, value, _number);
            Shadowed(new Rect(r.x, text.yMax - 6f, r.width, 18f), max, _small);
        }

        /// <summary>
        /// An orb's moving liquid, painted into a small texture about 30 times a second: a surface that rolls in two
        /// waves, a body that swirls (two layers of drifting noise), a bright line under the surface, what was just
        /// lost shown pale above it, and a few bubbles rising. Cheap: 128 x 128 pixels, the waves worked out per column.
        /// </summary>
        private sealed class LiquidOrb
        {
            private const int N = 128, Noise = 64;
            private const float Inner = 0.93f;
            private static float[] _noise;

            private readonly Texture2D _texture;
            private readonly Color32[] _px = new Color32[N * N];
            private readonly float[] _surface = new float[N];
            private readonly float _phase;
            private float _nextAt;

            public Texture2D Texture => _texture;

            public LiquidOrb(float phase)
            {
                _phase = phase;
                _texture = new Texture2D(N, N, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
                };
            }

            /// <summary>Smooth noise that tiles, sampled with wrap-around.</summary>
            private static float Sample(float x, float y)
            {
                if (_noise == null)
                {
                    var rng = new System.Random(7);
                    var raw = new float[Noise * Noise];
                    for (int i = 0; i < raw.Length; i++)
                        raw[i] = (float)rng.NextDouble();
                    _noise = new float[Noise * Noise];
                    for (int j = 0; j < Noise; j++) // one blur pass: soft blobs rather than grit
                        for (int i = 0; i < Noise; i++)
                        {
                            float sum = 0f;
                            for (int dj = -2; dj <= 2; dj++)
                                for (int di = -2; di <= 2; di++)
                                    sum += raw[((j + dj + Noise) % Noise) * Noise + (i + di + Noise) % Noise];
                            _noise[j * Noise + i] = sum / 25f;
                        }
                }
                x *= Noise;
                y *= Noise;
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                int xa = ((x0 % Noise) + Noise) % Noise, xb = (xa + 1) % Noise, ya = ((y0 % Noise) + Noise) % Noise, yb = (ya + 1) % Noise;
                float top = Mathf.Lerp(_noise[ya * Noise + xa], _noise[ya * Noise + xb], fx);
                float bottom = Mathf.Lerp(_noise[yb * Noise + xa], _noise[yb * Noise + xb], fx);
                return (Mathf.Lerp(top, bottom, fy) - 0.5f) * 4f + 0.5f; // the blur flattened it: stretch it back out
            }

            public void Paint(float level, float trail, Color liquid, float now)
            {
                if (now < _nextAt)
                    return;
                _nextAt = now + 1f / 30f;
                float t = now + _phase;
                level = Mathf.Clamp01(level);
                trail = Mathf.Clamp01(trail);
                // The surface's height per column; the waves calm as the orb nears empty or full.
                float calm = Mathf.Clamp01(Mathf.Min(level, 1f - level) * 8f);
                float baseY = -Inner + 2f * Inner * level, trailY = -Inner + 2f * Inner * trail;
                for (int i = 0; i < N; i++)
                {
                    float x = (i + 0.5f) / N * 2f - 1f;
                    _surface[i] = baseY + calm * (0.028f * Mathf.Sin(x * 6.5f + t * 2.1f) + 0.017f * Mathf.Sin(x * 12.5f - t * 3.3f));
                }
                for (int j = 0; j < N; j++)
                {
                    float y = (j + 0.5f) / N * 2f - 1f; // row 0 is the bottom
                    for (int i = 0; i < N; i++)
                    {
                        float x = (i + 0.5f) / N * 2f - 1f;
                        float d2 = x * x + y * y;
                        if (d2 > 0.9f)
                        {
                            float d = Mathf.Sqrt(d2);
                            if (d >= Inner + 0.02f)
                            {
                                _px[j * N + i] = default;
                                continue;
                            }
                        }
                        float edge = d2 / (Inner * Inner);
                        float alpha = Mathf.Clamp01((Inner * Inner - d2) * 30f);
                        float r = 0.035f, g = 0.03f, b = 0.03f; // the empty glass
                        float s = _surface[i];
                        if (y <= s)
                        {
                            float n = Sample(x * 0.55f + t * 0.021f, y * 0.55f - t * 0.034f) * 0.6f
                                      + Sample(x * 1.15f - t * 0.043f + 0.37f, y * 1.15f + t * 0.017f) * 0.4f;
                            float body = 0.5f + 0.75f * Mathf.Clamp01(n);
                            body *= 0.72f + 0.28f * (y + 1f) * 0.5f;                   // deeper is darker
                            float glow = Mathf.Clamp01(1f - (s - y) / 0.075f);        // the bright line under the surface
                            r = liquid.r * body + glow * 0.42f;
                            g = liquid.g * body + glow * 0.34f;
                            b = liquid.b * body + glow * 0.34f;
                            // Bubbles: three columns of them, each rising and winking out near the top.
                            for (int k = 0; k < 3; k++)
                            {
                                float bx = -0.45f + k * 0.42f + 0.06f * Mathf.Sin(t * (0.9f + k * 0.3f) + k);
                                float rise = Mathf.Repeat(t * (0.16f + k * 0.045f) + k * 0.37f, 1f);
                                float by = -Inner + rise * (s + Inner);
                                float dx = x - bx, dy = y - by;
                                float size = 0.035f + 0.012f * k;
                                float inBubble = 1f - (dx * dx + dy * dy) / (size * size);
                                if (inBubble > 0f)
                                {
                                    float add = inBubble * 0.3f * (1f - rise);
                                    r += add;
                                    g += add;
                                    b += add;
                                }
                            }
                        }
                        else if (y <= trailY)
                        {
                            r = Mathf.Lerp(r, liquid.r + 0.25f, 0.5f);
                            g = Mathf.Lerp(g, liquid.g + 0.25f, 0.5f);
                            b = Mathf.Lerp(b, liquid.b + 0.25f, 0.5f);
                        }
                        float shade = Mathf.Lerp(1f, 0.5f, edge * edge); // a ball, not a coin
                        _px[j * N + i] = new Color32((byte)(Mathf.Clamp01(r * shade) * 255f), (byte)(Mathf.Clamp01(g * shade) * 255f),
                                                     (byte)(Mathf.Clamp01(b * shade) * 255f), (byte)(alpha * 247f));
                    }
                }
                _texture.SetPixels32(_px);
                _texture.Apply(false);
            }
        }

        // ---- the action slots ----

        private void DrawSlots(float width, float panelY)
        {
            int n = BottomHudLayout.SlotCount(width);
            int page = Page;
            List<HudSlot> slots = BottomHudLayout.Fit(BarSlots(page), n);
            Event e = Event.current;
            DrawPager(width, panelY, page);
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(BottomHudLayout.SlotX(width, i), panelY + BottomHudLayout.SlotTop, BottomHudLayout.Slot, BottomHudLayout.Slot);
                HudSlot s = slots[i];
                bool over = r.Contains(e.mousePosition);
                LoadArt();
                Rect face = r;
                if (_slotArt != null)
                {
                    // The frame spans the slot and its gap; the picture sits in its opening.
                    float f = BottomHudLayout.Slot + BottomHudLayout.SlotGap, o = f * BottomHudLayout.SlotOpening;
                    var fr = new Rect(r.center.x - f / 2f, r.center.y - f / 2f, f, f);
                    face = new Rect(r.center.x - o / 2f, r.center.y - o / 2f, o, o);
                    Fill(face, new Color(0.03f, 0.028f, 0.026f, 0.92f));
                    DrawSlotFace(s, face);
                    DrawCooldown(page, i, face);
                    GUI.DrawTexture(fr, _slotArt);
                    if (over)
                        Fill(face, new Color(1f, 0.85f, 0.5f, 0.12f));
                }
                else
                {
                    Fill(r, over ? PaperdollSkin.Gold : PaperdollSkin.GoldDim);
                    Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(0.045f, 0.04f, 0.037f));
                    DrawSlotFace(s, new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4));
                    DrawCooldown(page, i, new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2));
                }
                Shadowed(new Rect(r.x + 3, r.y + 1, 14, 13), Hotkeys.SlotKeyLabel(i), _small, TextAnchor.UpperLeft, PaperdollSkin.GoldFaded);
                if (e.type == EventType.MouseDown && over)
                {
                    if (e.button == 1 || s.Kind == HudSlotKind.Empty)
                        Ui.Open(new HudSlotPicker(this, page, i));
                    else if (e.button == 0)
                        Use(page, i, s);
                    e.Use();
                }
                if (over && s.Kind != HudSlotKind.Empty && e.type == EventType.Repaint)
                    HoveredText = SlotName(s) + "\nRight-click to change";
            }
        }

        /// <summary>
        /// Between the slots and the mana orb: up, the bar's number, down. The wheel over the slots turns the bars too.
        /// </summary>
        private void DrawPager(float width, float panelY, int page)
        {
            float x = BottomHudLayout.PagerX(width), y = panelY + BottomHudLayout.SlotTop, w = BottomHudLayout.Pager;
            const float arrow = 15f;
            if (GUI.Button(new Rect(x, y, w, arrow), "\u25B2", _pager))
                TurnPage(-1);
            GUI.Label(new Rect(x, y + arrow, w, BottomHudLayout.Slot - 2f * arrow), (page + 1).ToString(), _pageNumber);
            if (GUI.Button(new Rect(x, y + BottomHudLayout.Slot - arrow, w, arrow), "\u25BC", _pager))
                TurnPage(1);
            Event e = Event.current;
            var row = new Rect(BottomHudLayout.SlotX(width, 0), y, x + w - BottomHudLayout.SlotX(width, 0), BottomHudLayout.Slot);
            if (e.type == EventType.ScrollWheel && row.Contains(e.mousePosition))
            {
                TurnPage(e.delta.y > 0 ? 1 : -1);
                e.Use();
            }
            if (e.type == EventType.Repaint && new Rect(x, y, w, BottomHudLayout.Slot).Contains(e.mousePosition))
                HoveredText = $"Action bar {page + 1} of {BottomHudLayout.Pages}";
        }

        private void DrawSlotFace(HudSlot s, Rect r)
        {
            switch (s.Kind)
            {
                case HudSlotKind.Spell:
                {
                    UiImage icon = Art.Gump(SpellIcon(s.id));
                    if (icon != null)
                        GUI.DrawTexture(r, icon.Texture, ScaleMode.ScaleToFit);
                    else
                        GUI.Label(r, s.label, _slotText);
                    break;
                }
                case HudSlotKind.Skill:
                    GUI.Label(r, s.label, _slotText);
                    break;
                case HudSlotKind.Item:
                {
                    UiImage img = Art.Item(s.graphic, Math.Max(0, s.hue));
                    if (img != null && img.Width > 0 && img.Height > 0)
                    {
                        float k = Mathf.Min(1f, Mathf.Min(r.width / img.Width, r.height / img.Height));
                        GUI.DrawTexture(new Rect(r.center.x - img.Width * k / 2f, r.center.y - img.Height * k / 2f, img.Width * k, img.Height * k), img.Texture);
                    }
                    int have = CountInPack(s.graphic, s.hue);
                    Shadowed(new Rect(r.x, r.yMax - 15f, r.width - 2f, 14f), have > 999 ? "999+" : have.ToString(), _small, TextAnchor.LowerRight,
                             have == 0 ? new Color(1f, 0.4f, 0.35f) : IronSkin.Text);
                    break;
                }
            }
        }

        /// <summary>The magery spells' small icons run from gump 0x08C0; the other schools have their own runs (SpellbookWindow).</summary>
        public static int SpellIcon(int spell) =>
            spell >= 1 && spell <= 64 ? 0x08C0 + spell - 1
            : spell >= 101 && spell < 101 + SpellNames.Necromancy.Length ? 0x5000 + spell - 101
            : spell >= 201 && spell < 201 + SpellNames.Chivalry.Length ? 0x5100 + spell - 201
            : 0;

        public static string SlotName(HudSlot s)
        {
            switch (s.Kind)
            {
                case HudSlotKind.Spell: return "Cast " + s.label;
                case HudSlotKind.Skill: return "Use " + s.label;
                case HudSlotKind.Item: return "Use " + s.label;
                default: return "Empty";
            }
        }

        /// <summary>Uses a slot unless its cooldown is still running, and starts its cooldown.</summary>
        private void Use(int page, int index, HudSlot s)
        {
            if (s == null || s.Kind == HudSlotKind.Empty)
                return;
            string key = page + ":" + index;
            float now = Time.unscaledTime;
            if (_cool.TryGetValue(key, out var c) && now < c.End)
                return;
            if (Activate(s))
            {
                _cool[key] = (now, now + BottomHudLayout.Cooldown(s, Session.World.Player?.Dex ?? 0));
                if (page == Page && index < BottomHudLayout.SlotCount(_width))
                {
                    float f = BottomHudLayout.Slot * 1.8f;
                    float sx = BottomHudLayout.SlotX(_width, index) + BottomHudLayout.Slot / 2f;
                    float sy = BottomHudLayout.Height - BottomHudLayout.PanelHeight + BottomHudLayout.SlotTop + BottomHudLayout.Slot / 2f;
                    _bursts.Play("sparkle", new Rect(sx - f / 2f, sy - f / 2f, f, f));
                }
            }
        }

        /// <summary>A running cooldown: the slot darkened from the top down to the time left, and the seconds over it.</summary>
        private void DrawCooldown(int page, int index, Rect r)
        {
            if (!_cool.TryGetValue(page + ":" + index, out var c))
                return;
            float now = Time.unscaledTime, left = c.End - now;
            if (left <= 0f)
            {
                _cool.Remove(page + ":" + index);
                return;
            }
            float share = Mathf.Clamp01(left / Mathf.Max(0.01f, c.End - c.Start));
            Fill(new Rect(r.x, r.y, r.width, r.height * share), new Color(0f, 0f, 0f, 0.62f));
            Fill(new Rect(r.x, r.y + r.height * share - 1f, r.width, 1f), new Color(1f, 0.85f, 0.45f, 0.7f));
            if (left >= 1f)
                Shadowed(r, Mathf.CeilToInt(left).ToString(), _number);
        }

        /// <summary>Does what a slot holds; false when there was nothing to do (no such item).</summary>
        private bool Activate(HudSlot s)
        {
            switch (s.Kind)
            {
                case HudSlotKind.Spell:
                    Session.CastSpell(s.id);
                    break;
                case HudSlotKind.Skill:
                    Session.UseSkill(s.id);
                    break;
                case HudSlotKind.Item:
                {
                    uint item = FindInPack(s.graphic, s.hue);
                    if (item != 0)
                        Session.Use(item);
                    else
                    {
                        BottomHudWindow.Say?.Invoke($"You have no {s.label}.");
                        return false;
                    }
                    break;
                }
            }
            return true;
        }

        /// <summary>The backpack and every bag in it whose contents the server has sent.</summary>
        private IEnumerable<ContainedItem> PackItems()
        {
            uint pack = Session.World.Worn(Session.World.Player, 21);
            if (pack == 0)
                yield break;
            var bags = new HashSet<uint> { pack };
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (ContainedItem c in Session.World.Contained.Values)
                    if (bags.Contains(c.Container) && Session.World.OpenContainers.ContainsKey(c.Serial) && bags.Add(c.Serial))
                        grew = true;
            }
            foreach (ContainedItem c in Session.World.Contained.Values)
                if (bags.Contains(c.Container))
                    yield return c;
        }

        private uint FindInPack(int graphic, int hue)
        {
            foreach (ContainedItem c in PackItems())
                if (c.Graphic == graphic && (hue < 0 || c.Hue == hue))
                    return c.Serial;
            return 0;
        }

        private int CountInPack(int graphic, int hue)
        {
            int n = 0;
            foreach (ContainedItem c in PackItems())
                if (c.Graphic == graphic && (hue < 0 || c.Hue == hue))
                    n += Math.Max(1, c.Amount);
            return n;
        }

        // ---- the menu row ----

        private void DrawMenu(float px, float pw, float panelY)
        {
            float y = panelY + BottomHudLayout.SlotTop + BottomHudLayout.Slot + 5f;
            float left = px + BottomHudLayout.Overlap + 8f, right = px + pw - BottomHudLayout.Overlap - 8f;
            const float warW = 78f;
            float each = Mathf.Min(86f, (right - left - warW - 8f) / Menu.Length);
            for (int i = 0; i < Menu.Length; i++)
                if (GUI.Button(new Rect(left + i * each, y, each - 3f, BottomHudLayout.MenuHeight), Menu[i].Label, _menu))
                    Ui.Command?.Invoke(Menu[i].Command);

            // War or peace, said plainly and switched here.
            bool war = Session.WarMode;
            Color was = GUI.backgroundColor, text = _menu.normal.textColor;
            GUI.backgroundColor = war ? new Color(1f, 0.35f, 0.3f) : Color.white;
            _menu.normal.textColor = _menu.hover.textColor = war ? new Color(1f, 0.55f, 0.45f) : IronSkin.ButtonText;
            if (GUI.Button(new Rect(right - warW, y, warW, BottomHudLayout.MenuHeight), war ? "WAR" : "Peace", _menu))
                Session.SetWarMode(!war);
            _menu.normal.textColor = text;
            _menu.hover.textColor = IronSkin.TextHot;
            GUI.backgroundColor = was;
        }

        // ---- drawing helpers ----

        private void EnsureStyles()
        {
            if (_number != null)
                return;
            _number = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
            if (PaperdollSkin.SerifFont != null)
                _number.font = PaperdollSkin.SerifFont;
            _number.normal.textColor = new Color(1f, 0.97f, 0.9f);
            _small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10, padding = new RectOffset(0, 0, 0, 0) };
            _small.normal.textColor = IronSkin.Text;
            _menu = new GUIStyle(GUI.skin.button) { fontSize = 11, padding = new RectOffset(2, 2, 1, 1) };
            _slotText = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10, wordWrap = true, padding = new RectOffset(1, 1, 1, 1) };
            _slotText.normal.textColor = IronSkin.ButtonText;
            _pager = new GUIStyle(GUI.skin.button) { fontSize = 8, padding = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleCenter };
            _pageNumber = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold, padding = new RectOffset(0, 0, 0, 0) };
            _pageNumber.normal.textColor = PaperdollSkin.Gold;
        }

        private static void Fill(Rect r, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
        }

        private static void Tint(Rect r, Texture2D t, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, t);
            GUI.color = was;
        }

        private static void Shadowed(Rect r, string text, GUIStyle style, TextAnchor? anchor = null, Color? color = null)
        {
            TextAnchor a = style.alignment;
            Color c = style.normal.textColor;
            if (anchor != null)
                style.alignment = anchor.Value;
            style.normal.textColor = new Color(0, 0, 0, 0.85f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = color ?? c;
            GUI.Label(r, text, style);
            style.normal.textColor = c;
            style.alignment = a;
        }

        /// <summary>The orbs' pictures, painted once: a soft-edged disc, the glass over the liquid, and the gilt rim.</summary>
        private static class Orbs
        {
            private const int N = 256;
            private static Texture2D _disc, _glass, _rim;

            public static Texture2D Disc => _disc ??= Paint((d, x, y) =>
            {
                // A solid disc inside the rim, a little darker toward its edge (a ball, not a coin).
                float a = Mathf.Clamp01((0.93f - d) * 60f);
                float shade = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(d / 0.93f) * Mathf.Clamp01(d / 0.93f));
                return new Color(shade, shade, shade, a);
            });

            public static Texture2D Glass => _glass ??= Paint((d, x, y) =>
            {
                if (d > 0.93f)
                    return Color.clear;
                // A soft highlight up and to the left, and the ball's own shadow round the lower edge.
                float hx = x + 0.34f, hy = y - 0.4f;
                float light = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx * 1.2f + hy * hy * 2.6f) / 0.5f);
                float edge = Mathf.Clamp01((d - 0.62f) / 0.31f);
                float dark = edge * edge * Mathf.Clamp01(0.55f - y * 0.45f) * 0.75f;
                return light > dark ? new Color(1f, 1f, 1f, light * light * 0.42f) : new Color(0f, 0f, 0f, dark);
            });

            public static Texture2D Rim => _rim ??= Paint((d, x, y) =>
            {
                // A gilt ring with a dark line each side, lit from above.
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.945f) / 0.055f);
                if (ring <= 0f)
                    return Color.clear;
                float lit = Mathf.Lerp(0.55f, 1.15f, Mathf.Clamp01(0.5f + y * 0.5f));
                Color gold = new Color(0.86f * lit, 0.69f * lit, 0.38f * lit);
                Color c = Color.Lerp(new Color(0.08f, 0.06f, 0.04f), gold, Mathf.Clamp01(ring * 2.2f - 0.55f));
                c.a = Mathf.Clamp01(ring * 6f);
                return c;
            });

            /// <summary>A soft vertical band, clear at both sides (the stamina bar's travelling light).</summary>
            public static Texture2D Sheen => _sheen ??= Paint((d, x, y) => new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(x)), 2f)));
            private static Texture2D _sheen;

            /// <summary>Paints a square picture from (distance from the centre 0..1.4, x, y) with x right and y up, -1..1.</summary>
            private static Texture2D Paint(Func<float, float, float, Color> at)
            {
                var t = new Texture2D(N, N, TextureFormat.RGBA32, true)
                {
                    hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear
                };
                var px = new Color[N * N];
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                    {
                        float x = (i + 0.5f) / N * 2f - 1f, y = (j + 0.5f) / N * 2f - 1f; // row 0 is the bottom
                        px[j * N + i] = at(Mathf.Sqrt(x * x + y * y), x, y);
                    }
                t.SetPixels(px);
                t.Apply(true, true);
                return t;
            }
        }
    }

    /// <summary>
    /// Chooses what an action slot does: a spell, a skill, or an item pointed at (a client-side cursor); or empties it.
    /// </summary>
    public sealed class HudSlotPicker : UiWindow
    {
        public override bool Themed => true;
        public override string Key => "hudslot";

        private readonly BottomHudWindow _hud;
        private readonly int _page, _index;
        private int _tab, _school;
        private Vector2 _scroll;
        private GUIStyle _title, _row;

        public HudSlotPicker(BottomHudWindow hud, int page, int index)
        {
            _hud = hud;
            _page = page;
            _index = index;
            Rect = new Rect(0, 0, 360, 420);
        }

        public override void OnOpened()
        {
            float sw = Screen.width / Ui.Scale, sh = Screen.height / Ui.Scale;
            Rect.position = new Vector2(Mathf.Round((sw - Rect.width) / 2f), Mathf.Round(sh - BottomHudLayout.Height - Rect.height - 16f));
        }

        public override void Draw()
        {
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
                _title.normal.textColor = PaperdollSkin.Gold;
                if (PaperdollSkin.SerifFont != null)
                    _title.font = PaperdollSkin.SerifFont;
                _row = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 6, 2, 2), fontSize = 13 };
            }
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            GUI.Label(new Rect(16, 12, Rect.width - 32, 24), $"Bar {_page + 1}, slot {_index + 1}", _title);

            GUILayout.BeginArea(new Rect(16, 44, Rect.width - 32, Rect.height - 60));
            _tab = GUILayout.Toolbar(_tab, new[] { "Spell", "Skill", "Item" });
            GUILayout.Space(6);
            _scroll = GUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 0:
                {
                    int was = _school;
                    _school = GUILayout.Toolbar(_school, new[] { "Magery", "Necromancy", "Chivalry" });
                    if (_school != was)
                        _scroll = Vector2.zero;
                    string[] names = _school == 1 ? SpellNames.Necromancy : _school == 2 ? SpellNames.Chivalry : SpellNames.Magery;
                    int first = _school == 1 ? 101 : _school == 2 ? 201 : 1;
                    for (int i = 0; i < names.Length; i++)
                    {
                        GUILayout.BeginHorizontal();
                        UiImage icon = Art.Gump(BottomHudWindow.SpellIcon(first + i));
                        Rect ir = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
                        if (icon != null)
                            GUI.DrawTexture(ir, icon.Texture, ScaleMode.ScaleToFit);
                        string label = _school == 0 ? $"{i / 8 + 1}.  {names[i]}" : names[i];
                        if (GUILayout.Button(label, _row, GUILayout.Height(24)))
                            Choose(new HudSlot { kind = (int)HudSlotKind.Spell, id = first + i, label = names[i] });
                        GUILayout.EndHorizontal();
                    }
                    break;
                }
                case 1:
                    for (int id = 0; id < GameSession.SkillNames.Length; id++)
                        if (GameSession.UsableSkills.Contains(id) && GUILayout.Button(GameSession.SkillNames[id], _row, GUILayout.Height(24)))
                            Choose(new HudSlot { kind = (int)HudSlotKind.Skill, id = id, label = GameSession.SkillNames[id] });
                    break;
                default:
                    GUILayout.Label("Point at an item (a potion, bandages, a tool). The slot then uses the first one like it in your backpack, and shows how many you have.");
                    if (GUILayout.Button("Target an item", GUILayout.Height(28)))
                    {
                        BottomHudWindow hud = _hud;
                        int index = _index, page = _page;
                        GameSession session = Session;
                        string NameOf(int graphic) => Art.Data.TileData.GetStatic(graphic).Name?.Trim() ?? $"0x{graphic:X4}";
                        BottomHudWindow.Say?.Invoke("Target the item for the slot.");
                        session.BeginLocalTarget(serial =>
                        {
                            int graphic = -1, hue = -1;
                            if (session.World.Contained.TryGetValue(serial, out ContainedItem c))
                            {
                                graphic = c.Graphic;
                                hue = c.Hue;
                            }
                            else if (session.World.Items.TryGetValue(serial, out WorldItem w) && !w.IsMulti)
                            {
                                graphic = w.Graphic;
                                hue = w.Hue;
                            }
                            if (graphic < 0)
                                return;
                            hud.SetSlot(page, index, new HudSlot { kind = (int)HudSlotKind.Item, graphic = graphic, hue = hue == 0 ? -1 : hue, label = NameOf(graphic) });
                        });
                        Close();
                    }
                    break;
            }
            GUILayout.EndScrollView();
            GUILayout.Space(6);
            if (GUILayout.Button("Empty this slot", GUILayout.Height(26)))
                Choose(new HudSlot());
            GUILayout.EndArea();
        }

        private void Choose(HudSlot slot)
        {
            _hud.SetSlot(_page, _index, slot);
            Close();
        }
    }
}
