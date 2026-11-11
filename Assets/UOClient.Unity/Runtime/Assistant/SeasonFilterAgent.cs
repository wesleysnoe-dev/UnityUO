using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// The season filter (TazUO's SeasonFilter): for each season the server may send, another can be shown instead
    /// (winter drawn as fall, say). It feeds the same place the options' "Show seasons" switch does
    /// (<see cref="FoliageWind.Shown"/>), so the leaves, the light's tint and the falling leaves all follow it; with that
    /// switch off the world stays in summer whatever is chosen here.
    /// </summary>
    public sealed class SeasonFilterAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            /// <summary>One entry per season, in the server's order (spring, summer, fall, winter, desolation): the season shown instead, or -1.</summary>
            public List<int> showAs = new List<int> { -1, -1, -1, -1, -1 };
        }

        private static readonly string[] Names = { "Spring", "Summer", "Fall", "Winter", "Desolation" };
        private const string File = "seasonfilter";
        private Settings _s = new Settings();
        private Func<Season, Season> _hook;

        public override string Name => "Season Filter";
        public override string Group => "Filters";
        public override bool Active => SeasonMap.Any(_s.showAs);

        public override void Attach()
        {
            _s = Host.Load<Settings>(File);
            if (_s.showAs == null)
                _s.showAs = new List<int>();
            SeasonMap.Tidy(_s.showAs);
            _hook = sent => SeasonMap.Apply(_s.showAs, sent);
            Apply();
        }

        public override void Detach()
        {
            if (FoliageWind.SeasonFilter == _hook)
                FoliageWind.SeasonFilter = null;
        }

        /// <summary>With every season shown as sent the drawing code is left without a filter at all.</summary>
        private void Apply()
        {
            if (Active)
                FoliageWind.SeasonFilter = _hook;
            else
                Detach();
        }

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Season Filter");
            AssistantGui.Note("Shows another season than the one the server sends: if winter looks bleak, have it drawn as fall. "
                              + "A season changes the leaves' colours, the light's tint and the falling leaves.");
            if (!FoliageWind.ShowSeasons)
                AssistantGui.Note("Seasons are switched off in Options > Video > Nature (\"Show seasons\"), so the world is drawn as "
                                  + "summer and nothing chosen here shows. Switch that on to use this filter.");
            if (Host.Session != null)
            {
                Season sent = Host.Session.Season, shown = FoliageWind.Current(Host.Session);
                AssistantGui.Note($"The server's season now: {Names[(int)sent]}. Shown: {Names[(int)shown]}.");
            }

            AssistantGui.Header("When the server sends");
            bool changed = false;
            for (int i = 0; i < SeasonMap.Seasons; i++)
            {
                GUILayout.BeginHorizontal();
                AssistantGui.Cell(Names[i], 100);
                AssistantGui.Cell("show", 40);
                int shown = _s.showAs[i];
                if (GUILayout.Button(shown < 0 ? "as sent" : Names[shown], GUILayout.Width(110)))
                {
                    _s.showAs[i] = SeasonMap.Next(shown);
                    changed = true;
                }
                GUILayout.EndHorizontal();
            }
            AssistantGui.Note("Click a button to step through the seasons; \"as sent\" leaves that season alone.");
            if (Active && GUILayout.Button("Show every season as sent", GUILayout.Width(210)))
            {
                for (int i = 0; i < SeasonMap.Seasons; i++)
                    _s.showAs[i] = SeasonMap.AsSent;
                changed = true;
            }
            if (!changed)
                return;
            Apply();
            Host.Save(File, _s);
        }
    }
}
