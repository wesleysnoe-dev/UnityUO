using System;
using System.Linq;
using UnityEngine;

namespace UOClient.Unity
{
    /// <summary>
    /// Keys and clicks aimed at the world rather than a window: the view keys (T, R, H, zoom), the game's
    /// shortcuts, world clicks and targeting, and dragged items released over the world.
    /// </summary>
    public sealed class WorldInput
    {
        private readonly GameContext _ctx;
        private readonly EntityViews _entities;
        private readonly UI.HoverTooltip _tooltip;
        private readonly ChatLine _chat;
        private readonly Lighting _lighting;
        private readonly RoofController _roofs;
        private readonly Func<UI.UiManager> _ui;
        private readonly Func<Hotkeys> _hotkeys;
        private float _lastWorldClick;
        private Vector2 _lastWorldClickAt;
        private uint _pendingClick; // a single click held until it can't be the start of a double-click
        private uint _pressedItem;  // a movable ground item under the held left button: dragging it picks it up
        private Vector2 _pressedAt;

        private const float DoubleClickTime = 0.35f;
        private const float DragDistance = 6f; // screen pixels the mouse moves before a press becomes a drag

        /// <summary>The window manager and the macros are read through <paramref name="ui"/> and <paramref name="hotkeys"/> on every use: each login makes new ones.</summary>
        public WorldInput(GameContext ctx, EntityViews entities, UI.HoverTooltip tooltip, ChatLine chat, Lighting lighting,
                          RoofController roofs, Func<UI.UiManager> ui, Func<Hotkeys> hotkeys)
        {
            _ctx = ctx;
            _entities = entities;
            _tooltip = tooltip;
            _chat = chat;
            _lighting = lighting;
            _roofs = roofs;
            _ui = ui;
            _hotkeys = hotkeys;
        }

        /// <summary>T overrides the time of day, R toggles roofs, H hides the HUD, the wheel zooms.</summary>
        public void HandleViewKeys(ref float zoom)
        {
            UI.UiManager ui = _ui();
            if (Input.GetKeyDown(KeyCode.T))
                _ctx.Hud.Log(null, _lighting.CycleOverride(_ctx.Session != null));
            if (Input.GetKeyDown(KeyCode.R))
                _ctx.Hud.Log(null, _roofs.Toggle(_ctx.World.RoofsVisible));
            if (Input.GetKeyDown(KeyCode.H))
                _ctx.Hud.Visible = !_ctx.Hud.Visible;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && (ui == null || !ui.IsOverUi(Input.mousePosition))) // windows (chat, world map) scroll themselves
                zoom =Mathf.Clamp(zoom * (wheel > 0 ? 1.12f : 1f / 1.12f), 0.25f, 4f);
        }

