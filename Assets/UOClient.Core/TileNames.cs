namespace UOClient.Core
{
    /// <summary>
    /// Names from tiledata as a click shows them. Tiledata writes plurals inline: <c>bottle%s%</c>, <c>loa%ves/f%</c>
    /// (plural text, then the singular after the slash). A single static is named in the singular, as ClassicUO's
    /// StringHelper.GetPluralAdjustedString does for a count of one.
    /// </summary>
    public static class TileNames
    {
        /// <summary>The singular of a tiledata name, trimmed; null when nothing is left.</summary>
        public static string Singular(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            var sb = new System.Text.StringBuilder(raw.Length);
            int i = 0;
            while (i < raw.Length)
            {
                int open = raw.IndexOf('%', i);
                int close = open < 0 ? -1 : raw.IndexOf('%', open + 1);
                if (open < 0 || close < 0)
                {
                    sb.Append(raw, i, raw.Length - i);
                    break;
                }
                sb.Append(raw, i, open - i);
                string inner = raw.Substring(open + 1, close - open - 1);
                int slash = inner.IndexOf('/');
                if (slash >= 0)
                    sb.Append(inner, slash + 1, inner.Length - slash - 1); // the singular form
                i = close + 1;
            }
            string s = sb.ToString().Trim();
            return s.Length == 0 ? null : s;
        }
    }
}
