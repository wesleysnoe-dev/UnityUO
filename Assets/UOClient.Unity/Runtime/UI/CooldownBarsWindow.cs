using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The cooldown bars (TazUO's CoolDownBar; rules in Cooldowns): each running timer a bar that empties, with its
    /// name and the time left, the one ending first on top. Nothing is drawn while none runs. The stack can be dragged by
    /// a bar; its place is kept for the run.
    /// </summary>
    public sealed class CooldownBarsWindow : UiWindow
    {
        public const string WindowKey = "cooldowns";

        /// <summary>The cooldowns and their running timers: shared, so timers run on while the window is closed or reopened.</summary>
        public static readonly Cooldowns Timers = new Cooldowns();

        private static readonly Vector2 DefaultPosition = new Vector2(540, 110);

        public CooldownBarsWindow()
        {
            Vector2 at = BarPositions.Get(WindowKey, DefaultPosition);
            Rect = new Rect(at.x, at.y, Cooldowns.BarWidth, 0);
        }

        public override string Key => WindowKey;
        public override bool CanClose => false;
        public override bool ClosesOnEsc => false;
        public override bool StepsAsideOnOpen => false;

        public override void Draw()
        {
            var active = Timers.Active(Time.realtimeSinceStartupAsDouble);
            var (w, h) = Cooldowns.StackSize(active.Count);
            Rect.size = new Vector2(w, h);
            if (active.Count > 0)
                BarPositions.Track(WindowKey, Rect.position);
            Color saved = GUI.color;
            float y = 0;
            foreach (var (def, left, fraction) in active)
            {
                Color colour = Art.HueColor(def.Hue, new Color(0.35f, 0.6f, 0.9f));
                GUI.color = new Color(0, 0, 0, 0.65f);
                GUI.DrawTexture(new Rect(0, y, w, Cooldowns.BarHeight), Texture2D.whiteTexture);
                GUI.color = new Color(colour.r, colour.g, colour.b, 0.75f);
                GUI.DrawTexture(new Rect(1, y + 1, (w - 2) * fraction, Cooldowns.BarHeight - 2), Texture2D.whiteTexture);
                GUI.color = saved;
                var label = new Rect(6, y, w - 12, Cooldowns.BarHeight);
                Text(new Rect(label.x + 1, label.y + 1, label.width, label.height), def.Name, Color.black, 12, TextAnchor.MiddleLeft);
                Text(label, def.Name, Color.white, 12, TextAnchor.MiddleLeft);
                string time = Cooldowns.TimeText(left);
                Text(new Rect(label.x + 1, label.y + 1, label.width, label.height), time, Color.black, 12, TextAnchor.MiddleRight);
                Text(label, time, Color.white, 12, TextAnchor.MiddleRight);
                y += Cooldowns.BarHeight + Cooldowns.BarGap;
            }
        }
    }
}
