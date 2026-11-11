using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UOClient.Core
{
    /// <summary>
    /// The hairstylist's three menus (ModernUO's HairstylistBuyGump, ChangeHairstyleGump and ChangeHairHueGump) read out
    /// of their gump layouts, so the client can show them as windows of its own instead of the server's parchment gumps.
    /// Each reader returns null for anything that is not that menu (no heading, nothing to pick, a control the menu
    /// does not have), and the gump is then shown as the server sent it. The answers are the gump's own: a service is
    /// its reply button; a style or a colour is its radio's switch with button <see cref="OkButton"/>; cancel is button 0.
    /// </summary>
    public static class BarberMenus
    {
        public const int ServicesHeading = 1018356; // Choose your hairstyle change:
        public const int HairHeading = 1018353;     // New Hairstyle
        public const int BeardHeading = 1018354;    // New Beard
        public const int ColourHeading = 1011013;   // Hair Color Selection Menu
        public const int BaldCliloc = 1011064;      // Bald
        public const int OkButton = 1;

        private const int PictureBox = 2620; // the dark box behind each style's picture (50 x 50)

        // ---- the services ----

        public sealed class Service
        {
            public int Cliloc;  // its name, or 0 when the server sent it as text
            public string Text; // the text sent (tags removed), or null
            public int Button;  // the reply button, or 0 when the bank cannot pay for it (listed, not offered)
        }

        public sealed class Services
        {
            public readonly List<Service> Items = new List<Service>();
            public string Footer; // the bank line, or null
        }

        public static Services ReadServices(ServerGump gump)
        {
            if (gump?.Commands == null || !HasHeading(gump.Commands, ServicesHeading) || HasOtherControls(gump.Commands, radios: true))
                return null;
            var menu = new Services();
            var buttons = new Dictionary<int, int>(); // by row y
            foreach (GumpCommand c in gump.Commands)
                if (c.Name == "button")
                {
                    if (c.Arg(4) == 0)
                        return null; // a page button: not this menu
                    buttons[c.Arg(1)] = c.Arg(6);
                }
            foreach (GumpCommand c in gump.Commands)
            {
                bool cliloc = c.Name == "xmfhtmlgump" || c.Name == "xmfhtmlgumpcolor";
                if (!cliloc && c.Name != "htmlgump" || cliloc && c.Arg(4) == ServicesHeading)
                    continue;
                string text = cliloc ? null : PlainText(gump.Line(c.Arg(4)));
                if (buttons.TryGetValue(c.Arg(1), out int button) && button > 0)
                    menu.Items.Add(new Service { Cliloc = cliloc ? c.Arg(4) : 0, Text = text, Button = button });
                else if (c.Arg(0) > 100) // indented like the offered rows: one the bank cannot pay for
                    menu.Items.Add(new Service { Cliloc = cliloc ? c.Arg(4) : 0, Text = text });
                else if (text != null)
                    menu.Footer = text;
            }
            return menu.Items.Count > 0 ? menu : null;
        }

        // ---- the styles ----

        public sealed class Style
        {
            public int Switch;       // the radio's switch
            public int Gump;         // its picture (a paperdoll hair gump), or 0 for bald
            public int CropX, CropY; // the middle of the part of that picture the server's box shows
        }

        public sealed class Styles
        {
            public bool Beard;
            public readonly List<Style> Items = new List<Style>();
        }

        public static Styles ReadStyles(ServerGump gump)
        {
            if (gump?.Commands == null || HasOtherControls(gump.Commands, radios: false))
                return null;
            bool hair = HasHeading(gump.Commands, HairHeading), beard = HasHeading(gump.Commands, BeardHeading);
            if (!hair && !beard)
                return null;
            var menu = new Styles { Beard = beard };
            Style last = null;
            int boxX = 0, boxY = 0;
            bool ok = false;
            foreach (GumpCommand c in gump.Commands)
                switch (c.Name)
                {
                    case "button":
                        if (c.Arg(4) == 0 || c.Arg(6) > OkButton)
                            return null;
                        ok |= c.Arg(6) == OkButton;
                        break;
                    case "radio":
                        menu.Items.Add(last = new Style { Switch = c.Arg(5) });
                        break;
                    case "resizepic":
                        if (c.Arg(2) == PictureBox)
                        {
                            boxX = c.Arg(0);
                            boxY = c.Arg(1);
                        }
                        break;
                    case "gumppic":
                        if (last == null || last.Gump != 0)
                            return null;
                        last.Gump = c.Arg(2);
                        last.CropX = boxX + 25 - c.Arg(0);
                        last.CropY = boxY + 25 - c.Arg(1);
                        break;
                }
            return ok && menu.Items.Count > 0 ? menu : null;
        }

        /// <summary>
        /// The paperdoll gump that shows a style on a body: the server's picture number put right (its stock list names
        /// 708, 707, 713 and 808 for long hair, pigtails, the topknot and the mustache, which in this art are other
        /// pieces; tiledata's are 701, 902, 903 and 803) and taken from the body's own set (male 50000+, female 60000+).
        /// 0 (bald) stays 0.
        /// </summary>
        public static int PictureFor(int gump, bool female)
        {
            if (gump == 0)
                return 0;
            int style = gump % 10000;
            switch (style)
            {
                case 708: style = 701; break;
                case 707: style = 902; break;
                case 713: style = 903; break;
                case 808: style = 803; break;
            }
            return (female ? 60000 : 50000) + style;
        }

        /// <summary>A style's name by its picture (the paperdoll gump, male 50000+ or female 60000+), or null when unknown.</summary>
        public static string StyleName(int gump)
        {
            switch (gump % 10000)
            {
                case 700: return "Short";
                case 701: case 708: return "Long";
                case 702: return "Ponytail";
                case 703: return "Mohawk";
                case 710: return "Pageboy";
                case 712: return "Buns";
                case 900: return "Afro";
                case 901: return "Receding";
                case 902: case 707: return "Pigtails";
                case 903: case 713: return "Topknot";
                case 800: return "Goatee";
                case 801: return "Long beard";
                case 802: return "Short beard";
                case 803: case 808: return "Mustache";
                case 904: return "Short beard & mustache";
                case 905: return "Long beard & mustache";
                case 906: return "Vandyke";
                default: return gump == 0 ? "Bald" : null;
            }
        }

        // ---- the colours ----

        public sealed class Shade
        {
            public int Switch; // the radio's switch
            public int Hue;    // the hue it dyes
        }

        public sealed class ColourGroup
        {
            public int Page;
            public int Hue; // the group's first hue (its swatch)
            public readonly List<Shade> Shades = new List<Shade>();
        }

        public sealed class Colours
        {
            public readonly List<ColourGroup> Groups = new List<ColourGroup>();
        }

        public static Colours ReadColours(ServerGump gump)
        {
            if (gump?.Commands == null || !HasHeading(gump.Commands, ColourHeading) || HasOtherControls(gump.Commands, radios: false))
                return null;
            var menu = new Colours();
            int page = 0, hue = 0;
            bool ok = false;
            ColourGroup current = null;
            foreach (GumpCommand c in gump.Commands)
                switch (c.Name)
                {
                    case "page":
                        page = c.Arg(0);
                        current = menu.Groups.Find(g => g.Page == page);
                        break;
                    case "text":
                        hue = c.Arg(2) + 1; // a label's hue is sent one less (AddLabel(hue - 1))
                        break;
                    case "button":
                        if (c.Arg(4) == 0)
                        {
                            if (page == 0 && c.Arg(5) > 0 && !menu.Groups.Exists(g => g.Page == c.Arg(5)))
                                menu.Groups.Add(new ColourGroup { Page = c.Arg(5), Hue = hue });
                        }
                        else if (c.Arg(6) == OkButton)
                            ok = true;
                        else if (c.Arg(6) != 0)
                            return null;
                        break;
                    case "radio":
                        if (current == null)
                            return null;
                        current.Shades.Add(new Shade { Switch = c.Arg(5), Hue = hue });
                        break;
                }
            menu.Groups.RemoveAll(g => g.Shades.Count == 0);
            return ok && menu.Groups.Count > 0 ? menu : null;
        }

        // ---- shared ----

        private static bool HasHeading(IList<GumpCommand> commands, int cliloc)
        {
            foreach (GumpCommand c in commands)
                if ((c.Name == "xmfhtmlgump" || c.Name == "xmfhtmlgumpcolor") && c.Arg(4) == cliloc)
                    return true;
            return false;
        }

        private static bool HasOtherControls(IList<GumpCommand> commands, bool radios)
        {
            foreach (GumpCommand c in commands)
                if (c.Name == "checkbox" || c.Name == "textentry" || c.Name == "textentrylimited" || c.Name == "buttontileart"
                    || radios && c.Name == "radio")
                    return true;
            return false;
        }

        private static readonly Regex Tags = new Regex("<[^>]*>");

        /// <summary>Html as plain text: its tags removed.</summary>
        public static string PlainText(string html) => html == null ? "" : Tags.Replace(html, "").Trim();
    }
}
