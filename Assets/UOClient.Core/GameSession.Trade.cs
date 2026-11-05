using System;
using System.Collections.Generic;
using System.Text;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>A context (popup) menu the server sent for something we asked about.</summary>
    public sealed class ContextMenu
    {
        public uint Serial;
        public readonly List<ContextMenuEntry> Entries = new List<ContextMenuEntry>();
    }

    public struct ContextMenuEntry
    {
        public int Index;     // sent back to choose it
        public int Cliloc;
        public string Text;
        public bool Disabled;
        public ushort Hue;
    }

    /// <summary>One line of a vendor's buy or sell list.</summary>
    public sealed class ShopItem
    {
        public uint Serial;
        public ushort Graphic;
        public ushort Hue;
        public int Amount;    // in stock (buy) or owned (sell)
        public int Price;
        public string Name;
    }

    public sealed class ShopList
    {
        public uint Vendor;
        public bool Selling;  // true: we sell to the vendor
        public readonly List<ShopItem> Items = new List<ShopItem>();
    }

    /// <summary>The server waits for a line of text (rename a pet, name a rune, ...).</summary>
    public sealed class TextPrompt
    {
        public uint Serial;
        public int PromptId;
        public bool Unicode;
    }

    /// <summary>Context menus, vendors, text prompts and spellbook contents.</summary>
    public sealed partial class GameSession
    {
        public event Action<ContextMenu> ContextMenuOpened;
        public event Action<ShopList> ShopOpened;
        public event Action<uint> ShopClosed;
        public event Action<TextPrompt> PromptRequested;
        public event Action<uint, int, ulong> SpellbookContent; // book, first spell id, known-spell bits

        public TextPrompt Prompt { get; private set; }

        private uint _buyVendor;

        private void RegisterTradeHandlers()
        {
            _handlers[0x74] = BuyList;
            _handlers[0x9E] = SellList;
            _handlers[0x3B] = r => ShopClosed?.Invoke(r.ReadUInt32());
            _handlers[0x9A] = r => AskPrompt(r, false);
            _handlers[0xC2] = r => AskPrompt(r, true);
        }

        /// <summary>0xBF sub-commands handled here; true if consumed.</summary>
        private bool TradeExtended(int sub, PacketReader r)
        {
            switch (sub)
            {
                case 0x14:
                    ReadContextMenu(r);
                    return true;
                case 0x1B:
                    // New spellbook content: unknown(2), book serial, book graphic, first spell id, 64 bits.
                    r.ReadUInt16();
                    uint book = r.ReadUInt32();
                    r.ReadUInt16();
                    int first = r.ReadUInt16();
                    ulong bits = 0;
                    for (int i = 0; i < 8; i++)
                        bits |= (ulong)r.ReadByte() << (i * 8);
                    SpellbookContent?.Invoke(book, first, bits);
                    return true;
            }
            return false;
        }

        // ---- context menus ----

        /// <summary>Asks for something's context menu (the classic shift-click or right-click popup).</summary>
        public void RequestContextMenu(uint serial)
        {
            if (State == SessionState.InWorld)
                _conn.Send(new PacketWriter(0xBF).UInt16(0x13).UInt32(serial).ToArray());
        }

        public void ChooseContextMenu(uint serial, int index)
        {
            if (State == SessionState.InWorld)
                _conn.Send(new PacketWriter(0xBF).UInt16(0x15).UInt32(serial).UInt16(index).ToArray());
        }

        private void ReadContextMenu(PacketReader r)
        {
            int kind = r.ReadUInt16(); // 1 classic, 2 newer (full cliloc numbers)
            var menu = new ContextMenu { Serial = r.ReadUInt32() };
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                var e = new ContextMenuEntry();
                int flags;
                if (kind == 2)
                {
                    e.Cliloc = (int)r.ReadUInt32();
                    e.Index = r.ReadUInt16();
                    flags = r.ReadUInt16();
                }
                else
                {
                    e.Index = r.ReadUInt16();
                    e.Cliloc = 3000000 + r.ReadUInt16();
                    flags = r.ReadUInt16();
                    if ((flags & 0x20) != 0)
                        e.Hue = r.ReadUInt16();
                }
                e.Disabled = (flags & 0x01) != 0;
                e.Text = Cliloc?.Get(e.Cliloc) ?? $"#{e.Cliloc}";
                menu.Entries.Add(e);
            }
            ContextMenuOpened?.Invoke(menu);
        }

        // ---- vendors ----

        /// <summary>
        /// 0x74: prices and names for the vendor's buy container. ModernUO lists the stock in order and tags
        /// each contained item with x = position + 1, so entries pair with items by X.
        /// </summary>
        private void BuyList(PacketReader r)
        {
            uint container = r.ReadUInt32();
            int count = r.ReadByte();
            var byX = new Dictionary<int, ContainedItem>();
            foreach (ContainedItem c in World.ItemsIn(container))
                byX[c.X] = c;

            var list = new ShopList { Vendor = _buyVendor };
            if (_buyVendor != 0 && _now - _buyVendorAt > 2)
                list.Vendor = 0; // a stale vendor from an earlier shop: wait for this one's 0x24
            for (int i = 0; i < count; i++)
            {
                int price = (int)r.ReadUInt32();
                int nameLength = r.ReadByte();
                string name = r.ReadAscii(nameLength).TrimEnd('\0');
                if (int.TryParse(name, out int cliloc) && Cliloc != null)
                    name = Cliloc.Get(cliloc) ?? name;
                if (!byX.TryGetValue(i + 1, out ContainedItem item))
                    continue;
                list.Items.Add(new ShopItem
                {
                    Serial = item.Serial, Graphic = item.Graphic, Hue = item.Hue, Amount = item.Amount,
                    Price = price, Name = Capitalize(name)
                });
            }
            // ModernUO sends the vendor (0x24 with gump 0x30) after the prices: hold the list until then.
            if (list.Vendor == 0)
            {
                _pendingBuy = list;
                return;
            }
            _buyVendor = 0;
            ShopOpened?.Invoke(list);
        }

        private ShopList _pendingBuy;
        private double _buyVendorAt;

        private void SellList(PacketReader r)
        {
            var list = new ShopList { Vendor = r.ReadUInt32(), Selling = true };
            int count = r.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                var item = new ShopItem
                {
                    Serial = r.ReadUInt32(),
                    Graphic = r.ReadUInt16(),
                    Hue = r.ReadUInt16(),
                    Amount = r.ReadUInt16(),
                    Price = r.ReadUInt16()
                };
                string name = r.ReadAscii(r.ReadUInt16());
                if (int.TryParse(name, out int cliloc) && Cliloc != null)
                    name = Cliloc.Get(cliloc) ?? name;
                item.Name = Capitalize(name);
                list.Items.Add(item);
            }
            ShopOpened?.Invoke(list);
        }

        /// <summary>Buys (serial, amount) pairs from a vendor's list; an empty order just closes it.</summary>
        public void Buy(uint vendor, IReadOnlyList<(uint Serial, int Amount)> order)
        {
            if (State != SessionState.InWorld)
                return;
            var w = new PacketWriter(0x3B).UInt32(vendor).Byte(order.Count > 0 ? 0x02 : 0x00);
            foreach (var (serial, amount) in order)
                w.Byte(0x1A).UInt32(serial).UInt16(amount);
            _conn.Send(w.ToArray());
        }

        public void Sell(uint vendor, IReadOnlyList<(uint Serial, int Amount)> order)
        {
            if (State != SessionState.InWorld)
                return;
            var w = new PacketWriter(0x9F).UInt32(vendor).UInt16(order.Count);
            foreach (var (serial, amount) in order)
                w.UInt32(serial).UInt16(amount);
            _conn.Send(w.ToArray());
        }

        /// <summary>Called for 0x24 with the buy gump (0x30): names the vendor of the list just received (or about to be).</summary>
        private void NoteBuyVendor(uint vendor)
        {
            if (_pendingBuy != null)
            {
                ShopList list = _pendingBuy;
                _pendingBuy = null;
                list.Vendor = vendor;
                ShopOpened?.Invoke(list);
                return;
            }
            // Servers that name the vendor first: the prices follow.
            _buyVendor = vendor;
            _buyVendorAt = _now;
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // ---- text prompts ----

        private void AskPrompt(PacketReader r, bool unicode)
        {
            Prompt = new TextPrompt { Serial = r.ReadUInt32(), PromptId = r.ReadInt32(), Unicode = unicode };
            PromptRequested?.Invoke(Prompt);
        }

        /// <summary>Answers the pending prompt; null cancels it.</summary>
        public void AnswerPrompt(string text)
        {
            TextPrompt p = Prompt;
            if (p == null || State != SessionState.InWorld)
                return;
            Prompt = null;
            int type = text == null ? 0 : 1;
            text ??= "";
            if (text.Length > 128)
                text = text.Substring(0, 128);
            var w = new PacketWriter(p.Unicode ? (byte)0xC2 : (byte)0x9A).UInt32(p.Serial).Int32(p.PromptId).Int32(type);
            if (p.Unicode)
            {
                w.Ascii("ENU", 4);
                foreach (byte b in Encoding.Unicode.GetBytes(text)) // little-endian, no terminator
                    w.Byte(b);
            }
            else
            {
                w.AsciiZ(text);
            }
            _conn.Send(w.ToArray());
        }
    }
}
