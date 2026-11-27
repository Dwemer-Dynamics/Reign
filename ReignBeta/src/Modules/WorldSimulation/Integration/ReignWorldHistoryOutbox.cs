using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReignBeta.WorldSimulation.Integration
{
    internal static class ReignWorldHistoryOutbox
    {
        internal const string OwnedHistoryMarker = "_reignOwnedHistory";

        internal static bool HasOwnedHistoryMarker(JObject item) =>
            item?.Property(OwnedHistoryMarker, StringComparison.Ordinal) != null;

        // Null means the ordinary format. Zero means a marked record has no usable sequence.
        internal static long? FindOwnedSequence(IEnumerable<string> records, IEnumerable<JObject> queued)
        {
            long? firstOwned = null;
            void Include(JObject item)
            {
                long sequence = OwnedSequence(item);
                if (!firstOwned.HasValue || sequence < firstOwned.Value) firstOwned = sequence;
            }

            foreach (JObject item in queued)
                if (HasOwnedHistoryMarker(item)) Include(item);

            foreach (string record in records)
            {
                if (firstOwned == 0L) break;
                // Most old rows need only a textual candidate check on this pass.
                if (record.IndexOf(OwnedHistoryMarker, StringComparison.Ordinal) < 0
                    && record.IndexOf("\\u", StringComparison.Ordinal) < 0) continue;
                if (IsOwnedRecord(record, ReadObject(record, out var item) ? item : null)) Include(item);
            }
            return firstOwned;
        }

        internal static bool PrecedesOwnedSequence(long? sequence, long? ownedSequence) => !ownedSequence.HasValue
            || sequence.HasValue && sequence.Value > 0 && sequence.Value < ownedSequence.Value;

        internal static bool TryReadOrdinaryRecord(string line, out JObject item, out bool owned)
        {
            ReadObject(line, out item);
            owned = IsOwnedRecord(line, item);
            return item != null && !owned;
        }

        internal static List<JObject> ReadOrdinaryPrefix(IEnumerable<string> records, int limit, long maximumSequence,
            long? ownedSequence)
        {
            var unique = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            // File.ReadLines can open the file before iteration. Dispose the reader even for a closed barrier.
            using var lines = records.GetEnumerator();
            if (ownedSequence == 0L) return new List<JObject>();
            while (lines.MoveNext())
            {
                string record = lines.Current;
                bool ordinary = TryReadOrdinaryRecord(record, out var item, out bool owned);
                if (owned) break;
                if (ordinary && TryOrdinarySequence(item, out long? sequence)
                    && PrecedesOwnedSequence(sequence, ownedSequence)
                    && ReignWorldHistoryFlushPolicy.IsSequenceWithinBoundary(sequence, maximumSequence))
                {
                    if (TryOrdinaryId(item, out string id) && !string.IsNullOrWhiteSpace(id)) unique[id] = item;
                }

                if (unique.Count >= Math.Max(1, limit)) break;
            }
            return unique.Values.OrderBy(x => x.Value<long?>("sequence") ?? 0L).ToList();
        }

        internal static void RemoveOrdinaryAcknowledged(string path, ISet<string> ids, long? ownedSequence)
        {
            Rewrite(path, (reader, writer) =>
            {
                bool afterBarrier = false;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (afterBarrier)
                    {
                        writer.WriteLine(line);
                        continue;
                    }
                    ReadObject(line, out var item);
                    afterBarrier = IsOwnedRecord(line, item);
                    if (afterBarrier || ownedSequence.HasValue && (!TryOrdinarySequence(item, out long? sequence)
                            || !PrecedesOwnedSequence(sequence, ownedSequence))
                        || TryOrdinaryId(item, out string id) && !ids.Contains(id ?? string.Empty))
                        writer.WriteLine(line);
                }
            });
        }

        internal static void RestoreThroughSequence(string path, long maximumSequence)
        {
            Rewrite(path, (reader, writer) =>
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    ReadObject(line, out var item);
                    if (IsOwnedRecord(line, item))
                    {
                        long owned = OwnedSequence(item);
                        if (owned == 0L || owned <= maximumSequence) writer.WriteLine(line);
                    }
                    else if (TryOrdinarySequence(item, out long? sequence) && (sequence ?? long.MaxValue) <= maximumSequence)
                        writer.WriteLine(line);
                }
            });
        }

        // Owned delivery is staged after the earlier ordinary prefix drains. Acknowledging it
        // may remove only that exact first row, never a duplicate ID or a later queued record.
        internal static bool HasExactOwnedFirstRecord(string path, JObject expected)
        {
            if (!HasOwnedHistoryMarker(expected) || !File.Exists(path)) return false;
            string line = File.ReadLines(path).FirstOrDefault();
            if (string.IsNullOrEmpty(line)) return false;
            try
            {
                using var reader = new JsonTextReader(new StringReader(line)) { DateParseHandling = DateParseHandling.None };
                var row = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                return !reader.Read() && JToken.DeepEquals(row, expected);
            }
            catch (JsonException) { return false; }
        }

        internal static bool AcknowledgeExactOwnedFirstRecord(string path, JObject expected)
        {
            if (!HasExactOwnedFirstRecord(path, expected)) return false;
            Rewrite(path, (reader, writer) =>
            {
                reader.ReadLine();
                string line;
                while ((line = reader.ReadLine()) != null) writer.WriteLine(line);
            });
            return true;
        }

        internal static bool RetainOwnedThroughSequence(JObject item, long maximumSequence)
        {
            long sequence = OwnedSequence(item);
            return sequence == 0L || sequence <= maximumSequence;
        }

        private static long OwnedSequence(JObject item)
        {
            JToken token = item?["sequence"];
            return token?.Type == JTokenType.Integer
                && long.TryParse(token.ToString(Formatting.None), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                    out long sequence) && sequence > 0 ? sequence : 0L;
        }

        private static bool ReadObject(string line, out JObject item)
        {
            item = null;
            try { item = JObject.Parse(line); }
            catch (JsonException) { }
            return item != null;
        }

        private static bool TryOrdinarySequence(JObject item, out long? sequence)
        {
            sequence = null;
            if (item == null) return false;
            // Preserve normal outbox coercion for unmarked saved rows.
            try { sequence = item.Value<long?>("sequence"); }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException
                || ex is OverflowException || ex is ArgumentException) { return false; }
            return true;
        }

        private static bool TryOrdinaryId(JObject item, out string id)
        {
            id = null;
            if (item == null) return false;
            try { id = item.Value<string>("eventId"); }
            catch (Exception ex) when (ex is InvalidCastException || ex is ArgumentException) { return false; }
            return true;
        }

        private static bool IsOwnedRecord(string line, JObject parsed)
        {
            if (parsed != null) return HasOwnedHistoryMarker(parsed);
            if (line.IndexOf(OwnedHistoryMarker, StringComparison.Ordinal) < 0
                && line.IndexOf("\\u", StringComparison.Ordinal) < 0) return false;

            // A torn final record can contain its complete top-level marker
            // before the syntax error. Its sequence is intentionally unknown.
            try
            {
                using (var reader = new JsonTextReader(new StringReader(line)))
                {
                    if (!reader.Read() || reader.TokenType != JsonToken.StartObject) return false;
                    while (reader.Read())
                        if (reader.TokenType == JsonToken.PropertyName && reader.Depth == 1
                            && string.Equals(reader.Value as string, OwnedHistoryMarker, StringComparison.Ordinal))
                            return true;
                }
            }
            catch (JsonException) { }
            return false;
        }

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

        internal static void AppendRecords(string path, string records, bool flushToDisk = false)
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
                if (flushToDisk) file.Flush(true);
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
