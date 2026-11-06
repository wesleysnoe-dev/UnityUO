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
    }
}
