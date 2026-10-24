#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        {
            var item = new FileCommit(path, Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllBytes(item.TemporaryPath, bytes);
                Commit(new[] { item }, () => { });
            }
            finally { item.Cleanup(false); }
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
