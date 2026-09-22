using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    /// <summary>
    /// Serializes JSON once into small pooled UTF-8 segments, then sends those
    /// segments with an exact Content-Length. This avoids both StringContent's
    /// full UTF-16/UTF-8 duplication and large-object-heap request buffers while
    /// remaining compatible with the .NET Framework HTTP transport used by the
    /// Bannerlord client.
    /// </summary>
    internal sealed class ReignJsonHttpContent : HttpContent
    {
        private const int SegmentBytes = 64 * 1024;
        private const int WriterBufferChars = 8 * 1024;
        private readonly List<byte[]> _segments = new List<byte[]>();
        private long _serializedBytes;
        private long _serializationDurationMs;
        private bool _buffersReturned;

        internal ReignJsonHttpContent(JToken payload)
        {
            Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };

            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                using (SegmentedWriteStream bytes = new SegmentedWriteStream(
                    _segments,
                    SegmentBytes))
                using (StreamWriter text = new StreamWriter(
                    bytes,
                    new UTF8Encoding(false),
                    WriterBufferChars,
                    true))
                using (JsonTextWriter json = new JsonTextWriter(text)
                {
                    CloseOutput = false,
                    Formatting = Formatting.None
                })
                {
                    (payload ?? new JObject()).WriteTo(json);
                    json.Flush();
                    text.Flush();
                    _serializedBytes = bytes.BytesWritten;
                }
            }
            catch
            {
                ReturnBuffers();
                throw;
            }
            finally
            {
                timer.Stop();
                _serializationDurationMs = timer.ElapsedMilliseconds;
            }
        }

        internal long SerializedBytes => Interlocked.Read(ref _serializedBytes);
        internal long SerializationDurationMs =>
            Interlocked.Read(ref _serializationDurationMs);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext context)
        {
            return WriteSegmentsAsync(stream);
        }

        private async Task WriteSegmentsAsync(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            long remaining = SerializedBytes;
            foreach (byte[] segment in _segments)
            {
                if (remaining <= 0) break;
                int count = (int)Math.Min(segment.Length, remaining);
                await stream.WriteAsync(
                    segment,
                    0,
                    count,
                    CancellationToken.None).ConfigureAwait(false);
                remaining -= count;
            }

            if (remaining != 0)
                throw new InvalidOperationException(
                    "The pooled JSON request buffer was incomplete.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = SerializedBytes;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ReturnBuffers();
            base.Dispose(disposing);
        }

        private void ReturnBuffers()
        {
            if (_buffersReturned) return;
            _buffersReturned = true;
            foreach (byte[] segment in _segments)
                ArrayPool<byte>.Shared.Return(segment);
            _segments.Clear();
        }

        private sealed class SegmentedWriteStream : Stream
        {
            private readonly List<byte[]> _segments;
            private readonly int _segmentBytes;
            private int _segmentOffset;
            private long _bytesWritten;

            internal SegmentedWriteStream(
                List<byte[]> segments,
                int segmentBytes)
            {
                _segments = segments
                    ?? throw new ArgumentNullException(nameof(segments));
                _segmentBytes = Math.Max(1024, segmentBytes);
            }

            internal long BytesWritten => _bytesWritten;
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _bytesWritten;
            public override long Position
            {
                get => _bytesWritten;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (buffer == null) throw new ArgumentNullException(nameof(buffer));
                if (offset < 0 || count < 0 || offset + count > buffer.Length)
                    throw new ArgumentOutOfRangeException();
                while (count > 0)
                {
                    byte[] segment = WritableSegment();
                    int writable = Math.Min(count,
                        segment.Length - _segmentOffset);
                    Buffer.BlockCopy(buffer, offset, segment,
                        _segmentOffset, writable);
                    offset += writable;
                    count -= writable;
                    _segmentOffset += writable;
                    _bytesWritten += writable;
                }
            }

            public override void WriteByte(byte value)
            {
                byte[] segment = WritableSegment();
                segment[_segmentOffset++] = value;
                _bytesWritten++;
            }

            private byte[] WritableSegment()
            {
                if (_segments.Count == 0
                    || _segmentOffset >= _segments[_segments.Count - 1].Length)
                {
                    _segments.Add(ArrayPool<byte>.Shared.Rent(_segmentBytes));
                    _segmentOffset = 0;
                }
                return _segments[_segments.Count - 1];
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }
        }
    }
}
