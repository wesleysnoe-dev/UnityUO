using System.Linq;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A mobile's health bar (TazUO's HealthBarGump). Classic style: ours shows hits, mana and stamina over the war / peace
    /// background; anyone else's shows their name and hits over 0x0804 in their notoriety hue, and a renamable pet's bar
    /// has heal / cure buttons and an editable name (Enter sends 0x75). Party style, for party members: name, heal / cure buttons and three 0x0029
    /// lines. It asks for the mobile's status as it opens (GameActions.RequestMobileStatus), follows the world's hits from
    /// then on, moves by dragging and closes on right-click. Out of range it keeps its name with the bar hidden; a dead
    /// mobile's closes (CloseHealthBarType's default, Dead) unless in the party. A click with a target cursor up targets
    /// the mobile. Nothing here attacks.
    /// </summary>
    public sealed partial class HealthBarWindow : UiWindow
    {
        private uint _serial;
        private string _name;
        private readonly CachedLabel _nameLabel = new CachedLabel();

        /// <summary>Where the last attack bar was left this session (TazUO's Profile.LastTargetHealthBarPos; not saved to disk).</summary>
        private static Vector2? _lastAttackPos;

        public HealthBarWindow(uint serial, float x, float y)
        {
            _serial = serial;
            Rect = new Rect(x, y, 130, 50);
        }

        public uint Serial => _serial;

        /// <summary>The one bar that follows our last attack (UseOneHPBarForLastAttack, on by default).</summary>
        public bool IsLastAttackBar { get; private set; }

        public override string Key => KeyFor(_serial);

        public override bool StepsAsideOnOpen => false;

        private Mobile Mobile => Session.World.Mobiles.TryGetValue(_serial, out Mobile m) ? m : null;

        /// <summary>A party member's bar takes the party style (ShowPartyStyleBar), switching as the party changes.</summary>
        private bool Party => Session.ShowsPartyHealthBar(_serial);

        /// <summary>A dead mobile's bar closes, except ours and a party member's (Update :2068, `!inparty`).</summary>
        public override bool StillValid
        {
            get
            {
                Mobile m = Mobile;
                return m == null || _serial == Session.World.PlayerSerial || !m.IsDead || Party;
            }
        }

        public override void OnOpened()
        {
            _name = Mobile?.Name;
            FitToBackground();
            Session.RequestStatus(_serial);
        }

        public override void OnClosed()
        {
            StopRename();
            _nameLabel.Release();
        }

        /// <summary>Points the bar at another mobile (BaseHealthBarGump.SetNewMobile): its name, its status asked for.</summary>
        private void SetMobile(uint serial)
        {
            _serial = serial;
            _name = Mobile?.Name;
            Session.RequestStatus(serial);
        }

        private void FitToBackground()
        {
            if (Party)
            {
                Rect.size = new Vector2(PartyWidth, PartyHeight);
                return;
            }
            UiImage bg = Art.Gump(Background(_serial == Session.World.PlayerSerial, Session.WarMode));
            if (bg != null)
                Rect.size = new Vector2(bg.Width, bg.Height);
        }

        public override void Draw()
        {
            if (Party)
            {
                StopRename();
                DrawParty();
                return;
            }
            State s = Compute(_serial, Session.World.PlayerSerial, Session.WarMode, Mobile, _name);
            _name = s.Name;
            if (IsLastAttackBar)
                _lastAttackPos = Rect.position;

            Event e = Event.current;
            var bounds = new Rect(0, 0, Rect.width, Rect.height);
            var nameBox = new Rect(NameX, NameY, NameWidth, NameBoxHeight);
            if (_renaming && !s.Renamable)
                StopRename(); // out of range, or no longer ours to rename (Update :2076-2089)
            bool healButtons = ShowsPetHealButtons(s, Session.PartyMembers.Contains(_serial));
            if (e.type == EventType.MouseDown && e.button == 0 && bounds.Contains(e.mousePosition)
                && !(healButtons && OnHealButtons(e.mousePosition)))
            {
                if (TryTarget(_serial))
                    return;
                if (!s.Self && nameBox.Contains(e.mousePosition))
                {
                    // OnMouseDown on the name box stops an edit; a press without a drag starts one (TextBoxOnMouseUp).
                    if (_renaming)
                        StopRename();
                    else if (CanRename(s, Session.Target != null) && e.clickCount == 1)
                        _namePressAt = Input.mousePosition;
                }
                if (e.clickCount == 2)
                {
                    StopRename();
                    _namePressAt = null;
                    DoubleClicked(s.Self);
                    e.Use();
                    return;
                }
            }
            if (_namePressAt is Vector3 pressedAt && !Input.GetMouseButton(0))
            {
                _namePressAt = null;
                if ((Input.mousePosition - pressedAt).sqrMagnitude < 1f && CanRename(s, Session.Target != null))
                    StartRename(s.Name);
            }

            UiImage bg = Art.Gump(s.Background, s.BackgroundHue);
            if (bg != null && (bg.Width != (int)Rect.width || bg.Height != (int)Rect.height))
                Rect.size = new Vector2(bg.Width, bg.Height);
            DrawImage(bg, 0, 0);

            if (s.Self)
            {
                Mobile m = Mobile;
                int[] widths = { s.Hits, s.Mana, s.Stamina };
                for (int i = 0; i < PlayerLines.Length; i++)
                {
                    (int x, int y) = PlayerLines[i];
                    if (i > 0 || s.ShowsRedLine)
                        DrawImage(Art.Gump(LineRed), x, y);
                    if (s.InRange && m != null)
                        DrawBar(i == 0 ? s.HitsLine : LineBlue, x, y, widths[i]);
                }
            }
            else
            {
                if (s.ShowsRedLine)
                    DrawImage(Art.Gump(LineRed), OtherLineX, OtherLineY);
                if (s.InRange)
                    DrawBar(s.HitsLine, OtherLineX, OtherLineY, s.Hits);
                if (_renaming)
                    DrawRenameBox(nameBox);
                else
                    DrawName(s.Name, s.NameHue, NameX, NameY, NameWidth);
                if (healButtons)
                    DrawHealButtons();
            }

            if (bounds.Contains(e.mousePosition))
                Hovered = _serial;
        }

        private void DrawName(string text, int hue, float x, float y, float width)
        {
            UiImage name = _nameLabel.Get(Art, text, hue);
            if (name == null)
                return;
            GUI.BeginGroup(new Rect(x, y, width, name.Height));
            DrawImage(name, 0, 0);
            GUI.EndGroup();
        }

        /// <summary>
        /// The party style (BuildGump :1689-1795): the name over an invisible 0x0803, heal and cure buttons on the left,
        /// and hits, mana and stamina on 0x0028 / 0x0029 lines. The buttons quick-heal and quick-cure this mobile
        /// (OnButtonClick :2275-2290); the cursor that spell brings is answered with it by the session.
        /// </summary>
        private void DrawParty()
        {
            PartyState s = ComputeParty(_serial, Session.World.PlayerSerial, Mobile, _name);
            if (!s.Self)
                _name = s.Name;
            if (IsLastAttackBar)
                _lastAttackPos = Rect.position;
            if (Rect.width != PartyWidth || Rect.height != PartyHeight)
                Rect.size = new Vector2(PartyWidth, PartyHeight);

            Event e = Event.current;
            var bounds = new Rect(0, 0, Rect.width, Rect.height);
            if (e.type == EventType.MouseDown && e.button == 0 && bounds.Contains(e.mousePosition)
                && !(s.ShowsBarsAndButtons && OnHealButtons(e.mousePosition)))
            {
                if (TryTarget(_serial))
                    return;
                if (e.clickCount == 2)
                {
                    DoubleClicked(s.Self);
                    e.Use();
                    return;
                }
            }

            DrawName(s.Name, s.NameHue, PartyNameX, PartyNameY, s.Self ? PartyNameWidthSelf : PartyNameWidthOther);

            if (s.ShowsBarsAndButtons)
                DrawHealButtons();

            int[] widths = { s.Hits, s.Mana, s.Stamina };
            for (int i = 0; i < PartyLineY.Length; i++)
            {
                if (i > 0 || s.ShowsHitsRedLine)
                    DrawImage(Art.Gump(LineRedParty), PartyLineX, PartyLineY[i]);
                if (s.ShowsBarsAndButtons)
                    DrawBar(LineBlueParty, PartyLineX, PartyLineY[i], widths[i]);
            }

            if (bounds.Contains(e.mousePosition))
                Hovered = _serial;
        }

        /// <summary>The heal and cure buttons take their own presses (a click there neither targets nor double-clicks).</summary>
        private bool OnHealButtons(Vector2 p)
        {
            UiImage heal = Art.Gump(HealButton), cure = Art.Gump(CureButton);
            return heal != null && new Rect(0, HealButtonY, heal.Width, heal.Height).Contains(p)
                || cure != null && new Rect(0, CureButtonY, cure.Width, cure.Height).Contains(p);
        }

        /// <summary>OnButtonClick (:2275-2290): quick heal or cure this mobile; the session answers the spell's cursor with it.</summary>
        private void DrawHealButtons()
        {
            if (GumpButton(HealButton, HealCurePressed, 0, HealButtonY))
                Session.QuickHeal(_serial);
            if (GumpButton(CureButton, HealCurePressed, 0, CureButtonY))
                Session.QuickCure(_serial);
        }

        // ---- renaming (BaseHealthBarGump's name box, editable while the mobile is renamable) ----

        /// <summary>The name box's height (StbTextBox Height 15, BuildGump :1928).</summary>
        private const int NameBoxHeight = 15;

        /// <summary>The box takes at most the 30 characters 0x75 carries.</summary>
        private const int RenameMaxLength = 30;

        private bool _renaming, _renameFocused;
        private int _renameKeyboard; // the text field's keyboard control id once it has the keys
        private string _renameText;
        private Vector3? _namePressAt;

        private string RenameControl => "rename:" + _serial.ToString("X8");

        private void StartRename(string current)
        {
            _renaming = true;
            _renameFocused = false;
            _renameKeyboard = 0;
            _renameText = current ?? "";
        }

        /// <summary>Ends an edit and gives the keys back if the box still has them (no GUI call: OnClosed runs outside OnGUI).</summary>
        private void StopRename()
        {
            if (!_renaming)
                return;
            _renaming = false;
            if (_renameKeyboard != 0 && GUIUtility.keyboardControl == _renameKeyboard)
                GUIUtility.keyboardControl = 0;
            _renameKeyboard = 0;
        }

        /// <summary>
        /// The editable name: Enter sends the rename (OnKeyDown :396-402, GameActions.Rename) and ends the edit, Escape or
        /// losing the keyboard ends it without sending. The session refuses a mobile that is not renamable.
        /// </summary>
        private void DrawRenameBox(Rect box)
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                e.Use();
                Session.Rename(_serial, _renameText);
                StopRename();
                return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                e.Use();
                StopRename();
                return;
            }
            GUI.SetNextControlName(RenameControl);
            _renameText = GUI.TextField(box, _renameText, RenameMaxLength);
            if (!_renameFocused)
            {
                GUI.FocusControl(RenameControl);
                _renameFocused = true;
            }
            else if (e.type == EventType.Repaint)
            {
                if (_renameKeyboard == 0 && GUI.GetNameOfFocusedControl() == RenameControl)
                    _renameKeyboard = GUIUtility.keyboardControl;
                else if (_renameKeyboard != 0 && GUIUtility.keyboardControl != _renameKeyboard)
                    StopRename(); // the keys went elsewhere (a press on the world, another field)
            }
        }

        /// <summary>GumpPicWithWidth: the line's gump tiled across the filled width.</summary>
        private void DrawBar(int gump, float x, float y, int width)
        {
            UiImage img = Art.Gump(gump);
            if (img != null && width > 0)
                DrawTiled(img, x, y, width, img.Height);
        }

        /// <summary>
        /// BaseHealthBarGump.OnMouseDoubleClick: ours opens the status (which replaces this bar, StatusGumpBarMutuallyExclusive);
        /// another's is used in peace mode. TazUO attacks from here in war mode; that is left out on purpose.
        /// </summary>
        private void DoubleClicked(bool self)
        {
            if (self)
            {
                Ui.Open(new StatusWindow());
                return;
            }
            if (!Session.WarMode)
                Session.DoubleClick(_serial);
        }

        /// <summary>The size TazUO centers a dragged-out bar by: the 0x0804 gump's (130 x 50 if the art is missing).</summary>
        private static Vector2 DragSize(GumpArt art)
        {
            UiImage bg = art.Gump(BackgroundOther);
            return bg != null ? new Vector2(bg.Width, bg.Height) : new Vector2(130, 50);
        }

        /// <summary>
        /// A mobile dragged out of the world (GameSceneInputHandler.OnMouseDragging :1300-1338): any bar it had closes, a new
        /// one opens centered on the press and rides the mouse until the button comes up.
        /// </summary>
        public static HealthBarWindow DragOut(UiManager ui, uint serial, Vector2 pressUi)
        {
            if (ui.Find(KeyFor(serial)) is UiWindow old)
                ui.Close(old);
            Vector2 size = DragSize(ui.Art);
            (float x, float y) = CenteredOn(pressUi.x, pressUi.y, (int)size.x, (int)size.y);
            HealthBarWindow bar = ui.Open(new HealthBarWindow(serial, x, y));
            bar.Rect.position = new Vector2(x, y); // as pressed, not clamped: it is under the mouse
            ui.BeginWindowDrag(bar, pressUi - bar.Rect.position);
            return bar;
        }

        /// <summary>
        /// TargetManager.LastAttack's setter (OpenHealthBarForLastAttack and UseOneHPBarForLastAttack, both on by default):
        /// the last attack bar moves to the new mobile, or opens where it was last left. A mobile that already has a bar
        /// keeps that one (TazUO would show two; our windows are one per serial).
        /// </summary>
        public static void ShowLastAttack(UiManager ui, uint serial)
        {
            if (!GameSession.IsMobileSerial(serial))
                return;
            HealthBarWindow last = ui.Windows.OfType<HealthBarWindow>().FirstOrDefault(w => w.IsLastAttackBar);
            if (ui.Find(KeyFor(serial)) != null)
                return;
            if (last != null)
            {
                last.SetMobile(serial);
                return;
            }
            Vector2 at = _lastAttackPos ?? new Vector2(0, (PaperdollWindow.Modern ? 0 : TopBarWindow.Height) + 4);
            HealthBarWindow bar = ui.Open(new HealthBarWindow(serial, at.x, at.y) { IsLastAttackBar = true });
            _lastAttackPos = bar.Rect.position;
        }
    }
}
