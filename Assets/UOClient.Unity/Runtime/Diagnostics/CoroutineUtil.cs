using System.Collections;
using System.Collections.Generic;

namespace UOClient.Unity
{
    /// <summary>Small helpers for the unattended tours' coroutines.</summary>
    internal static class CoroutineUtil
    {
        public static IEnumerator WaitFrames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        /// <summary>Steps through a sub-coroutine in place, so the caller yields exactly what it yields (no extra frame).</summary>
        public static IEnumerable<object> Inline(IEnumerator steps)
        {
            while (steps.MoveNext())
                yield return steps.Current;
        }
    }
}
