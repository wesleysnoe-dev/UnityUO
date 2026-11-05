using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>
    /// A secure trade with another player: two containers the server keeps (ours to drop offers into, theirs to look at),
    /// the accept checks and, on servers that announce an account ledger, the gold and platinum offered.
    /// </summary>
    public sealed class SecureTrade
    {
        /// <summary>The other player.</summary>
        public uint Them;
        public string TheirName;
        /// <summary>Our side of the trade: what we offer goes in here. Every 0x6F about this trade names it.</summary>
        public uint MyContainer;
        public uint TheirContainer;
        public bool IAccept, TheyAccept;
        /// <summary>What they offer from their account (0x6F 3).</summary>
        public int TheirGold, TheirPlatinum;
        /// <summary>What we offer from our account, as last sent (0x6F 3 outgoing).</summary>
        public int MyGold, MyPlatinum;
        /// <summary>Our account balance (0x6F 4); only servers with account gold send it, and only then can gold be offered.</summary>
        public int BalanceGold, BalancePlatinum;
        public bool HasLedger;
    }

    /// <summary>0x6F secure trading.</summary>
    public sealed partial class GameSession
    {
        /// <summary>Open trades by our container's serial.</summary>
        public readonly Dictionary<uint, SecureTrade> Trades = new Dictionary<uint, SecureTrade>();

        public event Action<SecureTrade> TradeOpened;
        /// <summary>Accept checks or gold changed. Items come and go through the containers (<see cref="World.ContainerChanged"/>).</summary>
        public event Action<SecureTrade> TradeChanged;
        public event Action<SecureTrade> TradeClosed;

        private void RegisterSecureTradeHandlers() => _handlers[0x6F] = SecureTradePacket;

        /// <summary>
        /// 0x6F as ModernUO's OutgoingSecureTradePackets write it: command, a container serial, two values, a bool.
        /// Display (0, 47 bytes): the other player, our container, theirs, true, their name (30 bytes Latin-1).
        /// Close (1), Update (2: our accept, theirs), UpdateGold (3: their gold, platinum), UpdateLedger (4: our balance)
        /// are 17 bytes, each naming our container. ModernUO sends an Update before the Display; like ClassicUO, updates
        /// for a trade not yet open are dropped. The accept checks are the server's: ModernUO clears both whenever an
        /// item goes into or out of either offer (SecureTradeContainer.ClearChecks) and sends them here.
        /// </summary>
        private void SecureTradePacket(PacketReader r)
        {
            int command = r.ReadByte();
            uint serial = r.ReadUInt32();
            if (command == 0)
            {
                var trade = new SecureTrade { Them = serial, MyContainer = r.ReadUInt32(), TheirContainer = r.ReadUInt32() };
                // As ClassicUO: no trade unless both containers were sent first (ModernUO equips them before the display).
                if (!KnownItem(trade.MyContainer) || !KnownItem(trade.TheirContainer))
                    return;
                bool named = r.ReadBool();
                trade.TheirName = named ? r.ReadAscii(30).TrimEnd('\0') : "";
                if (string.IsNullOrEmpty(trade.TheirName) && World.Mobiles.TryGetValue(serial, out Mobile them))
                    trade.TheirName = them.Name ?? "";
                Trades[trade.MyContainer] = trade;
                TradeOpened?.Invoke(trade);
                return;
            }
            if (!Trades.TryGetValue(serial, out SecureTrade t))
                return;
            switch (command)
            {
                case 1:
                    Trades.Remove(serial);
                    TradeClosed?.Invoke(t);
                    return;
                case 2:
                    t.IAccept = r.ReadUInt32() != 0;
                    t.TheyAccept = r.ReadUInt32() != 0;
                    break;
                case 3:
                    t.TheirGold = r.ReadInt32();
                    t.TheirPlatinum = r.ReadInt32();
                    break;
                case 4:
                    t.BalanceGold = r.ReadInt32();
                    t.BalancePlatinum = r.ReadInt32();
                    t.HasLedger = true;
                    break;
                default:
                    return;
            }
            TradeChanged?.Invoke(t);
        }

        private bool KnownItem(uint serial) => World.Contained.ContainsKey(serial) || World.Items.ContainsKey(serial);

        /// <summary>Still the server's trade for its container (not closed, nor replaced by a newer one there).</summary>
        public bool IsOpen(SecureTrade t) => t != null && Trades.TryGetValue(t.MyContainer, out SecureTrade open) && open == t;

        /// <summary>Ends the trade; the server closes both sides and gives the items back.</summary>
        public void CancelTrade(SecureTrade t)
        {
            if (IsOpen(t) && State == SessionState.InWorld)
                _conn.Send(TradeCancelRequest(t.MyContainer));
        }

        /// <summary>Ticks or clears our accept box. The server answers with the new state of both (0x6F 2).</summary>
        public void AcceptTrade(SecureTrade t, bool accept)
        {
            if (IsOpen(t) && State == SessionState.InWorld)
                _conn.Send(TradeAcceptRequest(t.MyContainer, accept));
        }

        /// <summary>
        /// Offers gold and platinum from the account, kept within the ledger balance as ClassicUO does. The server clears
        /// both accept boxes and tells the other side; it doesn't echo the offer back.
        /// </summary>
        public void OfferTradeGold(SecureTrade t, int gold, int platinum)
        {
            if (!IsOpen(t) || !t.HasLedger)
                return;
            gold = Math.Max(0, Math.Min(gold, t.BalanceGold));
            platinum = Math.Max(0, Math.Min(platinum, t.BalancePlatinum));
            if (gold == t.MyGold && platinum == t.MyPlatinum)
                return;
            t.MyGold = gold;
            t.MyPlatinum = platinum;
            if (State == SessionState.InWorld)
                _conn.Send(TradeGoldRequest(t.MyContainer, gold, platinum));
        }

        /// <summary>0x6F 1 as ModernUO's SecureTrade handler reads it: cancel, our container (8 bytes, as ClassicUO sends).</summary>
        public static byte[] TradeCancelRequest(uint container) =>
            new PacketWriter(0x6F).Byte(1).UInt32(container).ToArray();

        /// <summary>0x6F 2: check, our container, 1 accept / 0 not (12 bytes).</summary>
        public static byte[] TradeAcceptRequest(uint container, bool accept) =>
            new PacketWriter(0x6F).Byte(2).UInt32(container).UInt32(accept ? 1u : 0u).ToArray();

        /// <summary>0x6F 3: update gold, our container, gold, platinum (16 bytes).</summary>
        public static byte[] TradeGoldRequest(uint container, int gold, int platinum) =>
            new PacketWriter(0x6F).Byte(3).UInt32(container).Int32(gold).Int32(platinum).ToArray();
    }
}