        /// <summary>The map tile under the mouse with its surface z near the player, or null over a window.</summary>
        public Vector3Int? MouseTile()
        {
            UI.UiManager ui = _ui();
            if (ui != null && ui.IsOverUi(Input.mousePosition))
                return null;
            Vector3 wp = _ctx.Camera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0));
            Vector2Int tile = _ctx.World.TileAt(_ctx.World.FromUnity(new Vector2(wp.x, wp.y)), _ctx.Player.Z);
            return new Vector3Int(tile.x, tile.y, _ctx.Walk.SurfaceZ(tile.x, tile.y, _ctx.Player.Z));
        }

        /// <summary>
        /// P paperdoll, B backpack, F1 help, Tab war mode, Escape cancels a target. Clicks in the world: single
        /// click names (and on a mobile opens its context menu: a vendor's Buy/Sell/Train), double-click uses
        /// (attacks in war mode), shift-click opens anything's context menu, dragging a ground item picks it up, and with a
        /// target cursor up, a click targets.
        /// </summary>
        public void Update()
        {
            UI.UiManager ui = _ui();
            if (_ctx.Session == null)
                return;
            // Like ClassicUO, the single click waits out the double-click time, so a double-click doesn't flash a popup.
            // A press still held on a movable item may yet become a drag: its click waits for the release.
            if (_pendingClick != 0 && Time.realtimeSinceStartup - _lastWorldClick >= DoubleClickTime && !(_pressedItem != 0 && Input.GetMouseButton(0)))
            {
                if (_ctx.Session.Target == null)
                    _ctx.Session.SingleClick(_pendingClick);
                _pendingClick = 0;
            }
            if (ui == null || _chat.Typing || ui.HasKeyboardFocus)
                return;
            _hotkeys()?.Update(_ctx.Session);
            _tooltip.UpdateHover(ui, _entities.PickAt);
            if (Input.GetKeyDown(KeyCode.P))
                _ctx.Session.OpenPaperdoll();
            if (Input.GetKeyDown(KeyCode.B) && !_ctx.Session.OpenBackpack())
                _ctx.Hud.Log(null, "You have no backpack.");
            if (Input.GetKeyDown(KeyCode.F1))
                _ctx.Session.RequestHelp();
            if (Input.GetKeyDown(KeyCode.Tab))
                _ctx.Session.SetWarMode(!_ctx.Session.WarMode);
            if (Input.GetKeyDown(KeyCode.K))
                ui.Open(new UI.SkillsWindow());
            if (Input.GetKeyDown(KeyCode.C))
                ui.Open(new UI.StatusWindow());
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_ctx.Session.Target != null)
                    _ctx.Session.CancelTarget();
                else if (ui.Windows.LastOrDefault(w => w.CanClose) is UI.UiWindow top)
                    ui.Close(top); // the topmost window that can close (not the top bar)
            }

            // Pressing on a movable ground item and moving the mouse picks it up (0x07), as in ClassicUO; the
            // UI carries it from there and the release decides the drop (a window, a mobile, an item, a tile).
            if (_pressedItem != 0)
            {
                if (!Input.GetMouseButton(0) || _ctx.Session.Target != null || ui.Dragging != null)
                    _pressedItem = 0;
                else if (((Vector2)Input.mousePosition - _pressedAt).sqrMagnitude > DragDistance * DragDistance)
                {
                    BeginGroundDrag(ui, _pressedItem);
                    _pressedItem = 0;
                }
            }

            // The quest arrow: left click notifies the server, right click cancels it (0xBF 0x07).
            if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !ui.IsOverUi(Input.mousePosition) && OverQuestArrow())
            {
                _ctx.Session.ClickQuestArrow(Input.GetMouseButtonDown(1));
                return;
            }

            if (!Input.GetMouseButtonDown(0) || ui.IsOverUi(Input.mousePosition) || _ctx.Hud.JournalRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
                return;
            Vector2 mouse = Input.mousePosition;
            uint picked = _entities.PickAt(mouse);
            UI.UiWindow popup = ui.Find("context");
            if (popup != null)
                ui.Close(popup);

            // Shift+click: the context menu at once, for items too (a plain click on a mobile asks after the double-click time).
            if (_ctx.Session.Target == null && picked != 0 && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                _ctx.Session.RequestContextMenu(picked);
                return;
            }

            // Target cursor: whatever is under the mouse, or the ground there.
            if (_ctx.Session.Target != null)
            {
                if (picked != 0 && !_ctx.Session.Target.IsMulti) // placement goes where its preview stands
                {
                    _ctx.Session.TargetObject(picked);
                }
                else if (_ctx.Session.Target.AllowGround)
                {
                    Vector3Int tile = MouseTile().Value;
                    // A static there (a tree to chop, a floor) is targeted as itself, else the land.
                    var st = _ctx.Session.Target.IsMulti ? null : _ctx.Walk.TargetStaticAt(tile.x, tile.y, _ctx.Player.Z);
                    if (st is var (cell, surface, height))
                        _ctx.Session.TargetStatic(tile.x, tile.y, cell.Z, cell.Graphic, surface, height);
                    else
                        _ctx.Session.TargetGround(tile.x, tile.y, tile.z);
                }
                else
                {
                    _ctx.Hud.Log(null, "Target an object.");
                }
                return;
            }

            _pressedItem = picked != 0 && _ctx.Session.World.Items.TryGetValue(picked, out Core.WorldItem ground) && ground.Movable ? picked : 0;
            _pressedAt = mouse;

            bool doubleClick = Time.realtimeSinceStartup - _lastWorldClick < DoubleClickTime && (mouse - _lastWorldClickAt).sqrMagnitude < 64;
            _lastWorldClick = doubleClick ? 0 : Time.realtimeSinceStartup;
            _lastWorldClickAt = mouse;
            if (!doubleClick && _pendingClick != 0)
                _ctx.Session.SingleClick(_pendingClick); // a quick click elsewhere: the first one still counts
            _pendingClick = doubleClick ? 0 : picked;
            if (picked == 0 || !doubleClick)
                return; // a single click goes out once the double-click time passes
            if (picked == _ctx.Session.World.PlayerSerial)
                _ctx.Session.OpenPaperdoll();
            else if (_ctx.Session.WarMode && _ctx.Session.World.Mobiles.ContainsKey(picked))
                _ctx.Session.Attack(picked);
            else
                _ctx.Session.Use(picked);
        }

        /// <summary>Lifts a ground item onto the cursor; a drop the server or a window refuses puts it back where it lay.</summary>
        private void BeginGroundDrag(UI.UiManager ui, uint serial)
        {
            if (!_ctx.Session.World.Items.TryGetValue(serial, out Core.WorldItem item))
                return;
            _pendingClick = 0; // a drag, not a click
            _lastWorldClick = 0;
            UI.UiImage art = ui.Art.Item(item.Graphic, item.Hue);
            ui.BeginDrag(new UI.DraggedItem
            {
                Serial = item.Serial,
                Graphic = item.Graphic,
                Hue = item.Hue,
                Amount = Math.Max(1, item.Amount),
                FromGround = true,
                FromX = item.X,
                FromY = item.Y,
                FromZ = item.Z,
                Grab = art != null ? new Vector2(art.Width / 2f, art.Height / 2f) : Vector2.zero
            });
        }

        /// <summary>The mouse is over the quest arrow's on-screen or edge marker.</summary>
        public bool OverQuestArrow() =>
            _ctx.Hud.QuestArrowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));

        /// <summary>
        /// A dragged item released over the world at a screen point (bottom-left origin): onto a character there
        /// (giving it, or loading a pack animal), into a container, corpse or like stack there, beside any other
        /// item (on its tile, on top of a table), else onto the tile under the cursor. What the server refuses bounces back.
        /// </summary>
        public void DropInWorld(UI.DraggedItem item, Vector2 screen)
        {
            uint under = _entities.PickAt(screen);
            if (under != 0 && under != item.Serial)
            {
                if (!_ctx.Session.World.Items.TryGetValue(under, out Core.WorldItem onto))
                {
                    _ctx.Session.DropInto(item.Serial, under, 0xFFFF, 0xFFFF); // a mobile
                    return;
                }
                if (!onto.IsMulti)
                {
                    IO.Formats.StaticTileData data = _ctx.Data.TileData.GetStatic(onto.Graphic);
                    if (Core.GameSession.DropsInto(item.Graphic, _ctx.Data.TileData.GetStatic(item.Graphic).Flags, onto.Graphic, data.Flags))
                        _ctx.Session.DropInto(item.Serial, under, 0xFFFF, 0xFFFF);
                    else
                        _ctx.Session.DropOnGround(item.Serial, onto.X, onto.Y, onto.Z + (data.IsSurface ? data.Height : 0));
                    return;
                }
            }
            Vector3 wp = _ctx.Camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0));
            Vector2Int tile = _ctx.World.TileAt(_ctx.World.FromUnity(new Vector2(wp.x, wp.y)), _ctx.Player.Z);
            _ctx.Session.DropOnGround(item.Serial, tile.x, tile.y, _ctx.Walk.SurfaceZ(tile.x, tile.y, _ctx.Player.Z));
        }
    }
}
