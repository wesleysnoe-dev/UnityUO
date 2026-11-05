using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The party (0xBF 0x06), like ClassicUO's party gump: the members in the server's order with the leader first,
    /// their health when in range, a Remove button per member for the leader, Add (the server then asks for a target),
    /// Leave, and whether the party may loot our corpse. Typed party chat starts with "/" ("/2 hi" whispers the second).
    /// </summary>
    public sealed class PartyWindow : UiWindow
    {
        private const float Row = 22;
        private static readonly Color Gold = new Color(1f, 0.9f, 0.6f), Plain = new Color(0.95f, 0.92f, 0.84f), Dim = new Color(0.7f, 0.7f, 0.65f);

        public PartyWindow() => Rect = new Rect(40, 120, 260, 140);

        public override string Key => "party";

        public override void Draw()
        {
            var members = Session.PartyMembers;
            Rect.height = 104 + Mathf.Max(1, members.Count) * Row;
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(0, 10, Rect.width, 20), "Party", Gold, 15, TextAnchor.UpperCenter, true);

            uint me = Session.World.PlayerSerial;
            bool leader = Session.InParty && Session.PartyLeader == me;
            float y = 36;
            if (members.Count == 0)
                Text(new Rect(16, y + 2, Rect.width - 32, 18), "You are not in a party.", Dim, 12);
            for (int i = 0; i < members.Count; i++, y += Row)
            {
                uint serial = members[i];
                string name = $"{i + 1}. {Session.PartyMemberName(serial)}" + (i == 0 ? " (leader)" : "");
                Text(new Rect(16, y + 2, 150, 18), name, serial == me ? Gold : Plain, 12);
                if (Session.World.Mobiles.TryGetValue(serial, out Mobile m) && m.HitsMax > 0)
                {
                    var bar = new Rect(16, y + 17, 140, 3);
                    GUI.DrawTexture(bar, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0.3f, 0.05f, 0.05f), 0, 0);
                    bar.width *= Mathf.Clamp01(m.Hits / (float)m.HitsMax);
                    GUI.DrawTexture(bar, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0.2f, 0.75f, 0.25f), 0, 0);
                }
                if (leader && serial != me && GUI.Button(new Rect(Rect.width - 86, y, 70, 20), "Remove"))
                    Session.PartyRemove(serial);
            }

            y = Rect.height - 62;
            bool canLoot = GUI.Toggle(new Rect(16, y, Rect.width - 32, 20), Session.PartyCanLoot, " Party can loot me");
            if (canLoot != Session.PartyCanLoot && Session.InParty)
                Session.SetPartyCanLoot(canLoot);
            y += 26;
            if ((leader || !Session.InParty) && GUI.Button(new Rect(16, y, 100, 24), "Add"))
                Session.PartyInvite();
            if (Session.InParty && GUI.Button(new Rect(Rect.width - 116, y, 100, 24), leader ? "Disband" : "Leave"))
                Session.LeaveParty();
        }
    }

    /// <summary>
    /// An invitation (0xBF 0x06 0x07): Accept or Decline, as typing /accept or /decline does. Goes away when the server
    /// drops the invitation (ModernUO declines on its own after 30 seconds); closing it answers nothing.
    /// </summary>
    public sealed class PartyInviteWindow : UiWindow
    {
        private readonly uint _leader;

        public PartyInviteWindow(uint leader)
        {
            _leader = leader;
            Rect = new Rect(Screen.width / 2f / Mathf.Max(0.75f, Screen.height / 1000f) - 150, 220, 300, 110);
        }

        public override string Key => "partyinvite";

        public override bool StillValid => Session.PartyInviter == _leader;

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(0, 12, Rect.width, 20), "Party invitation", new Color(1f, 0.9f, 0.6f), 15, TextAnchor.UpperCenter, true);
            Text(new Rect(0, 38, Rect.width, 18), $"{Session.PartyMemberName(_leader)} invites you to join the party.",
                new Color(0.95f, 0.92f, 0.84f), 12, TextAnchor.UpperCenter);
            if (GUI.Button(new Rect(30, Rect.height - 40, 100, 26), "Accept"))
            {
                Session.AcceptPartyInvite();
                Close();
            }
            if (GUI.Button(new Rect(Rect.width - 130, Rect.height - 40, 100, 26), "Decline"))
            {
                Session.DeclinePartyInvite();
                Close();
            }
        }
    }
}
