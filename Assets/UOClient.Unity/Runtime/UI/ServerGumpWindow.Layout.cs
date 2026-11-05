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
        /// The radio group of each radio by command index. TazUO counts group and endgroup alike (group++), whatever
        /// number the group carries, so radios after an endgroup never join those before it.
        /// </summary>
        public static Dictionary<int, int> RadioGroups(IList<GumpCommand> commands)
        {
            var groups = new Dictionary<int, int>();
            int group = 0;
            for (int i = 0; i < commands.Count; i++)
            {
                GumpCommand c = commands[i];
                if (c.Name == "group" || c.Name == "endgroup")
                    group++;
                else if (c.Name == "radio")
                    groups[i] = group;
            }
            return groups;
        }

        /// <summary>
        /// The page a page button shows: TazUO's Gump.Update turns ActivePage 0 into 1, so a button to page 0 (and a
        /// gump with no page button pressed yet) shows page 1.
        /// </summary>
        public static int PageShown(int page) => page == 0 ? 1 : page;

        /// <summary>
        /// What pressing a button or buttontileart (x y up down type param id ...) does, as TazUO's Button(parts):
        /// type 0 switches to page param locally, any other type replies with id (never param).
        /// </summary>
        public static (bool IsPage, int Value) ButtonPress(GumpCommand c) =>
            c.Arg(4) == 0 ? (true, PageShown(c.Arg(5))) : (false, c.Arg(6));

        /// <summary>
        /// Whether right-click closes a server gump. TazUO's GumpHelpers.cs:90 starts every server gump with
        /// CanCloseWithRightClick, and only noclose clears it (:427-430); Gump.CloseWithRightClick (Gump.cs:304-309)
        /// then does nothing. nodispose leaves right-click alone.
        /// </summary>
        public static bool ClosesOnRightClick(ServerGump g) => !g.NoClose;

        /// <summary>The button a right-click close answers with: Gump.cs:311-314 sends OnButtonClick(0) for a server gump.</summary>
        public const int RightClickReplyButton = 0;

        /// <summary>
        /// TazUO's CanCloseWithEsc for a server gump: set at GumpHelpers.cs:91, cleared by nodispose (:431-434).
        /// Kept for fidelity only: nothing in TazUO reads it (Control.cs:93 declares it; see EscCloses).
        /// </summary>
        public static bool CanCloseWithEsc(ServerGump g) => !g.NoDispose;

        /// <summary>
        /// Whether Esc closes a server gump: never, nodispose or not. TazUO's Esc (GameSceneInputHandler.cs:1370-1419)
        /// only cancels a target, releases the chat box and stops auto-walk; Gump has no key handler, a text box's Esc
        /// (StbTextBox.cs:521-528) only drops focus and selection, and CanCloseWithEsc is never read.
        /// </summary>
        public static bool EscCloses(ServerGump g) => false;
    }

    /// <summary>
    /// The checkboxes, radios and text entries of a server gump and what the 0xB1 reply carries from them, kept per
    /// control (command) as TazUO keeps one Checkbox/RadioButton/StbTextBox each. Gump.OnButtonClick walks the
    /// controls in layout order, every page alike: each checked one adds its switch id, each text box its (id, text).
    /// </summary>
    public sealed class GumpControls
    {
        private readonly List<GumpCommand> _commands;
        private readonly Dictionary<int, int> _radioGroup;
        private readonly Dictionary<int, bool> _checked = new Dictionary<int, bool>();
        private readonly Dictionary<int, string> _texts = new Dictionary<int, string>();

        public GumpControls(ServerGump gump)
        {
            _commands = gump.Commands;
            _radioGroup = ServerGumpWindow.RadioGroups(_commands);
            for (int i = 0; i < _commands.Count; i++)
            {
                GumpCommand c = _commands[i];
                switch (c.Name)
                {
                    case "checkbox":
                    case "radio":
                        // Checkbox(parts): IsChecked = parts[5] == "1". Set before the radio has a parent, so a
                        // group the server sends with two radios on keeps both on.
                        _checked[i] = c.Arg(4) == 1;
                        break;
                    case "textentry":
                    case "textentrylimited":
                        // StbTextBox.SetText cuts the starting text to its limit, as typing is.
                        string text = gump.Line(c.Arg(6));
                        _texts[i] = text.Length > c.TextLimit ? text.Substring(0, c.TextLimit) : text;
                        break;
                }
            }
        }

        public bool IsChecked(int index) => _checked.TryGetValue(index, out bool on) && on;

        /// <summary>
        /// A left click on a checkbox or radio: Checkbox.OnMouseUp flips it, radios included, so a checked radio
        /// clicked again goes off. A radio turning on (RadioButton.HandleClick) turns off the rest of its group.
        /// </summary>
        public void Click(int index)
        {
            if (!_checked.ContainsKey(index))
                return;
            bool on = !_checked[index];
            _checked[index] = on;
            if (on && _radioGroup.TryGetValue(index, out int group))
                foreach (KeyValuePair<int, int> other in _radioGroup)
                    if (other.Value == group && other.Key != index)
                        _checked[other.Key] = false;
        }

        public string Text(int index) => _texts.TryGetValue(index, out string t) ? t : "";

        public void SetText(int index, string text)
        {
            if (_texts.ContainsKey(index))
                _texts[index] = text ?? "";
        }

        /// <summary>The switch ids of the checked checkboxes and radios, in layout order.</summary>
        public List<int> Switches()
        {
            var on = new List<int>();
            for (int i = 0; i < _commands.Count; i++)
                if (IsChecked(i))
                    on.Add(_commands[i].Arg(5));
            return on;
        }

        /// <summary>Every text entry as (id, text), in layout order.</summary>
        public List<(int Id, string Text)> Entries()
        {
            var entries = new List<(int, string)>();
            for (int i = 0; i < _commands.Count; i++)
                if (_texts.TryGetValue(i, out string t))
                    entries.Add((_commands[i].Arg(5), t));
            return entries;
        }
    }

    public sealed partial class ServerGumpWindow
    {

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
