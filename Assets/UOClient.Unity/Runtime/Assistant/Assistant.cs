using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UOClient.Core;
using UOClient.IO;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// The assistant (TazUO's Legion Assistant): the tools that act for the player (auto loot, scavenger, dress agent,
    /// organizer, healing...). Each tool is an <see cref="AssistantAgent"/> in a file of its own, listed in
    /// <see cref="Create"/>; this class gives them the session, the game data, a place to save their settings
    /// (UserSettings/assistant/NAME.json, one file each) and a tick every frame, and the window (AssistantWindow) draws
    /// each agent's own tab. One lives per session.
    /// </summary>
    public sealed class AssistantHost
    {
        public GameSession Session { get; private set; }
        public UOData Data { get; private set; }
        public UI.UiManager Ui { get; private set; }
        public readonly List<AssistantAgent> Agents = new List<AssistantAgent>();

        /// <summary>A line for the player (the journal's system text).</summary>
        public Action<string> Log = _ => { };

        private static string Folder => Path.Combine(ClientPaths.ProjectRoot, "UserSettings", "assistant");

        /// <summary>Every agent, in the window's order. A new tool is one line here and one file.</summary>
        private static IEnumerable<AssistantAgent> Create()
        {
            yield return new FriendsAgent();
            yield return new AutoLootAgent();
            yield return new ScavengerAgent();
            yield return new OrganizerAgent();
            yield return new DressAgent();
            yield return new AutoBuyAgent();
            yield return new AutoSellAgent();
            yield return new BandageAgent();
            yield return new SelfHealAgent();
            yield return new StatLockAgent();
            yield return new JournalFilterAgent();
            yield return new SoundFilterAgent();
            yield return new MusicFilterAgent();
            yield return new GraphicFilterAgent();
            yield return new SeasonFilterAgent();
        }

        /// <summary>Self-tests only: the host of the session now running.</summary>
        public static AssistantHost Latest { get; private set; }

        public void Attach(GameSession session, UOData data, UI.UiManager ui, Action<string> log)
        {
            Latest = this;
            Session = session;
            Data = data;
            Ui = ui;
            Log = log ?? (_ => { });
            foreach (AssistantAgent a in Create())
            {
                a.Host = this;
                Agents.Add(a);
                try { a.Attach(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public void Detach()
        {
            foreach (AssistantAgent a in Agents)
            {
                try { a.Detach(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            Agents.Clear();
            Session = null;
        }

        /// <summary>Once a frame while in the world.</summary>
        public void Tick()
        {
            if (Session == null || Session.State != SessionState.InWorld || Session.World.Player == null)
                return;
            float now = Time.unscaledTime;
            foreach (AssistantAgent a in Agents)
            {
                try { a.Tick(now); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ---- what agents share ----

        /// <summary>Our backpack's serial, or 0.</summary>
        public uint Backpack => Session.World.Worn(Session.World.Player, 21);

        /// <summary>The items directly in a container, as the server last told us.</summary>
        public IEnumerable<ContainedItem> ItemsIn(uint container)
        {
            foreach (ContainedItem c in Session.World.Contained.Values)
                if (c.Container == container)
                    yield return c;
        }

        /// <summary>How many of an item (by graphic and hue; -1 = any hue) a container holds, stacks counted by amount.</summary>
        public int CountIn(uint container, int graphic, int hue)
        {
            int n = 0;
            foreach (ContainedItem c in ItemsIn(container))
                if (c.Graphic == graphic && (hue < 0 || c.Hue == hue))
                    n += Math.Max(1, c.Amount);
            return n;
        }

        /// <summary>An item's name: its tooltip's first line when we have it, else tiledata's.</summary>
        public string ItemName(uint serial, int graphic)
        {
            if (Session.Properties.TryGetValue(serial, out PropertyList p) && p.Lines.Count > 0 && !string.IsNullOrWhiteSpace(p.Lines[0]))
                return StripTags(p.Lines[0]);
            string name = Data?.TileData.GetStatic(graphic).Name;
            return string.IsNullOrWhiteSpace(name) ? $"0x{graphic:X4}" : name.Trim();
        }

        /// <summary>An item's name and tooltip as one text for rules that match on words (asking the server for it if we lack it).</summary>
        public string ItemText(uint serial, int graphic)
        {
            PropertyList p = Session.GetProperties(serial);
            string name = Data?.TileData.GetStatic(graphic).Name ?? "";
            return p == null ? name : name + "\n" + StripTags(p.Text);
        }

        private static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]*>", "").Trim();

        /// <summary>
        /// Where a serial is: a ground item or mobile's tile, or null (inside something, or unknown). Agents use it for
        /// reach (a corpse within 2 tiles).
        /// </summary>
        public (int X, int Y)? TileOf(uint serial)
        {
            if (Session.World.Items.TryGetValue(serial, out WorldItem i))
                return (i.X, i.Y);
            if (Session.World.Mobiles.TryGetValue(serial, out Mobile m))
                return (m.X, m.Y);
            return null;
        }

        // ---- settings ----

        public T Load<T>(string name) where T : class, new()
        {
            try
            {
                string path = Path.Combine(Folder, name + ".json");
                if (File.Exists(path))
                    return JsonUtility.FromJson<T>(File.ReadAllText(path)) ?? new T();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient assistant: could not read {name}.json ({e.Message}); starting with defaults.");
            }
            return new T();
        }

        public void Save(string name, object settings)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, name + ".json"), temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(settings, true));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient assistant: could not save {name}.json ({e.Message}).");
            }
        }
    }

    /// <summary>
    /// One assistant tool. It keeps its own settings (loaded in <see cref="Attach"/>, saved when its tab changes them),
    /// watches the session in <see cref="Attach"/> / <see cref="Tick"/>, and draws its tab with GUILayout
    /// (<see cref="AssistantGui"/> has the shared pieces). Everything it does goes through <see cref="Host"/>.
    /// </summary>
    public abstract class AssistantAgent
    {
        public AssistantHost Host;

        /// <summary>The tab's name.</summary>
        public abstract string Name { get; }

        /// <summary>The heading it is listed under in the window: "Agents", "General" or "Filters".</summary>
        public virtual string Group => "Agents";

        /// <summary>Whether the tool is switched on (shown as a mark beside its name).</summary>
        public virtual bool Active => false;

        public virtual void Attach() { }
        public virtual void Detach() { }
        public virtual void Tick(float now) { }

        /// <summary>The tab, inside a GUILayout area <paramref name="width"/> wide (the window scrolls it).</summary>
        public abstract void DrawTab(float width);
    }

    /// <summary>The pieces the agents' tabs are made of (GUILayout, in the window's IronSkin).</summary>
    public static class AssistantGui
    {
        private static GUIStyle _head, _note, _cell;

        private static void Ensure()
        {
            if (_head != null)
                return;
            _head = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _head.normal.textColor = UI.PaperdollSkin.Gold;
            _note = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _note.normal.textColor = UI.PaperdollSkin.GoldFaded;
            _cell = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        }

        public static void Header(string text)
        {
            Ensure();
            GUILayout.Space(6);
            GUILayout.Label(text, _head);
        }

        public static void Note(string text)
        {
            Ensure();
            GUILayout.Label(text, _note);
        }

        public static void Cell(string text, float width)
        {
            Ensure();
            GUILayout.Label(text, _cell, GUILayout.Width(width), GUILayout.Height(22));
        }

        /// <summary>A checkbox; true when it changed.</summary>
        public static bool Toggle(ref bool value, string label, float width = 0)
        {
            bool now = width > 0 ? GUILayout.Toggle(value, " " + label, GUILayout.Width(width)) : GUILayout.Toggle(value, " " + label);
            if (now == value)
                return false;
            value = now;
            return true;
        }

        /// <summary>A whole-number box; true when it changed. An empty or bad entry is <paramref name="empty"/>.</summary>
        public static bool Int(ref int value, float width, int min, int max, int empty = 0, string emptyText = "")
        {
            string was = value == empty && emptyText != null ? emptyText : value.ToString();
            string typed = GUILayout.TextField(was, 8, GUILayout.Width(width));
            if (typed == was)
                return false;
            int parsed = empty;
            if (!string.IsNullOrWhiteSpace(typed) && typed != emptyText && !int.TryParse(typed.Trim(), out parsed))
                return false;
            parsed = string.IsNullOrWhiteSpace(typed) ? empty : parsed == empty ? empty : Mathf.Clamp(parsed, min, max);
            if (parsed == value)
                return false;
            value = parsed;
            return true;
        }

        /// <summary>A text box; true when it changed.</summary>
        public static bool Text(ref string value, float width, int limit = 40)
        {
            string typed = GUILayout.TextField(value ?? "", limit, GUILayout.Width(width));
            if (typed == (value ?? ""))
                return false;
            value = typed;
            return true;
        }

        /// <summary>An item's picture in a fixed box (its art scaled to fit), in its hue.</summary>
        public static void Icon(UI.GumpArt art, int graphic, int hue, float size)
        {
            Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            if (graphic < 0 || art == null)
                return;
            UI.UiImage img = art.Item(graphic, Math.Max(0, hue));
            if (img == null || img.Width <= 0 || img.Height <= 0)
                return;
            float k = Mathf.Min(1f, Mathf.Min(size / img.Width, size / img.Height));
            float w = img.Width * k, h = img.Height * k;
            GUI.DrawTexture(new Rect(r.x + (size - w) / 2f, r.y + (size - h) / 2f, w, h), img.Texture);
        }
    }
}
