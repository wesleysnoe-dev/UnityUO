using System;
using System.Collections.Generic;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the layout rules.
    public sealed partial class ServerGumpWindow
    {
        /// <summary>TazUO's HtmlControl background (ResizePic 0x2486), narrowed by its 16-pixel scrollbar when there is one.</summary>
        public const int HtmlBackgroundGump = 0x2486, HtmlScrollbarWidth = 16;

        /// <summary>The 50% black CheckerTrans draws, and the alpha it leaves the controls it covers at (GumpHelpers.ApplyTrans).</summary>
        public const float CheckerAlpha = 0.5f;

        /// <summary>
        /// The client hue of a text, croppedtext or textentry hue: TazUO's Label, CroppedText and StbTextBox add one,
        /// as gump hues are 0-based (a label at 1152 shows as hue 1153, white).
        /// </summary>
        public static int TextHue(int serverHue) => (ushort)(serverHue + 1);

        /// <summary>The client hue of gumppic's hue=N: GumpPic adds one, then TransformHue leaves 2 and below unhued.</summary>
        public static int PicHue(int serverHue)
        {
            int hue = (ushort)(serverHue + 1);
            return hue <= 2 ? 0 : hue;
        }

        /// <summary>
        /// The radio group of each radio by switch id. TazUO counts group and endgroup alike (group++), whatever
        /// number the group carries, so radios after an endgroup never join those before it.
        /// </summary>
        public static Dictionary<int, int> RadioGroups(IList<GumpCommand> commands)
        {
            var groups = new Dictionary<int, int>();
            int group = 0;
            foreach (GumpCommand c in commands)
            {
                if (c.Name == "group" || c.Name == "endgroup")
                    group++;
                else if (c.Name == "radio")
                    groups[c.Arg(5)] = group;
            }
            return groups;
        }

        /// <summary>Commands TazUO turns into a control of the gump (the ones checkertrans and tooltips can reach).</summary>
        public static bool IsControl(string name)
        {
            switch (name)
            {
                case "page":
                case "group":
                case "endgroup":
                case "tooltip":
                case "itemproperty":
                case "noclose":
                case "nodispose":
                case "nomove":
                case "noresize":
                case "mastergump":
                case "togglelimitgumpscale":
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Indices of the commands a checkertrans fades to <see cref="CheckerAlpha"/>: GumpHelpers.ApplyTrans makes every
        /// control already added that overlaps it, on page 0 or the checkertrans' own page, half transparent. Controls
        /// added after it are untouched, so an AddAlphaRegion over a background lets the world show through the
        /// background but not through the labels and buttons laid on it afterwards.
        /// </summary>
        public static HashSet<int> CheckerFaded(IList<GumpCommand> commands, Func<GumpCommand, (int X, int Y, int W, int H)> bounds)
        {
            var faded = new HashSet<int>();
            var pages = new int[commands.Count];
            int page = 0;
            for (int i = 0; i < commands.Count; i++)
            {
                GumpCommand c = commands[i];
                pages[i] = page;
                if (c.Name == "page")
                {
                    page = c.Arg(0);
                    continue;
                }
                if (c.Name != "checkertrans")
                    continue;

                int x = c.Arg(0), y = c.Arg(1), x2 = x + c.Arg(2), y2 = y + c.Arg(3);
                for (int j = 0; j < i; j++)
                {
                    GumpCommand o = commands[j];
                    if (!IsControl(o.Name) || o.Name == "checkertrans" || (pages[j] != 0 && pages[j] != page))
                        continue;
                    (int ox, int oy, int ow, int oh) = bounds(o);
                    if (x < ox + ow && ox < x2 && y < oy + oh && oy < y2)
                        faded.Add(j);
                }
            }
            return faded;
        }
    }
}
