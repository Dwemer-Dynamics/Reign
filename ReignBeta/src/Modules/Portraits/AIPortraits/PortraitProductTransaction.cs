#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace AIPortraits
{
    // Keep each original until the entire validated product has been accepted.
    // WSL's Windows file share does not implement ReplaceFile (Win32 error 50).
    internal static class PortraitProductTransaction
    {
        internal sealed class FileCommit
        {
            internal readonly string Path;
            internal readonly string TemporaryPath;
            internal readonly string BackupPath;
            private readonly Action<string, string, string> replace;
            private readonly Action<string, string> move;
            private bool hadOriginal;
            private bool originalBackedUp;
            private bool committed;

            internal FileCommit(string path, string token,
                Action<string, string, string>? replace = null, Action<string, string>? move = null)
            {
                Path = path;
                TemporaryPath = path + ".tmp." + token;
                BackupPath = path + ".bak." + token;
                this.replace = replace ?? ReplacePreservingOriginal;
                this.move = move ?? ((source, destination) => File.Move(source, destination));
            }

            internal void Commit()
            {
                hadOriginal = File.Exists(Path);
                if (!hadOriginal)
                {
                    move(TemporaryPath, Path);
                    committed = true;
                    return;
                }
                try
                {
                    replace(TemporaryPath, Path, BackupPath);
                    originalBackedUp = true;
                    committed = true;
                }
                catch (Exception ex)
                {
                    // A partially completed replacement also needs rollback. Do not
                    // attempt a fallback if the original or staged file has moved.
                    originalBackedUp = File.Exists(BackupPath);
                    if (!ReplacementUnsupported(ex) || originalBackedUp
                        || !File.Exists(Path) || !File.Exists(TemporaryPath)) throw;
                    move(Path, BackupPath);
                    originalBackedUp = true;
                    move(TemporaryPath, Path);
                    committed = true;
                }
            }

            internal void Rollback()
            {
                if (originalBackedUp)
                {
                    if (!File.Exists(BackupPath))
                        throw new IOException("The original portrait backup is missing: " + BackupPath);
                    if (File.Exists(Path)) File.Delete(Path);
                    move(BackupPath, Path);
                    originalBackedUp = false;
                }
                else if (committed && !hadOriginal && File.Exists(Path)) File.Delete(Path);
                committed = false;
            }

            internal void Cleanup(bool accepted)
            {
                try { if (File.Exists(TemporaryPath)) File.Delete(TemporaryPath); } catch { }
                // A failed rollback must never delete the last recoverable original.
                if (accepted)
                    try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }
            }
        }

        internal static void Commit(IEnumerable<FileCommit> files, Action validate)
        {
            var items = files.ToList();
            bool accepted = false;
            try
            {
                foreach (var item in items) item.Commit();
                validate();
                accepted = true;
            }
            catch (Exception failure)
            {
                var failures = new List<Exception> { failure };
                for (int index = items.Count - 1; index >= 0; index--)
                {
                    try { items[index].Rollback(); }
                    catch (Exception rollback)
                    {
                        failures.Add(new IOException("Portrait restoration failed; retain the original at "
                            + items[index].BackupPath, rollback));
                    }
                }
                if (failures.Count > 1)
                    throw new AggregateException("Portrait replacement failed; original backups were retained for recovery.", failures);
                throw;
            }
            finally { foreach (var item in items) item.Cleanup(accepted); }
        }

        internal static void WriteAllBytes(string path, byte[] bytes)
            => WriteAllBytes((path, bytes));

        internal static void WriteAllBytes(params (string Path, byte[] Bytes)[] files)
        {
            string token = Guid.NewGuid().ToString("N");
            var items = files.Select(file => new FileCommit(file.Path, token)).ToArray();
            try
            {
                for (int index = 0; index < files.Length; index++)
                    File.WriteAllBytes(items[index].TemporaryPath, files[index].Bytes);
                Commit(items, () => { });
            }
            finally
            {
                foreach (var item in items) item.Cleanup(false);
            }
        }

        internal static (string? Master, string? Custom, string? GenerationReceipt, string? OutfitReceipt)
            ReadPublicationBasis((string Master, string Custom, string GenerationReceipt, string OutfitReceipt) paths)
            => (ReadDigestOrAbsent(paths.Master), ReadDigestOrAbsent(paths.Custom), ReadDigestOrAbsent(paths.GenerationReceipt),
                ReadDigestOrAbsent(paths.OutfitReceipt));

        internal static bool PublishIfCurrent(
            (string? Master, string? Custom, string? GenerationReceipt, string? OutfitReceipt) expected,
            (string Master, string Custom, string GenerationReceipt, string OutfitReceipt) paths, Action publish)
        {
            if (ReadPublicationBasis(paths) != expected) return false;
            publish();
            return true;
        }

        private static string? ReadDigestOrAbsent(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var hash = SHA256.Create();
                return Convert.ToBase64String(hash.ComputeHash(stream));
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        internal static bool RemovePendingReservation(ConcurrentDictionary<string, string> pending, string key, string owner)
            => ((ICollection<KeyValuePair<string, string>>)pending).Remove(new KeyValuePair<string, string>(key, owner));

        internal static bool CurrentFileMatches(string path, Func<string, bool> isCurrent)
            => File.Exists(path) && isCurrent(File.ReadAllText(path));

        internal static bool DeleteIfCurrent(string path, object gate, Func<string, bool> isCurrent)
        {
            lock (gate)
            {
                if (!CurrentFileMatches(path, isCurrent)) return false;
                File.Delete(path);
                return true;
            }
        }

        private static bool ReplacementUnsupported(Exception ex)
        {
            if (ex is PlatformNotSupportedException) return true;
            if (!(ex is IOException)) return false;
            int error = ex.HResult & 0xffff;
            return error == 1 || error == 50; // ERROR_INVALID_FUNCTION / ERROR_NOT_SUPPORTED
        }

        internal static bool UsesWslRename(string path) => path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase);

        private static void ReplacePreservingOriginal(string source, string destination, string backup)
        {
            // ReplaceFile is unsupported on WSL's 9P share and can partially
            // create its backup before failing. Use the existing recoverable
            // rename transaction directly on that known filesystem.
            if (UsesWslRename(destination))
            {
                File.Move(destination, backup);
                File.Move(source, destination);
            }
            else File.Replace(source, destination, backup);
        }
    }
}
