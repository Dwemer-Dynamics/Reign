using System;

namespace ReignBeta.UI
{
    // Engine-independent cleanup orchestration; each delegate closes over only the resource it owns.
    internal static class ReignPartyChatCleanup
    {
        internal static void Run(Action<Exception> reportFailure, params Action[] steps)
        {
            foreach (Action step in steps)
            {
                try { step?.Invoke(); }
                catch (Exception ex)
                {
                    // Even failed logging must not strand input, focus, or the active-state request.
                    try { reportFailure?.Invoke(ex); }
                    catch { }
                }
            }
        }
    }
}
