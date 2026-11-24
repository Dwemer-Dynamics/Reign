using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ReignBeta.WorldSimulation.Integration
{
    internal static class ReignWorldHistoryOutbox
    {
        // The caller serializes all other removals from this queue. Producers may append
        // at any time. The exact prefix stays visible until the durable append returns.
        internal static int PersistQueuePrefix<T>(ConcurrentQueue<T> pending, int limit,
            Action<IReadOnlyList<T>> persist)
        {
            var prefix = pending.Take(limit).ToList();
            if (prefix.Count == 0) return 0;
            persist(prefix);
            foreach (T item in prefix)
            {
                if (!pending.TryDequeue(out T removed) || !EqualityComparer<T>.Default.Equals(item, removed))
                    throw new InvalidOperationException("The world-history outbox queue changed during its synchronized append.");
            }
            return prefix.Count;
        }

        internal static void AppendRecords(string path, string records)
        {
            if (string.IsNullOrEmpty(records)) return;
            using (var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                // Keep a failed partial append from swallowing the first record of its
                // replay. A valid existing final record is preserved byte-for-byte.
                if (file.Length > 0)
                {
                    file.Position = file.Length - 1;
                    if (file.ReadByte() != '\n') file.WriteByte((byte)'\n');
                }
                byte[] bytes = new UTF8Encoding(false).GetBytes(records);
                file.Write(bytes, 0, bytes.Length);
            }
        }

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
