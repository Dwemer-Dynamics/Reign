using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ReignBeta.Save
{
    public static class ReignSavePayloadCodec
    {
        private const string EnvelopePrefix = "reign-gzip-v1:";
        private const int MaximumChunkCharacters = 12000;

        public static List<string> Encode(string payload)
        {
            byte[] source = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            byte[] compressed;
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionMode.Compress, true))
                    gzip.Write(source, 0, source.Length);
                compressed = output.ToArray();
            }

            string envelope = EnvelopePrefix + Convert.ToBase64String(compressed);
            List<string> chunks = new List<string>(
                Math.Max(1, (envelope.Length + MaximumChunkCharacters - 1) / MaximumChunkCharacters));
            for (int offset = 0; offset < envelope.Length; offset += MaximumChunkCharacters)
            {
                int length = Math.Min(MaximumChunkCharacters, envelope.Length - offset);
                string chunk = envelope.Substring(offset, length);
                AssertSafeChunk(chunk);
                chunks.Add(chunk);
            }
            return chunks;
        }

        public static string Decode(List<string> chunks)
        {
            if (chunks == null || chunks.Count == 0)
                throw new InvalidDataException("The Reign save payload has no chunks.");

            StringBuilder envelope = new StringBuilder();
            foreach (string chunk in chunks)
            {
                AssertSafeChunk(chunk);
                envelope.Append(chunk);
            }
            string value = envelope.ToString();
            if (!value.StartsWith(EnvelopePrefix, StringComparison.Ordinal))
                throw new InvalidDataException("The Reign save payload format is not supported.");

            byte[] compressed = Convert.FromBase64String(value.Substring(EnvelopePrefix.Length));
            using (MemoryStream input = new MemoryStream(compressed))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                gzip.CopyTo(output);
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private static void AssertSafeChunk(string chunk)
        {
            if (chunk == null)
                throw new InvalidDataException("A Reign save payload chunk is null.");
            int archiveEntryBytes = Encoding.UTF8.GetByteCount(chunk) + sizeof(int);
            if (archiveEntryBytes > MaximumChunkCharacters + sizeof(int))
                throw new InvalidDataException(
                    "A Reign save payload chunk exceeds the Bannerlord archive safety limit.");
        }
    }
}
