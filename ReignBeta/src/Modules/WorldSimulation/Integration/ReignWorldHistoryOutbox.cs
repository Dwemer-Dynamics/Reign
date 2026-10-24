using System;
using System.IO;
using System.Text;

namespace ReignBeta.Integration
{
    internal static class ReignWorldHistoryOutbox
    {
        internal static void Rewrite(string path, Action<StreamReader, StreamWriter> rewrite)
        {
            // pending.jsonl is already close to MAX_PATH for recovered branches.
            // An independent 8.3 scratch name never lengthens that file's path.
            // Keep it beside the original so replacement stays on the same volume.
            string temp = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetRandomFileName());
            bool ownsTemp = false;
            bool replacementAttempted = false;
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownsTemp = true;
                    using (var reader = new StreamReader(path, Encoding.UTF8, true))
                    using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
                        rewrite(reader, writer);
                }
                replacementAttempted = true;
                File.Replace(temp, path, null);
            }
            finally
            {
                // ReplaceFile failures can leave the complete scratch as the
                // only recovery copy. Preserve it once replacement has begun.
                if (ownsTemp && !replacementAttempted && File.Exists(path) && File.Exists(temp))
                    File.Delete(temp);
            }
        }
    }
}
