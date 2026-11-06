using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A secure trade (0x6F), laid out like TazUO's TradingGump: the classic gump 0x0866, or on 7.0.45.65+ clients
    /// 0x088A with gold and platinum from the account. Our offer on the left with our accept box, theirs on the right
    /// with their name and box. Drop items into our box to offer them, drag them back out, double-click to look inside
    /// a bag. Closing the window cancels the trade.
    /// </summary>
    public sealed partial class SecureTradeWindow : UiWindow
    {
        private const float DoubleClickTime = 0.4f;

        private static readonly Color Ink = new Color(0.22f, 0.16f, 0.08f);

        private readonly SecureTrade _trade;
        private readonly List<(ContainedItem Item, UiImage Image, Rect Rect, bool Mine)> _drawn = new List<(ContainedItem, UiImage, Rect, bool)>();
        private TradeLayout _layout = Classic;
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

        private Rect MyBox => new Rect(_layout.MyBox.X, _layout.MyBox.Y, BoxWidth, BoxHeight);
        private Rect TheirBox => new Rect(_layout.TheirBox.X, _layout.TheirBox.Y, BoxWidth, BoxHeight);

        public SecureTrade Trade => _trade;
        public override string Key => KeyFor(_trade);
        public static string KeyFor(SecureTrade t) => $"trade:{t.MyContainer}";

        public override bool StillValid => Session.IsOpen(_trade);

        public override void OnOpened()
        {
            _layout = LayoutFor(Session.Version);
            UiImage bg = Art.Gump(_layout.Background);
            if (bg != null)
                _gumpSize = new Vector2(bg.Width, bg.Height);
            Rect.size = _gumpSize;
        }

        /// <summary>Closed by the player: the trade is cancelled. Closed because the server ended it: nothing to send.</summary>
        public override void OnClosed() => Session.CancelTrade(_trade);

        public override void Draw()
        {
            UiImage bg = Art.Gump(_layout.Background);
            if (bg != null)
                DrawImage(bg, 0, 0);
            else
                DrawResizePic(9200, 0, 0, _gumpSize.x, _gumpSize.y);

            string me = Session.World.Player?.Name ?? "";
            Text(new Rect(_layout.MyName.X, _layout.MyName.Y, 120, 16), me, Ink, 12);
            Text(new Rect(_layout.TheirNameRight - 160, _layout.TheirNameY, 160, 16), _trade.TheirName, Ink, 12, TextAnchor.UpperRight);
            if (CheckBox(_trade.IAccept, _layout.MyCheck.X, _layout.MyCheck.Y))
                Session.AcceptTrade(_trade, !_trade.IAccept);
            DrawImage(Art.Gump(_trade.TheyAccept ? Checked : Unchecked), _layout.TheirCheck.X, _layout.TheirCheck.Y);

            _drawn.Clear();
            PlaceItems(_trade.MyContainer, MyBox, true);
            PlaceItems(_trade.TheirContainer, TheirBox, false);
            ContainedItem over = MouseIsOver ? ItemAt(Event.current.mousePosition)?.Item : null;
            DrawItems(over);
            if (_layout.HasCoins)
                DrawCoins();

            if (Event.current.type == EventType.Repaint)
                Hovered = over?.Serial ?? 0;
            if (_pendingLook != 0 && Time.realtimeSinceStartup - _lastClick >= DoubleClickTime)
            {
                if (Session.Target == null)
                    Session.Look(_pendingLook);
                _pendingLook = 0;
            }
            HandleMouse();
        }

        /// <summary>TazUO's Checkbox(0x0867, 0x0868) clear, (0x0869, 0x086A) ticked.</summary>
        private bool CheckBox(bool on, float x, float y)
        {
            if (Art.Gump(on ? Checked : Unchecked) == null)
                return GUI.Button(new Rect(x, y, 20, 20), on ? "X" : "");
            return on ? GumpButton(Checked, CheckedPressed, x, y) : GumpButton(Unchecked, UncheckedPressed, x, y);
        }

        /// <summary>Items at the positions the server gives, kept inside the box as TazUO does.</summary>
        private void PlaceItems(uint container, Rect box, bool mine)
        {
            foreach (ContainedItem item in Session.World.ItemsIn(container))
            {
                UiImage img = Art.Item(item.Graphic, item.Hue);
                if (img == null)
                    continue;
                (int fx, int fy) = FitInBox(item.X, item.Y, img.Width, img.Height);
                _drawn.Add((item, img, new Rect(box.x + fx, box.y + fy, img.Width, img.Height), mine));
            }
        }

        /// <summary>The placed items; the one under the mouse outlined, in its own hue (the outline alone is the mouse-over cue).</summary>
        private void DrawItems(ContainedItem over)
        {
            foreach (var (item, img, r, _) in _drawn)
            {
                if (item == over)
                    DrawOutline(img, r.x, r.y);
                DrawImage(img, r.x, r.y);
                if (item.Amount > 1)
                    Text(new Rect(r.x, r.y + r.height - 12, r.width + 20, 12), item.Amount.ToString(), Ink, 10);
            }
        }

        /// <summary>
        /// Gold and platinum on the 0x088A gump, where TazUO puts them: our balance (0x6F 4) across the top, our offer
        /// as two 100 x 20 entries kept within it, their offer (0x6F 3) beside them.
        /// </summary>
        private void DrawCoins()
        {
            var label = new Color(0.92f, 0.88f, 0.78f); // hue 0x0481
            Text(Box(_layout.MyGoldBalance, 120, 16), _trade.BalanceGold.ToString("N0"), label, 12);
            Text(Box(_layout.MyPlatBalance, 120, 16), _trade.BalancePlatinum.ToString("N0"), label, 12);
            Text(Box(_layout.TheirGold, 120, 16), _trade.TheirGold.ToString("N0"), label, 12);
            Text(Box(_layout.TheirPlat, 120, 16), _trade.TheirPlatinum.ToString("N0"), label, 12);

            _goldText = Digits(GUI.TextField(Box(_layout.MyGoldEntry, 100, 20), _goldText, 10));
            _platText = Digits(GUI.TextField(Box(_layout.MyPlatEntry, 100, 20), _platText, 10));
            int.TryParse(_goldText, out int gold);
            int.TryParse(_platText, out int plat);
            // TazUO sends each change as typed; the session clamps to the ledger and skips repeats.
            Session.OfferTradeGold(_trade, gold, plat);
            if (gold > _trade.MyGold)
                _goldText = _trade.MyGold.ToString();
            if (plat > _trade.MyPlatinum)
                _platText = _trade.MyPlatinum.ToString();
        }

        private static Rect Box((int X, int Y) at, float w, float h) => new Rect(at.X, at.Y, w, h);

        private static string Digits(string s)
        {
            string d = new string(s.Where(char.IsDigit).ToArray()).TrimStart('0');
            return d.Length == 0 ? "0" : d;
        }

        private (ContainedItem Item, Vector2 Offset, bool Mine)? ItemAt(Vector2 local)
        {
            var (i, px, py) = MousePick.WindowItem(_drawn.Count, local.x, local.y, Ui.Scale, k => (_drawn[k].Rect.x, _drawn[k].Rect.y),
                _ => false, (k, x, y) => _drawn[k].Image.HitTest(x, y));
            return i < 0 ? ((ContainedItem, Vector2, bool)?)null : (_drawn[i].Item, new Vector2(px, py), _drawn[i].Mine);
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
        /// Let go inside our box, the item is offered where it was dropped, centred on the cursor (TazUO's
        /// TradingGump.OnMouseUp); onto a bag in our offer, into it. Anywhere else on the gump TazUO doesn't drop it,
        /// so it goes back where it came from.
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
            Rect box = MyBox;
            if (!box.Contains(local))
                return false;
            UiImage art = Art.Item(item.Graphic, item.Hue);
            (int bx, int by) = MousePick.UiPixel(local.x, local.y, box.x, box.y, Ui.Scale); // the box pixel the cursor shows
            (int x, int y) = art == null ? FitInBox(bx, by, 0, 0) : DropInBox(bx, by, art.Width, art.Height);
            Session.DropInto(item.Serial, _trade.MyContainer, x, y);
            return true;
        }
    }
}
