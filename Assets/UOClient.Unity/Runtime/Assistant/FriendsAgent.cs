using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// The friends list (TazUO's FriendsListManager): characters added by targeting them, kept by serial with the name
    /// they had. The list does nothing by itself; other tools and the display ask <see cref="IsFriend"/>.
    /// </summary>
    public sealed class FriendsAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public List<FriendEntry> friends = new List<FriendEntry>();
        }

        private const string File = "friends";
        private Settings _s = new Settings();
        private readonly HashSet<uint> _serials = new HashSet<uint>(); // the list as IsFriend looks it up
        private static FriendsAgent _current;

        public override string Name => "Friends";
        public override string Group => "General";
        public override bool Active => _s.friends.Count > 0;

        /// <summary>Whether a character is on the friends list of the session now running (false when there is none).</summary>
        public static bool IsFriend(uint serial) => _current != null && _current._serials.Contains(serial);

        public override void Attach()
        {
            _s = Host.Load<Settings>(File);
            _s.friends.RemoveAll(f => f == null || f.serial == 0);
            Index();
            _current = this;
        }

        public override void Detach()
        {
            if (ReferenceEquals(_current, this))
                _current = null;
        }

        private void Index()
        {
            _serials.Clear();
            foreach (FriendEntry f in _s.friends)
                _serials.Add(f.serial);
        }

        private void Changed()
        {
            Index();
            Host.Save(File, _s);
        }

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Friends");
            AssistantGui.Note("Characters you count as friends. Add one by targeting it; it is remembered by its serial, so it stays "
                              + "a friend under another name.");
            if (GUILayout.Button("Add by targeting a character", GUILayout.Width(220)))
            {
                Host.Log("Target the character to add to your friends.");
                Host.Session.BeginLocalTarget(AddTargeted);
            }

            AssistantGui.Header("Your friends");
            if (_s.friends.Count == 0)
            {
                AssistantGui.Note("No friends added yet.");
                return;
            }
            float nameW = Mathf.Max(100f, width - 300f);
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Name", nameW);
            AssistantGui.Cell("Serial", 90);
            AssistantGui.Cell("Added", 80);
            AssistantGui.Cell("", 60);
            GUILayout.EndHorizontal();

            int remove = -1;
            bool renamed = false;
            for (int i = 0; i < _s.friends.Count; i++)
            {
                FriendEntry f = _s.friends[i];
                // In sight: the list follows the name the server gives now.
                bool here = Host.Session.World.Mobiles.TryGetValue(f.serial, out Mobile m);
                if (here)
                    renamed |= FriendList.Rename(f, m.Name);
                GUILayout.BeginHorizontal();
                AssistantGui.Cell(f.name, nameW);
                AssistantGui.Cell($"0x{f.serial:X8}", 90);
                AssistantGui.Cell(f.added, 80);
                AssistantGui.Cell(here ? "in sight" : "", 60);
                if (GUILayout.Button("Remove", GUILayout.Width(64)))
                    remove = i;
                GUILayout.EndHorizontal();
            }
            if (remove >= 0)
            {
                Host.Log($"Removed {_s.friends[remove].name} from your friends.");
                _s.friends.RemoveAt(remove);
                Changed();
            }
            else if (renamed)
                Host.Save(File, _s);
        }

        private void AddTargeted(uint serial)
        {
            GameSession s = Host.Session;
            if (s == null || !s.World.Mobiles.TryGetValue(serial, out Mobile m))
            {
                Host.Log("That is not a character.");
                return;
            }
            if (serial == s.World.PlayerSerial)
            {
                Host.Log("That is you.");
                return;
            }
            string name = string.IsNullOrWhiteSpace(m.Name) ? FriendList.Unnamed : m.Name.Trim();
            if (!FriendList.Add(_s.friends, serial, name, DateTime.Now))
            {
                Host.Log($"{name} is already one of your friends.");
                return;
            }
            Changed();
            Host.Log($"Added {name} to your friends.");
        }

        /// <summary>Self-tests: a friend added without the cursor.</summary>
        public void AddForTest(uint serial, string name)
        {
            if (FriendList.Add(_s.friends, serial, name, DateTime.Now))
                Index();
        }
    }
}
