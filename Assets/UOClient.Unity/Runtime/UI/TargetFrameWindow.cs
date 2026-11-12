using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The target frame, part of the bottom HUD: at the top of the screen, the one you are fighting (the server's
    /// combatant, else your last attack): name in its notoriety colour, health bar gliding to its value with what was
    /// just lost lingering pale, and how far away it is. A click on it with a target cursor up targets it; a double-click
    /// attacks it (as a double-click on it in the world). It shows nothing while there is no one, or they are dead or out
    /// of sight.
    /// </summary>
    public sealed class TargetFrameWindow : UiWindow
    {
        public const string WindowKey = "targetframe";
        private const float Width = 320f, Height = 58f, Top = 44f;

        public override bool Themed => true;
        public override string Key => WindowKey;
        public override bool CanClose => false;
        public override bool ClosesOnEsc => false;
        public override bool Movable => false;
        public override bool StepsAsideOnOpen => false;

        private uint _serial;
        private float _shown = -1f, _trail;
        private GUIStyle _name, _small;

        public TargetFrameWindow() => Rect = new Rect(0, 0, 0, 0);

        /// <summary>Who the frame is about: the server's combatant first, else the last one we attacked.</summary>
        private Mobile Subject(out uint serial)
        {
            GameSession s = Session;
            serial = s.Combatant != 0 ? s.Combatant : s.LastAttack;
            if (serial == 0 || serial == s.World.PlayerSerial || !s.World.Mobiles.TryGetValue(serial, out Mobile m) || m.IsDead)
                return null;
            return m;
        }

        public override void Draw()
        {
            Mobile m = Subject(out uint serial);
            float sw = Screen.width / Ui.Scale;
            if (m == null)
            {
                Rect = new Rect(Mathf.Round(sw / 2f), Top, 0, 0); // nothing shown, nothing to click or hover
                _serial = 0;
                return;
            }
            Rect = new Rect(Mathf.Round((sw - Width) / 2f), Top, Width, Height);
            if (_name == null)
            {
                _name = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 15, clipping = TextClipping.Clip };
                if (PaperdollSkin.SerifFont != null)
                    _name.font = PaperdollSkin.SerifFont;
                _small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = 11 };
                _small.normal.textColor = PaperdollSkin.GoldFaded;
            }

            float hits = BottomHudLayout.Ratio(m.Hits, m.HitsMax);
            if (serial != _serial || _shown < 0f)
            {
                _serial = serial;
                _shown = _trail = hits;
            }
            if (Event.current.type == EventType.Repaint)
            {
                float dt = Mathf.Min(0.1f, Time.unscaledDeltaTime);
                _shown = BottomHudLayout.Ease(_shown, hits, dt);
                _trail = _trail < _shown ? _shown : BottomHudLayout.Ease(_trail, _shown, dt, 2.2f);
            }

            IronSkin.Frame(new Rect(0, 0, Width, Height));
            _name.normal.textColor = DemoHud.NotorietyColor(m.Notoriety);
            GUI.Label(new Rect(14, 6, Width - 90, 22), string.IsNullOrEmpty(m.Name) ? "Someone" : m.Name, _name);
            Mobile me = Session.World.Player;
            if (me != null)
                GUI.Label(new Rect(Width - 84, 6, 70, 22), $"{System.Math.Max(System.Math.Abs(m.X - me.X), System.Math.Abs(m.Y - me.Y))} tiles", _small);

            var bar = new Rect(14, 30, Width - 28, 15);
            Fill(bar, PaperdollSkin.GoldDim);
            Fill(new Rect(bar.x + 1, bar.y + 1, bar.width - 2, bar.height - 2), new Color(0.05f, 0.045f, 0.04f));
            var inner = new Rect(bar.x + 2, bar.y + 2, bar.width - 4, bar.height - 4);
            Color fill = m.Poisoned ? new Color(0.3f, 0.75f, 0.2f) : m.YellowHits ? new Color(0.85f, 0.75f, 0.2f) : new Color(0.78f, 0.1f, 0.08f);
            Fill(new Rect(inner.x, inner.y, inner.width * Mathf.Clamp01(_trail), inner.height), new Color(fill.r, fill.g, fill.b, 0.35f));
            Fill(new Rect(inner.x, inner.y, inner.width * Mathf.Clamp01(_shown), inner.height), fill);
            // Our own pets and party show their numbers; anyone else the server reports as a share.
            string text = m.HitsMax == 100 && !m.HasFullStatus ? $"{Mathf.RoundToInt(hits * 100f)}%" : $"{m.Hits} / {m.HitsMax}";
            var label = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            label.normal.textColor = new Color(1f, 0.96f, 0.9f);
            GUI.Label(bar, text, label);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && new Rect(0, 0, Width, Height).Contains(e.mousePosition))
            {
                if (Session.Target != null)
                    Session.TargetObject(serial);
                else if (e.clickCount == 2)
                    Session.DoubleClick(serial);
                e.Use();
            }
        }

        private static void Fill(Rect r, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
        }
    }
}
