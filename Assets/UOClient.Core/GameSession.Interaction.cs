using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UOClient.IO.Formats;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>Using things, containers, the paperdoll, drag and drop, and server gumps.</summary>
    public sealed partial class GameSession
    {
        /// <summary>The server refused to let us pick something up (reason code: 0 can't lift, 1 out of range, 2 out of sight, 3 belongs to another, 4 already holding, 5 inspect).</summary>
        public event Action<int> LiftRejected;

        /// <summary>Serial of the item currently held on the cursor (lifted, not yet dropped), or 0.</summary>
        public uint Holding { get; private set; }

        private void RegisterInteractionHandlers()
        {
            _handlers[0x88] = DisplayPaperdoll;
            _handlers[0x24] = DisplayContainer;
            _handlers[0x3C] = ContainerContent;
            _handlers[0x25] = ContainerContentUpdate;
            _handlers[0x27] = r => { Holding = 0; LiftRejected?.Invoke(r.ReadByte()); };
            _handlers[0x29] = _ => Holding = 0; // drop acknowledged
            _handlers[0x23] = _ => { };         // drag animation for others
            _handlers[0xDD] = CompressedGump;
            _handlers[0xB0] = PlainGump;
        }

        // ---- requests ----

        /// <summary>
        /// Minimum time between uses. ModernUO's Mobile.ActionDelay is 1000 ms with AOS rules (500 ms before),
        /// configurable server-side as "actionDelay"; faster uses get "You must wait to perform another action".
        /// </summary>
        public double UseDelay
        {
            get => _actions.Delay;
            set => _actions.Delay = value;
        }

        // Uses and lifts share the server's action delay; drops and equips follow their lift (see ActionQueue).
        private readonly ActionQueue _actions = new ActionQueue();

        /// <summary>
        /// Double-click (use): opens doors, containers, paperdolls (for mobiles), etc. Queued and sent at the
        /// server's pace, as the classic client does, so quick successive uses all go through.
        /// </summary>
        public void Use(uint serial)
        {
            if (State != SessionState.InWorld)
                return;
            _actions.Use(serial, new PacketWriter(0x06, 5).UInt32(serial).ToArray());
            SendQueuedActions();
        }

        private void SendQueuedActions()
        {
            if (State == SessionState.InWorld)
                _actions.Pump(_now, _conn.Send);
        }

        /// <summary>Opens our own paperdoll (double-click on self with the paperdoll flag).</summary>
        public void OpenPaperdoll() => Use(World.PlayerSerial | 0x80000000);

        /// <summary>
        /// A double-click in the world, as ClassicUO's GameActions.DoubleClick: in war mode another mobile is
        /// attacked, after asking for its status so its health bar fills; anything else is used. On ourselves that
        /// is a plain use, not the paperdoll flag: ModernUO's Mobile.OnDoubleClick dismounts a rider and shows the
        /// paperdoll otherwise.
        /// </summary>
        public void DoubleClick(uint serial)
        {
            if (State != SessionState.InWorld)
                return;
            if (AttacksOnDoubleClick(serial, World.PlayerSerial, WarMode))
            {
                _conn.Send(StatusRequest(serial));
                Attack(serial);
            }
            else
            {
                Use(serial);
            }
        }

        /// <summary>A double-click attacks only in war mode, and only another mobile (mobile serials are below 0x40000000).</summary>
        public static bool AttacksOnDoubleClick(uint serial, uint player, bool warMode) =>
            warMode && serial != 0 && serial < 0x40000000 && serial != player;

        /// <summary>Opens our backpack, if we wear one.</summary>
        public bool OpenBackpack()
        {
            uint pack = World.Worn(World.Player, 21);
            if (pack == 0)
                return false;
            Use(pack);
            return true;
        }

        /// <summary>Single-click: the server answers with the thing's name over it.</summary>
        public void Look(uint serial)
        {
            if (State == SessionState.InWorld)
                _conn.Send(new PacketWriter(0x09, 5).UInt32(serial).ToArray());
        }

        /// <summary>Asks for a mobile's status (hits etc.).</summary>
        public void RequestStatus(uint serial)
        {
            if (State == SessionState.InWorld)
                _conn.Send(StatusRequest(serial));
        }

        /// <summary>0x34 type 4, as ModernUO's MobileQuery reads it; answered with 0x11 if the mobile is in view.</summary>
        public static byte[] StatusRequest(uint serial) =>
            new PacketWriter(0x34, 10).UInt32(0xEDEDEDED).Byte(4).UInt32(serial).ToArray();

        /// <summary>The Help button: the server answers with its help gump.</summary>
        public void RequestHelp()
        {
            if (State == SessionState.InWorld)
                _conn.Send(new PacketWriter(0x9B, 258).Zero(257).ToArray());
        }

        /// <summary>Picks an item up onto the cursor. The server answers with a lift reject if it can't be.</summary>
        public void Lift(uint serial, int amount)
        {
            if (State != SessionState.InWorld)
                return;
            Holding = serial;
            // ModernUO refuses a lift inside the action delay of a use (and a lift starts one), so it waits its turn.
            _actions.Paced(LiftRequest(serial, amount));
            SendQueuedActions();
        }

        /// <summary>0x07 as ModernUO's LiftReq reads it: serial, amount (at least 1).</summary>
        public static byte[] LiftRequest(uint serial, int amount) =>
            new PacketWriter(0x07, 7).UInt32(serial).UInt16(Math.Max(1, Math.Min(amount, 60000))).ToArray();

        /// <summary>Shift-dragging splits a stack, as in ClassicUO: more than one of a stackable (tiledata Generic) thing.</summary>
        public static bool CanSplit(int amount, TileFlags flags) => amount > 1 && (flags & TileFlags.Generic) != 0;

        /// <summary>
        /// The amount the split picker lifts: what was typed or slid to, kept within 1..the stack. ModernUO's Mobile.Lift
        /// leaves the rest behind as a new item and the lifted part keeps the serial.
        /// </summary>
        public static int SplitAmount(int requested, int stack) => Math.Max(1, Math.Min(requested, Math.Max(1, stack)));

        /// <summary>The split picker's text box: digits only, an empty box is 0 until something is typed.</summary>
        public static int ParseSplitAmount(string text, int stack)
        {
            int value = 0;
            foreach (char c in text ?? "")
            {
                if (c < '0' || c > '9')
                    continue;
                value = value * 10 + (c - '0');
                if (value >= stack)
                    return stack;
            }
            return value;
        }

        /// <summary>Drops the held item into a container at (x, y) in the container gump.</summary>
        public void DropInto(uint serial, uint container, int x, int y)
        {
            if (State != SessionState.InWorld)
                return;
            _actions.Follow(DropRequest(serial, x, y, 0, container));
            SendQueuedActions();
        }

        /// <summary>Drops the held item on the ground.</summary>
        public void DropOnGround(uint serial, int x, int y, int z)
        {
            if (State != SessionState.InWorld)
                return;
            _actions.Follow(DropRequest(serial, x, y, z, 0xFFFFFFFF));
            SendQueuedActions();
        }

        /// <summary>
        /// 0x08 as ModernUO's DropReq reads it from a client with container grid lines: serial, x, y, z, grid index,
        /// destination (0xFFFFFFFF = the ground). x = y = 0xFFFF (-1) into an item is "onto it": a container takes it
        /// wherever it fits, a like stack merges.
        /// </summary>
        public static byte[] DropRequest(uint serial, int x, int y, int z, uint destination) =>
            new PacketWriter(0x08, 15).UInt32(serial).UInt16(x).UInt16(y).Byte((byte)(sbyte)z).Byte(0).UInt32(destination).ToArray();

        public const ushort CorpseGraphic = 0x2006;

        /// <summary>Spellbooks take scrolls dropped onto them though tiledata doesn't call them containers.</summary>
        private static readonly HashSet<ushort> Spellbooks = new HashSet<ushort> { 0x0EFA, 0x2252, 0x2253, 0x238C, 0x23A0, 0x2D50, 0x2D9D };

        /// <summary>
        /// Whether an item dropped onto another goes into it, as ClassicUO decides: a container, a corpse, a spellbook,
        /// or a stack of the same thing. Sent "onto" anything else, ModernUO's Item.OnDragDrop refuses and it bounces back,
        /// so the drop goes beside it instead (on the ground there, or at that spot in the open container).
        /// </summary>
        public static bool DropsInto(ushort held, TileFlags heldFlags, ushort target, TileFlags targetFlags) =>
            (targetFlags & TileFlags.Container) != 0 || target == CorpseGraphic || Spellbooks.Contains(target)
            || (held == target && (heldFlags & TileFlags.Generic) != 0);

        /// <summary>Equips the held item on a mobile (usually ourselves); the server picks the layer.</summary>
        public void Equip(uint serial, byte layer, uint mobile)
        {
            if (State != SessionState.InWorld)
                return;
            _actions.Follow(new PacketWriter(0x13, 10).UInt32(serial).Byte(layer).UInt32(mobile).ToArray());
            SendQueuedActions();
        }

        /// <summary>
        /// Answers a server gump. Button 0 means "closed". Switches are the ids of checked checkboxes and
        /// radios; text entries are (id, text). The gump is removed locally.
        /// </summary>
        public void ReplyGump(ServerGump gump, int buttonId, IList<int> switches, IList<(int Id, string Text)> texts)
        {
            World.RemoveGump(gump);
            if (State != SessionState.InWorld)
                return;
            _conn.Send(GumpReply(gump.Serial, gump.TypeId, buttonId, switches, texts));
        }

        /// <summary>
        /// 0xB1 as ModernUO's GumpSystem.DisplayGumpResponse reads it: serial, type id, button, the switch count
        /// and ids, the text count, then per entry uint16 id, uint16 length and big-endian UTF-16 text. A text
        /// longer than 239 characters gets the whole response dropped, so it is cut there.
        /// </summary>
        public static byte[] GumpReply(uint serial, uint typeId, int buttonId, IList<int> switches, IList<(int Id, string Text)> texts)
        {
            var w = new PacketWriter(0xB1).UInt32(serial).UInt32(typeId).Int32(buttonId);
            w.Int32(switches?.Count ?? 0);
            if (switches != null)
                foreach (int s in switches)
                    w.Int32(s);
            w.Int32(texts?.Count ?? 0);
            if (texts != null)
            {
                foreach (var (id, text) in texts)
                {
                    string t = text ?? "";
                    if (t.Length > 239)
                        t = t.Substring(0, 239);
                    w.UInt16(id).UInt16(t.Length);
                    foreach (char c in t)
                        w.UInt16(c);
                }
            }
            return w.ToArray();
        }

        // ---- handlers ----

        private void DisplayPaperdoll(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            string title = r.ReadAscii(60);
            World.RaisePaperdollOpened(serial, title);
        }

        private void DisplayContainer(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            ushort gump = r.ReadUInt16();
            if (gump == 0xFFFF)
                return; // spellbooks: their contents follow as 0xBF 0x1B
            if (gump == 0x30)
            {
                NoteBuyVendor(serial); // the vendor's buy list (0x74) follows
                return;
            }
            var c = new OpenContainer { Serial = serial, Gump = gump };
            World.OpenContainers[serial] = c;
            World.RaiseContainerOpened(c);
        }

        private void ContainerContent(PacketReader r)
        {
            int count = r.ReadUInt16();
            var changed = new HashSet<uint>();
            for (int i = 0; i < count; i++)
            {
                ContainedItem item = ReadContained(ref r);
                changed.Add(item.Container);
                NoteBulletinItem(item);
            }
            foreach (uint c in changed)
                World.RaiseContainerChanged(c);
        }

        private void ContainerContentUpdate(PacketReader r)
        {
            ContainedItem item = ReadContained(ref r);
            World.RaiseContainerChanged(item.Container);
            NoteBulletinItem(item);
        }

        /// <summary>One container entry (7.0+ layout with the grid byte): serial, graphic, offset, amount, x, y, grid, container, hue.</summary>
        private ContainedItem ReadContained(ref PacketReader r)
        {
            uint serial = r.ReadUInt32();
            var item = new ContainedItem
            {
                Serial = serial,
                Graphic = r.ReadUInt16()
            };
            r.ReadByte();
            item.Amount = r.ReadUInt16();
            item.X = r.ReadInt16();
            item.Y = r.ReadInt16();
            r.ReadByte(); // grid index
            item.Container = r.ReadUInt32();
            item.Hue = r.ReadUInt16();

            // An item moved into a container is no longer on the ground or worn (or in another container).
            World.Detach(serial, contained: World.Contained.TryGetValue(serial, out ContainedItem previous) && previous.Container != item.Container);
            World.Contained[serial] = item;
            return item;
        }

        private void CompressedGump(PacketReader r)
        {
            var g = new ServerGump
            {
                Serial = r.ReadUInt32(),
                TypeId = r.ReadUInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32()
            };
            byte[] layout = ReadPacked(ref r);
            // ModernUO writes the layout as Latin-1 (cliloc arguments carry names with accents).
            string layoutText = Encoding.GetEncoding(28591).GetString(layout).TrimEnd('\0');
            int lineCount = r.ReadInt32();
            byte[] lines = ReadPacked(ref r);

            g.Commands = ServerGump.ParseLayout(layoutText);
            g.Lines = ParseLines(lines, lineCount);
            g.ApplyFlags();
            World.AddGump(g);
        }

        private void PlainGump(PacketReader r)
        {
            var g = new ServerGump
            {
                Serial = r.ReadUInt32(),
                TypeId = r.ReadUInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32()
            };
            int layoutLength = r.ReadUInt16();
            string layoutText = r.ReadAscii(layoutLength);
            int lineCount = r.ReadUInt16();
            var lines = new string[lineCount];
            for (int i = 0; i < lineCount; i++)
            {
                int len = r.ReadUInt16();
                var sb = new StringBuilder(len);
                for (int c = 0; c < len; c++)
                    sb.Append((char)r.ReadUInt16());
                lines[i] = sb.ToString();
            }
            g.Commands = ServerGump.ParseLayout(layoutText);
            g.Lines = lines;
            g.ApplyFlags();
            World.AddGump(g);
        }

        /// <summary>
        /// 0xBF 0x04: ModernUO closes the oldest gump of that type it holds for us (NetStateGumps.Close and a
        /// singleton re-send), so only that one goes; other open gumps of the same type are still live server-side.
        /// No reply: the server has already forgotten it.
        /// </summary>
        private void CloseGumpFromServer(uint typeId, int buttonId)
        {
            foreach (ServerGump g in World.Gumps)
            {
                if (g.TypeId == typeId)
                {
                    World.RemoveGump(g);
                    return;
                }
            }
        }

        /// <summary>Gump payload: int32 (packed length + 4) or 0, int32 unpacked length, zlib data.</summary>
        private static byte[] ReadPacked(ref PacketReader r)
        {
            int packed = r.ReadInt32();
            if (packed <= 4)
            {
                if (packed == 4)
                    r.ReadInt32();
                return Array.Empty<byte>();
            }
            int unpacked = r.ReadInt32();
            byte[] data = new byte[packed - 4];
            for (int i = 0; i < data.Length; i++)
                data[i] = r.ReadByte();
            return Inflate(data, unpacked);
        }

        internal static byte[] Inflate(byte[] zlib, int expected)
        {
            var output = new byte[expected];
            using (var input = new MemoryStream(zlib, 2, zlib.Length - 2)) // skip the zlib header
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                int total = 0;
                while (total < expected)
                {
                    int n = deflate.Read(output, total, expected - total);
                    if (n == 0)
                        break;
                    total += n;
                }
            }
            return output;
        }

        /// <summary>Gump text lines: uint16 length, then big-endian UTF-16 characters.</summary>
        internal static string[] ParseLines(byte[] data, int count)
        {
            var lines = new string[Math.Max(0, count)];
            int pos = 0;
            for (int i = 0; i < lines.Length && pos + 2 <= data.Length; i++)
            {
                int len = (data[pos] << 8) | data[pos + 1];
                pos += 2;
                var sb = new StringBuilder(len);
                for (int c = 0; c < len && pos + 2 <= data.Length; c++, pos += 2)
                    sb.Append((char)((data[pos] << 8) | data[pos + 1]));
                lines[i] = sb.ToString();
            }
            for (int i = 0; i < lines.Length; i++)
                lines[i] ??= "";
            return lines;
        }
    }
}
