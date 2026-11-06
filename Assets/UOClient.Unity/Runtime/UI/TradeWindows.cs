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
    public sealed class ShopWindow : UiWindow
    {
        private readonly ShopList _list;
        private readonly Dictionary<uint, int> _order = new Dictionary<uint, int>();
        private Vector2 _scroll;

        public ShopWindow(ShopList list)
        {
            _list = list;
            Rect = new Rect(160, 100, 420, 470);
        }

        public uint Vendor => _list.Vendor;
        public override string Key => (_list.Selling ? "sell:" : "buy:") + _list.Vendor;

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            string who = Session.World.Mobiles.TryGetValue(_list.Vendor, out Mobile m) && !string.IsNullOrEmpty(m.Name) ? m.Name : "the vendor";
            Text(new Rect(0, 10, Rect.width, 22), (_list.Selling ? "Sell to " : "Buy from ") + who, new Color(1f, 0.9f, 0.6f), 16, TextAnchor.UpperCenter, true);

            const float row = 46;
            var view = new Rect(12, 40, Rect.width - 24, Rect.height - 130);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 18, _list.Items.Count * row));
            for (int i = 0; i < _list.Items.Count; i++)
            {
                ShopItem item = _list.Items[i];
                float y = i * row;
                _order.TryGetValue(item.Serial, out int chosen);
                UiImage icon = Art.Item(item.Graphic, item.Hue);
                if (icon != null)
                {
                    float s = Mathf.Min(1f, 40f / Mathf.Max(icon.Width, icon.Height));
                    GUI.DrawTexture(new Rect(4 + (40 - icon.Width * s) / 2, y + 3 + (40 - icon.Height * s) / 2, icon.Width * s, icon.Height * s), icon.Texture);
                }
                Text(new Rect(52, y + 4, 220, 20), item.Name, new Color(0.95f, 0.92f, 0.82f));
                Text(new Rect(52, y + 22, 220, 20), $"{item.Price} gp   ({item.Amount} {(_list.Selling ? "owned" : "in stock")})", new Color(0.8f, 0.76f, 0.62f), 12);
                if (chosen > 0)
                    Text(new Rect(258, y + 12, 40, 20), chosen.ToString(), new Color(1f, 0.85f, 0.4f), 15, TextAnchor.UpperRight, true);

                bool add = GUI.Button(new Rect(306, y + 10, 26, 24), "+")
                           || GUI.Button(new Rect(0, y, 250, row), GUIContent.none, GUIStyle.none);
                if (add)
                    _order[item.Serial] = Event.current.shift ? item.Amount : Mathf.Min(item.Amount, chosen + 1);
                if (GUI.Button(new Rect(336, y + 10, 26, 24), "-") && chosen > 0)
                    _order[item.Serial] = chosen - 1;
            }
            GUI.EndScrollView();

            int total = _list.Items.Sum(it => (_order.TryGetValue(it.Serial, out int n) ? n : 0) * it.Price);
            Text(new Rect(20, Rect.height - 84, 260, 20), $"Total: {total} gp", new Color(1f, 0.9f, 0.6f), 15, TextAnchor.UpperLeft, true);
            if (!_list.Selling && Session.World.Player != null)
                Text(new Rect(220, Rect.height - 84, 180, 20), $"You have {Session.World.Player.Gold} gp", new Color(0.8f, 0.76f, 0.62f), 13, TextAnchor.UpperRight);

            if (GUI.Button(new Rect(20, Rect.height - 52, 110, 28), "Accept"))
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
            if (GUI.Button(new Rect(150, Rect.height - 52, 110, 28), "Clear"))
                _order.Clear();
            if (GUI.Button(new Rect(Rect.width - 130, Rect.height - 52, 110, 28), "Close"))
                Close();
        }
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
