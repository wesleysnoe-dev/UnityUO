using UOClient.Core;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check what a click sends.

    public sealed partial class SpellbookWindow
    {
        /// <summary>
        /// A click on the plain list's index-th spell (0-based within its school, the schools not drawn classic): cast it
        /// from this book, TazUO's GameActions.CastSpellFromBook (0x12 0x27), so ModernUO's CastSpellRequest gets the book.
        /// The classic Magery / Necromancy / Chivalry book does not use it (CastBookSpell).
        /// </summary>
        public static void CastFromBook(GameSession session, uint book, int first, int index) =>
            session?.CastSpellFromBook(first + index, book);

        /// <summary>
        /// A double-click on a classic book's detail icon or index name (SpellbookGump.OnIconDoubleClick :793-804,
        /// OnLabelMouseDoubleClick :1354-1368): GameActions.CastSpell, the 0xBF 0x1C cast with 2 (no book). ModernUO's
        /// IncomingExtendedCommandPackets.CastSpell then passes no book and Spellbook.CastSpellRequest finds one that has it.
        /// </summary>
        public static void CastBookSpell(GameSession session, int first, int index) => session?.CastSpell(first + index);

        /// <summary>TazUO's Mouse.MOUSE_DELAY_DOUBLE_CLICK (Mouse.cs:13): a press this soon after the last is a double-click.</summary>
        public const double DoubleClickSeconds = 0.35;

        /// <summary>
        /// A double-click on a floating spell icon dragged out of the book (TazUO's UseSpellButtonGump.OnMouseDoubleClick,
        /// :219-232, SingleClickIconUse off by default): GameActions.CastSpell, the 0xBF 0x1C cast with no book.
        /// </summary>
        public static void CastIcon(GameSession session, int spellId) => session?.CastSpell(spellId);

        /// <summary>A press on a spell icon becomes a drag once it moves 5 px either way (TazUO Profile.MinGumpMoveDistance, UIManager :1081).</summary>
        public const int DragDistance = 5;

        public static bool StartsDrag(float dx, float dy) => System.Math.Abs(dx) >= DragDistance || System.Math.Abs(dy) >= DragDistance;

        /// <summary>The floating icon opens with the mouse 22 px into it (OnIconDragBegin :822-826, LClickPosition - 22).</summary>
        public const int FloatingIconGrab = 22;

        /// <summary>UseSpellButtonGump.GetSpellTooltip (:153-186) for the AoS schools; 0 for anything else.</summary>
        public static int SpellIconTooltip(int spellId)
        {
            if (spellId >= 1 && spellId <= 64)
                return 3002011 + (spellId - 1);
            if (spellId >= 101 && spellId <= 117)
                return 1060509 + (spellId - 101);
            if (spellId >= 201 && spellId <= 210)
                return 1060585 + (spellId - 201);
            return 0;
        }
    }

    /// <summary>What a press in a classic book is on: a detail page's spell icon, an index name, or anything else.</summary>
    public enum BookTarget { None, Icon, Index }

    /// <summary>What the book does for a mouse event: nothing, cast the spell (0xBF 0x1C), or drag its icon out.</summary>
    public enum BookAction { None, Cast, DragOut }

    /// <summary>The IMGUI left-button event SpellbookWindow.Pointer was handed.</summary>
    public enum PointerEvent { Down, Drag, Up }

    /// <summary>
    /// A classic book's left button as TazUO delivers it (GameController.cs:1122-1172, :1209-1218): a press within 350 ms
    /// of the last counted one is a double-click instead of a press (the next press starts over), and a handled
    /// double-click's release is swallowed. Single clicks never cast. The window feeds it IMGUI events in UI pixels (its
    /// position plus the local mouse, so a book dragged by a name counts as moved) and does what it answers.
    /// </summary>
    public sealed class SpellbookClicks
    {
        private double _lastPress = double.NegativeInfinity;
        private bool _pressed, _swallowRelease;
        private BookTarget _target;
        private int _toPage;
        private float _x, _y;

        /// <summary>The spread a clicked index name opens once no double-click can follow (_enqueuePage); -1 for none.</summary>
        public int EnqueuedPage { get; private set; } = -1;

        /// <summary>The spell (0-based in its school) the last press was on.</summary>
        public int Spell { get; private set; } = -1;

        /// <summary>True while an icon press holds the pointer: the window keeps IMGUI's hotControl, so the drags are the book's.</summary>
        public bool HoldingIcon => _pressed && _target == BookTarget.Icon;

        /// <summary>
        /// SpellbookWindow.Pointer's whole decision for one left-button event, here so the tests run the window's own
        /// routing: the book action, and whether the event is used. A used press keeps GUI.DragWindow (drawn last) from
        /// moving the book: an icon press (held until its release or drag-out) and a double-click's cast. An index name's
        /// press is not used, so it drags the book as TazUO's CanMove label does. The window holds hotControl exactly
        /// while HoldingIcon. <paramref name="target"/>, <paramref name="spell"/> and <paramref name="toPage"/> are read on Down only.
        /// </summary>
        public (BookAction Action, bool Use) Route(PointerEvent kind, BookTarget target, int spell, int toPage, float x, float y, double now)
        {
            switch (kind)
            {
                case PointerEvent.Down:
                    BookAction pressed = Press(target, spell, toPage, x, y, now);
                    return (pressed, pressed == BookAction.Cast || HoldingIcon);
                case PointerEvent.Drag:
                    return HoldingIcon ? (Drag(x, y), true) : (BookAction.None, false);
                default:
                    bool held = HoldingIcon;
                    Release(x, y);
                    return (BookAction.None, held);
            }
        }

        /// <summary>The press is lost without its release (the window closed, or the button was seen up with no MouseUp): drop it.</summary>
        public void Cancel()
        {
            _pressed = false;
            _swallowRelease = false;
        }

        /// <summary>
        /// A left press on <paramref name="target"/>: Cast when it is the second of a double-click on an icon or name
        /// (which also drops a queued page, OnLabelMouseDoubleClick); otherwise it is held for Drag and Release.
        /// </summary>
        public BookAction Press(BookTarget target, int spell, int toPage, float x, float y, double now)
        {
            if (now - _lastPress <= SpellbookWindow.DoubleClickSeconds)
            {
                _lastPress = double.NegativeInfinity;
                _pressed = false;
                if (target == BookTarget.None)
                    return BookAction.None;
                if (target == BookTarget.Index)
                    EnqueuedPage = -1;
                _swallowRelease = true;
                Spell = spell;
                return BookAction.Cast;
            }
            _lastPress = now;
            _pressed = true;
            _swallowRelease = false;
            _target = target;
            Spell = spell;
            _toPage = toPage;
            _x = x;
            _y = y;
            return BookAction.None;
        }

        /// <summary>
        /// The held button moved: an icon press 5 px out becomes its floating icon (OnIconDragBegin :806-830), once, and
        /// the press is spent, so neither its release nor anything after casts.
        /// </summary>
        public BookAction Drag(float x, float y)
        {
            if (!_pressed || _target != BookTarget.Icon || !SpellbookWindow.StartsDrag(x - _x, y - _y))
                return BookAction.None;
            _pressed = false;
            return BookAction.DragOut;
        }

        /// <summary>The button came up: an index name let go of where it was pressed (LDragOffset zero, OnLabelMouseUp :1342-1352) queues its spread.</summary>
        public void Release(float x, float y)
        {
            if (_pressed && !_swallowRelease && _target == BookTarget.Index && System.Math.Abs(x - _x) < 1 && System.Math.Abs(y - _y) < 1)
                EnqueuedPage = _toPage;
            _pressed = false;
            _swallowRelease = false;
        }

        /// <summary>Update (:1386-1393): the queued spread, once 350 ms have passed since the last press; -1 until then.</summary>
        public int Tick(double now)
        {
            if (EnqueuedPage < 0 || now - _lastPress < SpellbookWindow.DoubleClickSeconds)
                return -1;
            int page = EnqueuedPage;
            EnqueuedPage = -1;
            return page;
        }
    }
}
