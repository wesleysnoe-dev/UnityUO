using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UOClient.Core;
using UOClient.Net;
using static UOClient.Unity.CoroutineUtil;

namespace UOClient.Unity
{
    /// <summary>
    /// Unattended check: waits for the world, takes a screenshot, then runs a long route across
    /// several chunk boundaries while recording frame times, and writes a report.
    /// Online, it then goes through sets of windows, vendor, effects, tooltips, dragging, gumps and macros.
    /// </summary>
    internal sealed class CaptureTour
    {
        private readonly GameContext _ctx;
        private readonly ICaptureHost _host;

        private string _dir;
        private string _startCut;
        private int _rejected, _startX, _startY, _steps;
        private string _frameReport, _hitchReport;
        private readonly StringBuilder _uiReport = new StringBuilder("offline: no windows");
        private bool _gotCursor, _warOn;
        private int _effectsLive;
        private string _tooltipReport, _paperdollTip, _dragReport;

        public CaptureTour(GameContext ctx, ICaptureHost host)
        {
            _ctx = ctx;
            _host = host;
        }

        // Read on every use, as WorldDemo's own properties were: the binder is rebuilt when a session starts.
        private UI.UiManager Ui => _host.Ui?.Ui;
        private UI.ChatLog ChatLog => _host.Ui?.ChatLog;

        public IEnumerator Run(bool quitWhenDone)
        {
            _dir = Path.Combine(ClientPaths.ProjectRoot, "Library", "UOClient");
            Directory.CreateDirectory(_dir);
            float waitUntil = Time.realtimeSinceStartup + 30f;
            while (!_host.Ready && Time.realtimeSinceStartup < waitUntil)
                yield return null;
            if (!_host.Ready)
            {
                File.WriteAllText(Path.Combine(_dir, "demo_capture_done.txt"), "not ready: " + (_host.LoadError ?? _ctx.Hud.ConnectionStatus));
                if (quitWhenDone)
                    Application.Quit();
                yield break;
            }
            yield return WaitFrames(30);
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "stream_1_start.png"));
            yield return WaitFrames(10);
            _startCut = $"start {_ctx.Player.X},{_ctx.Player.Y},{_ctx.Player.Z}: upper floors cut at {(_host.CutZ.HasValue ? _host.CutZ.Value.ToString() : "none (outdoors)")}";

            foreach (object step in Inline(RunRoute()))
                yield return step;

            if (_ctx.Session != null && Ui != null)
            {
                foreach (object step in Inline(WindowsSet())) yield return step;
                foreach (object step in Inline(ButtonsSet())) yield return step;
                foreach (object step in Inline(ShopSet())) yield return step;
                foreach (object step in Inline(EffectsAndHouseSet())) yield return step;
                foreach (object step in Inline(TooltipSet())) yield return step;
                foreach (object step in Inline(DragSet())) yield return step;
                foreach (object step in Inline(GumpSet())) yield return step;
                foreach (object step in Inline(MacroSet())) yield return step;
                AppendUiSummary();
            }

            WriteReport();

