using System.Collections.Generic;

namespace UOClient.Core
{
    /// <summary>
    /// The public moongate's menu (ModernUO's MoongateGump) read out of its gump layout, so the client can show it as a
    /// window of its own instead of the server's stone gump: the facets (page buttons on page 0, each named by the html
    /// after it) and, per facet page, its destinations (a radio and the html after it). OKAY replies button 1 with the
    /// chosen radio's switch; CANCEL replies button 0.
    /// </summary>
    public sealed class MoongateMenu
    {
        /// <summary>"Pick your destination:", the menu's heading and what tells it from any other gump.</summary>
        public const int HeadingCliloc = 1012011;

        /// <summary>The reply button that travels (OKAY).</summary>
        public const int OkButton = 1;

        public sealed class Facet
        {
            public int Page;       // the gump page with its destinations
            public int NameCliloc; // its name
            public readonly List<Destination> Destinations = new List<Destination>();
        }

        public struct Destination
        {
            public int Switch;     // the radio's switch id, sent with OKAY
            public int NameCliloc; // its name
        }

        /// <summary>The facets in the order the gump lists them.</summary>
        public readonly List<Facet> Facets = new List<Facet>();

        /// <summary>The facet shown first: page 1, where the server puts the one the gate stands on.</summary>
        public Facet First => Facets.Find(f => f.Page == 1) ?? (Facets.Count > 0 ? Facets[0] : null);

        /// <summary>
        /// The menu in <paramref name="commands"/>, or null when they are not the moongate's: no heading, no facet, a
        /// facet without destinations, or a control the menu does not have (a text entry, a checkbox, another reply
        /// button), in which case the gump is shown as the server sent it.
        /// </summary>
        public static MoongateMenu TryRead(IList<GumpCommand> commands)
        {
            if (commands == null)
                return null;
            var menu = new MoongateMenu();
            bool heading = false, ok = false;
            int page = 0;
            Facet pendingFacet = null;
            int pendingSwitch = -1;
            Facet current = null;
            foreach (GumpCommand c in commands)
            {
                switch (c.Name)
                {
                    case "page":
                        page = c.Arg(0);
                        pendingFacet = null;
                        pendingSwitch = -1;
                        current = menu.Facets.Find(f => f.Page == page);
                        break;

                    case "button":
                        pendingFacet = null;
                        if (c.Arg(4) == 0) // a page button: on page 0 a facet's tab; on a facet's own page its lit copy
                        {
                            if (page == 0 && c.Arg(5) > 0 && !menu.Facets.Exists(f => f.Page == c.Arg(5)))
                            {
                                pendingFacet = new Facet { Page = c.Arg(5) };
                                menu.Facets.Add(pendingFacet);
                            }
                        }
                        else if (c.Arg(6) == OkButton)
                            ok = true;
                        else if (c.Arg(6) != 0)
                            return null; // a reply the menu does not have
                        break;

                    case "radio":
                        if (page == 0 || current == null)
                            return null;
                        pendingSwitch = c.Arg(5);
                        break;

                    case "xmfhtmlgump":
                    case "xmfhtmlgumpcolor":
                        if (c.Arg(4) == HeadingCliloc)
                            heading = true;
                        else if (pendingFacet != null)
                            pendingFacet.NameCliloc = c.Arg(4);
                        else if (pendingSwitch >= 0 && current != null)
                            current.Destinations.Add(new Destination { Switch = pendingSwitch, NameCliloc = c.Arg(4) });
                        pendingFacet = null;
                        pendingSwitch = -1;
                        break;

                    case "checkbox":
                    case "textentry":
                    case "textentrylimited":
                    case "buttontileart":
                        return null;
                }
            }
            if (!heading || !ok || menu.Facets.Count == 0)
                return null;
            foreach (Facet f in menu.Facets)
                if (f.NameCliloc == 0 || f.Destinations.Count == 0)
                    return null;
            return menu;
        }
    }
}
