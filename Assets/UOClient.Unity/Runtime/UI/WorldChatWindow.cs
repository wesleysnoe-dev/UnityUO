using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    public enum ChatChannel { All, Global, Trade, Faction, Guild, Whisper, News, Other }

    /// <summary>
    /// Every journal line since login, sorted into the shard's chat channels the way the UO Smallz client's
    /// WorldChatGump does: Global, Trade and Whisper are "encoded" speech (type 0xC0) named "[G] Name",
    /// "[T] Name", "[W] Name"; Faction is alliance speech, Guild is guild speech, News is a system line named "News".
    /// Kept outside the window so closing and reopening it loses nothing.
    /// </summary>
    public sealed class ChatLog
    {
        public const int Capacity = 1000;
        public readonly List<(JournalEntry Entry, ChatChannel Channel)> Lines = new List<(JournalEntry, ChatChannel)>();
        public readonly int[] Unread = new int[7];
        public int Version { get; private set; }

        public void Add(JournalEntry e)
        {
            ChatChannel c = ChannelOf(e);
            Lines.Add((e, c));
            if (Lines.Count > Capacity)
                Lines.RemoveAt(0);
            if (c != ChatChannel.Other)
                Unread[(int)c]++;
            Version++;
        }

        public static ChatChannel ChannelOf(JournalEntry e)
        {
            string n = e.Name ?? "";
            switch (e.Type)
            {
                case 0xC0: // encoded
                    if (n.StartsWith("[G]", StringComparison.Ordinal)) return ChatChannel.Global;
                    if (n.StartsWith("[T]", StringComparison.Ordinal)) return ChatChannel.Trade;
                    if (n.StartsWith("[W]", StringComparison.Ordinal)) return ChatChannel.Whisper;
                    return ChatChannel.Other;
                case 14: return ChatChannel.Faction; // alliance
                case 13: return ChatChannel.Guild;
                default:
                    return string.Equals(n, "News", StringComparison.OrdinalIgnoreCase) && (e.Serial == 0xFFFFFFFF || e.Serial == 0)
                        ? ChatChannel.News : ChatChannel.Other;
            }
        }

        /// <summary>Who said a chat line: "[G] Bob" -> Bob, "[W] to Bob" -> Bob, "[Guild][Bob]" -> Bob.</summary>
        public static string SenderOf(string name, ChatChannel channel)
        {
            if (string.IsNullOrEmpty(name) || channel == ChatChannel.News || channel == ChatChannel.Other || channel == ChatChannel.All)
                return null;
            if (channel == ChatChannel.Faction || channel == ChatChannel.Guild)
            {
                int open = name.LastIndexOf('['), close = name.LastIndexOf(']');
                return open >= 0 && close > open + 1 ? name.Substring(open + 1, close - open - 1).Trim() : name.Trim();
            }
            int cut = name.IndexOf("] ", StringComparison.Ordinal);
            string who = cut >= 0 ? name.Substring(cut + 2).Trim() : name.Trim();
            if (who.StartsWith("to ", StringComparison.Ordinal))
                who = who.Substring(3).Trim();
            return who.Length > 0 ? who : null;
        }
    }

    /// <summary>
    /// The shard's chat window (the top bar's Chat): tabs per channel with unread counts, the lines of the
    /// active tab, a Join / View / Fill button on lines that offer something, and a message box that sends
    /// to the tab's channel ([g, [t, [f, [gu, [w Name; plain speech on All; News is read only).
    /// Double-click a line to whisper whoever said it.
    /// </summary>
    public sealed class WorldChatWindow : UiWindow
    {
        private static readonly (ChatChannel Channel, string Name)[] Tabs =
        {
            (ChatChannel.All, "All"), (ChatChannel.Global, "Global"), (ChatChannel.Trade, "Trade"),
            (ChatChannel.Faction, "Faction"), (ChatChannel.Guild, "Guild"), (ChatChannel.Whisper, "Whispers"), (ChatChannel.News, "News")
        };

        private readonly ChatLog _log;
        private readonly Func<uint, Color> _hueColor;
        private int _active;
        private string _text = "", _whisperTo;
        private Vector2 _scroll;
        private int _seenVersion = -1;
        private bool _stickToBottom = true;
        private float _lastClick;
        private int _lastClickRow = -1;
        private GUIStyle _line, _tab, _link;

        public WorldChatWindow(ChatLog log, Func<uint, Color> hueColor)
        {
            _log = log;
            _hueColor = hueColor;
            Rect = new Rect(120, 140, 600, 330);
        }

        public override string Key => "worldchat";

        private ChatChannel Active => Tabs[_active].Channel;

        public override void Draw()
        {
            EnsureStyles();
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            GUI.color = new Color(0, 0, 0, 0.45f);
            GUI.DrawTexture(new Rect(8, 36, Rect.width - 16, Rect.height - 76), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Tabs, with the count of lines that came in while another tab was showing.
            float tw = (Rect.width - 16) / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                int unread = i == 0 ? 0 : _log.Unread[(int)Tabs[i].Channel];
                string label = unread > 0 && i != _active ? $"{Tabs[i].Name} ({unread})" : Tabs[i].Name;
                bool on = GUI.Toggle(new Rect(8 + i * tw, 8, tw - 2, 24), i == _active, label, _tab);
                if (on && i != _active)
                {
                    _active = i;
                    _stickToBottom = true;
                }
            }
            if (_active > 0)
                _log.Unread[(int)Active] = 0;

            // The lines of this tab.
            var rows = new List<(JournalEntry Entry, ChatChannel Channel)>();
            foreach (var l in _log.Lines)
                if (Active == ChatChannel.All || l.Channel == Active)
                    rows.Add(l);
            var view = new Rect(8, 36, Rect.width - 16, Rect.height - 76);
            float width = view.width - 22, y = 0;
            var heights = new float[rows.Count];
            var contents = new GUIContent[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                contents[i] = new GUIContent(Format(rows[i].Entry, rows[i].Channel));
                heights[i] = _line.CalcHeight(contents[i], width - (rows[i].Entry.Link != null ? 56 : 0));
                y += heights[i];
            }
            if (_seenVersion != _log.Version && _stickToBottom)
                _scroll.y = float.MaxValue;
            _seenVersion = _log.Version;
            Vector2 before = _scroll;
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, width, Mathf.Max(y, view.height - 4)));
            y = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                JournalEntry e = rows[i].Entry;
                var r = new Rect(4, y, width - (e.Link != null ? 56 : 0), heights[i]);
                GUI.Label(r, contents[i], _line);
                if (e.Link != null && GUI.Button(new Rect(width - 52, y, 50, 18), "[" + LinkLabel(e.Link) + "]", _link))
                    Session.Say("[" + e.Link);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(Event.current.mousePosition))
                {
                    if (_lastClickRow == i && Time.realtimeSinceStartup - _lastClick < 0.4f)
                    {
                        string who = ChatLog.SenderOf(e.Name, rows[i].Channel);
                        if (who != null && who != Session.World.Player?.Name)
                        {
                            _whisperTo = who;
                            _active = Array.FindIndex(Tabs, t => t.Channel == ChatChannel.Whisper);
                        }
                    }
                    _lastClick = Time.realtimeSinceStartup;
                    _lastClickRow = i;
                }
                y += heights[i];
            }
            GUI.EndScrollView();
            if (_scroll.y != before.y)
                _stickToBottom = _scroll.y >= y - view.height - 2;

            DrawInput();
        }

        private void DrawInput()
        {
            float y = Rect.height - 34;
            if (Active == ChatChannel.News)
            {
                Text(new Rect(12, y + 4, 300, 20), "News is read only", new Color(0.75f, 0.72f, 0.62f), 13);
                return;
            }
            string prompt = Active switch
            {
                ChatChannel.Global => "Global:",
                ChatChannel.Trade => "Trade:",
                ChatChannel.Faction => "Faction:",
                ChatChannel.Guild => "Guild:",
                ChatChannel.Whisper => _whisperTo != null ? $"To {_whisperTo}:" : "Whisper (name, then message):",
                _ => "Say:"
            };
            float pw = Mathf.Min(220, _line.CalcSize(new GUIContent(prompt)).x + 8);
            Text(new Rect(12, y + 4, pw, 20), prompt, new Color(1f, 0.85f, 0.45f), 13);
            float x = 12 + pw;
            if (Active == ChatChannel.Whisper && _whisperTo != null)
            {
                if (GUI.Button(new Rect(x, y + 3, 20, 20), "x"))
                    _whisperTo = null;
                x += 24;
            }
            GUI.SetNextControlName("worldchat-input");
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                         && GUI.GetNameOfFocusedControl() == "worldchat-input";
            _text = GUI.TextField(new Rect(x, y + 2, Rect.width - x - 12, 24), _text, 200);
            if (enter)
            {
                Send(_text.Trim());
                _text = "";
                Event.current.Use();
            }
        }

        private void Send(string text)
        {
            if (text.Length == 0)
                return;
            string line = Active switch
            {
                ChatChannel.Global => "[g " + text,
                ChatChannel.Trade => "[t " + text,
                ChatChannel.Faction => "[f " + text,
                ChatChannel.Guild => "[gu " + text,
                ChatChannel.Whisper => _whisperTo != null ? $"[w {_whisperTo} {text}" : "[w " + text,
                _ => text // All: said out loud
            };
            Session.Say(line);
            _stickToBottom = true;
        }

        private string Format(JournalEntry e, ChatChannel c)
        {
            string time = e.Time.ToString("HH:mm");
            string who = c == ChatChannel.Other || c == ChatChannel.All ? e.Speaker : e.Name;
            string color = ColorUtility.ToHtmlStringRGB(Readable(_hueColor(e.Hue)));
            string text = Escape(e.Text);
            return string.IsNullOrEmpty(who)
                ? $"<color=#8a8270>{time}</color>  <color=#{color}>{text}</color>"
                : $"<color=#8a8270>{time}</color>  <color=#e8c070>{Escape(who)}:</color> <color=#{color}>{text}</color>";
        }

        /// <summary>Speech hues meant for the world can be dark; on the chat's dark background they are lifted, keeping their tint.</summary>
        private static Color Readable(Color c)
        {
            float luminance = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            return luminance >= 0.55f ? c : Color.Lerp(c, Color.white, (0.55f - luminance) / 0.55f * 0.8f);
        }

        private static string Escape(string s) => s?.Replace("<", "<​") ?? ""; // keep rich text from reading speech as tags

        private static string LinkLabel(string link) =>
            link.StartsWith("join", StringComparison.Ordinal) ? "Join"
            : link.StartsWith("view", StringComparison.Ordinal) ? "View"
            : link.StartsWith("fill", StringComparison.Ordinal) ? "Fill" : "Open";

        private void EnsureStyles()
        {
            if (_line != null)
                return;
            _line = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 13, padding = new RectOffset(2, 2, 1, 1) };
            _line.normal.textColor = new Color(0.92f, 0.9f, 0.84f);
            _tab = new GUIStyle(GUI.skin.button) { fontSize = 12 };
            _link = new GUIStyle(GUI.skin.button) { fontSize = 11, padding = new RectOffset(1, 1, 1, 1) };
            _link.normal.textColor = new Color(0.55f, 0.85f, 1f);
        }
    }
}
