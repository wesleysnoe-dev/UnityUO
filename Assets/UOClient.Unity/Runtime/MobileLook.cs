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

        /// <summary>Builds a look from a body and worn items (graphic, hue per layer), resolving each item's animation.</summary>
        public static MobileLook From(UOData data, int body, int hue, IEnumerable<(byte Layer, int Graphic, int Hue)> equipment)
        {
            var look = new MobileLook();
            AnimationLibrary anims = data.AnimationLibrary;
            look.Body = body;
            look.Group = anims.GroupOf(body);
            look.BodyActions = anims.Actions(body);
            look.WarActions = anims.Actions(body, warMode: true);
            look.DeathActions = (anims.DeathAction(body, false, running: true), anims.DeathAction(body, true, running: true));
            int skin = hue & 0x3FFF;
            if (skin == 0)
                skin = anims.ReplacementHue(body); // Body.def stand-ins come with their own hue

            var byLayer = new Dictionary<byte, (int Graphic, int Hue)>();
            foreach (var e in equipment)
                byLayer[e.Layer] = (e.Graphic, e.Hue);

            // People walk, run and stand by what they hold: layer 1 (one-handed) and 2 (two-handed or shield).
            if (anims.UsesPeopleActions(body))
            {
                bool one = byLayer.ContainsKey(1), two = byLayer.TryGetValue(2, out var hand2);
                int twoAnim = two ? data.TileData.GetStatic(hand2.Graphic).AnimId : 0;
                look.BodyActions = AnimationLibrary.PeopleActions(false, one, two, twoAnim);
                look.WarActions = AnimationLibrary.PeopleActions(true, one, two, twoAnim);
            }

            // The mount goes first, so the rider is drawn over it.
            if (look.Group == AnimGroup.Human && byLayer.TryGetValue(25, out var mount))
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

            look.Layers.Add(new Layer { Body = body, Hue = skin });
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
