using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>A dye tub's hue picker (0x95): answered once with a hue, or left unanswered when cancelled.</summary>
    public sealed class HuePickerRequest
    {
        public uint Serial;
        public ushort Graphic;
        public bool Answered;
    }

    /// <summary>One entry of a legacy menu: its 1-based index (what the server reads back), art and hue (item menus only), text.</summary>
    public sealed class LegacyMenuEntry
    {
        public int Index;
        public ushort Graphic;
        public ushort Hue;
        public string Text;
    }

    /// <summary>
    /// A legacy menu (0x7C): ModernUO's ItemListMenu (pictures to pick from) or QuestionMenu (text answers, TazUO's
    /// GrayMenuGump). Answered once, with an entry or as cancelled.
    /// </summary>
    public sealed class LegacyMenu
    {
        public uint Serial;
        public ushort MenuId;
        public string Question;
        /// <summary>A question menu (text answers, no art): TazUO tells them apart by the first entry's graphic being 0.</summary>
        public bool IsQuestion;
        public readonly List<LegacyMenuEntry> Entries = new List<LegacyMenuEntry>();
        public bool Answered;
    }

    /// <summary>Dye tubs (0x95), legacy menus (0x7C) and links the server asks us to open (0xA5).</summary>
    public sealed partial class GameSession
    {
        /// <summary>The server opened a hue picker (a dye tub was used).</summary>
        public event Action<HuePickerRequest> HuePickerRequested;

        /// <summary>The server opened a legacy menu.</summary>
        public event Action<LegacyMenu> MenuOpened;

        /// <summary>
        /// The server asked to open a link: the raw text as sent. Nothing is opened here; whoever handles it must ask the
        /// player and check it with <see cref="TryParseSafeUrl"/> first (TazUO launches it unasked).
        /// </summary>
        public event Action<string> OpenUrlRequested;

        private void RegisterMenuHandlers()
        {
            _handlers[0x95] = HuePicker;
            _handlers[0x7C] = OpenMenu;
            _handlers[0xA5] = r => OpenUrlRequested?.Invoke(r.ReadAsciiZ());
        }

        // ---- hue picker ----

        /// <summary>TazUO's ColorPickerBox: 10 rows of 20 swatches, a slider of 5 graduations (starting at 1).</summary>
        public const int DyeRows = 10, DyeColumns = 20, DyeGraduations = 5, DyeDefaultGraduation = 1;

        /// <summary>
        /// The hue of swatch <paramref name="index"/> at a graduation (ColorPickerBox.CreateTexture: graduation + 2 + 5 per
        /// swatch), so 2..1001: every hue ModernUO's Utility.ClipDyedHue lets through.
        /// </summary>
        public static ushort DyeHue(int graduation, int index)
        {
            graduation = Math.Max(0, Math.Min(DyeGraduations - 1, graduation));
            index = Math.Max(0, Math.Min(DyeRows * DyeColumns - 1, index));
            return (ushort)(graduation + 2 + 5 * index);
        }

        /// <summary>0x95 (9 bytes): serial, 2 unused, the tub's graphic (TazUO's DyeData reads it the same way).</summary>
        private void HuePicker(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            r.ReadUInt16();
            ushort graphic = r.ReadUInt16();
            HuePickerRequested?.Invoke(new HuePickerRequest { Serial = serial, Graphic = graphic });
        }

        /// <summary>
        /// OK on the hue picker: the 0x95 reply, once. Cancelling sends nothing, as in TazUO (its server picker has no
        /// cancel; ModernUO just keeps the picker waiting). False when nothing was sent.
        /// </summary>
        public bool AnswerHuePicker(HuePickerRequest request, int hue)
        {
            if (request == null || request.Answered)
                return false;
            request.Answered = true;
            if (State != SessionState.InWorld)
                return false;
            _conn.Send(HuePickerResponse(request.Serial, request.Graphic, hue));
            return true;
        }

        /// <summary>
        /// 0x95 reply, 9 bytes as ModernUO's HuePickerResponse reads it: serial, item id (ignored there), hue (it masks
        /// 0x3FFF and clips to 2..1001, so it is kept there here).
        /// </summary>
        public static byte[] HuePickerResponse(uint serial, ushort graphic, int hue) =>
            new PacketWriter(0x95, 9).UInt32(serial).UInt16(graphic).UInt16(Math.Max(2, Math.Min(1001, hue))).ToArray();

        // ---- legacy menus ----

        /// <summary>
        /// 0x7C as ModernUO's OutgoingMenuPackets write it: serial, menu id, question (byte length, Latin-1), entry count,
        /// then per entry uint16 graphic, uint16 hue, name (byte length) — a question menu writes a zero uint32 in place of
        /// graphic and hue. An entry cut short is dropped; a menu of none still opens, so it can be cancelled.
        /// </summary>
        public static LegacyMenu ReadMenu(PacketReader r)
        {
            var menu = new LegacyMenu
            {
                Serial = r.ReadUInt32(),
                MenuId = r.ReadUInt16()
            };
            menu.Question = r.ReadAscii(r.ReadByte());
            int count = r.ReadByte();
            // OpenMenu peeks the first entry's graphic: 0 is a question menu.
            menu.IsQuestion = count == 0 || r.Remaining < 2 || PeekUInt16(r) == 0;
            for (int i = 0; i < count; i++)
            {
                if (r.Remaining < 5)
                    break;
                var e = new LegacyMenuEntry { Index = i + 1 };
                if (menu.IsQuestion)
                {
                    r.ReadUInt32();
                }
                else
                {
                    e.Graphic = r.ReadUInt16();
                    e.Hue = r.ReadUInt16();
                }
                int length = r.ReadByte();
                if (r.Remaining < length)
                    break;
                e.Text = r.ReadAscii(length);
                menu.Entries.Add(e);
            }
            return menu;
        }

        private static ushort PeekUInt16(PacketReader r) => r.ReadUInt16(); // r is a copy: the caller's position stays

        private void OpenMenu(PacketReader r)
        {
            if (State != SessionState.InWorld)
                return; // TazUO: only in game
            MenuOpened?.Invoke(ReadMenu(r));
        }

        /// <summary>
        /// Answers a menu once: <paramref name="entry"/> chosen, or null to cancel (TazUO's right-click / Cancel, index 0;
        /// ModernUO calls the menu's OnCancel). An entry not of this menu cancels. False when nothing was sent.
        /// </summary>
        public bool AnswerMenu(LegacyMenu menu, LegacyMenuEntry entry)
        {
            if (menu == null || menu.Answered)
                return false;
            menu.Answered = true;
            if (State != SessionState.InWorld)
                return false;
            if (entry != null && !menu.Entries.Contains(entry))
                entry = null;
            _conn.Send(entry == null ? MenuResponse(menu.Serial, menu.MenuId, 0, 0, 0)
                : menu.IsQuestion ? MenuResponse(menu.Serial, menu.MenuId, entry.Index, 0, 0)
                : MenuResponse(menu.Serial, menu.MenuId, entry.Index, entry.Graphic, entry.Hue));
            return true;
        }

        /// <summary>
        /// 0x7D, 13 bytes as ModernUO's MenuResponse reads it: serial, menu id, 1-based index (0 cancels), graphic, hue.
        /// TazUO's Send_MenuResponse / Send_GrayMenuResponse pad what they leave out with zeros.
        /// </summary>
        public static byte[] MenuResponse(uint serial, ushort menuId, int index, ushort graphic, ushort hue) =>
            new PacketWriter(0x7D, 13).UInt32(serial).UInt16(menuId).UInt16(index).UInt16(graphic).UInt16(hue).ToArray();

        // ---- links ----

        /// <summary>The longest link we offer to open.</summary>
        public const int MaxUrlLength = 2048;

        /// <summary>
        /// Whether a link from the server may be offered to the browser: an absolute http or https URL with a host, no
        /// user name or password in it (they disguise the real host), no spaces or control characters, not too long.
        /// Everything else (file:, javascript:, data:, mailto:, a bare path or command line) is refused.
        /// </summary>
        public static bool TryParseSafeUrl(string text, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim();
            if (text.Length > MaxUrlLength)
                return false;
            foreach (char c in text)
                if (c <= ' ' || c == 0x7F || c > 0x7E)
                    return false;
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri parsed))
                return false;
            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
                return false;
            if (string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo))
                return false;
            uri = parsed;
            return true;
        }
    }
}
