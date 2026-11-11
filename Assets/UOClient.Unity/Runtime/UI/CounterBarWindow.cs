using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The counter bar (TazUO's CounterBarGump; rules in HudBars): a row of cells, each an item kind dropped onto it, with
    /// how many of it the backpack holds, bags inside it included. Double-click a cell to use one, right-click to clear
    /// it. The cells are kept in UserSettings/unity-uo-counters.txt, written whenever one changes.
    /// </summary>
    public sealed class CounterBarWindow : UiWindow
    {
        public const string WindowKey = "counterbar", FileName = "unity-uo-counters.txt";

        /// <summary>The options' counter bar settings (GameOptions.More.cs).</summary>
        public static bool Enabled { get; set; }
        public static int CellSize { get; set; } = 40;
        public static int Columns { get; set; } = 5;
        public static int Rows { get; set; } = 1;
        public static bool Abbreviate { get; set; }
        public static int AbbreviateFrom { get; set; } = 1000;
        public static bool HighlightLow { get; set; }
        public static int LowBelow { get; set; } = 5;

        private static Vector2 _lastPosition = new Vector2(540, 56); // kept for the run: where the bar was left

        private readonly List<CounterCell> _cells;
        private readonly Dictionary<(int Graphic, int Hue), (int Amount, uint First)> _totals = new Dictionary<(int, int), (int, uint)>();
        private float _countedAt = -1f, _lastClick;
        private int _lastClickCell = -1;
        private bool _selfTestFilled;

        private static string FilePath => Path.Combine(ClientPaths.ProjectRoot, "UserSettings", FileName);

        public CounterBarWindow()
        {
            _cells = Load();
            Rect = new Rect(_lastPosition.x, _lastPosition.y, 10, 10);
        }

        public override string Key => WindowKey;
        public override bool CanClose => false; // right-click clears a cell; the options switch the bar off
        public override bool StepsAsideOnOpen => false;

        /// <summary>-bars-selftest: both bars show for the run, the counter bar with gold, bandages and two reagents (for captures).</summary>
        public static readonly bool SelfTest = Array.IndexOf(Environment.GetCommandLineArgs(), "-bars-selftest") >= 0;

        private static List<CounterCell> Load()
        {
            if (SelfTest)
                return HudBars.ParseCounters("3821:0\n3617:0\n-\n3962:0\n3973:0");
            try
            {
                if (File.Exists(FilePath))
                    return HudBars.ParseCounters(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: {FilePath} is unreadable ({e.Message}); the counter bar starts empty.");
            }
            return new List<CounterCell>();
        }

        private void Save()
        {
            if (SelfTest)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, HudBars.FormatCounters(_cells));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: could not write {FilePath} ({e.Message}).");
            }
        }

        private void Set(int index, CounterCell cell)
        {
            while (_cells.Count <= index)
                _cells.Add(new CounterCell());
            _cells[index] = cell;
            Save();
        }

        /// <summary>How many of each kind the backpack holds, bags inside it included (four times a second).</summary>
        private void Count()
        {
            if (Time.realtimeSinceStartup - _countedAt < 0.25f)
                return;
            _countedAt = Time.realtimeSinceStartup;
            _totals.Clear();
            World world = Session.World;
            uint pack = world.Worn(world.Player, 21);
            if (pack == 0)
                return;
            foreach (ContainedItem item in world.Contained.Values)
            {
                // Up through the bags it sits in, to the backpack (a few steps at most; a loop in bad data stops at 16).
                uint at = item.Container;
                for (int depth = 0; at != pack && depth < 16 && world.Contained.TryGetValue(at, out ContainedItem parent); depth++)
                    at = parent.Container;
                if (at != pack)
                    continue;
                var key = ((int)item.Graphic, (int)item.Hue);
                _totals.TryGetValue(key, out var have);
                _totals[key] = (have.Amount + Math.Max(1, item.Amount), have.First != 0 ? have.First : item.Serial);
            }
            // -bars-selftest: once the backpack's contents are known, the first kinds found take the cells.
            if (SelfTest && !_selfTestFilled && _totals.Count > 0)
            {
                _selfTestFilled = true;
                _cells.Clear();
                foreach (var key in _totals.Keys)
                    if (_cells.Count < Columns * Rows)
                        _cells.Add(new CounterCell { Graphic = key.Graphic, Hue = key.Hue });
            }
        }

        public override void Draw()
        {
            int cell = CellSize, columns = Columns, rows = Rows;
            var (w, h) = HudBars.CounterBarSize(cell, columns, rows);
            Rect.size = new Vector2(w, h);
            _lastPosition = Rect.position;
            Count();

            Color saved = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = saved;

            Event e = Event.current;
            int over = MouseIsOver ? HudBars.CounterCellAt(e.mousePosition.x, e.mousePosition.y, cell, columns, rows) : -1;
            HoveredText = null;
            for (int i = 0; i < columns * rows; i++)
            {
                float x = 4 + (i % columns) * (cell + 1), y = 4 + (i / columns) * (cell + 1);
                GUI.color = new Color(1, 1, 1, i == over ? 0.22f : 0.1f);
                GUI.DrawTexture(new Rect(x, y, cell, cell), Texture2D.whiteTexture);
                GUI.color = saved;
                CounterCell c = i < _cells.Count ? _cells[i] : new CounterCell();
                if (c.Empty)
                    continue;
                _totals.TryGetValue((c.Graphic, c.Hue), out var total);
                UiImage img = Art.Item(c.Graphic, c.Hue);
                if (img != null)
                {
                    // The art fits the cell, never enlarged; faint when there is none left.
                    float scale = Mathf.Min(1f, Mathf.Min((cell - 4f) / img.Width, (cell - 14f) / img.Height));
                    float iw = img.Width * scale, ih = img.Height * scale;
                    if (total.Amount == 0)
                        GUI.color = new Color(1, 1, 1, 0.3f);
                    GUI.DrawTexture(new Rect(x + (cell - iw) / 2, y + (cell - 12 - ih) / 2, iw, ih), img.Texture);
                    GUI.color = saved;
                }
                string text = HudBars.CounterText(total.Amount, Abbreviate, AbbreviateFrom);
                bool low = HudBars.CounterIsLow(total.Amount, HighlightLow, LowBelow);
                var label = new Rect(x, y + cell - 14, cell, 14);
                Text(new Rect(label.x + 1, label.y + 1, label.width, label.height), text, Color.black, 11, TextAnchor.MiddleCenter);
                Text(label, text, low ? new Color(1f, 0.35f, 0.3f) : new Color(0.95f, 0.93f, 0.85f), 11, TextAnchor.MiddleCenter);
                if (i == over)
                    HoveredText = $"{Art.Data.TileData.GetStatic(c.Graphic).Name}: {total.Amount}";
            }

            if (over < 0 || Ui.Dragging != null)
                return;
            if (e.type == EventType.MouseDown && e.button == 1)
            {
                if (over < _cells.Count && !_cells[over].Empty)
                    Set(over, new CounterCell());
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && over < _cells.Count && !_cells[over].Empty)
            {
                // A double-click uses the first one found; a single press still moves the bar.
                if (_lastClickCell == over && Time.realtimeSinceStartup - _lastClick < 0.4f)
                {
                    if (_totals.TryGetValue((_cells[over].Graphic, _cells[over].Hue), out var total) && total.First != 0)
                        Session.Use(total.First);
                    _lastClickCell = -1;
                    e.Use();
                }
                else
                {
                    _lastClickCell = over;
                    _lastClick = Time.realtimeSinceStartup;
                }
            }
        }

        /// <summary>An item dropped on a cell is what the cell counts; the item itself goes back where it came from.</summary>
        public override bool Drop(DraggedItem item, Vector2 local)
        {
            int at = HudBars.CounterCellAt(local.x, local.y, CellSize, Columns, Rows);
            if (at >= 0)
            {
                Set(at, new CounterCell { Graphic = item.Graphic, Hue = item.Hue });
                _countedAt = -1f;
            }
            return false; // not taken: UiManager puts it back
        }
    }
}
