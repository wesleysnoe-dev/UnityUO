using System;
using System.Linq;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// How a corpse (0x2006) is drawn, as TazUO's <c>ItemView.DrawCorpse</c> (ItemView.cs:175-249): the body it was
    /// (<c>Amount</c>, Item.cs:502-504) lying in the last frame of its death action (AnimIndex 99 clamped to the last
    /// frame, Item.cs:432 and 622-630), facing its <c>Layer</c>'s low 3 bits, in the corpse's hue, with what it still
    /// wears on top for a human. Hidden while its owner falls (ItemView.cs:183). No UnityEngine here, so the choice is
    /// tested alongside the formats.
    /// </summary>
    public static class CorpseDraw
    {
        public enum Art
        {
            /// <summary>Its owner is still falling: nothing drawn (the falling body stands in for it).</summary>
            Hidden,
            /// <summary>The frames are still being built: keep what was drawn, if anything.</summary>
            Building,
            /// <summary>The body in its death action's last frame.</summary>
            Body,
            /// <summary>The body has no death frames at all: the 0x2006 art, so a corpse is never invisible.</summary>
            StaticArt
        }

        public struct Pick
        {
            public Art Art;
            public int Body, Action, Direction, FileDir, Frame;
            public bool Mirror;
        }

        private const byte MountLayer = 25;

        /// <summary>
        /// The corpse as a look: its body and hue, and the 0x89 pieces still inside it, stacked by
        /// <see cref="MobileLook.DrawOrder"/> for the file direction, as DrawCorpse indexes <c>LayerOrder.UsedLayers</c> after
        /// GetAnimDirection (ItemView.cs:191-228).
        /// </summary>
        public static MobileLook Look(UOData data, World world, WorldItem corpse) =>
            MobileLook.From(data, corpse.Amount, corpse.Hue, world.CorpseWears(corpse).Where(e => e.Layer != MountLayer));

        /// <summary>
        /// The death action, as DrawCorpse asks for it: <c>GetDeathAction(body, …, UsedLayer)</c> with <c>isRunning</c>
        /// left false (ItemView.cs:200-205). The fall itself is asked for with it true (DisplayDeath.cs:55-61); the two
        /// only differ for sea monsters.
        /// </summary>
        public static int Action(AnimationLibrary anims, WorldItem corpse) => anims.DeathAction(corpse.Amount, corpse.UsedLayer);

        /// <summary>
        /// What to draw for <paramref name="corpse"/>. <paramref name="frames"/> (action, file direction) returns null while
        /// building and an empty array when the body has none; it is asked even while hidden, so the frames are ready when
        /// the fall ends.
        /// </summary>
        public static Pick Select<T>(WorldItem corpse, int action, bool falling, Func<int, int, T[]> frames, out T[] chosen)
        {
            int direction = corpse.Layer & 7;
            int fileDir = AnimationFile.FileDirection(direction, out bool mirror);
            var pick = new Pick { Body = corpse.Amount, Action = action, Direction = direction, FileDir = fileDir, Mirror = mirror };
            chosen = frames(action, fileDir);
            if (falling)
                pick.Art = Art.Hidden;
            else if (chosen == null)
                pick.Art = Art.Building;
            else if (chosen.Length == 0)
                pick.Art = Art.StaticArt;
            else
            {
                pick.Art = Art.Body;
                pick.Frame = chosen.Length - 1;
            }
            return pick;
        }
    }
}
