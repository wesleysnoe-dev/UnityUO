using System;
using System.Collections.Generic;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// Customized houses: a house with a design (0xD8) is drawn from the design instead of its multi.mul
    /// layout. The design is resolved against that layout's bounds, so walls and floors land where the server
    /// placed them.
    /// </summary>
    internal static class CustomHouses
    {
        /// <summary>The parts to draw for a multi: its design if the server sent one, else its multi.mul layout.</summary>
        public static IEnumerable<MultiComponent> PartsOf(UOData data, Core.World world, WorldItem multi)
        {
            MultiComponent[] layout = data.Multis.Get(multi.Graphic);
            if (world != null && world.HouseDesigns.TryGetValue(multi.Serial, out HouseDesign design))
                return design.Components(layout);
            return layout;
        }

        /// <summary>Redraws a house when its design arrives or changes, and notes customize mode in the journal.</summary>
        public static void Attach(GameSession session, Action<WorldItem> redraw, Action<string> log)
        {
            session.World.HouseDesignChanged += d =>
            {
                if (session.World.Items.TryGetValue(d.Serial, out WorldItem house) && house.IsMulti)
                    redraw(house);
            };
            session.HouseCustomizationChanged += (serial, begin) =>
                log(begin ? "House customization started." : "House customization ended.");
        }
    }
}
