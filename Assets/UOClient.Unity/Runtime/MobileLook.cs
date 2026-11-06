using System.Collections.Generic;
using System.Text;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>What a mobile looks like: body, skin hue, and worn items in draw order. Immutable once built.</summary>
    public sealed class MobileLook
    {
        public struct Layer
        {
            public int Body;   // animation body id (the item's tiledata AnimId for equipment)
            public int Hue;    // hues.mul index (entry + 1); 0 = unhued
            public bool Partial;
            public bool IsMount; // the creature being ridden: plays its own walk/run/stand under the rider
            public byte Slot;    // the worn item's layer (20 = cloak); 0 for the body and the mount
        }

        public readonly List<Layer> Layers = new List<Layer>();

        /// <summary>The body as the server sent it (before Body.def / bodyconv), which the action maps are keyed by.</summary>
        public int Body;

        /// <summary>The body's action layout (humans, animals and monsters number their actions differently).</summary>
        public AnimGroup Group = AnimGroup.Human;

        /// <summary>Riding: the rider uses the mounted actions, the mount layer its own.</summary>
        public bool Mounted;

        /// <summary>The mount's action layout, when mounted.</summary>
        public AnimGroup MountGroup = AnimGroup.Animal;

        /// <summary>The body's and the mount's walk/run/stand (UOP bodies number them differently from their layout).</summary>
        public (int Walk, int Run, int Stand) BodyActions = AnimationLibrary.DefaultActions(AnimGroup.Human);
        public (int Walk, int Run, int Stand) MountActions = AnimationLibrary.DefaultActions(AnimGroup.Animal);

        /// <summary>The body's walk/run/stand in war mode (UOP creatures, and MUL humans by the weapon held).</summary>
        public (int Walk, int Run, int Stand) WarActions = AnimationLibrary.DefaultActions(AnimGroup.Human);

        /// <summary>
        /// The body's falls: the first, and the second the server asks for with 0xAF's running flag. TazUO's DisplayDeath
        /// passes that flag as <c>second</c> with <c>isRunning</c> true (DisplayDeath.cs:55-61).
        /// </summary>
        public (int First, int Second) DeathActions = (21, 22);

        /// <summary>Identifies the look for sharing frames between identical mobiles.</summary>
        public string Key
        {
            get
            {
                var sb = new StringBuilder();
                foreach (Layer l in Layers)
                    sb.Append(l.IsMount ? "m" : "").Append(l.Body).Append(':').Append(l.Hue).Append(l.Partial ? "p" : "")
                        .Append('@').Append(l.Slot).Append(';');
                return sb.ToString();
            }
        }

        public int BaseBody
        {
            get
            {
                foreach (Layer l in Layers)
                    if (!l.IsMount)
                        return l.Body;
                return 0;
            }
        }

        /// <summary>
        /// The order worn layers are drawn in over the body, per facing (0 north … 7 northwest); later is on top. TazUO's
        /// <c>LayerOrder.UsedLayers</c> (LayerOrder.cs:7-64): facing the viewer (3) the cloak goes first, under every other
        /// piece; every other way it goes near the end, over robe and hair (last of all facing north).
        /// </summary>
        public static readonly byte[][] UsedLayers =
        {
            new byte[] { 5, 4, 3, 24, 13, 8, 9, 14, 15, 19, 7, 23, 17, 22, 10, 11, 12, 16, 18, 1, 6, 2, 20 },
            new byte[] { 5, 4, 3, 24, 13, 8, 9, 14, 15, 19, 7, 23, 17, 22, 10, 11, 12, 16, 18, 1, 20, 6, 2 },
            new byte[] { 5, 4, 3, 24, 13, 8, 9, 14, 15, 19, 7, 23, 17, 22, 10, 11, 12, 16, 18, 1, 20, 6, 2 },
            new byte[] { 20, 5, 4, 3, 24, 13, 8, 9, 14, 15, 19, 7, 23, 17, 22, 12, 10, 11, 16, 18, 6, 1, 2 },
            new byte[] { 5, 4, 3, 24, 13, 8, 9, 14, 15, 19, 7, 23, 17, 22, 10, 11, 12, 16, 18, 1, 20, 6, 2 },
            new byte[] { 5, 4, 3, 24, 13, 8, 9, 14, 15, 19, 7, 23, 17, 22, 10, 11, 12, 16, 18, 1, 20, 6, 2 },
            new byte[] { 5, 4, 3, 24, 13, 8, 9, 14, 15, 19, 7, 23, 17, 22, 10, 11, 12, 16, 18, 1, 20, 6, 2 },
            new byte[] { 5, 4, 3, 24, 13, 8, 9, 14, 15, 19, 7, 23, 17, 22, 10, 11, 12, 16, 18, 1, 20, 6, 2 }
        };

        /// <summary>
        /// The lowest facing drawing its layers in the same order as <paramref name="direction"/>: frames built for one
        /// serve every facing of the same class (and file direction).
        /// </summary>
        public static int OrderClass(int direction)
        {
            direction &= 7;
            for (int d = 0; d < direction; d++)
                if (SameOrder(UsedLayers[d], UsedLayers[direction]))
                    return d;
            return direction;
        }

        private static bool SameOrder(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        /// <summary>
        /// The layers as drawn facing <paramref name="direction"/>: the mount, the body, then what is worn in
        /// <see cref="UsedLayers"/> order (MobileView.cs:335-441). Corpses pass their file direction, as ItemView.DrawCorpse
        /// indexes the table after GetAnimDirection (ItemView.cs:191-228).
        /// </summary>
        public List<Layer> DrawOrder(int direction)
        {
            var ordered = new List<Layer>(Layers.Count);
            foreach (Layer l in Layers)
                if (l.Slot == 0)
                    ordered.Add(l);
            foreach (byte slot in UsedLayers[direction & 7])
                foreach (Layer l in Layers)
                    if (l.Slot == slot)
                        ordered.Add(l);
            return ordered;
        }

        /// <summary>The hair and beard layers, which a ghost does not draw (MobileView.cs:372, 1245).</summary>
        public const byte HairLayer = 11, BeardLayer = 16, MountLayer = 25;

        /// <summary>The hue TazUO draws a dead non-human in (MobileView.cs:120-123).</summary>
        public const int DeadHue = 0x0386;

        /// <summary>TazUO's <c>HumanGraphicCheck</c> (Mobile.cs:1127-1140), by the body as sent.</summary>
        public static bool IsHumanGraphic(int body) =>
            body >= 0x0190 && body <= 0x0193 || body >= 0x00B7 && body <= 0x00BA || body >= 0x025D && body <= 0x0260
            || body == 0x029A || body == 0x029B || body == 0x02B6 || body == 0x02B7 || body == 0x03DB || body == 0x03DF
            || body == 0x03E2 || body == 0x02E8 || body == 0x02E9 || body == 0x04E5;

        /// <summary>
        /// Builds a look from a body and worn items (graphic, hue per layer), resolving each item's animation.
        /// <see cref="Body"/> stays the body as sent; the art is drawn from <see cref="AnimationLibrary.AnimationBody"/>, so a
        /// ghost (402/403, 607/608, 694/695) draws as its living body, as TazUO's MobileView does. A ghost also draws no
        /// hair, beard (MobileView.cs:372) or mount (:172), and keeps the hues as sent: TazUO overrides a dead mobile's hue
        /// (0x0386) only when it is not human (:120-123), and every ghost body is human there (Mobile.cs:1127-1140).
        /// A dead bonded pet (<paramref name="bondedDead"/>, 0xBF 0x19 version 0) keeps its body and is that non-human
        /// dead case: drawn in <see cref="DeadHue"/>.
        /// </summary>
        public static MobileLook From(UOData data, int body, int hue, IEnumerable<(byte Layer, int Graphic, int Hue)> equipment,
            bool bondedDead = false)
        {
            var look = new MobileLook();
            AnimationLibrary anims = data.AnimationLibrary;
            int drawn = AnimationLibrary.AnimationBody(body);
            bool ghost = drawn != body;
            look.Body = body;
            look.Group = anims.GroupOf(drawn);
            look.BodyActions = anims.Actions(drawn);
            look.WarActions = anims.Actions(drawn, warMode: true);
            look.DeathActions = (anims.DeathAction(drawn, false, running: true), anims.DeathAction(drawn, true, running: true));
            int skin = hue & 0x3FFF;
            if (skin == 0)
                skin = anims.ReplacementHue(drawn); // Body.def stand-ins come with their own hue
            if (bondedDead && !IsHumanGraphic(body))
                skin = DeadHue;

            var byLayer = new Dictionary<byte, (int Graphic, int Hue)>();
            foreach (var e in equipment)
            {
                if (ghost && (e.Layer == HairLayer || e.Layer == BeardLayer || e.Layer == MountLayer))
                    continue;
                byLayer[e.Layer] = (e.Graphic, e.Hue);
            }

            // People walk, run and stand by what they hold: layer 1 (one-handed) and 2 (two-handed or shield).
            if (anims.UsesPeopleActions(drawn))
            {
                bool one = byLayer.ContainsKey(1), two = byLayer.TryGetValue(2, out var hand2);
                int twoAnim = two ? data.TileData.GetStatic(hand2.Graphic).AnimId : 0;
                look.BodyActions = AnimationLibrary.PeopleActions(false, one, two, twoAnim);
                look.WarActions = AnimationLibrary.PeopleActions(true, one, two, twoAnim);
            }

            // The mount goes first, so the rider is drawn over it.
            if (look.Group == AnimGroup.Human && byLayer.TryGetValue(MountLayer, out var mount))
            {
                int mountBody = AnimationLibrary.MountBody(mount.Graphic);
                if (mountBody == 0)
                    mountBody = data.TileData.GetStatic(mount.Graphic).AnimId;
                if (mountBody != 0)
                {
                    look.Mounted = true;
                    look.MountGroup = anims.GroupOf(mountBody);
                    look.MountActions = anims.Actions(mountBody);
                    look.Layers.Add(new Layer { Body = mountBody, Hue = mount.Hue & 0x3FFF, IsMount = true });
                }
            }

            look.Layers.Add(new Layer { Body = drawn, Hue = skin });
            if (look.Group != AnimGroup.Human)
                return look; // creatures wear nothing visible

            // Stored in one order; DrawOrder puts them in the facing's.
            foreach (byte layer in UsedLayers[0])
            {
                if (!byLayer.TryGetValue(layer, out var item))
                    continue;
                StaticTileData td = data.TileData.GetStatic(item.Graphic);
                if (td.AnimId == 0)
                    continue;
                // Equipconv.def and the shard's EquipConv lines: e.g. a faction tabard drawn as the stock surcoat in its faction hue.
                int anim = td.AnimId, itemHue = item.Hue & 0x3FFF;
                if (data.TryEquipConv(body, anim, out EquipConvEntry conv))
                {
                    anim = conv.Anim;
                    if (itemHue == 0)
                        itemHue = conv.Hue & 0x3FFF;
                }
                look.Layers.Add(new Layer
                {
                    Body = anim,
                    Hue = itemHue,
                    Partial = (td.Flags & TileFlags.PartialHue) != 0 || (item.Hue & 0x8000) != 0,
                    Slot = layer
                });
            }
            return look;
        }
    }
}