            if (quitWhenDone)
                Application.Quit();
        }

        /// <summary>Run a long route: keep heading one way, turning when blocked, so the player crosses chunk edges.</summary>
        private IEnumerator RunRoute()
        {
            Action<int, int, sbyte, byte> onReject = (x, y, z, d) => _rejected++;
            if (_ctx.Session != null)
                _ctx.Session.MoveRejected += onReject;
            _startX = _ctx.Player.X;
            _startY = _ctx.Player.Y;
            int[] preference = { 2, 3, 1, 4, 0, 5, 7, 6 }; // east first, then around
            int heading = 0, stuckFor = 0;
            _host.Frames.Reset();
            _host.Sections.Hitches.Clear();
            float until = Time.realtimeSinceStartup + 25f;
            while (_steps < 60 && Time.realtimeSinceStartup < until)
            {
                int dir2 = preference[heading % preference.Length];
                bool moved = _host.TryStep(dir2, run: true);
                if (moved && _ctx.Player.IsMoving)
                {
                    _steps++;
                    stuckFor = 0;
                }
                else if (!_ctx.Player.IsMoving && ++stuckFor > 20)
                {
                    heading++;
                    stuckFor = 0;
                }
                yield return null;
            }
            _frameReport = _host.Frames.Summary();
            _hitchReport = _host.Sections.Hitches.Count == 0 ? "none" : "\n  " + string.Join("\n  ", _host.Sections.Hitches);
            yield return WaitFrames(30);
            if (_ctx.Session != null)
                _ctx.Session.MoveRejected -= onReject;
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "stream_2_after_run.png"));
            yield return WaitFrames(10);
        }

        /// <summary>Windows: paperdoll, backpack and the server's help gump (uses are paced by the session).</summary>
        private IEnumerator WindowsSet()
        {
            _ctx.Session.OpenPaperdoll();
            _ctx.Session.OpenBackpack();
            _ctx.Session.RequestHelp();
            float uiUntil = Time.realtimeSinceStartup + 8f;
            bool HasAll() => Ui.Windows.Any(w => w is UI.PaperdollWindow) && Ui.Windows.Any(w => w is UI.ContainerWindow)
                             && Ui.Windows.Any(w => w is UI.ServerGumpWindow);
            while (!HasAll() && Time.realtimeSinceStartup < uiUntil)
                yield return null;
            yield return WaitFrames(30);
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui_1_windows.png"));
            yield return WaitFrames(10);
            _uiReport.Clear().Append(
                $"{Ui.Windows.Count} windows: " + string.Join(", ", Ui.Windows.Select(w => $"{w.Key} at {w.Rect.x:F0},{w.Rect.y:F0} {w.Rect.width:F0}x{w.Rect.height:F0}")) +
                $"; backpack 0x{_ctx.Session.World.Worn(_ctx.Session.World.Player, 21):X8}, {_ctx.Session.World.OpenContainers.Count} containers open, " +
                $"{_ctx.Session.World.Contained.Count} contained items; use delay {_ctx.Session.UseDelay}s; last journal: " +
                string.Join(" | ", _ctx.Session.World.Journal.Skip(Math.Max(0, _ctx.Session.World.Journal.Count - 3)).Select(j => j.Text)));
        }

        /// <summary>Second set: the paperdoll's buttons, war mode, and a skill that asks for a target.</summary>
        private IEnumerator ButtonsSet()
        {
            foreach (UI.UiWindow w in Ui.Windows.ToList())
                if (!(w is UI.PaperdollWindow) && w.CanClose)
                    Ui.Close(w);
            _host.RunCommand("skills");
            _host.RunCommand("status");
            _host.RunCommand("options");
            _ctx.Session.SetWarMode(true);
            _ctx.Session.UseSkill(1); // Anatomy
            float targetUntil = Time.realtimeSinceStartup + 4f;
            while (_ctx.Session.Target == null && Time.realtimeSinceStartup < targetUntil)
                yield return null;
            _gotCursor = _ctx.Session.Target != null;
            yield return WaitFrames(20);
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui_2_buttons.png"));
            yield return WaitFrames(10);
            if (_gotCursor)
                _ctx.Session.TargetObject(_ctx.Session.World.PlayerSerial);
            float warUntil = Time.realtimeSinceStartup + 2f;
            while (!_ctx.Session.WarMode && Time.realtimeSinceStartup < warUntil)
                yield return null;
            _warOn = _ctx.Session.WarMode;
            _ctx.Session.Say("Testing overhead speech");
            yield return new WaitForSecondsRealtime(1.2f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui_3_overheads.png"));
            yield return WaitFrames(10);
            _ctx.Session.SetWarMode(false);
        }

        /// <summary>Third set: a vendor's context menu, then its buy list; buys the cheapest thing and sells it back.</summary>
        private IEnumerator ShopSet()
        {
            foreach (UI.UiWindow w in Ui.Windows.ToList())
                if (w.CanClose) Ui.Close(w);
            string shopReport = "no vendor in range";
            var candidates = _ctx.Session.World.Mobiles.Values
                .Where(v => (v.Notoriety == 1 || v.Notoriety == 7) && v.IsHuman && v.Serial != _ctx.Session.World.PlayerSerial
                            && !string.IsNullOrEmpty(v.Name) && Math.Max(Math.Abs(v.X - _ctx.Player.X), Math.Abs(v.Y - _ctx.Player.Y)) <= 12)
                .OrderBy(v => Math.Max(Math.Abs(v.X - _ctx.Player.X), Math.Abs(v.Y - _ctx.Player.Y)))
                .Take(8).ToList();
            Core.ContextMenu menu = null;
            Core.ShopList shop = null;
            Action<Core.ContextMenu> onMenu = cm => menu = cm;
            Action<Core.ShopList> onShop = s => shop = s;
            _ctx.Session.ContextMenuOpened += onMenu;
            _ctx.Session.ShopOpened += onShop;
            Core.Mobile vendor = null;
            var tried = new List<string>();
            foreach (Core.Mobile c in candidates)
            {
                menu = null;
                _ctx.Session.RequestContextMenu(c.Serial);
                float menuUntil = Time.realtimeSinceStartup + 3f;
                while (menu == null && Time.realtimeSinceStartup < menuUntil)
                    yield return null;
                tried.Add($"{c.Name}: [{(menu == null ? "no menu" : string.Join(", ", menu.Entries.Select(e => e.Text)))}]");
                vendor = c;
                if (menu != null && menu.Entries.Any(e => e.Cliloc == 3006103))
                    break;
                if (Ui.Find("context") is UI.UiWindow stale)
                    Ui.Close(stale);
            }
            if (vendor != null)
            {
                yield return WaitFrames(10);
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui_4_context.png"));
                yield return WaitFrames(10);
                shopReport = string.Join("; ", tried) + $" -> {vendor.Name} ({Math.Max(Math.Abs(vendor.X - _ctx.Player.X), Math.Abs(vendor.Y - _ctx.Player.Y))} tiles): ";
                if (menu != null)
                {
                    shopReport += "menu [" + string.Join(", ", menu.Entries.Select(e => e.Text + (e.Disabled ? " (off)" : ""))) + "]";
                    Core.ContextMenuEntry buy = menu.Entries.FirstOrDefault(e => e.Cliloc == 3006103);
                    if (buy.Text != null && !buy.Disabled)
                    {
                        if (Ui.Find("context") is UI.UiWindow cw)
                            Ui.Close(cw);
                        _ctx.Session.ChooseContextMenu(vendor.Serial, buy.Index);
                        float shopUntil = Time.realtimeSinceStartup + 4f;
                        while (shop == null && Time.realtimeSinceStartup < shopUntil)
                            yield return null;
                        yield return WaitFrames(30);
                        ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui_5_shop.png"));
                        yield return WaitFrames(10);
                        shopReport += shop == null ? "; buy list never came"
                            : $"; buy list of {shop.Items.Count}: " + string.Join(", ", shop.Items.Take(6).Select(it => $"{it.Name} {it.Price}gp x{it.Amount}"));

                        // Buy the cheapest thing (the test character's own gold), check the pack and the gold, then sell it back.
                        ShopItem cheap = shop?.Items.Where(it => it.Price > 0 && it.Price <= 25).OrderBy(it => it.Price).FirstOrDefault();
                        uint pack = _ctx.Session.World.Worn(_ctx.Session.World.Player, 21);
                        if (cheap != null && pack != 0)
                        {
                            // Everything in the pack, counted by amount: a vendor's display graphic can differ from the item it sells.
                            int CountInPack() => _ctx.Session.World.ItemsIn(pack).Where(i => i.Graphic != 0x0EED).Sum(i => Math.Max(1, i.Amount)); // not the gold
                            int goldBefore = _ctx.Session.World.Player.Gold, countBefore = CountInPack();
                            bool closed = false;
                            Action<uint> onClosed = _ => closed = true;
                            _ctx.Session.ShopClosed += onClosed;
                            _ctx.Session.Buy(vendor.Serial, new[] { (cheap.Serial, 1) });
                            foreach (UI.UiWindow w in Ui.Windows.Where(w => w is UI.ShopWindow).ToList())
                                Ui.Close(w);
                            float buyUntil = Time.realtimeSinceStartup + 5f;
                            while ((CountInPack() == countBefore || _ctx.Session.World.Player.Gold == goldBefore) && Time.realtimeSinceStartup < buyUntil)
                                yield return null;
                            int countAfter = CountInPack(), goldAfter = _ctx.Session.World.Player.Gold;
                            // What came back after the order (ids of the trade, container and status packets).
                            string replies = string.Join(" ", _ctx.Session.RecentPackets.Reverse().Take(60).Reverse()
                                .Where(pk => pk[0] == 0x3B || pk[0] == 0x25 || pk[0] == 0x3C || pk[0] == 0x11 || pk[0] == 0xF3 || pk[0] == 0x1A)
                                .Select(pk => pk[0] == 0x25 ? $"25(0x{(pk[1] << 24 | pk[2] << 16 | pk[3] << 8 | pk[4]):X8} g{pk[5] << 8 | pk[6]:X4} in 0x{(pk[15] << 24 | pk[16] << 16 | pk[17] << 8 | pk[18]):X8})" : $"{pk[0]:X2}"));
                            shopReport += $"\n  packets after the order: {replies}";
                            shopReport += $"\n  bought 1 {cheap.Name} for {cheap.Price}gp: pack {countBefore} -> {countAfter}, gold {goldBefore} -> {goldAfter}, " +
                                          $"vendor closed the trade: {closed}";
                            _ctx.Session.ShopClosed -= onClosed;

                            // Sell it back: the vendor's Sell entry opens our sell list.
                            shop = null;
                            menu = null;
                            _ctx.Session.RequestContextMenu(vendor.Serial);
                            float m2 = Time.realtimeSinceStartup + 4f;
                            while (menu == null && Time.realtimeSinceStartup < m2)
                                yield return null;
                            Core.ContextMenuEntry sell = menu?.Entries.FirstOrDefault(e => e.Cliloc == 3006104) ?? default;
                            if (Ui.Find("context") is UI.UiWindow cw2)
                                Ui.Close(cw2);
                            if (sell.Text != null && !sell.Disabled)
                            {
                                _ctx.Session.ChooseContextMenu(vendor.Serial, sell.Index);
                                float s2 = Time.realtimeSinceStartup + 4f;
                                while ((shop == null || !shop.Selling) && Time.realtimeSinceStartup < s2)
                                    yield return null;
                                yield return WaitFrames(20);
                                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui_5b_sell.png"));
                                yield return WaitFrames(10);
                                ShopItem mine = shop?.Items.FirstOrDefault(it => string.Equals(it.Name, cheap.Name, StringComparison.OrdinalIgnoreCase)) ?? shop?.Items.FirstOrDefault(it => it.Graphic == cheap.Graphic);
                                if (mine != null)
                                {
                                    int g2 = _ctx.Session.World.Player.Gold, c2 = CountInPack();
                                    _ctx.Session.Sell(vendor.Serial, new[] { (mine.Serial, 1) });
                                    foreach (UI.UiWindow w in Ui.Windows.Where(w => w is UI.ShopWindow).ToList())
                                        Ui.Close(w);
                                    float sellUntil = Time.realtimeSinceStartup + 5f;
                                    while ((CountInPack() == c2 || _ctx.Session.World.Player.Gold == g2) && Time.realtimeSinceStartup < sellUntil)
                                        yield return null;
                                    shopReport += $"\n  sold it back for {mine.Price}gp: pack {c2} -> {CountInPack()}, gold {g2} -> {_ctx.Session.World.Player.Gold}";
                                }
                                else
                                {
                                    shopReport += $"\n  sell list ({shop?.Items.Count ?? 0} items) has no {cheap.Name}" + (shop == null ? " (no sell list came)" : "");
                                }
                            }
                            else
                            {
                                shopReport += "\n  no Sell entry on this vendor";
                            }
                        }
                    }
                }
                else
                {
                    shopReport += "no context menu";
                }
            }
            _ctx.Session.ContextMenuOpened -= onMenu;
            _ctx.Session.ShopOpened -= onShop;
            _uiReport.Append("\nshop: " + shopReport);
        }

        /// <summary>Fourth set: a house and effects, injected locally (a fireball across the street, a sparkle on us, lightning).</summary>
        private IEnumerator EffectsAndHouseSet()
        {
            foreach (UI.UiWindow w in Ui.Windows.ToList())
                if (w.CanClose) Ui.Close(w);
            uint me = _ctx.Session.World.PlayerSerial;
            byte[] Effect(byte type, uint src, uint dst, int graphic, int sx, int sy, int sz, int tx, int ty, int tz, int duration, bool explode) =>
                new PacketWriter(0x70, 28).Byte(type).UInt32(src).UInt32(dst).UInt16(graphic)
                    .UInt16(sx).UInt16(sy).Byte((byte)(sbyte)sz).UInt16(tx).UInt16(ty).Byte((byte)(sbyte)tz)
                    .Byte(7).Byte(duration).UInt16(0).Byte(1).Byte(explode ? 1 : 0).ToArray();
            int ax = _ctx.Player.X, ay = _ctx.Player.Y, az = _ctx.Player.Z;

            // A small house set down next to us (local only; removed again after the picture).
            const uint fakeHouse = 0x4FFFFFF0;
            _ctx.Session.Inject(new PacketWriter(0xF3, 26).UInt16(1).Byte(2).UInt32(fakeHouse).UInt16(0x64).Byte(0)
                .UInt16(1).UInt16(1).UInt16(ax + 7).UInt16(ay - 1).Byte((byte)(sbyte)az).Byte(0).UInt16(0).Byte(0).UInt16(0).ToArray(), false);
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui_7_house.png"));
            yield return WaitFrames(10);
            int houseParts = _host.Entities.MultiParts(fakeHouse);
            _ctx.Session.Inject(new PacketWriter(0x1D, 5).UInt32(fakeHouse).ToArray(), false);
            _uiReport.Append($"\nhouse: {houseParts} parts drawn, {(_host.Entities.HasMulti(fakeHouse) ? "not removed" : "removed")}");
            _ctx.Session.Inject(Effect(0, 0, 0, 0x36D4, ax - 6, ay - 4, az, ax + 4, ay + 3, az, 0, true), false);   // fireball
            _ctx.Session.Inject(Effect(3, me, me, 0x376A, ax, ay, az, ax, ay, az, 40, false), false);              // sparkles
            _ctx.Session.Inject(Effect(1, 0, 0, 0, ax + 2, ay - 2, az, ax + 2, ay - 2, az, 0, false), false);      // lightning
            yield return new WaitForSecondsRealtime(0.25f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui_6_effects.png"));
            yield return WaitFrames(10);
            _effectsLive = _host.Effects.Count;
        }

        /// <summary>Tooltips (a backpack item; a paperdoll item, which must sit above the paperdoll), a sound, the chat window and the world map.</summary>
        private IEnumerator TooltipSet()
        {
            _tooltipReport = "no backpack items";
            uint packSerial = _ctx.Session.World.Worn(_ctx.Session.World.Player, 21);
            ContainedItem first = packSerial == 0 ? null : _ctx.Session.World.ItemsIn(packSerial).FirstOrDefault();
            if (first != null)
            {
                float tipUntil = Time.realtimeSinceStartup + 4f;
                PropertyList props = null;
                while ((props = _ctx.Session.GetProperties(first.Serial)) == null && Time.realtimeSinceStartup < tipUntil)
                    yield return null;
                _tooltipReport = props == null ? $"no reply for 0x{first.Serial:X}" : $"0x{first.Serial:X}: " + string.Join(" / ", props.Lines);
            }
            // A footstep-like sound through the same path the server's 0x54 uses.
            _ctx.Session.Inject(new PacketWriter(0x54, 12).Byte(1).UInt16(0x42).UInt16(0).UInt16(_ctx.Player.X + 1).UInt16(_ctx.Player.Y).UInt16(_ctx.Player.Z).ToArray(), false);
            yield return WaitFrames(5);
            // Fifth set: a paperdoll item's tooltip (it must sit above the paperdoll), the chat window and the world map.
            _paperdollTip = "no paperdoll";
            _ctx.Session.OpenPaperdoll();
            float pdUntil = Time.realtimeSinceStartup + 4f;
            while (!Ui.Windows.Any(w => w is UI.PaperdollWindow) && Time.realtimeSinceStartup < pdUntil)
                yield return null;
            if (Ui.Windows.FirstOrDefault(w => w is UI.PaperdollWindow) is UI.UiWindow pd && _ctx.Session.World.Player != null)
            {
                EquippedItem worn = _ctx.Session.World.Player.Equipment.Values.FirstOrDefault(eq => eq.Layer != 11 && eq.Layer != 21 && eq.Layer != 16);
                _host.Hover.ForceHover(worn.Serial, pd.Rect.position + new Vector2(60, 120)); // over the paperdoll's body
                float tipUntil2 = Time.realtimeSinceStartup + 4f;
                while (_ctx.Session.GetProperties(worn.Serial) == null && Time.realtimeSinceStartup < tipUntil2)
                    yield return null;
                yield return new WaitForSecondsRealtime(0.6f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui_8_paperdoll_tooltip.png"));
                yield return WaitFrames(10);
                _paperdollTip = $"0x{worn.Serial:X} layer {worn.Layer}: {string.Join(" / ", _ctx.Session.GetProperties(worn.Serial)?.Lines ?? new List<string>())}";
                _host.Hover.ClearForced();
                Ui.Close(pd);
            }
            _host.RunCommand("worldchat");
            _host.RunCommand("worldmap");
            float mapUntil = Time.realtimeSinceStartup + 30f;
            yield return new WaitForSecondsRealtime(2f);
            while (Time.realtimeSinceStartup < mapUntil && !UI.WorldMapWindow.Ready(_ctx.Session.World.Map))
                yield return null;
            yield return WaitFrames(20);
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui_9_chat_map.png"));
            yield return WaitFrames(10);
        }

        /// <summary>Sixth set: moving an item inside the backpack, then to the ground and back.</summary>
        private IEnumerator DragSet()
        {
            _dragReport = "no item to move";
            uint myPack = _ctx.Session.World.Worn(_ctx.Session.World.Player, 21);
            ContainedItem moving = myPack == 0 ? null : _ctx.Session.World.ItemsIn(myPack).FirstOrDefault(i => i.Graphic != 0x0EED); // not the gold
            if (moving != null)
            {
                uint serial = moving.Serial;
                int ox = moving.X, oy = moving.Y, nx = ox < 100 ? ox + 30 : ox - 30;
                ContainedItem Now() => _ctx.Session.World.ItemsIn(myPack).FirstOrDefault(i => i.Serial == serial);
                yield return new WaitForSecondsRealtime(1.2f); // the server refuses lifts faster than its action delay
                _ctx.Session.Lift(serial, moving.Amount);
                yield return new WaitForSecondsRealtime(0.4f);
                _ctx.Session.DropInto(serial, myPack, nx, oy);
                float d1 = Time.realtimeSinceStartup + 3f;
                while ((Now()?.X ?? ox) == ox && Time.realtimeSinceStartup < d1)
                    yield return null;
                _dragReport = $"0x{moving.Graphic:X4} moved in the pack {ox},{oy} -> {Now()?.X},{Now()?.Y}";

                yield return new WaitForSecondsRealtime(1.2f); // the server refuses lifts faster than its action delay

                _ctx.Session.Lift(serial, moving.Amount);
                yield return new WaitForSecondsRealtime(0.4f);
                // A free tile next to us (a wall or stall would refuse it).
                var free = new[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) }.Select(o => (X: _ctx.Player.X + o.Item1, Y: _ctx.Player.Y + o.Item2))
                    .FirstOrDefault(c => _ctx.Walk.TryStep(c.X, c.Y, _ctx.Player.Z) != null);
                if (free.X == 0) free = (_ctx.Player.X + 1, _ctx.Player.Y);
                _ctx.Session.DropOnGround(serial, free.X, free.Y, _ctx.Walk.SurfaceZ(free.X, free.Y, _ctx.Player.Z));
                float d2 = Time.realtimeSinceStartup + 3f;
                while (!_host.Entities.HasItem(serial) && Time.realtimeSinceStartup < d2)
                    yield return null;
                bool onGround = _host.Entities.HasItem(serial) && _ctx.Session.World.Items.ContainsKey(serial);
                yield return new WaitForSecondsRealtime(0.3f);
                yield return new WaitForSecondsRealtime(1.2f); // the server refuses lifts faster than its action delay
                _ctx.Session.Lift(serial, moving.Amount);
                yield return new WaitForSecondsRealtime(0.4f);
                _ctx.Session.DropInto(serial, myPack, ox, oy);
                float d3 = Time.realtimeSinceStartup + 3f;
                while (Now() == null && Time.realtimeSinceStartup < d3)
                    yield return null;
                _dragReport += $"; dropped at our feet: {(onGround ? "drawn in the world" : "never appeared")}; back in the pack: {(Now() != null ? $"yes at {Now().X},{Now().Y}" : "NO")}";
            }
        }

        /// <summary>Seventh set: the shard's own gumps ([groups, and an offer from a chat link; wait a little for one).</summary>
        private IEnumerator GumpSet()
        {
            string gumpReport = "";
            float linkUntil = Time.realtimeSinceStartup + 20f;
            while (!ChatLog.Lines.Any(l => l.Entry.Link != null) && Time.realtimeSinceStartup < linkUntil)
                yield return null;
            foreach (string command in new[] { "[groups", ChatLog.Lines.Select(l => l.Entry.Link).LastOrDefault(l => l != null) is string link ? "[" + link : null })
            {
                if (command == null)
                    continue;
                int before = Ui.Windows.Count(w => w is UI.ServerGumpWindow);
                ServerGump got = null;
                Action<ServerGump> onGump = g => got = g;
                _ctx.Session.World.GumpOpened += onGump;
                _ctx.Session.Say(command);
                float gUntil = Time.realtimeSinceStartup + 8f;
                while (got == null && Time.realtimeSinceStartup < gUntil)
                    yield return null;
                _ctx.Session.World.GumpOpened -= onGump;
                yield return WaitFrames(20);
                string shot = command.StartsWith("[groups") ? "ui_10_groups.png" : "ui_11_link.png";
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, shot));
                yield return WaitFrames(10);
                var win = got == null ? null : Ui.Windows.OfType<UI.ServerGumpWindow>().FirstOrDefault(w => w.Gump == got);
                string reply = string.Join(" | ", _ctx.Session.World.Journal.Skip(Math.Max(0, _ctx.Session.World.Journal.Count - 3)).Where(j => j.Speaker == null).Select(j => j.Text));
                gumpReport += $"\n  {command}: " + (got == null ? $"no gump; server said: {reply}" :
                    $"gump 0x{got.TypeId:X} {got.Commands.Count} commands, {got.Lines.Length} lines, window {(win != null ? $"{win.Rect.width:F0}x{win.Rect.height:F0}" : "not opened")}; " +
                    $"kinds: {string.Join(" ", got.Commands.GroupBy(c => c.Name).Select(g => $"{g.Key}x{g.Count()}"))}");
                if (win != null)
                    Ui.Close(win); // answers with button 0, as right-clicking would
            }
            _uiReport.Append($"\nitems: {_dragReport}\ngumps:{gumpReport}");
        }

        /// <summary>Eighth set: macros, through the same Run a key press uses.</summary>
        private IEnumerator MacroSet()
        {
            string macroReport = "";
            foreach (string action in new[] { "skill Hiding", "bandageself" })
            {
                int journalBefore = _ctx.Session.World.Journal.Count;
                yield return new WaitForSecondsRealtime(1.2f);
                _host.Ui.Hotkeys.Run(_ctx.Session, action);
                float mUntil = Time.realtimeSinceStartup + 6f;
                while (Time.realtimeSinceStartup < mUntil)
                {
                    _host.Ui.Hotkeys.Update(_ctx.Session); // answers the bandage's cursor when it comes
                    if (_ctx.Session.World.Journal.Skip(journalBefore).Any(j => j.Speaker == null || j.Serial == _ctx.Session.World.PlayerSerial))
                        break;
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(0.5f);
                macroReport += $"\n  {action}: " + string.Join(" | ", _ctx.Session.World.Journal.Skip(journalBefore)
                    .Where(j => j.Speaker == null || j.Serial == _ctx.Session.World.PlayerSerial).Select(j => j.Text).Take(3));
            }
            _uiReport.Append($"\nmacros:{macroReport}");
        }

        /// <summary>The closing lines of the online part: tooltips, chat, map, effects, audio and the buttons set.</summary>
        private void AppendUiSummary()
        {
            Audio sound = _host.Ui.Sound;
            var byChannel = ChatLog.Lines.GroupBy(l => l.Channel).Select(g => $"{g.Key} {g.Count()}");
            _uiReport.Append($"\npaperdoll tooltip: {_paperdollTip}" +
                             $"\nchat: {ChatLog.Lines.Count} lines ({string.Join(", ", byChannel)}), {ChatLog.Lines.Count(l => l.Entry.Link != null)} with links" +
                             $"{(ChatLog.Lines.FirstOrDefault(l => l.Entry.Link != null) is var linked && linked.Entry.Link != null ? $", e.g. \"{linked.Entry.Text}\" -> [{linked.Entry.Link}" : "")}" +
                             $"\nworld map: {(UI.WorldMapWindow.Ready(_ctx.Session.World.Map) ? "drawn" : "not ready")}; top bar {(Ui.Find("topbar") != null ? "open" : "missing")}");

            _uiReport.Append($"\neffects: {_effectsLive} running after 0.25 s\ntooltip: {_tooltipReport}" +
                             $"\naudio: {sound.SoundsPlayed} sounds played, music {(sound.MusicPlaying ?? "none")}");
            _uiReport.Append($"\nbuttons: {Ui.Windows.Count} windows ({string.Join(", ", Ui.Windows.Select(w => w.Key))}); {_ctx.Session.Skills.Count} skills; " +
                             $"war mode {(_warOn ? "on" : "never confirmed")}; anatomy cursor {(_gotCursor ? "received and answered" : "never came")}; " +
                             $"{_host.Overheads.Count} overheads; last journal: " +
                             string.Join(" | ", _ctx.Session.World.Journal.Skip(Math.Max(0, _ctx.Session.World.Journal.Count - 3)).Select(j => j.Text)));
        }

        private void WriteReport()
        {
            File.WriteAllText(Path.Combine(_dir, "demo_capture_done.txt"),
                $"{(Application.isEditor ? "editor" : "player")} {Screen.width}x{Screen.height}, vSync {QualitySettings.vSyncCount}, target fps {Application.targetFrameRate}\n" +
                $"{(_ctx.Session != null ? "online: " + _ctx.Session.Status : "offline")}\n" +
                $"{_startCut}\n" +
                $"route: {_steps} steps from {_startX},{_startY} to {_ctx.Player.X},{_ctx.Player.Y}, {_rejected} rejected\n" +
                $"frames during the run: {_frameReport}\n" +
                $"hitches: {_hitchReport}\n" +
                $"chunks: {_ctx.World.LoadedChunks} loaded, {_ctx.World.PendingChunks} pending, {_ctx.World.ChunksBuilt} built, worker {_ctx.World.WorkerMs / Math.Max(1, _ctx.World.ChunksBuilt):F1} ms/chunk\n" +
                $"character/item art: {_ctx.MobileArt.Built} built off-thread, {_ctx.MobileArt.WorkerMs / Math.Max(1, _ctx.MobileArt.Built):F1} ms each, {_ctx.MobileArt.PendingRequests} pending\n" +
                $"{_host.Entities.Others.Count} mobiles, {_host.Entities.ItemCount} items drawn\n" +
                $"slow packets: {(_ctx.Session == null || _ctx.Session.SlowPackets.Count == 0 ? "none" : string.Join(", ", _ctx.Session.SlowPackets))}\n" +
                $"bodies: {_host.Entities.BodyReport()}\n" +
                $"multis: {_host.Entities.MultiCount} ({_host.Entities.MultiPartCount} parts; ids {string.Join(", ", _ctx.Session?.World.Items.Values.Where(i => i.IsMulti).Select(i => $"0x{i.Graphic:X}").Distinct() ?? Array.Empty<string>())})\n" +
                $"ui: {_uiReport}");
        }
    }
}
