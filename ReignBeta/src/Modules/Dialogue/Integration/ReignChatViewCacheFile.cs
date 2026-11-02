using System;
using System.IO;

namespace ReignBeta.Integration
{
    // Also exercised under the game's .NET Framework file APIs by the offline probe.
    internal static class ReignChatViewCacheFile
    {
        private static readonly object CommitGate = new object();

        internal static string TemporaryPath(string destination)
        {
            // Appending a GUID to the 64-character image name exceeds MAX_PATH at
            // ordinary Steam install locations. Keep the sibling's name independent.
            return Path.Combine(Path.GetDirectoryName(destination), ".view-" + Guid.NewGuid().ToString("N") + ".tmp");
        }

        internal static void WriteAtomic(string destination, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = TemporaryPath(destination);
            try
            {
                File.WriteAllBytes(temporary, bytes);
                lock (CommitGate)
                {
                    if (File.Exists(destination)) File.Replace(temporary, destination, null);
                    else File.Move(temporary, destination);
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
