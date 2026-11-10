using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.Core;
using ContextMenu = UOClient.Core.ContextMenu;

namespace UOClient.Unity.UI
{
    /// <summary>A context (popup) menu at the mouse: each entry sends its choice; anything else closes it.</summary>
    public sealed partial class ContextMenuWindow : UiWindow
    {
        private const float Row = 22;
        private readonly ContextMenu _menu;

        public ContextMenuWindow(ContextMenu menu, Vector2 at)
        {
            _menu = menu;
            float width = 60 + menu.Entries.Select(e => (e.Text ?? "").Length).DefaultIfEmpty(10).Max() * 7.5f;
            Rect = new Rect(at.x, at.y, Mathf.Clamp(width, 140, 360), 16 + menu.Entries.Count * Row);
        }

        public override string Key => "context";

        // A press that drifts on the frame must not drag the menu away from under the click.
        public override bool Movable => false;

        /// <summary>Puts the whole menu on a view of the given size (UI units), at the cursor or flipped to its other side.</summary>
        public void FitTo(Vector2 mouse, float viewWidth, float viewHeight)
        {
            var (x, y) = Place(mouse.x, mouse.y, Rect.width, Rect.height, viewWidth, viewHeight);
            Rect = new Rect(x, y, Rect.width, Rect.height);
        }

        public override void Draw()
        {
            DrawResizePic(3000, 0, 0, Rect.width, Rect.height); // the light popup frame
            for (int i = 0; i < _menu.Entries.Count; i++)
            {
                ContextMenuEntry e = _menu.Entries[i];
                var r = new Rect(8, 8 + i * Row, Rect.width - 16, Row);
                bool hover = !e.Disabled && r.Contains(Event.current.mousePosition);
                Color color = e.Disabled ? new Color(0.45f, 0.45f, 0.45f) : hover ? new Color(1f, 1f, 0.6f) : new Color(0.1f, 0.08f, 0.05f);
                Text(new Rect(r.x + 6, r.y + 3, r.width - 6, r.height), e.Text, color, 14);
                if (!e.Disabled && GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    Session.ChooseContextMenu(_menu.Serial, e.Index);
                    Close();
                }
            }
        }
    }

    /// <summary>
    /// A vendor's buy or sell list: click a line (or +) to add one, shift-click for all; - takes one back.
    /// Accept sends the order; the server closes the window when the trade is done.
    /// </summary>
    public sealed partial class ShopWindow : UiWindow
    {
        private readonly ShopList _list;
        private readonly Dictionary<uint, int> _order = new Dictionary<uint, int>();
        private readonly Dictionary<UiImage, (int X, int Y, int W, int H)> _bounds = new Dictionary<UiImage, (int, int, int, int)>();
        private Vector2 _scroll;

        // TazUO's ShopGump: the shop's two scrolls (buy 0x0870 / 0x0871, sell 0x0872 / 0x0873) split into a top, a middle
        // band repeated to the list's height, and a foot; the bill of sale sits to the right, half way down.
        private const int TopH = 64, LeftFootH = 116, RightFootH = 93, RightOffset = 32, ListHeight = 200, BillRow = 22;

        private Vector2 _billScroll;

        public ShopWindow(ShopList list)
        {
            _list = list;
            Rect = new Rect(120, 90, 534, 470);
        }

        public uint Vendor => _list.Vendor;
        public override string Key => (_list.Selling ? "sell:" : "buy:") + _list.Vendor;

