using System;
using System.IO;
using TaleWorlds.Library;

namespace ReignBeta.Integration
{
    public static class ReignLog
    {
        private const long MaximumLogBytes = 8L * 1024L * 1024L;
        private const long RetainedLogBytes = 4L * 1024L * 1024L;
        private static readonly object FileLock = new object();

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Exception(string context, Exception ex)
        {
            Write("ERROR", context + ": " + ex);
        }

        private static void Write(string level, string message)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] [" + level + "] " + message;
            Debug.Print("[Bannerlord Reign] " + line);

            try
            {
                string directory = Path.Combine(BasePath.Name, "Modules", "ReignBeta", "logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "reignbeta.log");
                lock (FileLock)
                {
                    TrimTail(path);
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging should never threaten campaign stability.
            }
        }

        private static void TrimTail(string path)
        {
            if (!File.Exists(path)) return;
            FileInfo info = new FileInfo(path);
            if (info.Length <= MaximumLogBytes) return;

            int length = (int)Math.Min(info.Length, RetainedLogBytes + 4096L);
            byte[] buffer = new byte[length];
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                stream.Seek(-length, SeekOrigin.End);
                int read = 0;
                while (read < buffer.Length)
                {
                    int amount = stream.Read(buffer, read, buffer.Length - read);
                    if (amount <= 0) break;
                    read += amount;
                }
                int start = 0;
                if (info.Length > length)
                {
                    while (start < read && buffer[start] != (byte)'\n') start++;
                    if (start < read) start++;
                }
                stream.SetLength(0);
                stream.Position = 0;
                stream.Write(buffer, start, Math.Max(0, read - start));
            }
        }
    }
}
