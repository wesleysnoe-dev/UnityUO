using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>
    /// Fills the HUD model each frame: the player's position and stats, the connection text, the name labels
    /// over everyone in view and their nameplates, the health bars under their feet, where the quest arrow points and the weather. The buff
    /// bar is a window (UI.BuffBarWindow).
    /// </summary>
    public sealed class HudPresenter
    {
        private readonly GameContext _ctx;
        private readonly EntityViews _entities;

        public HudPresenter(GameContext ctx, EntityViews entities)
        {
            _ctx = ctx;
            _entities = entities;
        }

        /// <summary>The options' ShowMobilesHP, MobileHPShowWhen, MobileHPType and ShowTargetIndicator (GameOptions.HealthDisplay).</summary>
        public bool ShowMobilesHP { get; set; } = GameOptions.Defaults().showMobilesHP;
        public int MobileHPShowWhen { get; set; } = GameOptions.Defaults().mobileHPShowWhen;
        public int MobileHPType { get; set; } = GameOptions.Defaults().mobileHPType;
        public bool ShowTargetIndicator { get; set; } = GameOptions.Defaults().showTargetIndicator;

        /// <summary>A hue's text colour (hues.mul), for the % text; until set, TazUO's four hues' rough colours.</summary>
        public Func<int, Color> HueColor { get; set; }

        public void Update()
        {
            _ctx.Hud.UpdatePlayer(_ctx.Player.X, _ctx.Player.Y, _ctx.Player.IsMoving, Time.deltaTime);
            _ctx.Hud.Labels.Clear();
            _ctx.Hud.Plates.Clear();
            _ctx.Hud.QuestArrow = null;
            _ctx.Hud.WeatherCount = 0;
            if (_ctx.Session == null)
                return;

            // Weather: rain or snow particles while the server's weather has something falling.
            Core.Weather weather = _ctx.Session.Weather;
            _ctx.Hud.WeatherCount = weather.IsFalling ? weather.Count : 0;
            _ctx.Hud.WeatherSnow = weather.Type == WeatherType.Snow;
            _ctx.Hud.WeatherStorm = weather.Type == WeatherType.FierceStorm;

            // Quest arrow: the target tile's center at the player's height (the packet has no z), as Avatar anchors it.
            Core.QuestArrow arrow = _ctx.Session.QuestArrow;
            if (arrow != null)
            {
                var tile = new Vector2((arrow.X - arrow.Y) * 22, (arrow.X + arrow.Y) * 22 + 22 - _ctx.Player.Z * 4) * _ctx.ArtScale;
                Vector3 s = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(tile));
                _ctx.Hud.QuestArrow = new Vector2(s.x, Screen.height - s.y);
                _ctx.Hud.QuestArrowTile = new Vector2Int(arrow.X, arrow.Y);
            }

            // Connection text only matters while not playing (connecting, or after a disconnect).
            _ctx.Hud.ConnectionStatus = _ctx.Session.State == SessionState.InWorld ? "" : _ctx.Session.Status;

            Core.Mobile p = _ctx.Session.World.Player;
            if (p != null && p.HitsMax > 0)
            {
                _ctx.Hud.Hits = p.Hits;
                _ctx.Hud.HitsMax = p.HitsMax;
                _ctx.Hud.Mana = p.Mana;
                _ctx.Hud.ManaMax = Math.Max(p.ManaMax, 1);
                _ctx.Hud.Stamina = p.Stamina;
                _ctx.Hud.StaminaMax = Math.Max(p.StaminaMax, 1);
            }

            UpdateNamePlates(p);

            foreach (KeyValuePair<uint, Avatar> kv in _entities.Others)
            {
                if (!_ctx.Session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m) || string.IsNullOrEmpty(m.Name))
                    continue;
                Vector3 s = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(kv.Value.AnchorPixels - new Vector2(0, 75 * _ctx.ArtScale)));
                if (s.x < -50 || s.x > Screen.width + 50 || s.y < -50 || s.y > Screen.height + 50)
                    continue;
                _ctx.Hud.Labels.Add((m.Name, new Vector2(s.x, Screen.height - s.y), DemoHud.NotorietyColor(m.Notoriety)));
            }

            // Health under the feet and over the head (the options' ShowMobilesHP / MobileHPShowWhen / MobileHPType /
            // ShowTargetIndicator): everyone, or anyone hurt plus whoever we fight and ourselves in war mode; the line, the
            // % text or both; a target keeps its line (TazUO's forceDraw).
            _ctx.Hud.HealthBars.Clear();
            void Bar(Core.Mobile m, Avatar a)
            {
                if (m == null || a == null || !a.Visible)
                    return;
                bool alsoWhenFull = m.Serial == _ctx.Session.Combatant
                                    || (m.Serial == _ctx.Session.World.PlayerSerial && _ctx.Session.WarMode);
                bool isTarget = m.Serial != _ctx.Session.World.PlayerSerial
                                && (m.Serial == _ctx.Session.Combatant || m.Serial == _ctx.Session.LastAttack || m.Serial == _ctx.Session.LastTarget);
                var (line, percent) = GameOptions.HealthDisplay(ShowMobilesHP, MobileHPType, MobileHPShowWhen, m.Hits, m.HitsMax,
                    m.IsDead, alsoWhenFull, isTarget, ShowTargetIndicator);
                if (!line && !percent)
                    return;
                Vector3 s = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(a.AnchorPixels + new Vector2(0, 10 * _ctx.ArtScale)));
                Vector3 head = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(a.AnchorPixels - new Vector2(0, 75 * _ctx.ArtScale)));
                int perc = GameOptions.HitsPercent(m.Hits, m.HitsMax);
                int hue = GameOptions.HitsPercentHue(perc);
                _ctx.Hud.HealthBars.Add(new DemoHud.HealthBar
                {
                    Screen = new Vector2(s.x, Screen.height - s.y),
                    Ratio = m.Hits / (float)m.HitsMax,
                    Color = DemoHud.NotorietyColor(m.Notoriety),
                    Line = line,
                    Percent = percent ? perc : -1,
                    Head = new Vector2(head.x, Screen.height - head.y),
                    PercentColor = Hue(hue),
                    Serial = m.Serial,
                });
            }
            Bar(p, _ctx.Player);
            foreach (KeyValuePair<uint, Avatar> kv in _entities.Others)
                if (_ctx.Session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m))
                    Bar(m, kv.Value);
        }

        private Color Hue(int hue) => HueColor != null ? HueColor(hue) : DemoHud.PercentFallbackColor(hue);

        private readonly HashSet<uint> _plateEligible = new HashSet<uint>();

        /// <summary>
        /// The nameplates (TazUO's NameOverHeadManager / NameOverheadGump): every character in view, ourselves included,
        /// that the options' filter allows and that has a name, unless closed with a right-click or hidden at full health,
        /// with its bars from the hits (and our own and party members' mana / stamina) the server sent. No line of sight is
        /// checked (this client has none, nor does TazUO's plate): a plate shows for anyone drawn, culled only by the screen
        /// and an empty name. The plain names over heads stay as well (TazUO draws both, the plate above the name).
        /// DemoHud places and culls them; their order (top of the screen first) is the avoid-overlap order.
        /// </summary>
        private void UpdateNamePlates(Core.Mobile player)
        {
            DemoHud hud = _ctx.Hud;
            GameOptions o = hud.NamePlateOptions;
            hud.Plates.Clear();
            _plateEligible.Clear();
            bool showing = o.nameOverheadToggled;
            if (showing)
            {
                Plate(player, _ctx.Player, true);
                foreach (KeyValuePair<uint, Avatar> kv in _entities.Others)
                    if (_ctx.Session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m))
                        Plate(m, kv.Value, false);
                hud.Plates.Sort((a, b) => a.Head.y != b.Head.y ? a.Head.y.CompareTo(b.Head.y) : a.Serial.CompareTo(b.Serial));
            }
            NamePlates.PruneClosed(hud.ClosedNamePlates, showing, _plateEligible);

            void Plate(Core.Mobile m, Avatar a, bool isPlayer)
            {
                if (m == null || a == null || !a.Visible)
                    return;
                NamePlates.Subject subject = Subject(m, isPlayer);
                if (string.IsNullOrEmpty(m.Name) || !NamePlates.Allowed(o.nameOverheadFlags, subject))
                    return;
                _plateEligible.Add(m.Serial);
                if (!NamePlates.Shows(o, subject, _ctx.Session.WarMode, hud.ClosedNamePlates))
                    return;
                Vector3 head = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(a.AnchorPixels - new Vector2(0, 75 * _ctx.ArtScale)));
                if (head.x < -400 || head.x > Screen.width + 400 || head.y < -200 || head.y > Screen.height + 200)
                    return; // far off screen; DemoHud culls the rest by the plate's real size

                List<NamePlates.Resource> bars = NamePlates.Bars(o, subject);
                var plate = new DemoHud.NamePlate
                {
                    Serial = m.Serial,
                    Name = m.Name,
                    Head = new Vector2(head.x, Screen.height - head.y),
                    NameColor = DemoHud.NotorietyColor(m.Notoriety),
                    LastTarget = m.Serial == _ctx.Session.LastTarget,
                    BarCount = bars.Count,
                };
                int backgroundNotoriety = NamePlates.ResolveBackgroundNotoriety(o.namePlateBackgroundMode, subject);
                plate.Background = backgroundNotoriety >= 0 ? DemoHud.NotorietyColor(backgroundNotoriety)
                    : DemoHud.ToColor(NamePlates.FixedBackground(o));
                for (int i = 0; i < bars.Count; i++)
                {
                    DemoHud.PlateBar bar = PlateBar(o, subject, bars[i]);
                    if (i == 0) plate.Bar0 = bar;
                    else if (i == 1) plate.Bar1 = bar;
                    else plate.Bar2 = bar;
                }
                hud.Plates.Add(plate);
            }
        }

        private NamePlates.Subject Subject(Core.Mobile m, bool isPlayer) => new NamePlates.Subject
        {
            Serial = m.Serial,
            Name = m.Name,
            Notoriety = m.Notoriety,
            IsPlayer = isPlayer,
            InParty = _ctx.Session.PartyMembers.Contains(m.Serial),
            IsHuman = m.IsHuman,
            Renamable = m.Renamable,
            Poisoned = m.Poisoned,
            YellowHits = m.YellowHits,
            Hits = m.Hits,
            HitsMax = m.HitsMax,
            Mana = m.Mana,
            ManaMax = m.ManaMax,
            Stamina = m.Stamina,
            StaminaMax = m.StaminaMax,
        };

        /// <summary>A bar's fill: health in the chosen fixed colour or the status hue (the notoriety colour at full for others); mana and stamina by their thresholds.</summary>
        private DemoHud.PlateBar PlateBar(GameOptions o, in NamePlates.Subject m, NamePlates.Resource r)
        {
            double percent;
            Color fill;
            switch (r)
            {
                case NamePlates.Resource.Hits:
                    percent = NamePlates.Percent(m.Hits, m.HitsMax);
                    if (NamePlates.FixedFill(o.namePlateHealthBarMode) is NamePlates.Rgb fixedFill)
                        fill = DemoHud.ToColor(fixedFill);
                    else
                    {
                        int hue = NamePlates.StatusHue(m, percent);
                        fill = hue < 0 ? DemoHud.NotorietyColor(m.Notoriety) : Hue(hue);
                    }
                    break;
                case NamePlates.Resource.Mana:
                    percent = NamePlates.Percent(m.Mana, m.ManaMax);
                    fill = Hue(NamePlates.ResourceHue(r, percent));
                    break;
                default:
                    percent = NamePlates.Percent(m.Stamina, m.StaminaMax);
                    fill = Hue(NamePlates.ResourceHue(r, percent));
                    break;
            }
            return new DemoHud.PlateBar { Resource = r, Percent = (float)percent, Fill = fill };
        }
    }
}
