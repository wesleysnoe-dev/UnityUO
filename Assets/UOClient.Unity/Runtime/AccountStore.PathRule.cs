using System.IO;
using System.Linq;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own to check the rule.
    public sealed partial class AccountStore
    {
        /// <summary>
        /// Remembered logins are kept per server: the local test server uses the test account file, any other
        /// shard its own file. So the local test account is never offered to (or sent to) the live shard.
        /// </summary>
        public static string PathFor(string accountFile, string projectRoot, string host)
        {
            string local = Path.IsPathRooted(accountFile) ? accountFile : Path.Combine(projectRoot, accountFile);
            if (host == "127.0.0.1" || host == "localhost" || host == "::1")
                return local;
            string safe = new string(host.Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' ? ch : '_').ToArray());
            return Path.Combine(Path.GetDirectoryName(local), $"uoclient-account-{safe}.json");
        }
    }
}
