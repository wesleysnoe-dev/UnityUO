using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.Fx
{
    /// <summary>
    /// Sparks where a weapon hit lands: damage to a mobile (0x0B) right after someone near it swung at it (an attack
    /// animation: 0xE2 type 0, or 0x6E's weapon swings 9-14, bow shots 18-19 for people, 4-6 for creatures) throws a quick
    /// burst of sparks at its chest. Damage with no swing before it (poison, spells, bleeding) throws none.
    /// Fed by the session's events; drawn by EntityViews, which knows where everyone is drawn.
    /// </summary>
    public static class HitSparks
    {
        /// <summary>How long after a swing its hit may land (s), and how near the swinger must be (tiles; a bow, farther).</summary>
        private const float SwingWindow = 1.2f;
        private const int MeleeReach = 2, BowReach = 12;

        private static readonly List<(float Time, int X, int Y, bool Ranged, uint Serial)> Swings = new List<(float, int, int, bool, uint)>();
        private static readonly List<uint> Pending = new List<uint>();

        /// <summary>A server animation: an attack swing is remembered for a moment.</summary>
        public static void Animated(GameSession session, ServerAnimation a)
        {
            if (!FxCatalog.Enabled || session == null || !session.World.Mobiles.TryGetValue(a.Serial, out Mobile m))
                return;
            bool human = MobileLook.IsHumanGraphic(m.Body);
            bool bow = !a.IsNew && human && (a.Action == 18 || a.Action == 19);
            bool attack = a.IsNew ? a.Type == 0 : human ? a.Action >= 9 && a.Action <= 14 || bow : a.Action >= 4 && a.Action <= 6;
            if (!attack)
                return;
            Swings.RemoveAll(s => Time.time - s.Time > SwingWindow);
            Swings.Add((Time.time, m.X, m.Y, bow, a.Serial));
        }

        /// <summary>A mobile took damage: sparks, if someone near it just swung.</summary>
        public static void Damaged(GameSession session, uint serial)
        {
            if (!FxCatalog.Enabled || session == null || !session.World.Mobiles.TryGetValue(serial, out Mobile target))
                return;
            foreach (var s in Swings)
            {
                if (s.Serial == serial || Time.time - s.Time > SwingWindow)
                    continue;
                int reach = s.Ranged ? BowReach : MeleeReach;
                if (Mathf.Max(Mathf.Abs(s.X - target.X), Mathf.Abs(s.Y - target.Y)) <= reach)
                {
                    Pending.Add(serial);
                    return;
                }
            }
        }

        /// <summary>Throws this frame's sparks at the mobiles hit (<paramref name="drawnAt"/>: a serial's avatar, or null).</summary>
        public static void Draw(System.Func<uint, Avatar> drawnAt, int scale)
        {
            foreach (uint serial in Pending)
            {
                Avatar a = drawnAt(serial);
                if (a == null || a.LastRect.width <= 0f)
                    continue;
                Rect r = a.LastRect;
                // About the chest: a little above the frame's middle, just in front of the body.
                var at = new Vector3(r.center.x, r.center.y + r.height * 0.1f, a.LastDepth - 0.02f);
                FxParticles.Sparks(at, new Color(1f, 0.82f, 0.45f), scale);
            }
            Pending.Clear();
        }
    }
}
