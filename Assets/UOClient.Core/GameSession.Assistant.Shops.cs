using System.Collections.Generic;

namespace UOClient.Core
{
    /// <summary>Vendor lists the assistant answers by itself (auto buy, auto sell).</summary>
    public sealed partial class GameSession
    {
        /// <summary>
        /// The vendor list last answered without the shop window. Whoever shows shop windows passes this one over:
        /// its order is already with the server, so a window for it would only offer stock that is being traded.
        /// </summary>
        public ShopList AnsweredShop { get; private set; }

        /// <summary>
        /// Answers a vendor's list (from <see cref="ShopOpened"/>) with an order, as the shop window's accept does, and
        /// marks the list answered. Call it from a ShopOpened handler that runs before the window is opened. An empty
        /// order does nothing: the list stays for the window.
        /// </summary>
        public void AnswerShop(ShopList list, IReadOnlyList<(uint Serial, int Amount)> order)
        {
            if (list == null || order == null || order.Count == 0 || State != SessionState.InWorld)
                return;
            AnsweredShop = list;
            if (list.Selling)
                Sell(list.Vendor, order);
            else
                Buy(list.Vendor, order);
        }
    }
}
