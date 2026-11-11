using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// The list editor the sound and music filters share: the filtered ids (each with Play and remove), the ids heard
    /// lately (each with Filter and Play) and a box to add one by number.
    /// </summary>
    public static class IdFilterEditor
    {
        /// <summary>What the editor keeps between frames for one tab.</summary>
        public sealed class State
        {
            public string Typed = "";
        }

        /// <summary>
        /// Draws the lists; true when the filtered ids changed (the agent saves then). <paramref name="noun"/> is "sound"
        /// or "track"; <paramref name="play"/> plays an id whether filtered or not.
        /// </summary>
        public static bool Draw(State state, List<int> ids, List<int> recent, int maxId, string noun, float width, Action<int> play)
        {
            bool changed = false;
            AssistantGui.Header($"Filtered {noun}s");
            if (ids.Count == 0)
                AssistantGui.Note("Nothing is filtered.");
            int remove = -1;
            for (int i = 0; i < ids.Count; i++)
            {
                GUILayout.BeginHorizontal();
                AssistantGui.Cell(Label(ids[i]), 130);
                if (GUILayout.Button("Play", GUILayout.Width(60)))
                    play(ids[i]);
                if (GUILayout.Button("X", GUILayout.Width(24)))
                    remove = i;
                GUILayout.EndHorizontal();
            }
            if (remove >= 0)
            {
                ids.RemoveAt(remove);
                changed = true;
            }
            if (ids.Count > 1 && GUILayout.Button("Clear all", GUILayout.Width(90)))
            {
                ids.Clear();
                changed = true;
            }

            AssistantGui.Header("Heard lately");
            if (recent.Count == 0)
                AssistantGui.Note($"No {noun} has played yet.");
            int add = -1;
            for (int i = 0; i < recent.Count; i++)
            {
                GUILayout.BeginHorizontal();
                AssistantGui.Cell(Label(recent[i]), 130);
                if (GUILayout.Button("Play", GUILayout.Width(60)))
                    play(recent[i]);
                if (IdFilter.Contains(ids, recent[i]))
                    AssistantGui.Cell("filtered", 70);
                else if (GUILayout.Button("Filter", GUILayout.Width(70)))
                    add = recent[i];
                GUILayout.EndHorizontal();
            }
            if (add >= 0)
                changed |= IdFilter.Add(ids, add);

            AssistantGui.Header("Add by number");
            GUILayout.BeginHorizontal();
            AssistantGui.Text(ref state.Typed, 90, 8);
            int typed = IdFilter.Parse(state.Typed, maxId);
            GUI.enabled = typed >= 0;
            if (GUILayout.Button("Filter", GUILayout.Width(70)) && IdFilter.Add(ids, typed))
            {
                state.Typed = "";
                changed = true;
            }
            if (GUILayout.Button("Play", GUILayout.Width(60)))
                play(typed);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            AssistantGui.Note($"The {noun}'s number, or hex with 0x in front (0 to {maxId}).");
            return changed;
        }

        private static string Label(int id) => $"{id}  (0x{id:X})";
    }
}
