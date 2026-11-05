using System;
using System.Collections.Generic;
using System.Text;

namespace UOClient.Core
{
    /// <summary>One layout command of a server gump, e.g. {button 10 20 4005 4007 1 0 3}.</summary>
    public sealed class GumpCommand
    {
        public string Name;

        /// <summary>Numeric arguments in order (non-numeric ones are kept in <see cref="Words"/>).</summary>
        public int[] Args;

        /// <summary>All arguments as written, for commands with text (xmfhtmltok @args@, gumppic hue=N).</summary>
        public string[] Words;

        public int Arg(int i, int fallback = 0) => i < Args.Length ? Args[i] : fallback;

        /// <summary>Value of a key=value word (gumppic x y id hue=33).</summary>
        public int Named(string key, int fallback = 0)
        {
            foreach (string w in Words)
                if (w.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase) && int.TryParse(w.Substring(key.Length + 1), out int v))
                    return v;
            return fallback;
        }

        /// <summary>The @...@ cliloc arguments (xmfhtmltok, tooltip) without the @s, or null if there are none.</summary>
        public string ClilocArgs
        {
            get
            {
                foreach (string w in Words)
                    if (w.Length >= 2 && w[0] == '@' && w[w.Length - 1] == '@')
                        return w.Substring(1, w.Length - 2);
                return null;
            }
        }

        /// <summary>
        /// Characters a text entry takes: textentrylimited's size (x y w h hue id text size) when set, else 239,
        /// ModernUO's cap on a reply text. ModernUO writes size 0 by default, which means no limit of its own.
        /// </summary>
        public int TextLimit => Name == "textentrylimited" && Arg(7) > 0 ? Math.Min(Arg(7), MaxReplyText) : MaxReplyText;

        public const int MaxReplyText = 239;

        public override string ToString() => "{" + Name + " " + string.Join(" ", Words) + "}";
    }

    /// <summary>A gump (window) the server sent: position, layout commands and text lines.</summary>
    public sealed class ServerGump
    {
        public uint Serial;
        public uint TypeId;
        public int X, Y;
        public List<GumpCommand> Commands = new List<GumpCommand>();
        public string[] Lines = Array.Empty<string>();

        public bool NoClose, NoMove, NoDispose, NoResize;

        public string Line(int index) => index >= 0 && index < Lines.Length ? Lines[index] : "";

        /// <summary>Parses a layout string such as "{page 0}{resizepic 0 0 9200 300 200}{text 20 20 0 0}".</summary>
        public static List<GumpCommand> ParseLayout(string layout)
        {
            var commands = new List<GumpCommand>();
            int i = 0;
            while (i < layout.Length)
            {
                int open = layout.IndexOf('{', i);
                if (open < 0)
                    break;
                int close = FindClose(layout, open + 1);
                if (close < 0)
                    break;
                string body = layout.Substring(open + 1, close - open - 1).Trim();
                i = close + 1;
                if (body.Length == 0)
                    continue;

                List<string> words = Split(body);
                if (words.Count == 0)
                    continue;
                var args = new List<int>();
                for (int w = 1; w < words.Count; w++)
                    if (int.TryParse(words[w], out int v))
                        args.Add(v);
                commands.Add(new GumpCommand
                {
                    Name = words[0].ToLowerInvariant(),
                    Args = args.ToArray(),
                    Words = words.GetRange(1, words.Count - 1).ToArray()
                });
            }
            return commands;
        }

        /// <summary>Matching '}' that is not inside an @...@ argument block.</summary>
        private static int FindClose(string s, int from)
        {
            bool inArgs = false;
            for (int i = from; i < s.Length; i++)
            {
                if (s[i] == '@')
                    inArgs = !inArgs;
                else if (s[i] == '}' && !inArgs)
                    return i;
            }
            return -1;
        }

        /// <summary>Splits on whitespace, keeping @...@ blocks (cliloc arguments) as single words.</summary>
        private static List<string> Split(string body)
        {
            var words = new List<string>();
            var sb = new StringBuilder();
            bool inArgs = false;
            foreach (char c in body)
            {
                if (c == '@')
                    inArgs = !inArgs;
                if (!inArgs && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        words.Add(sb.ToString());
                        sb.Clear();
                    }
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0)
                words.Add(sb.ToString());
            return words;
        }

        internal void ApplyFlags()
        {
            foreach (GumpCommand c in Commands)
            {
                switch (c.Name)
                {
                    case "noclose": NoClose = true; break;
                    case "nomove": NoMove = true; break;
                    case "nodispose": NoDispose = true; break;
                    case "noresize": NoResize = true; break;
                }
            }
        }
    }

    /// <summary>An item inside a container (backpack, bank box, chest).</summary>
    public sealed class ContainedItem
    {
        public uint Serial;
        public uint Container;
        public ushort Graphic;
        public int Amount;
        public int X, Y;
        public ushort Hue;
    }

    /// <summary>A container the server told us to show.</summary>
    public sealed class OpenContainer
    {
        public uint Serial;
        public ushort Gump;
    }
}
