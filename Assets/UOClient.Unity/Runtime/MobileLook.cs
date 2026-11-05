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

        /// <summary>The body's walk/run/stand in war mode (only UOP creatures differ from <see cref="BodyActions"/>).</summary>
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
                    sb.Append(l.IsMount ? "m" : "").Append(l.Body).Append(':').Append(l.Hue).Append(l.Partial ? "p" : "").Append(';');
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

        // Paperdoll-style order for front-facing views: what is listed later is drawn on top.
        private static readonly byte[] LayerOrder =
        {
            20, // cloak (behind the body when seen from the front)
            5, 4, 3, 24, 13, 8, 9, 14, 19, 7, 23, 17, 10, 11, 22, 12, 16, 18, 6, 1, 2
        };

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

            foreach (byte layer in LayerOrder)
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
                    Partial = (td.Flags & TileFlags.PartialHue) != 0 || (item.Hue & 0x8000) != 0
                });
            }
            return look;
        }
    }
}
