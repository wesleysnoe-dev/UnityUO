using System;
using System.Buffers.Binary;
using System.IO;

namespace UOClient.IO.Formats
{
    /// <summary>One unifont glyph: offsets, size and 1-bit rows (MSB first, ((Width - 1) / 8 + 1) bytes a row).</summary>
    public readonly struct UniGlyph
    {
        public readonly sbyte OffsetX, OffsetY, Width, Height;
        public readonly byte[] Data;

        public UniGlyph(sbyte offsetX, sbyte offsetY, sbyte width, sbyte height, byte[] data)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
            Width = width;
            Height = height;
            Data = data;
        }

        public int Stride => (Width - 1) / 8 + 1;

        /// <summary>The pen advance, OffsetX + Width + 1, cast to sbyte as FontsLoader.cs:1246 does.</summary>
        public int Advance => (sbyte)(OffsetX + Width + 1);

        /// <summary>How far down the glyph reaches from the line top, OffsetY + Height (FontsLoader.cs:1393).</summary>
        public int Bottom => OffsetY + Height;

        public bool IsSet(int x, int y) => (Data[y * Stride + (x >> 3)] & (0x80 >> (x & 7))) != 0;
    }

    /// <summary>
    /// A unicode font (unifont.mul, unifont1.mul ...): 0x10000 int32 offsets, one per UTF-16 char (0 = none), each
    /// pointing at sbyte OffsetX, OffsetY, Width, Height and the 1-bit rows. Measured as TazUO's FontsLoader does
    /// (E:\UOOverhaul\build\tazuo\src\ClassicUO.Assets\FontsLoader.cs): a char only has a glyph when its offset is
    /// non-zero and its Width and Height are both above zero (:213-270, Data stays null otherwise); a space with no
    /// glyph advances 8 (:30); every other char without one draws nothing and takes no room.
    /// </summary>
    public sealed class UniFont
    {
        /// <summary>FontsLoader.cs:30.</summary>
        public const int SpaceWidth = 8;

        /// <summary>The height of a line HTML text lays out, whatever is on it (FontsLoader.cs:31, :3433).</summary>
        public const int HtmlLineHeight = 18;

        /// <summary>The height of a line with nothing drawn on it (FontsLoader.cs:1428, :1601).</summary>
        public const int EmptyLineHeight = 14;

        /// <summary>The height of a line holding only spaces (FontsLoader.cs:1570-1573).</summary>
        public const int SpaceLineHeight = 5;

        private const int TableSize = 0x10000 * 4;

        private readonly byte[] _file;
        private readonly UniGlyph?[] _glyphs = new UniGlyph?[0x10000];
        private readonly bool[] _loaded = new bool[0x10000];
        private int _maxCharHeight = -1;

        public UniFont(string path) : this(File.ReadAllBytes(path)) { }

        public UniFont(byte[] file) => _file = file ?? throw new ArgumentNullException(nameof(file));

        /// <summary>The glyph for a char, or false when the font has none (FontsLoader.cs:189-211).</summary>
        public bool TryGetGlyph(char c, out UniGlyph glyph)
        {
            int index = c;
            UniGlyph? cached;
            lock (_glyphs)
            {
                if (!_loaded[index])
                {
                    _glyphs[index] = Load(index);
                    _loaded[index] = true;
                }
                cached = _glyphs[index];
            }
            glyph = cached ?? default;
            return cached.HasValue;
        }

        // FontsLoader.cs:213-270.
        private UniGlyph? Load(int index)
        {
            if (index * 4 + 4 > _file.Length)
                return null;
            int lookup = BinaryPrimitives.ReadInt32LittleEndian(_file.AsSpan(index * 4));
            if (lookup <= 0 || lookup + 4 > _file.Length)
                return null;
            sbyte ox = (sbyte)_file[lookup], oy = (sbyte)_file[lookup + 1], w = (sbyte)_file[lookup + 2], h = (sbyte)_file[lookup + 3];
            if (w <= 0 || h <= 0)
                return null;
            int size = ((w - 1) / 8 + 1) * h;
            var data = new byte[size];
            Buffer.BlockCopy(_file, lookup + 4, data, 0, Math.Max(0, Math.Min(size, _file.Length - lookup - 4)));
            return new UniGlyph(ox, oy, w, h, data);
        }

        /// <summary>A char's advance (GetCharWidthUnicode, FontsLoader.cs:1262-1282): 0 for '\0', '\r' and chars with no glyph.</summary>
        public int GetCharWidth(char c)
        {
            if (c == 0 || c == '\r')
                return 0;
            if (TryGetGlyph(c, out UniGlyph g))
                return g.Advance;
            return c == ' ' ? SpaceWidth : 0;
        }

        /// <summary>
        /// The width of the widest '\n'-separated line, unwrapped (GetWidthUnicode, FontsLoader.cs:1220-1260). This is
        /// the width a Label or an unbounded RenderedText lays its text out in; the texture adds 4 (:1693).
        /// </summary>
        public int Measure(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            int line = 0, max = 0;
            foreach (char c in text)
            {
                if (c != '\r' && TryGetGlyph(c, out UniGlyph g))
                    line += g.Advance;
                else if (c == ' ')
                    line += SpaceWidth;
                else if (c == '\n')
                {
                    max = Math.Max(max, line);
                    line = 0;
                }
            }
            return Math.Max(max, line);
        }

        /// <summary>
        /// The height of unwrapped text, the per-line heights summed (GetHeightUnicode, FontsLoader.cs:3444-3484, with
        /// HTML off and the width its own Measure, so no line wraps). A line is as tall as GetInfoUnicode's MaxHeight
        /// (:1373-1605, no flags): the lowest OffsetY + Height of its glyphs; 5 when it holds only spaces; 14 when
        /// nothing on it is drawn and it ends in '\n' (or the text ends in one, or in '\r'). 0 for empty text.
        /// </summary>
        public int Height(string text) => Height(text, out _);

        /// <summary>As <see cref="Height(string)"/>, also giving the number of lines laid out.</summary>
        public int Height(string text, out int lines)
        {
            lines = 0;
            if (string.IsNullOrEmpty(text))
                return 0;
            int total = 0, lineMax = 0, readWidth = 0;
            foreach (char c in text)
            {
                if (c == '\n')
                {
                    // :1415-1443.
                    total += lineMax == 0 ? EmptyLineHeight : lineMax;
                    lines++;
                    lineMax = 0;
                    readWidth = 0;
                    continue;
                }
                bool has = TryGetGlyph(c, out UniGlyph g);
                if (!has && c != ' ' && c != '\r')
                    continue; // :1387-1390
                if (c == ' ')
                {
                    // :1395-1399, :1566-1573.
                    readWidth = SpaceWidth;
                    if (lineMax <= 0)
                        lineMax = SpaceLineHeight;
                }
                else
                {
                    // :1575-1582; a '\r' without a glyph is _nullChar, all zero.
                    readWidth += c == '\r' ? 0 : g.OffsetX + g.Width + 1;
                    int charHeight = has ? g.Bottom : 0;
                    if (charHeight > lineMax)
                        lineMax = charHeight;
                }
            }
            // :1591-1605.
            char last = text[text.Length - 1];
            if (readWidth == 0 && (last == '\n' || last == '\r'))
                lineMax = EmptyLineHeight;
            lines++;
            return total + lineMax;
        }

        /// <summary>The lowest OffsetY + Height of any glyph in the font: the tallest line one char can make.</summary>
        public int MaxCharHeight
        {
            get
            {
                if (_maxCharHeight >= 0)
                    return _maxCharHeight;
                int max = 0;
                int count = Math.Min(0x10000, _file.Length / 4);
                for (int i = 0; i < count; i++)
                    if (TryGetGlyph((char)i, out UniGlyph g))
                        max = Math.Max(max, g.Bottom);
                return _maxCharHeight = max;
            }
        }
    }
}
