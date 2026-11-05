using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// Converts the small HTML dialect UO gumps use (br, basefont/font color, center, b, i, a, div...)
    /// into IMGUI rich text. Unknown tags are dropped; colors are balanced.
    /// </summary>
    public static class GumpHtml
    {
        private static readonly Regex Tag = new Regex(@"<\s*(/?)\s*([a-zA-Z]+)([^>]*)>", RegexOptions.Compiled);
        // Shards write tags and attributes in any case (<BASEFONT COLOR=#F4F4F4 SIZE=5>).
        private static readonly Regex ColorAttr = new Regex(@"color\s*=\s*[""']?(#?[0-9a-fA-F]{6}|#?[0-9a-fA-F]{3}|[a-zA-Z]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SizeAttr = new Regex(@"size\s*=\s*[""']?\+?(\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AlignAttr = new Regex(@"align\s*=\s*[""']?(center|right)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // HTML font sizes 1-7 as pixel sizes, around the gump text size (size 3 = normal).
        private static readonly int[] SizePixels = { 0, 10, 12, 14, 16, 18, 21, 24 };

        public static string ToRichText(string html, out bool centered)
        {
            centered = false;
            if (string.IsNullOrEmpty(html))
                return "";

            var sb = new StringBuilder(html.Length + 32);
            int last = 0;
            bool center = false;
            // The open tags, as TazUO's GetHTMLData stacks them: a closing tag takes away the last open one of its
            // kind, and each character takes its look from everything still open (GetCurrentHTMLInfo). So a <B> the
            // server never closes stays bold to the end, and misnested tags don't leave stray markup.
            var open = new List<(string Name, string Color, int Size)>();
            foreach (Match m in Tag.Matches(html))
            {
                AppendRun(sb, html.Substring(last, m.Index - last), open);
                last = m.Index + m.Length;
                bool closing = m.Groups[1].Value == "/";
                string name = m.Groups[2].Value.ToLowerInvariant();
                switch (name)
                {
                    case "br":
                    case "p":
                        sb.Append('\n');
                        break;
                    case "center":
                        if (!closing)
                            center = true;
                        break;
                    case "div":
                        if (!closing && AlignAttr.IsMatch(m.Groups[3].Value))
                            center = true;
                        break;
                    case "b":
                    case "i":
                    case "basefont":
                    case "font":
                    {
                        string kind = name == "font" ? "basefont" : name;
                        if (closing)
                        {
                            for (int k = open.Count - 1; k >= 0; k--)
                                if (open[k].Name == kind)
                                {
                                    open.RemoveAt(k);
                                    break;
                                }
                            break;
                        }
                        string color = null;
                        int size = 0;
                        if (kind == "basefont")
                        {
                            Match c = ColorAttr.Match(m.Groups[3].Value);
                            if (c.Success)
                                color = ColorValue(c.Groups[1].Value);
                            Match s = SizeAttr.Match(m.Groups[3].Value);
                            if (s.Success && int.TryParse(s.Groups[1].Value, out int n) && n >= 1 && n <= 7)
                                size = SizePixels[n];
                        }
                        open.Add((kind, color, size));
                        break;
                    }
                }
            }
            AppendRun(sb, html.Substring(last), open);

            centered = center;
            return sb.ToString();
        }

        /// <summary>Text between tags, wrapped in balanced rich-text tags for what is open around it.</summary>
        private static void AppendRun(StringBuilder sb, string text, List<(string Name, string Color, int Size)> open)
        {
            if (text.Length == 0)
                return;
            bool bold = false, italic = false;
            string color = null;
            int size = 0;
            foreach (var (name, c, s) in open)
            {
                bold |= name == "b";
                italic |= name == "i";
                if (c != null)
                    color = c;
                if (s != 0)
                    size = s;
            }
            if (size != 0) sb.Append("<size=").Append(size).Append('>');
            if (color != null) sb.Append("<color=").Append(color).Append('>');
            if (bold) sb.Append("<b>");
            if (italic) sb.Append("<i>");
            sb.Append(Escape(Decode(text)));
            if (italic) sb.Append("</i>");
            if (bold) sb.Append("</b>");
            if (color != null) sb.Append("</color>");
            if (size != 0) sb.Append("</size>");
        }

        // The entities shards write in text (ModernUO's EscapeHtml for a player's words). Decoded before Escape, so an
        // escaped "<b>" in a chat line shows as text instead of turning into rich-text markup.
        private static string Decode(string text) =>
            text.Replace("&nbsp;", " ").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

        // Rich text treats '<' as markup; plain text keeps angle brackets visible via a look-alike.
        private static string Escape(string text) => text.Replace("<", "‹").Replace(">", "›");

        private static string ColorValue(string v)
        {
            if (v.StartsWith("#"))
                return v.Length == 4 ? $"#{v[1]}{v[1]}{v[2]}{v[2]}{v[3]}{v[3]}" : v;
            if (Regex.IsMatch(v, "^[0-9a-fA-F]{6}$"))
                return "#" + v;
            switch (v.ToLowerInvariant())
            {
                case "white": return "#FFFFFF";
                case "black": return "#000000";
                case "red": return "#FF4040";
                case "green": return "#40FF40";
                case "blue": return "#6080FF";
                case "yellow": return "#FFFF40";
                case "orange": return "#FFA040";
                case "gray":
                case "grey": return "#A0A0A0";
                default: return "#FFFFFF";
            }
        }
    }
}
