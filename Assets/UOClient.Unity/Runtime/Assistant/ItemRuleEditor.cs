using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// The list editor the item agents share (auto loot, scavenger, organizer...): one row per <see cref="ItemRule"/> with
    /// its switch, picture, name, graphic, hue (blank = any), words to match and limit (blank = none), and under it
    /// "Add by targeting" (a client-side cursor: point at the item) and "Add empty".
    /// </summary>
    public static class ItemRuleEditor
    {
        /// <summary>Draws the list; true when anything changed (the agent saves then).</summary>
        public static bool Draw(AssistantHost host, List<ItemRule> rules, float width, bool withLimit = true)
        {
            bool changed = false;
            float nameW = Mathf.Max(90f, width - (withLimit ? 372f : 320f));
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("", 22);
            AssistantGui.Cell("", 26);
            AssistantGui.Cell("Name", nameW);
            AssistantGui.Cell("Graphic", 62);
            AssistantGui.Cell("Hue", 50);
            AssistantGui.Cell("Words", 90);
            if (withLimit)
                AssistantGui.Cell("Max", 48);
            GUILayout.EndHorizontal();

            int remove = -1;
            for (int i = 0; i < rules.Count; i++)
            {
                ItemRule r = rules[i];
                GUILayout.BeginHorizontal();
                changed |= AssistantGui.Toggle(ref r.enabled, "", 22);
                AssistantGui.Icon(host.Ui?.Art, r.graphic, r.hue, 24);
                changed |= AssistantGui.Text(ref r.name, nameW);
                string hex = r.graphic < 0 ? "" : $"0x{r.graphic:X4}";
                string typed = GUILayout.TextField(hex, 6, GUILayout.Width(62));
                if (typed != hex)
                {
                    int g = ParseGraphic(typed);
                    if (g != r.graphic)
                    {
                        r.graphic = g;
                        changed = true;
                    }
                }
                changed |= AssistantGui.Int(ref r.hue, 50, 0, 0x3FFF, -1, "");
                changed |= AssistantGui.Text(ref r.text, 90);
                if (withLimit)
                    changed |= AssistantGui.Int(ref r.max, 48, 1, 60000, 0, "");
                if (GUILayout.Button("X", GUILayout.Width(24)))
                    remove = i;
                GUILayout.EndHorizontal();
            }
            if (remove >= 0)
            {
                rules.RemoveAt(remove);
                changed = true;
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add by targeting an item", GUILayout.Width(190)))
            {
                host.Log("Target the item to add.");
                host.Session.BeginLocalTarget(serial =>
                {
                    if (!Describe(host, serial, out int graphic, out int hue))
                    {
                        host.Log("That is not an item.");
                        return;
                    }
                    rules.Add(new ItemRule { name = host.ItemName(serial, graphic), graphic = graphic, hue = -1 });
                    _ = hue; // any hue to start with, as TazUO: the hue box narrows it
                    Added?.Invoke();
                });
            }
            if (GUILayout.Button("Add empty", GUILayout.Width(90)))
            {
                rules.Add(new ItemRule { name = "New entry" });
                changed = true;
            }
            GUILayout.EndHorizontal();
            return changed;
        }

        /// <summary>Raised when a targeted item was added (outside the tab's draw): the open agent saves.</summary>
        public static System.Action Added;

        private static int ParseGraphic(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return -1;
            bool hex = text.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase);
            if (hex)
                text = text.Substring(2);
            if (text.Length == 0)
                return -1;
            return int.TryParse(text, hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int g) && g >= 0 && g <= 0xFFFF ? g : -1;
        }

        /// <summary>An item's graphic and hue by serial, on the ground or inside something.</summary>
        public static bool Describe(AssistantHost host, uint serial, out int graphic, out int hue)
        {
            graphic = hue = -1;
            if (host.Session.World.Contained.TryGetValue(serial, out ContainedItem c))
            {
                graphic = c.Graphic;
                hue = c.Hue;
                return true;
            }
            if (host.Session.World.Items.TryGetValue(serial, out WorldItem w) && !w.IsMulti)
            {
                graphic = w.Graphic;
                hue = w.Hue;
                return true;
            }
            // Something worn (ours or another's).
            foreach (Mobile m in host.Session.World.Mobiles.Values)
                foreach (EquippedItem e in m.Equipment.Values)
                    if (e.Serial == serial)
                    {
                        graphic = e.Graphic;
                        hue = e.Hue;
                        return true;
                    }
            return false;
        }
    }
}
