using System;
using System.Collections.Generic;

namespace UOClient.Core.Assistant
{
    /// <summary>One friend (TazUO's FriendEntry): the character's serial and the name it had when added. Public fields: saved with JsonUtility.</summary>
    [Serializable]
    public sealed class FriendEntry
    {
        public string name = "";
        public uint serial;

        /// <summary>The day it was added (yyyy-MM-dd), shown in the list.</summary>
        public string added = "";
    }

    /// <summary>The friends list's rules (TazUO's FriendsListManager): a character is a friend by serial, and is listed once.</summary>
    public static class FriendList
    {
        /// <summary>The name stored for a character whose name the server has not sent yet.</summary>
        public const string Unnamed = "Unknown";

        public static bool IsFriend(IEnumerable<FriendEntry> friends, uint serial)
        {
            if (friends != null && serial != 0)
                foreach (FriendEntry f in friends)
                    if (f != null && f.serial == serial)
                        return true;
            return false;
        }

        /// <summary>Adds a character; false when it is already listed (or has no serial).</summary>
        public static bool Add(List<FriendEntry> friends, uint serial, string name, DateTime today)
        {
            if (friends == null || serial == 0 || IsFriend(friends, serial))
                return false;
            friends.Add(new FriendEntry
            {
                serial = serial,
                name = string.IsNullOrWhiteSpace(name) ? Unnamed : name.Trim(),
                added = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            });
            return true;
        }

        /// <summary>Takes a character off the list; false when it was not on it.</summary>
        public static bool Remove(List<FriendEntry> friends, uint serial) =>
            friends != null && friends.RemoveAll(f => f == null || f.serial == serial) > 0;

        /// <summary>
        /// A friend seen again under a name we did not have (added before the server named it, or renamed): the list takes
        /// the new name. True when it changed.
        /// </summary>
        public static bool Rename(FriendEntry friend, string seenName)
        {
            if (friend == null || string.IsNullOrWhiteSpace(seenName) || friend.name == seenName.Trim())
                return false;
            friend.name = seenName.Trim();
            return true;
        }
    }
}
