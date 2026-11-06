using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A container as TazUO's GridContainer shows it (GridContainersDefaultToOldStyleView off, GameOptions.OpensGrid): its
    /// items in square cells, in the order they arrived (TazUO's AutoSort is off by default), in a scroll area over a
    /// background at ContainerOpacity. Each whole cell takes the mouse: click to name, double-click to use (or loot, in a
    /// corpse, with DoubleClickToLootInsideContainers), drag to move it into a bag or stack here, another container, the
    /// paperdoll or the world; drops on the grid go into the item under them or into this container. The item under the
    /// mouse is outlined (this client's cue; TazUO lays a white wash over the cell). The layout is in
    /// GridContainerWindow.Layout.cs; what TazUO's grid has beyond it is listed in LIVE_PARITY.md.
    /// </summary>
    public sealed partial class GridContainerWindow : UiWindow
    {
        private readonly uint _serial;
        private readonly ushort _gump;
        private readonly List<ContainedItem> _items = new List<ContainedItem>();
        private readonly List<(int X, int Y)> _origins = new List<(int, int)>();
        private readonly Dictionary<uint, byte> _wornLayers = new Dictionary<uint, byte>();
        private int _scroll, _maxScroll;
        private bool _barDragging;
        private ContainedItem _pressed;
        private bool _shiftPressed; // pressed with Shift on a stack: a drag splits it, a release opens its context menu
        private Vector2 _pressAt, _pressGrab;
        private float _lastClick;
        private uint _lastClickSerial;
        private uint _pendingLook; // a single click held until it can't be the start of a double-click or a drag

        private const float DoubleClickTime = 0.4f;

        /// <summary>
        /// The options' GridContainersDefaultToOldStyleView (TazUO's): on, containers open as the classic ContainerWindow;
        /// off, as this (SessionUiBinder, GameOptions.OpensGrid). Windows already open keep their style.
        /// </summary>
        public static bool DefaultToOldStyleView { get; set; } = GameOptions.Defaults().gridContainersDefaultToOldStyleView;

        /// <summary>The options' ContainerOpacity (TazUO's, 0-100): the background and border alpha.</summary>
        public static int Opacity { get; set; } = GameOptions.Defaults().containerOpacity;

        /// <summary>The options' GridContainersScale (TazUO's, 50-200 %): the cell size (GameOptions.GridItemSize).</summary>
        public static int ScalePercent { get; set; } = GameOptions.Defaults().gridContainersScale;

        /// <summary>The options' GridContainerScaleItems (TazUO's): the art scales with the cells (GameOptions.GridArtSize).</summary>
        public static bool ScaleItems { get; set; } = GameOptions.Defaults().gridContainerScaleItems;

        // The opaque bounds of each item art (TazUO's GetRealArtBounds), by the art it was measured on.
        private static readonly Dictionary<UiImage, (int X, int Y, int Width, int Height)> Bounds =
            new Dictionary<UiImage, (int, int, int, int)>();

        public GridContainerWindow(uint serial, ushort gump)
        {
            _serial = serial;
            _gump = gump;
            Rect = new Rect(100, 100, 0, 0); // GridContainer's _lastX / _lastY start; UiManager steps a new one aside
            FitToCells();
        }

        public override string Key => $"container:{_serial}"; // one window per container, whichever style
        public uint Serial => _serial;

        public override bool StillValid => Session.World.OpenContainers.ContainsKey(_serial);

        public override void OnClosed() => Session.World.OpenContainers.Remove(_serial);

        private int Cell => GameOptions.GridItemSize(ScalePercent);

        /// <summary>The window's size follows the cell size (again each frame, so a new scale takes at once).</summary>
        private void FitToCells()
        {
            var (w, h) = WindowSize(Cell);
            Rect.size = new Vector2(w, h);
        }

        private bool IsCorpse => Session.World.Items.TryGetValue(_serial, out WorldItem w) && w.IsCorpse;

        private bool IsPlayerBackpack => _serial != 0 && Session.World.Worn(Session.World.Player, 21) == _serial;

        /// <summary>What is listed, in arrival order: GameOptions.GridListsItem (a corpse's hair, beard, pack and mount left out).</summary>
        private void Collect()
        {
            _items.Clear();
            _wornLayers.Clear();
            bool corpse = Session.World.Items.TryGetValue(_serial, out WorldItem self) && self.IsCorpse;
            if (corpse && self.CorpseEquipment != null)
                foreach (KeyValuePair<byte, uint> e in self.CorpseEquipment)
                    _wornLayers[e.Value] = e.Key;
            foreach (ContainedItem item in Session.World.ItemsIn(_serial))
            {
                StaticTileData tile = Art.Data.TileData.GetStatic(item.Graphic);
                _wornLayers.TryGetValue(item.Serial, out byte worn);
                if (GameOptions.GridListsItem(corpse, worn, (tile.Flags & TileFlags.Wearable) != 0, tile.Layer))
                    _items.Add(item);
            }
        }

        private string ContainerName()
        {
            // The container's tooltip name as the server last sent it (not asked for here); TazUO's Item.Name.
            if (Session.Properties.TryGetValue(_serial, out PropertyList props) && props.Lines.Count > 0)
                return PlainText(props.Lines[0]);
            return null;
        }

        private static (int X, int Y, int Width, int Height) BoundsOf(UiImage img)
        {
            if (!Bounds.TryGetValue(img, out var b))
                Bounds[img] = b = OpaqueBounds(img.Width, img.Height, img.HitTest);
            return b;
        }

        /// <summary>Where an item's art is drawn in its cell (content pixels), as GridItem.Draw places its real bounds.</summary>
        private Rect ArtRect(UiImage img, (int X, int Y) origin, int cell)
        {
            var b = BoundsOf(img);
            var (w, ox) = GameOptions.GridArtSize(b.Width, cell, ScaleItems, ScalePercent);
            var (h, oy) = GameOptions.GridArtSize(b.Height, cell, ScaleItems, ScalePercent);
            return new Rect(origin.X + ox, origin.Y + oy, w, h);
        }

        /// <summary>The part <paramref name="b"/> (image pixels from the top-left) of a texture, drawn into <paramref name="dest"/>.</summary>
        private static void DrawPart(Texture2D tex, Rect dest, (int X, int Y, int Width, int Height) b, int imgW, int imgH)
        {
            if (tex == null || imgW <= 0 || imgH <= 0)
                return;
            // Textures are stored bottom row first (UnityTextures.ToTexture); an HD texture covers the same 0-1 range.
            var uv = new Rect((float)b.X / imgW, 1f - (float)(b.Y + b.Height) / imgH, (float)b.Width / imgW, (float)b.Height / imgH);
            GUI.DrawTextureWithTexCoords(dest, tex, uv);
        }

        private static void Fill(Rect r, Color c)
        {
            Color saved = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = saved;
        }

        public override void Draw()
        {
            FitToCells();
            int cell = Cell;
            float alpha = GameOptions.GridBackgroundAlpha(Opacity);
            int w = (int)Rect.width, h = (int)Rect.height;
            var area = ScrollArea(w, h);
            var view = new Rect(area.X, area.Y, area.Width - ScrollbarWidth, area.Height);

            // Background (AlphaBlendControl: black, Grid_UseContainerHue off) and the default border, both at the opacity.
            Fill(new Rect(BorderWidth, BorderWidth, w - BorderWidth * 2, h - BorderWidth * 2), new Color(0, 0, 0, alpha));
            Color saved = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            UiImage hb = Art.Gump(BorderHorizontal), vb = Art.Gump(BorderVertical);
            DrawTiled(hb, 0, 0, w, BorderWidth);
            DrawTiled(hb, 0, h - BorderWidth, w, BorderWidth);
            DrawTiled(vb, 0, BorderWidth, BorderWidth, h - BorderWidth * 2);
            DrawTiled(vb, w - BorderWidth, BorderWidth, BorderWidth, h - BorderWidth * 2);
            GUI.color = saved;

            Collect();
            Color textColor = Art.HueColor(TextHue, new Color(0.9f, 0.95f, 1f));
            Text(new Rect(BorderWidth + 2, BorderWidth + 2, w - BorderWidth * 2 - 4, LabelHeight - 2),
                 Title(ContainerName(), _items.Count), textColor, 12);

            CellOrigins(_items.Count, cell, area.Width, _origins);
            _maxScroll = MaxScroll(_origins, cell, area.Height);
            _scroll = ClampScroll(_scroll, _maxScroll);

            Vector2 mouse = Event.current.mousePosition;
            int overIndex = MouseIsOver ? CellAt(_origins, cell, mouse.x - area.X, mouse.y - area.Y, area.Width, area.Height, _scroll) : -1;
            ContainedItem over = overIndex >= 0 ? _items[overIndex] : null;

            // The cells, clipped to the scroll area left of the bar (GridScrollArea's ClipBegin).
            GUI.BeginGroup(view);
            DateTime now = DateTime.Now;
            for (int i = 0; i < _items.Count; i++)
            {
                var (ox, oy) = _origins[i];
                int y = oy - _scroll;
                if (y + cell <= 0 || y >= area.Height)
                    continue;
                var cellRect = new Rect(ox, y, cell, cell);
                Fill(cellRect, new Color(0, 0, 0, CellBackgroundAlpha));
                var edge = new Color(1, 1, 1, CellBorderAlpha);
                Fill(new Rect(ox, y, cell, 1), edge);
                Fill(new Rect(ox, y + cell - 1, cell, 1), edge);
                Fill(new Rect(ox, y + 1, 1, cell - 2), edge);
                Fill(new Rect(ox + cell - 1, y + 1, 1, cell - 2), edge);

                ContainedItem item = _items[i];
                UiImage img = Art.Item(ItemStack.DisplayedGraphic(item.Graphic, item.Amount), item.Hue); // drawn once (GridItem.cs:225)
                if (img == null)
                    continue;
                var b = BoundsOf(img);
                Rect art = ArtRect(img, (ox, y), cell);
                // The outline (art one pixel bigger all round, ObjectHighlight) under the art: the edge alone is the cue.
                if (item == over || (ContainerWindow.SelectedContainer != 0 && item.Serial == ContainerWindow.SelectedContainer))
                {
                    UiImage outline = Art.Outline(img);
                    float px = b.Width > 0 ? art.width / b.Width : 1, py = b.Height > 0 ? art.height / b.Height : 1;
                    DrawPart(outline?.Texture, new Rect(art.x - px, art.y - py, art.width + 2 * px, art.height + 2 * py),
                             (b.X, b.Y, b.Width + 2, b.Height + 2), outline?.Width ?? 0, outline?.Height ?? 0);
                }
                DrawPart(img.Texture, art, b, img.Width, img.Height);

                // GridItem's count: the amount of a stackable (Generic) item, bottom-left of the cell.
                if (item.Amount > 1 && (Art.Data.TileData.GetStatic(item.Graphic).Flags & TileFlags.Generic) != 0)
                {
                    Text(new Rect(ox + 2, y + cell - 14, cell - 2, 14), item.Amount.ToString(), Color.black, 10);
                    Text(new Rect(ox + 1, y + cell - 15, cell - 2, 14), item.Amount.ToString(), textColor, 10);
                }

                // A single click's answer over the cell for a few seconds (GridItem.AddText), as ContainerWindow does.
                if (Event.current.type == EventType.Repaint && Session.World.RecentLine(item.Serial, now, 3) is JournalEntry line)
                {
                    var r = new Rect(ox + cell / 2f - 90, Mathf.Max(0, y - 2), 180, 16);
                    Text(new Rect(r.x + 1, r.y + 1, r.width, r.height), line.Text, Color.black, 11, TextAnchor.MiddleCenter);
                    Text(r, line.Text, Art.HueColor(line.Hue, new Color(0.95f, 0.95f, 0.9f)), 11, TextAnchor.MiddleCenter);
                }
            }
            GUI.EndGroup();

            // The scroll bar (GridScrollArea's, 14 wide on the right).
            var bar = new Rect(area.X + area.Width - ScrollbarWidth, area.Y, ScrollbarWidth, area.Height);
            Fill(bar, new Color(0, 0, 0, Mathf.Max(0.35f, alpha)));
            var (top, height) = Thumb(_scroll, _maxScroll, area.Height);
            if (_maxScroll > 0)
                Fill(new Rect(bar.x + 2, bar.y + top, ScrollbarWidth - 4, height), new Color(0.75f, 0.72f, 0.65f, 0.9f));

            if (Event.current.type == EventType.Repaint)
                Hovered = over?.Serial ?? 0;
            if (_pendingLook != 0 && Time.realtimeSinceStartup - _lastClick >= DoubleClickTime)
            {
                if (Session.Target == null)
                    Session.Look(_pendingLook);
                _pendingLook = 0;
            }
            HandleMouse(area, bar);
        }

        /// <summary>The item under a window point and the art pixel there (the drag's grab point), or null.</summary>
        private (ContainedItem Item, Vector2 Grab)? ItemAt(Vector2 local)
        {
            var area = ScrollArea((int)Rect.width, (int)Rect.height);
            int cell = Cell;
            int i = CellAt(_origins, cell, local.x - area.X, local.y - area.Y, area.Width, area.Height, _scroll);
            if (i < 0 || i >= _items.Count)
                return null;
            ContainedItem item = _items[i];
            UiImage img = Art.Item(ItemStack.DisplayedGraphic(item.Graphic, item.Amount), item.Hue);
            if (img == null)
                return (item, Vector2.zero);
            Rect art = ArtRect(img, (_origins[i].X, _origins[i].Y - _scroll), cell);
            var (gx, gy) = ArtPixel(local.x - area.X, local.y - area.Y, art.x, art.y, art.width, art.height, BoundsOf(img), img.Width, img.Height);
            return (item, new Vector2(gx, gy));
        }

        private void HandleMouse((int X, int Y, int Width, int Height) area, Rect bar)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.ScrollWheel when MouseIsOver:
                    _scroll = ClampScroll(_scroll + (e.delta.y > 0 ? ScrollStep : -ScrollStep), _maxScroll);
                    e.Use();
                    break;
                case EventType.MouseDown when e.button == 0 && Ui.Dragging == null && bar.Contains(e.mousePosition):
                    _barDragging = true;
                    _scroll = ScrollFromBar(e.mousePosition.y - area.Y, _maxScroll, area.Height);
                    e.Use();
                    break;
                case EventType.MouseDown when e.button == 0 && Ui.Dragging == null: // holding a split stack, the click is its drop
                {
                    var hit = ItemAt(e.mousePosition);
                    _pressed = hit?.Item;
                    _pressGrab = hit?.Grab ?? Vector2.zero;
                    _pressAt = e.mousePosition;
                    if (_pressed == null)
                    {
                        // Empty grid space while targeting targets the container (GridContainer.ScrollArea_MouseUp);
                        // otherwise the press moves the window.
                        var inArea = new Rect(area.X, area.Y, area.Width - ScrollbarWidth, area.Height);
                        if (inArea.Contains(e.mousePosition))
                            TryTarget(_serial);
                        return;
                    }
                    if (TryTarget(_pressed.Serial))
                    {
                        _pressed = null;
                        return;
                    }
                    if (e.shift)
                    {
                        if (GameSession.CanSplit(_pressed.Amount, Art.Data.TileData.GetStatic(_pressed.Graphic).Flags))
                        {
                            _shiftPressed = true;
                            e.Use();
                            return;
                        }
                        Session.RequestContextMenu(_pressed.Serial);
                        _pressed = null;
                        e.Use();
                        return;
                    }
                    _shiftPressed = false;
                    if (Time.realtimeSinceStartup - _lastClick < DoubleClickTime && _lastClickSerial == _pressed.Serial)
                    {
                        if (!TryLoot(_pressed))
                            Session.Use(_pressed.Serial);
                        _lastClick = 0;
                        _pendingLook = 0;
                        _pressed = null;
                    }
                    else
                    {
                        if (_pendingLook != 0 && Session.Target == null)
                            Session.Look(_pendingLook); // a quick click on another item: the first one still counts
                        _lastClick = Time.realtimeSinceStartup;
                        _lastClickSerial = _pressed.Serial;
                        _pendingLook = _pressed.Serial;
                    }
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when e.button == 0 && _barDragging:
                    _scroll = ScrollFromBar(e.mousePosition.y - area.Y, _maxScroll, area.Height);
                    e.Use();
                    break;
                case EventType.MouseDrag when e.button == 0 && _pressed != null:
                    if ((e.mousePosition - _pressAt).sqrMagnitude > 16)
                    {
                        _pendingLook = 0; // a drag, not a click
                        Ui.Lift(new DraggedItem
                        {
                            Serial = _pressed.Serial,
                            Graphic = _pressed.Graphic,
                            Hue = _pressed.Hue,
                            Amount = _pressed.Amount,
                            FromContainer = _serial,
                            FromX = _pressed.X,
                            FromY = _pressed.Y,
                            Grab = _pressGrab
                        }, e.shift || _shiftPressed);
                        _pressed = null;
                        _shiftPressed = false;
                    }
                    e.Use();
                    break;
                case EventType.MouseUp when e.button == 0:
                    if (_shiftPressed && _pressed != null)
                        Session.RequestContextMenu(_pressed.Serial); // a shift-click, not a split
                    _pressed = null;
                    _shiftPressed = false;
                    if (_barDragging)
                    {
                        _barDragging = false;
                        e.Use();
                    }
                    break;
            }
        }

        /// <summary>A double-click that loots (GameOptions.GridLootsOnDoubleClick): lifted whole and dropped into the backpack.</summary>
        private bool TryLoot(ContainedItem item)
        {
            World world = Session.World;
            uint pack = world.Worn(world.Player, 21);
            StaticTileData tile = Art.Data.TileData.GetStatic(item.Graphic);
            if (!GameOptions.GridLootsOnDoubleClick(ContainerWindow.DoubleClickToLoot, IsCorpse, IsPlayerBackpack,
                    (tile.Flags & TileFlags.Container) != 0, tile.Layer, pack != 0))
                return false;
            Session.Lift(item.Serial, item.Amount);
            Session.DropInto(item.Serial, pack, 0xFFFF, 0xFFFF);
            return true;
        }

        public override bool Drop(DraggedItem item, Vector2 local)
        {
            ContainedItem under = ItemAt(local)?.Item;
            bool other = under != null && under.Serial != item.Serial;
            bool takes = other && GameSession.DropsInto(item.Graphic, Art.Data.TileData.GetStatic(item.Graphic).Flags,
                under.Graphic, Art.Data.TileData.GetStatic(under.Graphic).Flags);
            switch (DropKindFor(other, takes, item.FromContainer == _serial))
            {
                case DropKind.IntoItem:
                    Session.DropInto(item.Serial, under.Serial, 0xFFFF, 0xFFFF);
                    break;
                case DropKind.BackInPlace:
                    Session.DropInto(item.Serial, _serial, item.FromX, item.FromY);
                    break;
                default:
                    Session.DropInto(item.Serial, _serial, 0xFFFF, 0xFFFF);
                    break;
            }
            return true;
        }
    }
}
