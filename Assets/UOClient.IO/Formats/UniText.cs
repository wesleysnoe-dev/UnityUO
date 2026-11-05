using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UOClient.IO.Formats
{
    /// <summary>TazUO's FontStyle / UOFONT_* flags (RenderedText.cs:16-30, FontsLoader.cs:19-29).</summary>
    [Flags]
    public enum UniTextStyle : ushort
    {
        None = 0x0000,
        Solid = 0x0001,
        Italic = 0x0002,
        Indention = 0x0004,
        BlackBorder = 0x0008,
        Underline = 0x0010,
        Fixed = 0x0020,
        Cropped = 0x0040,
        BQ = 0x0080,
        ExtraHeight = 0x0100,
    }

    /// <summary>TEXT_ALIGN_TYPE.</summary>
    public enum UniTextAlign : byte
    {
        Left,
        Center,
        Right,
    }

    /// <summary>A rendered text: its pixels (0xAARRGGBB, transparent where nothing is drawn) and how many lines it laid out.</summary>
    public sealed class UniTextImage
    {
        public static readonly UniTextImage Empty = new UniTextImage(null, 0);

        public UniTextImage(Bitmap32 bitmap, int lines)
        {
            Bitmap = bitmap;
            Lines = lines;
        }

        /// <summary>Null when nothing was drawn (empty text, no font, zero width), as TazUO then makes no texture.</summary>
        public Bitmap32 Bitmap { get; }

        public int Lines { get; }
        public int Width => Bitmap?.Width ?? 0;
        public int Height => Bitmap?.Height ?? 0;
    }

    /// <summary>
    /// Unicode text laid out and drawn as TazUO's FontsLoader does it (E:\UOOverhaul\build\tazuo\src\ClassicUO.Assets\
    /// FontsLoader.cs), for the RenderedText a Label, CroppedText or HtmlControl makes: GenerateUnicode (:1044-1125) with
    /// its crop (GetTextByWidthUnicode, :1127-1218), the line layout of plain text (GetInfoUnicode, :1326-1608) and of HTML
    /// (GetInfoHTML, :2247-2485, with GetHTMLData, ParseHTMLTag and friends, :2487-3423), and the pixels
    /// (GeneratePixelsUnicode, :1610-2245): one pixel of room left of the first glyph, glyphs at OffsetX + 1, 4 extra pixels
    /// of width and height, the black border, bold (solid), italic and underline. The quirks are kept, so the widths, line
    /// breaks and pixels match: a wrapped line's first char is credited with the width of the char that did not fit, an
    /// HTML line is 18 tall whatever is on it, the crop keeps raw leading chars when the text has tags.
    /// Colours here are 0xAARRGGBB; TazUO's packed RGBA constants are converted where they are read.
    /// Web links are coloured and underlined but not clickable. Not ported: CropTexture (no gump control uses it).
    /// </summary>
    public sealed class UniText
    {
        /// <summary>The colour TazUO draws hue 0xFFFF in (FontsLoader.cs:1719-1722): white, alpha 0xFE.</summary>
        public const uint UnhuedArgb = 0xFEFFFFFF;

        /// <summary>An HTML colour of "none": the char takes the text's colour (FontsLoader.cs:1873).</summary>
        public const uint NoHtmlColor = 0xFFFFFFFF;

        /// <summary>The border's and GetPolygoneColor's black (FontsLoader.cs:1734).</summary>
        public const uint BlackArgb = 0xFF010101;

        public const int SpaceWidth = UniFont.SpaceWidth;
        public const int HtmlLineHeight = UniFont.HtmlLineHeight;
        private const float ItalicKoefficient = 3.3f;

        private const ushort Solid = (ushort)UniTextStyle.Solid, Italic = (ushort)UniTextStyle.Italic,
            Indention = (ushort)UniTextStyle.Indention, BlackBorder = (ushort)UniTextStyle.BlackBorder,
            Underline = (ushort)UniTextStyle.Underline, Fixed = (ushort)UniTextStyle.Fixed,
            Cropped = (ushort)UniTextStyle.Cropped, Bq = (ushort)UniTextStyle.BQ, ExtraHeight = (ushort)UniTextStyle.ExtraHeight;

        private readonly Func<int, UniFont> _fonts;

        // FontsLoader's HtmlStatus and IsUsingHTML, set for one Generate call.
        private bool _html, _backgroundColored;
        private uint _htmlColor, _backgroundColor, _linkColor, _visitedLinkColor;
        private int _marginX, _marginY, _marginWidth, _marginHeight;
        private readonly List<string> _links = new List<string>();

        /// <param name="fonts">Unicode font by index (0-19), null when the client has no such file.</param>
        public UniText(Func<int, UniFont> fonts) => _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));

        private UniFont Font(int font) => font >= 0 && font < 20 ? _fonts(font) : null;

        // GetCharUni (:189-211): a char with no glyph (or no font) is _nullChar, all zero and no Data.
        private UniGlyph Glyph(int font, char c)
        {
            UniFont f = Font(font);
            return f != null && f.TryGetGlyph(c, out UniGlyph g) ? g : default;
        }

        /// <summary>GetWidthUnicode (:1230-1260): the widest '\n' line, unwrapped; 0 with no font.</summary>
        public int Width(int font, string text) => Font(font)?.Measure(text) ?? 0;

        /// <summary>
        /// RenderedText.CreateTexture → GenerateUnicode (RenderedText.cs:646-764): the text drawn in <paramref name="argb"/>
        /// (GetPolygoneColor's shade, or <see cref="UnhuedArgb"/>) at most <paramref name="maxWidth"/> wide (0: its own
        /// width). With <paramref name="html"/> the tags are read and <paramref name="htmlColor"/> is the starting colour
        /// (SetUseHTML: <see cref="NoHtmlColor"/> leaves chars in <paramref name="argb"/>); a body bgcolor only fills when
        /// <paramref name="htmlBackgroundColored"/>.
        /// </summary>
        public UniTextImage Generate(string text, int font, uint argb, int maxWidth, UniTextAlign align, UniTextStyle style,
            bool html = false, uint htmlColor = NoHtmlColor, bool htmlBackgroundColored = false)
        {
            _html = html;
            _htmlColor = htmlColor;
            _backgroundColored = htmlBackgroundColored;
            try
            {
                return GenerateUnicode((byte)font, text, argb, maxWidth, align, (ushort)style);
            }
            finally
            {
                _html = false;
            }
        }

        /// <summary>GetTextByWidthUnicode (:1127-1218) on its own, with HTML off: what a crop to <paramref name="width"/> keeps.</summary>
        public string CropToWidth(int font, string text, int width, bool cropped) => TextByWidth((byte)font, text, width, cropped);

        /// <summary>GetCharWidthUnicode (:1262-1282): a char's advance, 0 for '\0', '\r', chars with no glyph and no font.</summary>
        public int CharWidth(int font, char c) => Font(font)?.GetCharWidth(c) ?? 0;

        /// <summary>
        /// The plain-text lines <paramref name="text"/> lays out in at most <paramref name="width"/> (GetInfoUnicode, as
        /// GeneratePixelsUnicode wraps it): each line's first char, char count, width and height.
        /// </summary>
        public List<(int CharStart, int CharCount, int Width, int MaxHeight)> Lines(int font, string text, int width,
            UniTextAlign align, UniTextStyle style)
        {
            var lines = new List<(int, int, int, int)>();
            if (string.IsNullOrEmpty(text))
                return lines;
            if (width == 0)
                width = Width(font, text);
            for (Line info = Info((byte)font, text, text.Length, align, (ushort)style, width); info != null; info = info.Next)
                lines.Add((info.CharStart, info.CharCount, info.Width, info.MaxHeight));
            return lines;
        }

        /// <summary>
        /// GetCaretPosUnicode (:3486-3598): where the caret before char <paramref name="pos"/> of plain
        /// <paramref name="text"/> sits, laid out in <paramref name="width"/> (0: the text's own) as GetInfoUnicode wraps it.
        /// Past a line's end it is at the line's width, the next line down by its height.
        /// </summary>
        public (int X, int Y) CaretPosition(int font, string text, int pos, int width, UniTextAlign align, UniTextStyle style)
        {
            int x = align == UniTextAlign.Center ? width >> 1 : align == UniTextAlign.Right ? width : 0, y = 0;
            if (Font(font) == null || string.IsNullOrEmpty(text))
                return (x, y);
            if (width == 0)
                width = Width(font, text);
            for (Line info = Info((byte)font, text, text.Length, align, (ushort)style, width); info != null; info = info.Next)
            {
                x = info.Align == UniTextAlign.Center ? Math.Max(0, (width - info.Width) >> 1)
                    : info.Align == UniTextAlign.Right ? width : 0;
                int len = info.CharCount;
                if (info.CharStart == pos)
                    return (x, y);
                if (pos <= info.CharStart + len && info.Data.Count >= len)
                {
                    for (int i = 0; i < len; i++)
                    {
                        char ch = info.Data[i].Item;
                        UniGlyph g = Glyph(font, ch);
                        if (ch != '\r' && g.Data != null)
                            x += (sbyte)(g.OffsetX + g.Width + 1);
                        else if (ch == ' ')
                            x += SpaceWidth;
                        if (info.CharStart + i + 1 == pos)
                            return (x, y);
                    }
                }
                else
                    x = width;
                if (info.Next != null)
                    y += info.MaxHeight;
            }
            return (x, y);
        }

        // ---- GenerateUnicode, :1044-1125 ----

        private UniTextImage GenerateUnicode(byte font, string str, uint color, int width, UniTextAlign align, ushort flags)
        {
            if (string.IsNullOrEmpty(str))
                return UniTextImage.Empty;
            if ((flags & (Fixed | Cropped)) != 0)
            {
                if (width == 0)
                    return UniTextImage.Empty;
                if (Width(font, str) > width)
                    return Pixels(font, TextByWidth(font, str, width, (flags & Cropped) != 0), color, width, align, flags);
            }
            return Pixels(font, str, color, width, align, flags);
        }

        // :1127-1218.
        private string TextByWidth(byte font, string str, int width, bool isCropped)
        {
            if (Font(font) == null || string.IsNullOrEmpty(str))
                return "";
            var sb = new StringBuilder(str.Length + 3);
            if (_html)
            {
                // The length the tags leave; that many chars from the end are measured, the rest kept as they are.
                int strLen = str.Length;
                HtmlData(font, str, ref strLen, UniTextAlign.Left, 0);
                int size = str.Length - strLen;
                if (size > 0)
                {
                    sb.Append(str, 0, size);
                    str = str.Substring(str.Length - strLen, strLen);
                    if (Width(font, str) < width)
                        isCropped = false;
                }
            }
            if (isCropped)
            {
                UniGlyph dot = Glyph(font, '.');
                if (dot.Data != null)
                    width -= dot.Width * 3 + 3;
            }
            int textLength = 0;
            foreach (char c in str)
            {
                UniGlyph g = Glyph(font, c);
                sbyte charWidth = 0;
                if (g.Data != null)
                    charWidth = (sbyte)(g.OffsetX + g.Width + 1);
                else if (c == ' ')
                    charWidth = SpaceWidth;
                if (charWidth == 0)
                    continue;
                textLength += charWidth;
                if (textLength > width)
                    break;
                sb.Append(c);
            }
            if (isCropped)
                sb.Append("...");
            return sb.ToString();
        }

        // ---- lines ----

        private struct CharData
        {
            public uint Color;
            public ushort Flags;
            public byte Font;
            public char Item;
            public ushort Link;

            public CharData(uint color, ushort flags, byte font, char item, ushort link)
            {
                Color = color;
                Flags = flags;
                Font = font;
                Item = item;
                Link = link;
            }
        }

        // MultilinesFontInfo (:3777-3798).
        private sealed class Line
        {
            public UniTextAlign Align;
            public int CharCount, CharStart, IndentionOffset, MaxHeight, Width;
            public readonly List<CharData> Data = new List<CharData>();
            public Line Next;

            // FastList.Length = n: drops what is past n.
            public void Cut(int n)
            {
                if (n < 0)
                    n = 0;
                if (n < Data.Count)
                    Data.RemoveRange(n, Data.Count - n);
            }
        }

        private Line Info(byte font, string str, int len, UniTextAlign align, ushort flags, int width)
        {
            // :1337-1340.
            _linkColor = 0xFF0000FF;
            _visitedLinkColor = 0xFFFF0000;
            _backgroundColor = 0;
            _marginX = _marginY = _marginWidth = _marginHeight = 0;
            if (Font(font) == null)
                return null;
            return _html ? InfoHtml(font, str, len, flags, align, width) : InfoPlain(font, str, len, align, flags, width);
        }

        // GetInfoUnicode, :1352-1607, with countret and countspaces off (as GeneratePixelsUnicode calls it).
        private Line InfoPlain(byte font, string str, int len, UniTextAlign align, ushort flags, int width)
        {
            var info = new Line { Align = align };
            Line ptr = info;
            int indentionOffset = 0, charCount = 0, lastSpace = 0, readWidth = 0;
            int extraHeight = (flags & ExtraHeight) != 0 ? 4 : 0;
            bool isFixed = (flags & Fixed) != 0, isCropped = (flags & Cropped) != 0;
            const uint charColor = NoHtmlColor;

            for (int i = 0; i < len; i++)
            {
                char si = str[i];
                if (si == '\n' && (isFixed || isCropped))
                    si = (char)0;
                UniGlyph g = Glyph(font, si);
                if (g.Data == null && si != ' ' && si != '\n' && si != '\r')
                    continue;
                int charWidth = g.OffsetX + g.Width + 1;
                int charHeight = g.OffsetY + g.Height;

                if (si == ' ')
                {
                    lastSpace = i;
                    ptr.Width += readWidth;
                    readWidth = 0;
                    ptr.CharCount += charCount;
                    charCount = 0;
                }

                int eval = ptr.CharStart;
                if (ptr.Width + readWidth + charWidth > width || si == '\n')
                {
                    if (lastSpace == ptr.CharStart && lastSpace == 0 && si != '\n')
                        ++eval;

                    if (si == '\n')
                    {
                        ptr.Width += readWidth;
                        ptr.CharCount += charCount;
                        lastSpace = i;
                        if (ptr.Width == 0)
                            ptr.Width = 1;
                        if (ptr.MaxHeight == 0)
                            ptr.MaxHeight = 14 + extraHeight;
                        ptr.Cut(ptr.CharCount);
                        ptr = ptr.Next = new Line { Align = align, CharStart = i + 1 };
                        readWidth = 0;
                        charCount = 0;
                        indentionOffset = 0;
                        continue;
                    }

                    if (lastSpace + 1 == eval && !isFixed && !isCropped)
                    {
                        // A word longer than the line: break it here.
                        ptr.Width += readWidth;
                        ptr.CharCount += charCount;
                        if (ptr.Width == 0)
                            ptr.Width = 1;
                        if (ptr.MaxHeight == 0)
                            ptr.MaxHeight = 14 + extraHeight;
                        ptr = ptr.Next = new Line { Align = align, CharStart = i };
                        lastSpace = i - 1;
                        charCount = 0;
                        if (ptr.Align == UniTextAlign.Left && (flags & Indention) != 0)
                            indentionOffset = 14;
                        ptr.IndentionOffset = indentionOffset;
                        readWidth = indentionOffset;
                    }
                    else
                    {
                        if (isFixed)
                        {
                            ptr.Data.Add(new CharData(charColor, flags, font, si, 0));
                            readWidth += si == '\r' ? 0 : charWidth;
                            if (charHeight > ptr.MaxHeight)
                                ptr.MaxHeight = charHeight + extraHeight;
                            charCount++;
                            ptr.Width += readWidth;
                            ptr.CharCount += charCount;
                        }

                        // Back to just after the last space; the new line starts there.
                        i = lastSpace + 1;
                        si = i < str.Length ? str[i] : '\0';
                        if (ptr.Width == 0)
                            ptr.Width = 1;
                        if (ptr.MaxHeight == 0)
                            ptr.MaxHeight = 14 + extraHeight;
                        charCount = 0;
                        ptr.Cut(ptr.CharCount);
                        if (isFixed || isCropped)
                            break;
                        ptr = ptr.Next = new Line { Align = align, CharStart = i };
                        if (ptr.Align == UniTextAlign.Left && (flags & Indention) != 0)
                            indentionOffset = 14;
                        ptr.IndentionOffset = indentionOffset;
                        readWidth = indentionOffset;
                    }
                }

                ptr.Data.Add(new CharData(charColor, flags, font, si, 0));
                if (si == ' ')
                {
                    readWidth += SpaceWidth;
                    if (ptr.MaxHeight <= 0)
                        ptr.MaxHeight = 5 + extraHeight;
                }
                else
                {
                    readWidth += si == '\r' ? 0 : charWidth;
                    if (charHeight > ptr.MaxHeight)
                        ptr.MaxHeight = charHeight + extraHeight;
                }
                charCount++;
            }

            ptr.Width += readWidth;
            ptr.CharCount += charCount;
            if (readWidth == 0 && len != 0 && (str[len - 1] == '\n' || str[len - 1] == '\r'))
            {
                ptr.Width = 1;
                ptr.MaxHeight = 14;
            }
            return info;
        }

        // GetInfoHTML, :2247-2485.
        private Line InfoHtml(byte font, string str, int len, ushort flags, UniTextAlign align, int width)
        {
            if (len <= 0)
                return null;
            HtmlChar[] data = HtmlData(font, str, ref len, align, flags);
            if (len <= 0)
                return null;

            var info = new Line { Align = data[0].Align };
            Line ptr = info;
            int indentionOffset = 0, charCount = 0, lastSpace = 0, readWidth = 0;
            bool isFixed = (flags & Fixed) != 0, isCropped = (flags & Cropped) != 0;

            for (int i = 0; i < len; i++)
            {
                char si = data[i].Char;
                UniGlyph g = Glyph(data[i].Font, si);
                if (si == '\r' || si == '\n')
                    si = si == '\r' || isFixed || isCropped ? (char)0 : '\n';
                if (g.Data == null && si != ' ' && si != '\n')
                    continue;

                if (si == ' ')
                {
                    lastSpace = i;
                    ptr.Width += readWidth;
                    readWidth = 0;
                    ptr.CharCount += charCount;
                    charCount = 0;
                }

                int solidWidth = data[i].Flags & Solid;
                int charWidth = g.OffsetX + g.Width + 1;

                if (ptr.Width + readWidth + charWidth + solidWidth > width || si == '\n')
                {
                    if (lastSpace == ptr.CharStart && lastSpace == 0 && si != '\n')
                        ptr.CharStart = 1;

                    if (si == '\n')
                    {
                        ptr.Width += readWidth;
                        ptr.CharCount += charCount;
                        lastSpace = i;
                        if (ptr.Width <= 0)
                            ptr.Width = 1;
                        ptr.MaxHeight = HtmlLineHeight;
                        ptr.Cut(ptr.CharCount);
                        ptr = ptr.Next = new Line { Align = data[i].Align, CharStart = i + 1 };
                        readWidth = 0;
                        charCount = 0;
                        indentionOffset = 0;
                        continue;
                    }

                    if (lastSpace + 1 == ptr.CharStart && !isFixed && !isCropped)
                    {
                        ptr.Width += readWidth;
                        ptr.CharCount += charCount;
                        if (ptr.Width <= 0)
                            ptr.Width = 1;
                        ptr.MaxHeight = HtmlLineHeight;
                        ptr = ptr.Next = new Line { Align = data[i].Align, CharStart = i };
                        lastSpace = i - 1;
                        charCount = 0;
                        if (ptr.Align == UniTextAlign.Left && (data[i].Flags & Indention) != 0)
                            indentionOffset = 14;
                        ptr.IndentionOffset = indentionOffset;
                        readWidth = indentionOffset;
                    }
                    else
                    {
                        if (isFixed)
                        {
                            ptr.Data.Add(new CharData(data[i].Color, data[i].Flags, data[i].Font, si, data[i].Link));
                            readWidth += charWidth;
                            ptr.MaxHeight = HtmlLineHeight;
                            charCount++;
                            ptr.Width += readWidth;
                            ptr.CharCount += charCount;
                        }

                        i = lastSpace + 1;
                        if (i >= len)
                            break;
                        si = data[i].Char;
                        solidWidth = data[i].Flags & Solid;
                        if (ptr.Width <= 0)
                            ptr.Width = 1;
                        ptr.MaxHeight = HtmlLineHeight;
                        ptr.Cut(ptr.CharCount);
                        charCount = 0;
                        if (isFixed || isCropped)
                            break;
                        ptr = ptr.Next = new Line { Align = data[i].Align, CharStart = i };
                        if (ptr.Align == UniTextAlign.Left && (data[i].Flags & Indention) != 0)
                            indentionOffset = 14;
                        ptr.IndentionOffset = indentionOffset;
                        readWidth = indentionOffset;
                    }
                }

                ptr.Data.Add(new CharData(data[i].Color, data[i].Flags, data[i].Font, si, data[i].Link));
                readWidth += si == ' ' ? SpaceWidth : charWidth + solidWidth;
                charCount++;
            }

            ptr.Width += readWidth;
            ptr.CharCount += charCount;
            ptr.MaxHeight = HtmlLineHeight;
            return info;
        }

        // GetHeightUnicode(info), :3425-3442.
        private int Height(Line info)
        {
            int h = 0;
            for (; info != null; info = info.Next)
                h += _html ? HtmlLineHeight : info.MaxHeight;
            return h;
        }

        // ---- pixels, GeneratePixelsUnicode :1610-2245 ----

        private UniTextImage Pixels(byte font, string str, uint color, int width, UniTextAlign align, ushort flags)
        {
            if (Font(font) == null || string.IsNullOrEmpty(str))
                return UniTextImage.Empty;
            int len = str.Length;
            if (width == 0)
            {
                width = Width(font, str);
                if (width == 0)
                    return UniTextImage.Empty;
            }

            Line info = Info(font, str, len, align, flags, width);
            if (info == null)
                return UniTextImage.Empty;
            if (_html && (_marginX != 0 || _marginWidth != 0))
            {
                int newWidth = width - (_marginX + _marginWidth);
                info = Info(font, str, len, align, flags, newWidth < 10 ? 10 : newWidth);
                if (info == null)
                    return UniTextImage.Empty;
            }

            width += 4;
            int height = Height(info);
            if (height == 0)
                return UniTextImage.Empty;
            height += _marginY + _marginHeight + 4;

            var bitmap = new Bitmap32(width, height);
            uint[] pData = bitmap.Pixels;
            int lineOffsY = _marginY;
            uint dataColor = color;
            bool isItalic = (flags & Italic) != 0, isSolid = (flags & Solid) != 0;
            bool isBlackBorder = (flags & BlackBorder) != 0, isUnderline = (flags & Underline) != 0;
            int linesCount = 0;

            for (Line ptr = info; ptr != null; ptr = ptr.Next)
            {
                linesCount++;
                int w = _marginY; // sic, :1745
                switch (ptr.Align)
                {
                    case UniTextAlign.Center:
                        w += (width - 8) / 2 - ptr.Width / 2;
                        if (w < 0)
                            w = 0;
                        break;
                    case UniTextAlign.Right:
                        w += width - 10 - ptr.Width;
                        if (w < 0)
                            w = 0;
                        break;
                    case UniTextAlign.Left when (flags & Indention) != 0:
                        w += ptr.IndentionOffset;
                        break;
                }

                int dataSize = ptr.Data.Count;
                for (int i = 0; i < dataSize; i++)
                {
                    CharData dataPtr = ptr.Data[i];
                    char si = dataPtr.Item;
                    UniGlyph g = Glyph(dataPtr.Font, si);
                    if (g.Data == null && si != ' ')
                        continue;

                    int offsX = 0, offsY = 0, dw, dh = 0;
                    if (si == ' ')
                        dw = SpaceWidth;
                    else
                    {
                        offsX = g.OffsetX + 1;
                        offsY = g.OffsetY;
                        dw = g.Width;
                        dh = g.Height;
                    }

                    int tmpW = w;
                    uint charColor = dataColor;
                    bool isBlackPixel = IsBlack(charColor);

                    if (si != ' ')
                    {
                        if (_html)
                        {
                            isItalic = (dataPtr.Flags & Italic) != 0;
                            isSolid = (dataPtr.Flags & Solid) != 0;
                            isBlackBorder = (dataPtr.Flags & BlackBorder) != 0;
                            isUnderline = (dataPtr.Flags & Underline) != 0;
                            if (dataPtr.Color != NoHtmlColor)
                            {
                                charColor = dataPtr.Color;
                                isBlackPixel = IsBlack(charColor);
                            }
                        }

                        int scanlineCount = ((dw - 1) >> 3) + 1;
                        int scanLineOff = 0;
                        for (int y = 0; y < dh; y++, scanLineOff += scanlineCount)
                        {
                            int testY = offsY + lineOffsY + y;
                            if (testY < 0)
                                testY = 0;
                            if (testY >= height)
                                break;
                            int italicOffset = isItalic ? (int)((dh - y) / ItalicKoefficient) : 0;
                            int testX = w + offsX + italicOffset + (isSolid ? 1 : 0);
                            for (int x = 0; x < dw; x++)
                            {
                                int nowX = testX + x;
                                if (nowX >= width)
                                    break;
                                if (nowX >= 0 && (g.Data[scanLineOff + (x >> 3)] & (1 << (7 - (x & 7)))) != 0)
                                    pData[testY * width + nowX] = charColor;
                            }
                        }

                        if (isSolid)
                            DrawSolid(pData, width, height, w, offsX, offsY, lineOffsY, dw, dh, isItalic, charColor);

                        if (isBlackBorder && !isBlackPixel)
                            DrawBorder(pData, width, height, w, offsX, offsY, lineOffsY, dw, dh, isItalic);

                        w += dw + offsX + (isSolid ? 1 : 0);
                    }
                    else
                    {
                        w += SpaceWidth;
                        if (_html)
                        {
                            isUnderline = (dataPtr.Flags & Underline) != 0;
                            if (dataPtr.Color != NoHtmlColor)
                                charColor = dataPtr.Color;
                        }
                    }

                    if (isUnderline)
                    {
                        // :2168-2197; the line under the base font's 'a'. Past the bottom it ends the whole line.
                        int minXOk = tmpW + offsX > 0 ? -1 : 0;
                        int maxXOk = w + offsX + dw < width ? 1 : 0;
                        UniGlyph a = Glyph(font, 'a');
                        int testY = lineOffsY + a.OffsetY + a.Height;
                        if (testY >= height)
                            break;
                        if (testY < 0)
                            testY = 0;
                        for (int cx = minXOk; cx < dw + maxXOk; cx++)
                        {
                            int testX = cx + tmpW + offsX + (isSolid ? 1 : 0);
                            if (testX >= width)
                                break;
                            if (testX >= 0)
                                pData[testY * width + testX] = charColor;
                        }
                    }
                }

                lineOffsY += ptr.MaxHeight;
            }

            if (_html && _backgroundColored && _backgroundColor != 0)
            {
                uint bg = _backgroundColor | 0xFF000000;
                for (int p = 0; p < pData.Length; p++)
                    if (pData[p] == 0)
                        pData[p] = bg;
            }
            return new UniTextImage(bitmap, linesCount);
        }

        private static bool IsBlack(uint c) => (c & 0xFF) <= 8 && ((c >> 8) & 0xFF) <= 8 && ((c >> 16) & 0xFF) <= 8;

        // Bold, :1942-2053: black filled in beside the glyph's pixels, then recoloured to the glyph's colour.
        private static void DrawSolid(uint[] pData, int width, int height, int w, int offsX, int offsY, int lineOffsY,
            int dw, int dh, bool isItalic, uint charColor)
        {
            uint solidColor = BlackArgb;
            if (solidColor == charColor)
                solidColor++;
            int minXOk = w + offsX > 0 ? -1 : 0;
            int maxXOk = (w + offsX + dw < width ? 1 : 0) + dw;
            for (int cy = 0; cy < dh; cy++)
            {
                int testY = offsY + lineOffsY + cy;
                if (testY >= height)
                    break;
                if (testY < 0)
                    testY = 0;
                int italicOffset = isItalic ? (int)((dh - cy) / ItalicKoefficient) : 0;
                for (int cx = minXOk; cx < maxXOk; cx++)
                {
                    int testX = cx + w + offsX + italicOffset;
                    if (testX >= width)
                        break;
                    int block = testY * width + testX;
                    if (block < 0 || pData[block] != 0)
                        continue;
                    int endX = cx < dw ? 2 : 1;
                    if (endX == 2 && testX + 1 >= width)
                        endX--;
                    for (int x = 0; x < endX; x++)
                    {
                        uint p = pData[testY * width + testX + x];
                        if (p != 0 && p != solidColor)
                        {
                            pData[block] = solidColor;
                            break;
                        }
                    }
                }
            }
            for (int cy = 0; cy < dh; cy++)
            {
                int testY = offsY + lineOffsY + cy;
                if (testY >= height)
                    break;
                if (testY < 0)
                    testY = 0;
                int italicOffset = isItalic ? (int)((dh - cy) / ItalicKoefficient) : 0;
                for (int cx = 0; cx < dw; cx++)
                {
                    int testX = cx + w + offsX + italicOffset;
                    if (testX >= width)
                        break;
                    int block = testY * width + testX;
                    if (block >= 0 && pData[block] == solidColor)
                        pData[block] = charColor;
                }
            }
        }

        // The black border, :2055-2144: every empty pixel of the glyph's box grown by one that touches a drawn,
        // non-black pixel (this glyph's or one already there) turns black. Which neighbours count is as TazUO has it.
        private static void DrawBorder(uint[] pData, int width, int height, int w, int offsX, int offsY, int lineOffsY,
            int dw, int dh, bool isItalic)
        {
            int minXOk = w + offsX > 0 ? -1 : 0;
            int minYOk = offsY + lineOffsY > 0 ? -1 : 0;
            int maxXOk = (w + offsX + dw < width ? 1 : 0) + dw;
            int maxYOk = (offsY + lineOffsY + dh < height ? 1 : 0) + dh;
            for (int cy = minYOk; cy < maxYOk; cy++)
            {
                int testY = offsY + lineOffsY + cy;
                if (testY < 0)
                    testY = 0;
                if (testY >= height)
                    break;
                int italicOffset = isItalic && cy >= 0 && cy < dh ? (int)((dh - cy) / ItalicKoefficient) : 0;
                for (int cx = minXOk; cx < maxXOk; cx++)
                {
                    int testX = cx + w + offsX + italicOffset;
                    if (testX >= width)
                        break;
                    int block = testY * width + testX;
                    if (block < 0 || pData[block] != 0)
                        continue;
                    int startX = cx > 0 ? -1 : 0;
                    int startY = cy > 0 ? -1 : 0;
                    int endX = cx < dw - 1 ? 2 : 1;
                    int endY = cy < dh - 1 ? 2 : 1;
                    if (endX == 2 && testX + 1 >= width)
                        endX--;
                    bool passed = false;
                    for (int x = startX; x < endX && !passed; x++)
                    {
                        int nowX = testX + x;
                        for (int y = startY; y < endY; y++)
                        {
                            int testBlock = (testY + y) * width + nowX;
                            if (testBlock < 0 || testBlock >= pData.Length)
                                continue;
                            uint p = pData[testBlock];
                            if (p != 0 && p != BlackArgb)
                            {
                                pData[block] = BlackArgb;
                                passed = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        // ---- HTML, :2487-3423 ----

        private enum Tag
        {
            None, B, I, A, U, P, Big, Small, Body, BaseFont, H1, H2, H3, H4, H5, H6, Br, Bq, Left, Center, Right, Div,
            Color, BodyBgColor,
        }

        private struct HtmlChar
        {
            public char Char;
            public byte Font;
            public UniTextAlign Align;
            public ushort Flags;
            public uint Color;
            public ushort Link;
        }

        private struct HtmlInfo
        {
            public Tag Tag;
            public UniTextAlign Align;
            public ushort Flags;
            public byte Font;
            public uint Color;
            public ushort Link;
        }

        // GetHTMLData, :2487-2634: each char with the look of the tags open around it; tags themselves drop out.
        private HtmlChar[] HtmlData(byte font, string str, ref int len, UniTextAlign align, ushort flags)
        {
            var data = new HtmlChar[Math.Max(0, len)];
            int newlen = 0;
            var info = new HtmlInfo { Tag = Tag.None, Align = align, Flags = flags, Font = font, Color = _htmlColor };
            var stack = new List<HtmlInfo> { info };
            HtmlInfo current = info;

            for (int i = 0; i < len; i++)
            {
                char si = str[i];
                if (si == '<')
                {
                    bool endTag = false;
                    var newInfo = new HtmlInfo { Tag = Tag.None, Align = UniTextAlign.Left, Font = 0xFF };
                    Tag tag = ParseTag(str, len, ref i, ref endTag, ref newInfo);
                    if (tag == Tag.None)
                        continue;
                    if (!endTag)
                    {
                        if (newInfo.Font == 0xFF)
                            newInfo.Font = stack[stack.Count - 1].Font;
                        if (tag != Tag.Body)
                            stack.Add(newInfo);
                        else
                        {
                            stack.Clear();
                            newlen = 0;
                            if (newInfo.Color != 0)
                                info.Color = newInfo.Color;
                            stack.Add(info);
                        }
                    }
                    else if (stack.Count > 1)
                    {
                        for (int j = stack.Count - 1; j >= 1; j--)
                            if (stack[j].Tag == tag)
                            {
                                stack.RemoveAt(j);
                                break;
                            }
                    }

                    current = CurrentInfo(stack);
                    switch (tag)
                    {
                        case Tag.Left:
                        case Tag.Center:
                        case Tag.Right:
                            if (newlen != 0)
                                endTag = true;
                            si = endTag ? '\n' : (char)0;
                            break;
                        case Tag.P:
                            si = endTag ? '\n' : (char)0;
                            break;
                        case Tag.BodyBgColor:
                        case Tag.Br:
                        case Tag.Bq:
                            si = '\n';
                            break;
                        default:
                            si = (char)0;
                            break;
                    }
                }

                if (si != 0)
                {
                    data[newlen++] = new HtmlChar
                    {
                        Char = si, Font = current.Font, Align = current.Align, Flags = current.Flags, Color = current.Color,
                        Link = current.Link,
                    };
                }
            }

            len = newlen;
            return data;
        }

        // GetCurrentHTMLInfo, :2636-2738.
        private HtmlInfo CurrentInfo(List<HtmlInfo> list)
        {
            var info = new HtmlInfo { Tag = Tag.None, Align = UniTextAlign.Left, Font = 0xFF };
            foreach (HtmlInfo current in list)
            {
                switch (current.Tag)
                {
                    case Tag.None:
                        info = current;
                        break;
                    case Tag.B:
                    case Tag.I:
                    case Tag.U:
                    case Tag.P:
                        info.Flags |= current.Flags;
                        info.Align = current.Align;
                        break;
                    case Tag.A:
                        info.Flags |= current.Flags;
                        info.Color = current.Color;
                        info.Link = current.Link;
                        break;
                    case Tag.Big:
                    case Tag.Small:
                    case Tag.H3:
                    case Tag.H6:
                        if (current.Font != 0xFF && Font(current.Font) != null)
                            info.Font = current.Font;
                        break;
                    case Tag.BaseFont:
                        if (current.Font != 0xFF && Font(current.Font) != null)
                            info.Font = current.Font;
                        if (current.Color != 0)
                            info.Color = current.Color;
                        break;
                    case Tag.H1:
                    case Tag.H2:
                    case Tag.H4:
                    case Tag.H5:
                        info.Flags |= current.Flags;
                        if (current.Font != 0xFF && Font(current.Font) != null)
                            info.Font = current.Font;
                        break;
                    case Tag.Bq:
                        info.Color = current.Color;
                        info.Flags |= current.Flags;
                        break;
                    case Tag.Left:
                    case Tag.Center:
                    case Tag.Right:
                    case Tag.Div:
                        info.Align = current.Align;
                        break;
                }
            }
            return info;
        }

        private static readonly Dictionary<string, Tag> TagNames = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase)
        {
            ["b"] = Tag.B, ["i"] = Tag.I, ["a"] = Tag.A, ["u"] = Tag.U, ["p"] = Tag.P, ["big"] = Tag.Big,
            ["small"] = Tag.Small, ["body"] = Tag.Body, ["basefont"] = Tag.BaseFont, ["h1"] = Tag.H1, ["h2"] = Tag.H2,
            ["h3"] = Tag.H3, ["h4"] = Tag.H4, ["h5"] = Tag.H5, ["h6"] = Tag.H6, ["br"] = Tag.Br, ["bq"] = Tag.Bq,
            ["left"] = Tag.Left, ["center"] = Tag.Center, ["right"] = Tag.Right, ["div"] = Tag.Div, ["color"] = Tag.Color,
        };

        // ParseHTMLTag, :2740-2966. Leaves i on the tag's '>' (or at len).
        private Tag ParseTag(string str, int len, ref int i, ref bool endTag, ref HtmlInfo info)
        {
            Tag tag = Tag.None;
            i++;
            if (i < len && str[i] == '/')
            {
                endTag = true;
                i++;
            }
            while (i < len && str[i] == ' ')
                i++;
            int j = i;
            for (; i < len; i++)
            {
                if (str[i] == '/')
                {
                    endTag = true; // <br/>
                    break;
                }
                if (str[i] == ' ' || str[i] == '>' || str[i] == '=')
                    break;
            }
            if (j == i || i >= len)
                return tag;

            string name = str.Substring(j, i - j);
            j = i;
            while (i < len && str[i] != '>')
                i++;

            if (!TagNames.TryGetValue(name, out tag))
            {
                // An unknown name: TazUO looks through the whole text, not just this tag.
                tag = Tag.None;
                if (str.IndexOf("bodybgcolor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tag = Tag.BodyBgColor;
                    j = str.IndexOf("bgcolor", StringComparison.OrdinalIgnoreCase);
                    endTag = false;
                }
                else if (str.IndexOf("basefont", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tag = Tag.BaseFont;
                    j = str.IndexOf("color", StringComparison.OrdinalIgnoreCase);
                    endTag = false;
                }
                else if (str.IndexOf("bodytext", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tag = Tag.Body;
                    j = str.IndexOf("text", StringComparison.OrdinalIgnoreCase);
                    endTag = false;
                }
            }

            if (!endTag)
            {
                InfoFromTag(tag, ref info);
                if (i < len && j != i)
                {
                    switch (tag)
                    {
                        case Tag.BodyBgColor:
                        case Tag.Body:
                        case Tag.BaseFont:
                        case Tag.A:
                        case Tag.Div:
                        case Tag.P:
                        case Tag.Color:
                            int cmdLen = i - j;
                            if (cmdLen >= 0 && j >= 0 && str.Length > j)
                                InfoFromContent(ref info, str.Substring(j, cmdLen));
                            break;
                    }
                }
            }
            return tag;
        }

        // GetHTMLInfoFromTag, :3327-3423.
        private static void InfoFromTag(Tag tag, ref HtmlInfo info)
        {
            info.Tag = tag;
            info.Align = UniTextAlign.Left;
            info.Flags = 0;
            info.Font = 0xFF;
            info.Color = 0;
            info.Link = 0;
            switch (tag)
            {
                case Tag.B: info.Flags = Solid; break;
                case Tag.I: info.Flags = Italic; break;
                case Tag.U: info.Flags = Underline; break;
                case Tag.P: info.Flags = Indention; break;
                case Tag.Big: info.Font = 0; break;
                case Tag.Small: info.Font = 2; break;
                case Tag.H1: info.Flags = Solid | Underline; info.Font = 0; break;
                case Tag.H2: info.Flags = Solid; info.Font = 0; break;
                case Tag.H3: info.Font = 0; break;
                case Tag.H4: info.Flags = Solid; info.Font = 2; break;
                case Tag.H5: info.Flags = Italic; info.Font = 2; break;
                case Tag.H6: info.Font = 2; break;
                case Tag.Bq: info.Flags = Bq; info.Color = 0xFF008000; break;
                case Tag.Left: info.Align = UniTextAlign.Left; break;
                case Tag.Center: info.Align = UniTextAlign.Center; break;
                case Tag.Right: info.Align = UniTextAlign.Right; break;
            }
        }

        private static void TrimStart(string s, ref int at)
        {
            while (at < s.Length && char.IsWhiteSpace(s[at]))
                at++;
        }

        // GetHTMLInfoFromContent, :2968-3177: name=value pairs, values bare or "quoted".
        private void InfoFromContent(ref HtmlInfo info, string content)
        {
            int at = 0;
            while (at < content.Length)
            {
                TrimStart(content, ref at);
                if (at >= content.Length)
                    break;

                int k = at;
                while (k < content.Length && !char.IsWhiteSpace(content[k]) && content[k] != '=' && content[k] != '\\')
                    k++;
                if (k >= content.Length || k == at)
                    break; // no stop char after the name (or no name): TazUO finds no command
                string command = content.Substring(at, k - at);
                at = k;
                TrimStart(content, ref at);

                string value = "";
                if (at < content.Length && content[at] == '=')
                {
                    at++;
                    TrimStart(content, ref at);
                    if (at < content.Length && content[at] == '"')
                    {
                        int end = content.IndexOf('"', at + 1);
                        if (end > at)
                        {
                            value = content.Substring(at + 1, end - at - 1);
                            at = end + 1;
                        }
                    }
                    else
                    {
                        k = at;
                        while (k < content.Length && !char.IsWhiteSpace(content[k]) && content[k] != '\\' && content[k] != '<'
                               && content[k] != '>' && content[k] != '=')
                            k++;
                        value = content.Substring(at, k - at);
                        at = k;
                    }
                }

                switch (info.Tag)
                {
                    case Tag.Body:
                    case Tag.BodyBgColor:
                        if (Is(command, "text"))
                            info.Color = ReadColor(value);
                        else if (Is(command, "bgcolor"))
                        {
                            if (_backgroundColored)
                                _backgroundColor = ReadColor(value);
                        }
                        else if (Is(command, "link"))
                            _linkColor = ReadColor(value);
                        else if (Is(command, "vlink"))
                            _visitedLinkColor = ReadColor(value);
                        else if (Is(command, "leftmargin"))
                            _marginX = ParseInt(value);
                        else if (Is(command, "topmargin"))
                            _marginY = ParseInt(value);
                        else if (Is(command, "rightmargin"))
                            _marginWidth = ParseInt(value);
                        else if (Is(command, "bottommargin"))
                            _marginHeight = ParseInt(value);
                        break;

                    case Tag.BaseFont:
                        if (Is(command, "color"))
                            info.Color = ReadColor(value);
                        else if (Is(command, "size"))
                        {
                            if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte size))
                                info.Font = (byte)(Is(value, "big") ? 4 : Is(value, "small") ? 0 : 1);
                            else
                                info.Font = (byte)(size == 0 || size == 4 ? 1 : size < 4 ? 2 : 0);
                        }
                        break;

                    case Tag.A:
                        if (Is(command, "href"))
                        {
                            info.Flags = Underline;
                            info.Color = _linkColor;
                            info.Link = LinkId(value);
                        }
                        break;

                    case Tag.Color:
                        if ((command.Length == 0 || Is(command, "color")) && value.Length != 0)
                            info.Color = ReadColor(value);
                        break;

                    case Tag.P:
                    case Tag.Div:
                        if (Is(command, "align"))
                        {
                            if (Is(value, "left"))
                                info.Align = UniTextAlign.Left;
                            else if (Is(value, "center"))
                                info.Align = UniTextAlign.Center;
                            else if (Is(value, "right"))
                                info.Align = UniTextAlign.Right;
                        }
                        break;
                }
                TrimStart(content, ref at);
            }
        }

        private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static int ParseInt(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        // GetWebLinkID, :3179-3206: one id per address, from 1. Links are never visited here.
        private ushort LinkId(string link)
        {
            int i = _links.IndexOf(link);
            if (i < 0)
            {
                _links.Add(link);
                i = _links.Count - 1;
            }
            return (ushort)(i + 1);
        }

        /// <summary>
        /// ReadColorFromTextBuffer (:3220-3325) as 0xAARRGGBB: "#RRGGBB" (or "#0xRRGGBB") opaque; a value starting with a
        /// digit is TazUO's packed RGBA hex; names from its table; anything else 0 (no colour).
        /// </summary>
        public static uint ReadColor(string buffer)
        {
            if (string.IsNullOrEmpty(buffer))
                return 0;
            if (buffer[0] == '#')
            {
                if (buffer.Length <= 1)
                    return 0;
                int start = buffer.Length > 3 && buffer[1] == '0' && buffer[2] == 'x' ? 3 : 1;
                uint.TryParse(buffer.Substring(start), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint cc);
                return 0xFF000000 | (cc & 0xFFFFFF);
            }
            if (char.IsNumber(buffer[0]))
            {
                string hex = buffer.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? buffer.Substring(2) : buffer;
                return uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgba) ? FromRgba(rgba) : 0;
            }
            switch (buffer.ToLowerInvariant())
            {
                case "red": return 0xFFFF0000;
                case "cyan": return 0xFF00FFFF;
                case "blue": return 0xFF0000FF;
                case "darkblue": return 0xFF0000A0;
                case "lightblue": return 0xFFADD8E6;
                case "purple": return 0xFF800080;
                case "yellow": return 0xFFFFFF00;
                case "lime": return 0xFF00FF00;
                case "magenta": return 0xFFFF00FF;
                case "white": return 0xFFFEFEFF;
                case "silver": return 0xFFC0C0C0;
                case "grey":
                case "gray": return 0xFF808080;
                case "black": return 0xFF010101;
                case "orange": return 0xFFFFA500;
                case "brown": return 0xFFA52A2A;
                case "maroon": return 0xFF800000;
                case "green": return 0xFF008000;
                case "olive": return 0xFF808000;
                default: return 0;
            }
        }

        /// <summary>TazUO's packed RGBA (0xBBGGRRAA, as RgbaToArgb takes it) as 0xAARRGGBB.</summary>
        public static uint FromRgba(uint rgba) =>
            ((rgba & 0xFF) << 24) | (((rgba >> 8) & 0xFF) << 16) | (((rgba >> 16) & 0xFF) << 8) | (rgba >> 24);
    }
}
