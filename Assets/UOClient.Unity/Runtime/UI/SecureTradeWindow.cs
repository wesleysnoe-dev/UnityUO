using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A secure trade (0x6F), laid out like ClassicUO's classic trading gump (0x0866): our offer on the left with our
    /// accept box, theirs on the right with their name and box. Drop items onto the window to offer them, drag them
    /// back out, double-click to look inside a bag. With an account ledger (newer servers) gold and platinum fields
    /// sit below. Closing the window cancels the trade.
    /// </summary>
    public sealed class SecureTradeWindow : UiWindow
    {
        private const int Background = 0x0866, Unchecked = 0x0867, Checked = 0x0869;
        private const float DoubleClickTime = 0.4f;
        private const int CoinsHeight = 74;

        private static readonly Rect MyBox = new Rect(45, 70, 110, 80);
        private static readonly Rect TheirBox = new Rect(192, 70, 110, 80);
        private static readonly Color Ink = new Color(0.22f, 0.16f, 0.08f);

        private readonly SecureTrade _trade;
        private readonly List<(ContainedItem Item, UiImage Image, Rect Rect, bool Mine)> _drawn = new List<(ContainedItem, UiImage, Rect, bool)>();
        private Vector2 _gumpSize = new Vector2(300, 190);
        private ContainedItem _pressed;
        private Vector2 _pressAt, _pressOffset;
        private float _lastClick;
        private uint _lastClickSerial, _pendingLook;
        private string _goldText = "0", _platText = "0";

        public SecureTradeWindow(SecureTrade trade)
        {
            _trade = trade;
            Rect = new Rect(260, 160, _gumpSize.x, _gumpSize.y);
        }

        public SecureTrade Trade => _trade;
        public override string Key => KeyFor(_trade);
        public static string KeyFor(SecureTrade t) => $"trade:{t.MyContainer}";

        public override bool StillValid => Session.IsOpen(_trade);

        public override void OnOpened()
        {
            UiImage bg = Art.Gump(Background);
            if (bg != null)
                _gumpSize = new Vector2(bg.Width, bg.Height);
            Rect.size = _gumpSize;
        }

        /// <summary>Closed by the player: the trade is cancelled. Closed because the server ended it: nothing to send.</summary>
        public override void OnClosed() => Session.CancelTrade(_trade);

        public override void Draw()
        {
            Rect.height = _gumpSize.y + (_trade.HasLedger ? CoinsHeight : 0);
            UiImage bg = Art.Gump(Background);
            if (bg != null)
                DrawImage(bg, 0, 0);
            else
                DrawResizePic(9200, 0, 0, _gumpSize.x, _gumpSize.y);

            if (CheckBox(_trade.IAccept, 52, 29))
                Session.AcceptTrade(_trade, !_trade.IAccept);
            string me = Session.World.Player?.Name ?? "";
            Text(new Rect(84, 40, 120, 16), me, Ink, 12);
            Text(new Rect(140, 170, 120, 16), _trade.TheirName, Ink, 12, TextAnchor.UpperRight);
            DrawImage(Art.Gump(_trade.TheyAccept ? Checked : Unchecked), 266, 160);

            _drawn.Clear();
            DrawItems(_trade.MyContainer, MyBox, true);
            DrawItems(_trade.TheirContainer, TheirBox, false);
            if (_trade.HasLedger)
                DrawCoins();

            if (Event.current.type == EventType.Repaint)
                Hovered = ItemAt(Event.current.mousePosition)?.Item.Serial ?? 0;
            if (_pendingLook != 0 && Time.realtimeSinceStartup - _lastClick >= DoubleClickTime)
            {
                if (Session.Target == null)
                    Session.Look(_pendingLook);
                _pendingLook = 0;
            }
            HandleMouse();
        }

        private bool CheckBox(bool on, float x, float y)
        {
            UiImage img = Art.Gump(on ? Checked : Unchecked);
            if (img == null)
                return GUI.Button(new Rect(x, y, 20, 20), on ? "X" : "");
            DrawImage(img, x, y);
            return GUI.Button(new Rect(x, y, img.Width, img.Height), GUIContent.none, GUIStyle.none);
        }

        /// <summary>Items at the positions the server gives, kept inside the box as ClassicUO does.</summary>
        private void DrawItems(uint container, Rect box, bool mine)
        {
            foreach (ContainedItem item in Session.World.ItemsIn(container))
            {
                UiImage img = Art.Item(item.Graphic, item.Hue);
                if (img == null)
                    continue;
                float x = box.x + Mathf.Clamp(item.X, 0, Mathf.Max(0, box.width - img.Width));
                float y = box.y + Mathf.Clamp(item.Y, 0, Mathf.Max(0, box.height - img.Height));
                DrawImage(img, x, y);
                var r = new Rect(x, y, img.Width, img.Height);
                if (item.Amount > 1)
                    Text(new Rect(x, y + img.Height - 12, img.Width + 20, 12), item.Amount.ToString(), Ink, 10);
                _drawn.Add((item, img, r, mine));
            }
        }

        /// <summary>Gold and platinum from the account (0x6F 3/4): ours editable within the balance, theirs as offered.</summary>
        private void DrawCoins()
        {
            float top = _gumpSize.y;
            DrawResizePic(9200, 0, top, _gumpSize.x, CoinsHeight);
            var label = new Color(0.92f, 0.88f, 0.78f);
            Text(new Rect(12, top + 8, 140, 16), "Your offer", label, 12, TextAnchor.UpperLeft, true);
            Text(new Rect(_gumpSize.x - 152, top + 8, 140, 16), "Their offer", label, 12, TextAnchor.UpperRight, true);

            Text(new Rect(12, top + 28, 34, 18), "Gold", label, 12);
            _goldText = Digits(GUI.TextField(new Rect(48, top + 26, 90, 18), _goldText, 10));
            Text(new Rect(12, top + 50, 34, 18), "Plat", label, 12);
            _platText = Digits(GUI.TextField(new Rect(48, top + 48, 90, 18), _platText, 10));
            int.TryParse(_goldText, out int gold);
            int.TryParse(_platText, out int plat);
            // ClassicUO sends each change as typed; the session clamps to the ledger and skips repeats.
            Session.OfferTradeGold(_trade, gold, plat);
            if (gold > _trade.MyGold)
                _goldText = _trade.MyGold.ToString();
            if (plat > _trade.MyPlatinum)
                _platText = _trade.MyPlatinum.ToString();

            Text(new Rect(_gumpSize.x - 152, top + 28, 140, 18), $"{_trade.TheirGold:N0} gold", label, 12, TextAnchor.UpperRight);
            Text(new Rect(_gumpSize.x - 152, top + 50, 140, 18), $"{_trade.TheirPlatinum:N0} plat", label, 12, TextAnchor.UpperRight);
            Text(new Rect(144, top + 28, 80, 40), $"of {_trade.BalanceGold:N0}\nof {_trade.BalancePlatinum:N0}",
                new Color(0.75f, 0.75f, 0.7f), 10);
        }

        private static string Digits(string s)
        {
            string d = new string(s.Where(char.IsDigit).ToArray()).TrimStart('0');
            return d.Length == 0 ? "0" : d;
        }

        private (ContainedItem Item, Vector2 Offset, bool Mine)? ItemAt(Vector2 local)
        {
            for (int i = _drawn.Count - 1; i >= 0; i--)
            {
                var (item, img, rect, mine) = _drawn[i];
                if (rect.Contains(local) && img.HitTest((int)(local.x - rect.x), (int)(local.y - rect.y)))
                    return (item, local - rect.position, mine);
            }
            return null;
        }

        private void HandleMouse()
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                {
                    var hit = ItemAt(e.mousePosition);
                    _pressed = hit?.Item;
                    _pressOffset = hit?.Offset ?? Vector2.zero;
                    _pressAt = e.mousePosition;
                    if (_pressed == null)
                        return;
                    if (TryTarget(_pressed.Serial))
                    {
                        _pressed = null;
                        return;
                    }
                    if (e.shift)
                    {
                        Session.RequestContextMenu(_pressed.Serial);
                        _pressed = null;
                        e.Use();
                        return;
                    }
                    if (Time.realtimeSinceStartup - _lastClick < DoubleClickTime && _lastClickSerial == _pressed.Serial)
                    {
                        Session.Use(_pressed.Serial); // a bag in either offer opens to show what's in it
                        _lastClick = 0;
                        _pendingLook = 0;
                        _pressed = null;
                    }
                    else
                    {
                        _lastClick = Time.realtimeSinceStartup;
                        _lastClickSerial = _pressed.Serial;
                        _pendingLook = _pressed.Serial;
                        if (!hit.Value.Mine)
                            _pressed = null; // their offer can be looked at, not taken
                    }
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when e.button == 0 && _pressed != null:
                    if ((e.mousePosition - _pressAt).sqrMagnitude > 16)
                    {
                        _pendingLook = 0;
                        Ui.BeginDrag(new DraggedItem
                        {
                            Serial = _pressed.Serial,
                            Graphic = _pressed.Graphic,
                            Hue = _pressed.Hue,
                            Amount = _pressed.Amount,
                            FromContainer = _trade.MyContainer,
                            FromX = _pressed.X,
                            FromY = _pressed.Y,
                            Grab = _pressOffset
                        });
                        _pressed = null;
                    }
                    e.Use();
                    break;
                case EventType.MouseUp when e.button == 0:
                    _pressed = null;
                    break;
            }
        }

        /// <summary>
        /// Anything dropped on the window is offered: into our container, where it was let go if that's inside our box
        /// (ClassicUO's drop on the trading gump), otherwise wherever the server puts it. Onto a bag in our offer: into it.
        /// </summary>
        public override bool Drop(DraggedItem item, Vector2 local)
        {
            var under = ItemAt(local);
            if (under != null && under.Value.Mine && under.Value.Item.Serial != item.Serial && GameSession.DropsInto(item.Graphic,
                    Art.Data.TileData.GetStatic(item.Graphic).Flags, under.Value.Item.Graphic,
                    Art.Data.TileData.GetStatic(under.Value.Item.Graphic).Flags))
            {
                Session.DropInto(item.Serial, under.Value.Item.Serial, 0xFFFF, 0xFFFF);
                return true;
            }
            if (!MyBox.Contains(local))
            {
                Session.DropInto(item.Serial, _trade.MyContainer, 0xFFFF, 0xFFFF);
                return true;
            }
            UiImage art = Art.Item(item.Graphic, item.Hue);
            int x = Mathf.Clamp((int)(local.x - MyBox.x - item.Grab.x), 0, Mathf.Max(0, (int)MyBox.width - (art?.Width ?? 20)));
            int y = Mathf.Clamp((int)(local.y - MyBox.y - item.Grab.y), 0, Mathf.Max(0, (int)MyBox.height - (art?.Height ?? 20)));
            Session.DropInto(item.Serial, _trade.MyContainer, x, y);
            return true;
        }
    }
}
