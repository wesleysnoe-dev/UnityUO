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

            var sb = new StringBuilder(html.Length);
            int last = 0;
            bool center = false;
            // What each open font tag added (color and/or size), closed in reverse when the tag closes.
            var open = new System.Collections.Generic.Stack<string>();
            foreach (Match m in Tag.Matches(html))
            {
                sb.Append(Escape(html.Substring(last, m.Index - last)));
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
                        sb.Append(closing ? $"</{name}>" : $"<{name}>");
                        break;
                    case "basefont":
                    case "font":
                        if (closing)
                        {
                            if (open.Count > 0)
                                sb.Append(open.Pop());
                        }
                        else
                        {
                            string closeWith = "";
                            Match c = ColorAttr.Match(m.Groups[3].Value);
                            if (c.Success)
                            {
                                sb.Append("<color=").Append(ColorValue(c.Groups[1].Value)).Append('>');
                                closeWith = "</color>";
                            }
                            Match s = SizeAttr.Match(m.Groups[3].Value);
                            if (s.Success && int.TryParse(s.Groups[1].Value, out int size) && size >= 1 && size <= 7)
                            {
                                sb.Append("<size=").Append(SizePixels[size]).Append('>');
                                closeWith = "</size>" + closeWith;
                            }
                            open.Push(closeWith);
                        }
                        break;
                }
            }
            sb.Append(Escape(html.Substring(last)));
            while (open.Count > 0)
                sb.Append(open.Pop());

            centered = center;
            return sb.ToString().Replace("&nbsp;", " ").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
        }

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
