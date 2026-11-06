using UOClient.Core;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check what a click sends.

    public sealed partial class SpellbookWindow
    {
        /// <summary>
        /// A click on the book's index-th spell (0-based within its school): cast it from this book, TazUO's
        /// GameActions.CastSpellFromBook (0x12 0x27), so ModernUO's CastSpellRequest gets the book we cast from.
        /// </summary>
        public static void CastFromBook(GameSession session, uint book, int first, int index) =>
            session?.CastSpellFromBook(first + index, book);

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
}