        public override void Draw()
        {
            UiImage leftArt = Art.Gump(_list.Selling ? 0x0872 : 0x0870), rightArt = Art.Gump(_list.Selling ? 0x0873 : 0x0871);
            if (leftArt == null || rightArt == null)
            {
                DrawResizePic(9200, 0, 0, Rect.width, Rect.height); // no shop art: a plain frame, the same parts on it
                leftArt ??= rightArt;
            }
            int leftW = leftArt?.Width ?? 283, leftArtH = leftArt?.Height ?? 307, rightW = rightArt?.Width ?? 283, rightArtH = rightArt?.Height ?? 248;
            float rightX = leftW - RightOffset, rightY = leftArtH / 2f - RightOffset;
            float rightMid = rightArtH - RightFootH - TopH + (ListHeight - (leftArtH - LeftFootH - TopH));
            float rightFootY = rightY + TopH + rightMid;
            Rect.width = rightX + rightW;
            Rect.height = Mathf.Max(TopH + ListHeight + LeftFootH, rightFootY + RightFootH);
            if (leftArt != null && rightArt != null)
            {
                DrawStretchedGump(leftArt, 0, 0, TopH, LeftFootH, ListHeight);
                DrawStretchedGump(rightArt, rightX, rightY, TopH, RightFootH, rightMid);
            }

            DrawInventory(new Rect(RightOffset, TopH, leftW - RightOffset * 2 + 5, ListHeight));
            DrawBill(new Rect(RightOffset / 2f + rightX, TopH + rightY, rightW - RightOffset * 2 + RightOffset / 2f + 5, rightMid));

            // The painted arrows page the lists (TazUO's hit boxes on the art).
            if (Hit(leftW - 50, TopH - 18, 18, 16)) _scroll.y = Mathf.Max(0, _scroll.y - 50);
            if (Hit(leftW - 50, TopH + ListHeight, 18, 16)) _scroll.y += 50;
            if (Hit(rightX + rightW - 50, rightY + TopH - 18, 18, 16)) _billScroll.y = Mathf.Max(0, _billScroll.y - BillRow * 2);
            if (Hit(rightX + rightW - 50, rightFootY, 18, 16)) _billScroll.y += BillRow * 2;

            // The total and the player's gold on the bill's "Total:" / "Gold Avail:" line.
            int total = _list.Items.Sum(it => (_order.TryGetValue(it.Serial, out int n) ? n : 0) * it.Price);
            float labelY = rightFootY + RightFootH - RightOffset * 3 + 15;
            float totalX = _list.Selling ? rightX + rightW - RightOffset * 3 : RightOffset + rightX + 36;
            Text(new Rect(totalX, labelY - 2, 90, 18), total.ToString(), Ink, 13, TextAnchor.UpperLeft, true);
            if (!_list.Selling && Session.World.Player != null)
                Text(new Rect(totalX + 120, labelY - 2, 90, 18), Session.World.Player.Gold.ToString(), Ink, 13, TextAnchor.UpperLeft, true);

            // Accept and Clear are the seals painted on the bill.
            float acceptX = RightOffset + rightX, acceptY = rightFootY + RightFootH - 50;
            if (Hit(acceptX, acceptY, 34, 30))
            {
                var order = _order.Where(kv => kv.Value > 0).Select(kv => (kv.Key, kv.Value)).ToList();
                if (order.Count > 0)
                {
                    if (_list.Selling)
                        Session.Sell(_list.Vendor, order);
                    else
                        Session.Buy(_list.Vendor, order);
                }
                Close();
            }
            if (Hit(acceptX + 175, acceptY, 20, 20))
                _order.Clear();
        }

        /// <summary>An invisible button over a part of the art (a painted seal or arrow).</summary>
        private static bool Hit(float x, float y, float w, float h) => GUI.Button(new Rect(x, y, w, h), GUIContent.none, GUIStyle.none);

