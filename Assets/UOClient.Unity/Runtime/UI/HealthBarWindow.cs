using System.Linq;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A mobile's classic health bar (TazUO's HealthBarGump, non-party style): ours shows hits, mana and stamina over the
    /// war / peace background; anyone else's shows their name and hits over 0x0804 in their notoriety hue. It asks for the
    /// mobile's status as it opens (GameActions.RequestMobileStatus), follows the world's hits from then on, moves by
    /// dragging and closes on right-click. Out of range it keeps its name with the bar hidden; a dead mobile's closes
    /// (CloseHealthBarType's default, Dead). A click with a target cursor up targets the mobile. Nothing here attacks.
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

        public override bool StillValid
        {
            get
            {
                Mobile m = Mobile;
                return m == null || _serial == Session.World.PlayerSerial || !m.IsDead;
            }
        }

        public override void OnOpened()
        {
            _name = Mobile?.Name;
            FitToBackground();
            Session.RequestStatus(_serial);
        }

        public override void OnClosed() => _nameLabel.Release();

        /// <summary>Points the bar at another mobile (BaseHealthBarGump.SetNewMobile): its name, its status asked for.</summary>
        private void SetMobile(uint serial)
        {
            _serial = serial;
            _name = Mobile?.Name;
            Session.RequestStatus(serial);
        }

        private void FitToBackground()
        {
            UiImage bg = Art.Gump(Background(_serial == Session.World.PlayerSerial, Session.WarMode));
            if (bg != null)
                Rect.size = new Vector2(bg.Width, bg.Height);
        }

        public override void Draw()
        {
            State s = Compute(_serial, Session.World.PlayerSerial, Session.WarMode, Mobile, _name);
            _name = s.Name;
            if (IsLastAttackBar)
                _lastAttackPos = Rect.position;

            Event e = Event.current;
            var bounds = new Rect(0, 0, Rect.width, Rect.height);
            if (e.type == EventType.MouseDown && e.button == 0 && bounds.Contains(e.mousePosition))
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
                UiImage name = _nameLabel.Get(Art, s.Name, s.NameHue);
                if (name != null)
                {
                    GUI.BeginGroup(new Rect(NameX, NameY, NameWidth, name.Height));
                    DrawImage(name, 0, 0);
                    GUI.EndGroup();
                }
            }

            if (bounds.Contains(e.mousePosition))
                Hovered = _serial;
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
            Vector2 at = _lastAttackPos ?? new Vector2(0, TopBarWindow.Height + 4);
            HealthBarWindow bar = ui.Open(new HealthBarWindow(serial, at.x, at.y) { IsLastAttackBar = true });
            _lastAttackPos = bar.Rect.position;
        }
    }
}
