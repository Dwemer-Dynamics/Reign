using System;

namespace ReignBeta.Dialogue.UI
{
    internal static class ReignChatText
    {
        internal static bool ContainsIgnoreCase(string text, string value)
        {
            return !string.IsNullOrEmpty(text)
                && !string.IsNullOrEmpty(value)
                && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