        /// <summary>The shop's inventory: each line's picture, name and price, and how many there are; a click adds one (Shift: all).</summary>
        private void DrawInventory(Rect view)
        {
            float content = 0;
            foreach (ShopItem it in _list.Items)
                content += LineHeight(it.Serial, Art.Data.TileData.GetStatic(it.Graphic).Height);
            // The mouse in the list's own coordinates, while it is on the visible list and no window covers it: at the
            // display pixel's centre, which is what the icon shows there (MousePick.UiPixel).
            float half = 0.5f / Ui.Scale;
            Vector2? mouse = MouseIsOver && view.Contains(Event.current.mousePosition)
                ? Event.current.mousePosition + new Vector2(half, half) - view.position + _scroll : (Vector2?)null;
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width, content), false, false, GUIStyle.none, GUIStyle.none);
            float y = 0;
            foreach (ShopItem item in _list.Items)
            {
                int row = LineHeight(item.Serial, Art.Data.TileData.GetStatic(item.Graphic).Height);
                _order.TryGetValue(item.Serial, out int chosen);
                var line = new Rect(0, y, view.width, row);
                bool over = mouse.HasValue && line.Contains(mouse.Value);
                DrawIcon(item, y, row, mouse);
                float mid = y + (row - 46) / 2f; // the text keeps its place in the line's middle
                Text(new Rect(55, mid + 6, 112, 18), item.Name, over ? InkHot : Ink, 12, TextAnchor.UpperLeft, true);
                Text(new Rect(55, mid + 24, 112, 18), $"{item.Price} gp", InkFaded, 12);
                Text(new Rect(168, mid + 15, view.width - 172, 18), (item.Amount - chosen).ToString(), Ink, 12, TextAnchor.UpperRight);
                if (GUI.Button(line, GUIContent.none, GUIStyle.none) && chosen < item.Amount)
                    _order[item.Serial] = Event.current.shift ? item.Amount : chosen + 1;
                y += row;
            }
            GUI.EndScrollView();
        }

        /// <summary>The bill of sale: each chosen line's amount and name, with more / less.</summary>
        private void DrawBill(Rect view)
        {
            var chosenItems = _list.Items.Where(it => _order.TryGetValue(it.Serial, out int n) && n > 0).ToList();
            _billScroll = GUI.BeginScrollView(view, _billScroll, new Rect(0, 0, view.width, chosenItems.Count * BillRow),
                false, false, GUIStyle.none, GUIStyle.none);
            float y = 0;
            foreach (ShopItem item in chosenItems)
            {
                int n = _order[item.Serial];
                Text(new Rect(4, y + 2, 30, 18), n.ToString(), Ink, 12, TextAnchor.UpperLeft, true);
                Text(new Rect(38, y + 2, view.width - 92, 18), item.Name, Ink, 12);
                if (InkButton(new Rect(view.width - 50, y, 22, 20), "+", 14) && n < item.Amount)
                    _order[item.Serial] = Event.current.shift ? item.Amount : n + 1;
                if (InkButton(new Rect(view.width - 26, y, 22, 20), "-", 14))
                    _order[item.Serial] = Event.current.shift ? 0 : n - 1;
                y += BillRow;
            }
            GUI.EndScrollView();
        }

        /// <summary>
        /// A line's picture as TazUO's ShopItem.Draw makes it: a creature on sale is its body standing (frame 0, file
        /// direction 1, the Body.def hue); an item is the opaque part of its art in the line's hue (Arts.GetRealArtBounds).
        /// With the mouse (<paramref name="mouse"/>, list coordinates) on its opaque pixels it is outlined, as the
        /// world's and the containers' items are (our addition: TazUO's ShopItem has no mouse-over art).
        /// </summary>
        private void DrawIcon(ShopItem item, float y, int row, Vector2? mouse)
        {
            bool body = DrawsBody(_list.Selling, item.Serial);
            UiImage icon = body ? Art.Body(item.Graphic) : Art.Item(item.Graphic, item.Hue);
            if (icon == null)
                return;
            if (!_bounds.TryGetValue(icon, out var src))
                _bounds[icon] = src = body ? (0, 0, icon.Width, icon.Height) : OpaqueBounds(icon.Bitmap);
            var (x, dy, w, h) = IconRect(body, src.W, src.H, row);
            (float X, float Y, float W, float H) dest = (x, y + dy, w, h);
            var px = mouse.HasValue ? IconPixel(mouse.Value.x, mouse.Value.y, dest, src) : null;
            if (px.HasValue && icon.HitTest(px.Value.X, px.Value.Y))
            {
                UiImage outline = Art.Outline(icon);
                var (od, os) = OutlineRect(dest, src);
                GUI.DrawTextureWithTexCoords(new Rect(od.X, od.Y, od.W, od.H), outline.Texture, Uv(os, outline));
            }
            GUI.DrawTextureWithTexCoords(new Rect(dest.X, dest.Y, dest.W, dest.H), icon.Texture, Uv(src, icon));
        }

        /// <summary>A part of a picture (top-left origin) as texture coordinates (bottom-left origin).</summary>
        private static Rect Uv((int X, int Y, int W, int H) part, UiImage img) =>
            new Rect((float)part.X / img.Width, 1f - (float)(part.Y + part.H) / img.Height, (float)part.W / img.Width, (float)part.H / img.Height);
    }

    /// <summary>The server asked for a line of text (a name, a rune description, ...).</summary>
    public sealed class PromptWindow : UiWindow
    {
        private readonly TextPrompt _prompt;
        private string _text = "";
        private bool _focused;

        public PromptWindow(TextPrompt prompt)
        {
            _prompt = prompt;
            Rect = new Rect(300, 260, 380, 130);
        }

        public override string Key => "prompt";

        // Closing without an answer cancels (a no-op once answered, or once a newer prompt replaced this one).
        public override void OnClosed() => Session.AnswerPrompt(_prompt, null);

        public override void Draw()
        {
            // Keys first, before the text box sees them: while it has the keyboard the world's Escape doesn't run.
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                e.Use();
                Finish(null);
                return;
            }
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(0, 10, Rect.width, 22), "Enter text", new Color(1f, 0.9f, 0.6f), 15, TextAnchor.UpperCenter, true);
            GUI.SetNextControlName("prompt-text");
            _text = GUI.TextField(new Rect(20, 44, Rect.width - 40, 24), _text, GameSession.MaxPromptLength);
            if (!_focused)
            {
                GUI.FocusControl("prompt-text");
                _focused = true;
            }
            if (GUI.Button(new Rect(40, 84, 110, 28), "OK") || enter)
                Finish(_text);
            else if (GUI.Button(new Rect(Rect.width - 150, 84, 110, 28), "Cancel"))
                Finish(null);
        }

        /// <summary>Sends the answer (null cancels), closes, and gives the keyboard back to the world.</summary>
        private void Finish(string text)
        {
            Session.AnswerPrompt(_prompt, text);
            GUIUtility.keyboardControl = 0;
            Close();
        }
    }

    /// <summary>A server gump's text entry dialog (0xAB): a caption, a text or number box and help text; answered with 0xAC.</summary>
    public sealed class TextEntryWindow : UiWindow
    {
        private readonly TextEntryRequest _entry;
        private string _text = "";
        private bool _focused, _answered;

        public TextEntryWindow(TextEntryRequest entry)
        {
            _entry = entry;
            Rect = new Rect(300, 240, 420, 170);
        }

        public override string Key => $"textentry-{_entry.Serial:X}-{_entry.ParentId}-{_entry.ButtonId}";

        public override void OnClosed()
        {
            if (!_answered)
                Session.AnswerTextEntry(_entry, null); // closing without an answer cancels
        }

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(16, 10, Rect.width - 32, 22), string.IsNullOrEmpty(_entry.Text) ? "Enter text" : _entry.Text,
                new Color(1f, 0.9f, 0.6f), 15, TextAnchor.UpperCenter, true);
            if (_entry.Style != 0)
            {
                GUI.SetNextControlName("textentry-text");
                int max = _entry.Numeric || _entry.Max <= 0 ? 10 : Mathf.Min(_entry.Max, 256);
                string typed = GUI.TextField(new Rect(20, 44, Rect.width - 40, 24), _text, _entry.Numeric ? 10 : max);
                if (_entry.Numeric)
                    typed = new string(typed.Where(char.IsDigit).ToArray());
                _text = typed;
                if (!_focused)
                {
                    GUI.FocusControl("textentry-text");
                    _focused = true;
                }
            }
            if (!string.IsNullOrEmpty(_entry.Description))
                Text(new Rect(20, 74, Rect.width - 40, 40), _entry.Description, new Color(0.85f, 0.85f, 0.85f), 12, TextAnchor.UpperLeft, false);

            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (GUI.Button(new Rect(40, Rect.height - 44, 110, 28), "OK") || enter)
            {
                if (_entry.Numeric && _entry.Max > 0 && long.TryParse(_text, out long n) && n > _entry.Max)
                    _text = _entry.Max.ToString();
                Answer(_text);
            }
            if (_entry.CanCancel && GUI.Button(new Rect(Rect.width - 150, Rect.height - 44, 110, 28), "Cancel"))
                Answer(null);
        }

        private void Answer(string text)
        {
            _answered = true;
            Session.AnswerTextEntry(_entry, text);
            Close();
        }
    }
}
