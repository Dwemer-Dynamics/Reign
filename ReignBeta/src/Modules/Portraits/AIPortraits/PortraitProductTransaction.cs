#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

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
            internal readonly string Token;
            internal bool OwnedByJournal;
            private readonly Action<string, string, string> replace;
            private readonly Action<string, string> move;
            private bool hadOriginal;
            private bool originalBackedUp;
            private bool committed;

            internal FileCommit(string path, string token,
                Action<string, string, string>? replace = null, Action<string, string>? move = null)
            {
                Path = path;
                Token = token;
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
                if (OwnedByJournal) return;
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

        // Use this only for local, fixed-name portrait products. Callers must
        // hold the same per-directory product lock across recovery and reads
        // or publication. The generic path also serves WSL and remains separate.
        internal const string RecoveryMarkerName = ".reign_portrait_transaction_v1";
        internal const string CommittedRecoveryMarkerName = RecoveryMarkerName + ".committed";
        private const int MaxRecoveryFiles = 32;
        private const int MaxRecoveryMarkerBytes = 32768;

        private sealed class RecoveryFile
        {
            internal readonly string Name;
            internal readonly string Token;
            internal readonly string? Before;
            internal readonly string After;

            internal RecoveryFile(string name, string token, string? before, string after)
            {
                Name = name;
                Token = token;
                Before = before;
                After = after;
            }

            internal string Destination(string directory) => System.IO.Path.Combine(directory, Name);
            internal string Staged(string directory) => Destination(directory) + ".tmp." + Token;
            internal string Backup(string directory) => Destination(directory) + ".bak." + Token;
            internal string BeforeImage(string directory, string id) => Destination(directory) + ".pre." + id;
            internal string RestoreImage(string directory, string id) => Destination(directory) + ".undo." + id;
            internal string DisplacedImage(string directory, string id) => Destination(directory) + ".post." + id;
        }

        internal static void WriteRecoverable(string directory, IReadOnlyCollection<string> managedNames,
            params (string Path, byte[] Bytes)[] files)
            => WriteRecoverable(directory, managedNames, null, null, null, null, files);

        internal static void WriteRecoverable(string directory, IReadOnlyCollection<string> managedNames,
            Action<int, string>? validateStaged, Action? validateBasis, Action? validate,
            Action<bool>? observeCommitBoundary, params (string Path, byte[] Bytes)[] files)
        {
            directory = ResolveRecoveryDirectory(directory);
            var names = ValidateManagedNames(managedNames);
            string token = Guid.NewGuid().ToString("N");
            var items = files.Select(file => new FileCommit(file.Path, token)).ToArray();
            ValidateRecoveryTargets(directory, names, items);
            if (!TryRecover(directory, managedNames))
                throw new IOException("An earlier portrait publication could not be recovered.");
            int stagedCount = 0;
            try
            {
                for (int index = 0; index < files.Length; index++)
                {
                    using (var writer = OpenNewDurableFile(items[index].TemporaryPath))
                    {
                        stagedCount++;
                        writer.Write(files[index].Bytes, 0, files[index].Bytes.Length);
                        writer.Flush(true);
                    }
                    validateStaged?.Invoke(index, items[index].TemporaryPath);
                }
                CommitRecoverable(directory, items, managedNames, validate ?? (() => { }),
                    validateBasis, observeCommitBoundary);
            }
            finally
            {
                for (int index = 0; index < stagedCount; index++)
                {
                    var item = items[index];
                    if (item.OwnedByJournal) continue;
                    using var sha = SHA256.Create();
                    TryDeletePreparedCopy(item.TemporaryPath, Convert.ToBase64String(sha.ComputeHash(files[index].Bytes)));
                }
            }
        }

        internal static void CommitRecoverable(string directory, IEnumerable<FileCommit> files,
            IReadOnlyCollection<string> managedNames, Action validate,
            Action? validateBasis = null, Action<bool>? observeCommitBoundary = null)
        {
            directory = ResolveRecoveryDirectory(directory);
            var names = ValidateManagedNames(managedNames);
            var items = files.ToArray();
            ValidateRecoveryTargets(directory, names, items);
            if (!TryRecover(directory, managedNames))
                throw new IOException("An earlier portrait publication could not be recovered.");

            string marker = System.IO.Path.Combine(directory, RecoveryMarkerName);
            string committedMarker = System.IO.Path.Combine(directory, CommittedRecoveryMarkerName);
            string id = Guid.NewGuid().ToString("N");
            var entries = new List<RecoveryFile>(items.Length);
            foreach (var item in items)
            {
                if (RecoveryDigest(item.BackupPath) != null)
                    throw new IOException("A staged portrait backup already exists.");
                // Flush staged bytes even when the caller used an ordinary write.
                RequireRegularRecoveryFile(item.TemporaryPath);
                using (var staged = new FileStream(item.TemporaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    staged.Flush(true);
                string after = RecoveryDigest(item.TemporaryPath)
                    ?? throw new IOException("A staged portrait product is missing.");
                entries.Add(new RecoveryFile(System.IO.Path.GetFileName(item.Path), item.Token, RecoveryDigest(item.Path), after));
            }

            if (!HasExactRecoverySpellings(directory, id, entries))
                throw new IOException("A portrait product has an ambiguous file spelling.");

            var preparedCopies = new List<RecoveryFile>(items.Length);
            bool publishingMarker = false;
            try
            {
                foreach (var entry in entries)
                {
                    if (entry.Before == null) continue;
                    CopyRecoveryImage(entry.Destination(directory), entry.BeforeImage(directory, id), entry.Before);
                    preparedCopies.Add(entry);
                }

                if (entries.Any(entry => RecoveryDigest(entry.Destination(directory)) != entry.Before))
                    throw new IOException("The current portrait changed before publication.");
                validateBasis?.Invoke();
                foreach (var item in items) item.OwnedByJournal = true;
                publishingMarker = true;
                string preparedDigest = PublishRecoveryMarker(marker, id, entries);

                for (int index = 0; index < items.Length; index++)
                {
                    var entry = entries[index];
                    if (RecoveryDigest(entry.Destination(directory)) != entry.Before
                        || RecoveryDigest(entry.Staged(directory)) != entry.After
                        || RecoveryDigest(entry.Backup(directory)) != null)
                        throw new IOException("A portrait publication file was changed by another writer.");
                    items[index].Commit();
                }

                observeCommitBoundary?.Invoke(false);
                validate();
                if (entries.Any(entry => RecoveryDigest(entry.Destination(directory)) != entry.After))
                    throw new IOException("The complete portrait publication did not match its staged files.");
                if (RecoveryDigest(marker) != preparedDigest)
                    throw new IOException("The portrait publication marker changed before commitment.");
                // WSL cannot use ReplaceFile. Both phases are the same flushed
                // manifest, and moving to an absent name is atomic on its share.
                File.Move(marker, committedMarker);
                observeCommitBoundary?.Invoke(true);
            }
            catch (Exception failure)
            {
                if (publishingMarker)
                {
                    if (!TryRecover(directory, managedNames))
                        throw new IOException("Portrait recovery is required. Original and unknown files were retained.", failure);
                }
                else
                {
                    foreach (var entry in preparedCopies)
                        TryDeletePreparedCopy(entry.BeforeImage(directory, id), entry.Before!);
                }
                throw;
            }
            if (!TryRecover(directory, managedNames))
                throw new IOException("The portrait was published, but its recovery state could not be retired.");
        }

        // A renderer can decline an uncertain product and ask a background
        // caller to recover it under the directory lock.
        internal static bool HasRecoveryJournal(string directory)
        {
            try
            {
                string root = ResolveRecoveryDirectory(directory);
                return RequireRegularRecoveryFile(System.IO.Path.Combine(root, RecoveryMarkerName))
                    || RequireRegularRecoveryFile(System.IO.Path.Combine(root, CommittedRecoveryMarkerName));
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
            catch (ArgumentException) { return true; }
            catch (NotSupportedException) { return true; }
        }

        // False means neither a reader nor a new writer may use this product.
        internal static bool TryRecover(string directory, IReadOnlyCollection<string> managedNames,
            Action<string, string>? recoveryRename = null)
        {
            try
            {
                directory = ResolveRecoveryDirectory(directory);
                var names = ValidateManagedNames(managedNames);
                string preparedMarker = System.IO.Path.Combine(directory, RecoveryMarkerName);
                string committedMarker = System.IO.Path.Combine(directory, CommittedRecoveryMarkerName);
                bool prepared = RequireRegularRecoveryFile(preparedMarker);
                bool committed = RequireRegularRecoveryFile(committedMarker);
                if (prepared && committed) return false;
                if (!prepared && !committed) return true;

                string marker = committed ? committedMarker : preparedMarker;
                string? markerDigest = RecoveryDigest(marker);
                if (markerDigest == null) return false;
                var (id, entries) = ReadRecoveryMarker(marker, names);
                if (RecoveryDigest(marker) != markerDigest || !HasExactRecoverySpellings(directory, id, entries)
                    || !CheckRecoveryFiles(directory, id, committed, entries)) return false;
                if (!committed)
                {
                    recoveryRename ??= File.Move;
                    // Preflight every destination and backup before changing any
                    // file. Recover in reverse write order and recheck each path.
                    foreach (var entry in entries.AsEnumerable().Reverse())
                        if (!RestoreRecoveryFile(directory, id, entry, recoveryRename)) return false;
                }

                if (entries.Any(entry => RecoveryDigest(entry.Destination(directory)) != (committed ? entry.After : entry.Before)))
                    return false;
                foreach (var entry in entries)
                {
                    if (!DeleteRecoveryFile(entry.Staged(directory), entry.After)
                        || !DeleteRecoveryFile(entry.Backup(directory), entry.Before)
                        || !DeleteRecoveryFile(entry.BeforeImage(directory, id), entry.Before)
                        || !DeleteRecoveryFile(entry.RestoreImage(directory, id), entry.Before)
                        || !DeleteRecoveryFile(entry.DisplacedImage(directory, id), entry.After)) return false;
                }
                return DeleteRecoveryFile(marker, markerDigest);
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (FormatException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        private static bool HasExactRecoverySpellings(string directory, string id, IEnumerable<RecoveryFile> entries)
        {
            var exact = new HashSet<string>(StringComparer.Ordinal) { RecoveryMarkerName, CommittedRecoveryMarkerName };
            foreach (var entry in entries)
            {
                exact.Add(entry.Name);
                exact.Add(System.IO.Path.GetFileName(entry.Staged(directory)));
                exact.Add(System.IO.Path.GetFileName(entry.Backup(directory)));
                exact.Add(System.IO.Path.GetFileName(entry.BeforeImage(directory, id)));
                exact.Add(System.IO.Path.GetFileName(entry.RestoreImage(directory, id)));
                exact.Add(System.IO.Path.GetFileName(entry.DisplacedImage(directory, id)));
            }

            var aliases = new HashSet<string>(exact, StringComparer.OrdinalIgnoreCase);
            return Directory.EnumerateFileSystemEntries(directory).Select(System.IO.Path.GetFileName)
                .All(name => !aliases.Contains(name) || exact.Contains(name));
        }

        private static bool CheckRecoveryFiles(string directory, string id, bool committed, IEnumerable<RecoveryFile> entries)
        {
            foreach (var entry in entries)
            {
                string? current = RecoveryDigest(entry.Destination(directory));
                string? original = RecoveryDigest(entry.BeforeImage(directory, id));
                string? backup = RecoveryDigest(entry.Backup(directory));
                string? staged = RecoveryDigest(entry.Staged(directory));
                string? restore = RecoveryDigest(entry.RestoreImage(directory, id));
                string? displaced = RecoveryDigest(entry.DisplacedImage(directory, id));
                if ((original != null && original != entry.Before) || (backup != null && backup != entry.Before)
                    || (staged != null && staged != entry.After) || (restore != null && restore != entry.Before)
                    || (displaced != null && (committed || entry.Before == null || displaced != entry.After)))
                    return false;
                if (committed)
                {
                    if (current != entry.After) return false;
                }
                else if (current != entry.Before && (!CanRevertRecoveryFile(entry, current, backup, displaced)
                    || (entry.Before != null && original != entry.Before)))
                    return false;
            }
            return true;
        }

        private static bool RestoreRecoveryFile(string directory, string id, RecoveryFile entry,
            Action<string, string> recoveryRename)
        {
            string target = entry.Destination(directory);
            string displacedPath = entry.DisplacedImage(directory, id);
            string? current = RecoveryDigest(target);
            if (current == entry.Before) return true;
            string? displaced = RecoveryDigest(displacedPath);
            if (!CanRevertRecoveryFile(entry, current, RecoveryDigest(entry.Backup(directory)), displaced)) return false;

            if (entry.Before == null)
                return DeleteRecoveryFile(target, entry.After);

            string restore = entry.RestoreImage(directory, id);
            string? staged = RecoveryDigest(restore);
            if (staged != null && staged != entry.Before) return false;
            if (staged == null) CopyRecoveryImage(entry.BeforeImage(directory, id), restore, entry.Before);
            if (RecoveryDigest(target) != current || RecoveryDigest(restore) != entry.Before) return false;

            if (current != null)
            {
                // Move the exact after-image aside first on every filesystem.
                // Its known bytes make an interrupted rename resumable.
                if (displaced != null || RecoveryDigest(displacedPath) != null) return false;
                recoveryRename(target, displacedPath);
                if (RecoveryDigest(displacedPath) != entry.After || RecoveryDigest(target) != null) return false;
            }
            else if (!CanRevertRecoveryFile(entry, null, RecoveryDigest(entry.Backup(directory)),
                RecoveryDigest(displacedPath))) return false;
            if (RecoveryDigest(restore) != entry.Before) return false;
            recoveryRename(restore, target);
            return RecoveryDigest(target) == entry.Before;
        }

        private static bool CanRevertRecoveryFile(RecoveryFile entry, string? current, string? backup, string? displaced)
            => current == entry.After
                // A forward or reverse rename can move the destination first.
                // Absence alone does not show that this transaction removed it.
                || (current == null && entry.Before != null && (backup == entry.Before || displaced == entry.After));

        private static string ResolveRecoveryDirectory(string directory)
        {
            string full = System.IO.Path.GetFullPath(directory);
            char separator = System.IO.Path.DirectorySeparatorChar;
            if (full.TrimEnd(separator) == System.IO.Path.GetPathRoot(full)?.TrimEnd(separator)
                || (full.StartsWith(@"\\", StringComparison.Ordinal) && !IsLocalWslProductDirectory(full)))
                throw new IOException("Recoverable portrait publication requires a local product directory or recognized WSL share.");
            try
            {
                const FileAttributes directoryBits = FileAttributes.Directory | FileAttributes.ReparsePoint;
                if ((File.GetAttributes(full) & directoryBits) != FileAttributes.Directory)
                    throw new IOException("An unexpected filesystem entry occupies the portrait product directory.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            return full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }

        private static bool IsLocalWslProductDirectory(string full)
        {
            if (!UsesWslRename(full)) return false;
            int start = full.IndexOf(System.IO.Path.DirectorySeparatorChar, 2) + 1;
            int end = full.IndexOf(System.IO.Path.DirectorySeparatorChar, start);
            if (start <= 2 || end <= start || end == full.Length - 1) return false;
            string distro = full.Substring(start, end - start);
            return distro.Length <= 64 && IsAsciiNameLetter(distro[0])
                && distro.All(character => IsAsciiNameLetter(character) || character == '.' || character == '_' || character == '-');
        }

        private static bool IsAsciiNameLetter(char value) =>
            (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') || (value >= '0' && value <= '9');
        private static HashSet<string> ValidateManagedNames(IReadOnlyCollection<string> names)
        {
            var exact = new HashSet<string>(names, StringComparer.Ordinal);
            if (names.Count == 0 || names.Count > MaxRecoveryFiles
                || exact.Count != names.Count || new HashSet<string>(exact, StringComparer.OrdinalIgnoreCase).Count != exact.Count
                || exact.Any(name => !IsManagedRecoveryName(name)))
                throw new ArgumentException("Portrait recovery requires a fixed, unambiguous file allowlist.", nameof(names));
            return exact;
        }

        private static bool IsManagedRecoveryName(string name) => !string.IsNullOrEmpty(name) && name.Length <= 64
            && name != "." && name != ".." && !name.EndsWith(".", StringComparison.Ordinal)
            && !name.StartsWith(".reign_portrait_transaction", StringComparison.OrdinalIgnoreCase)
            && name.All(character => (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9') || character == '.' || character == '_' || character == '-');

        private static void ValidateRecoveryTargets(string directory, HashSet<string> names, IReadOnlyCollection<FileCommit> files)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (files.Count == 0 || files.Count > names.Count)
                throw new ArgumentException("No valid recoverable portrait files were supplied.", nameof(files));
            foreach (var file in files)
            {
                string full = System.IO.Path.GetFullPath(file.Path);
                string name = System.IO.Path.GetFileName(full);
                if (full != System.IO.Path.Combine(directory, name) || !names.Contains(name) || !seen.Add(name)
                    || !IsRecoveryId(file.Token) || file.TemporaryPath != file.Path + ".tmp." + file.Token
                    || file.BackupPath != file.Path + ".bak." + file.Token)
                    throw new ArgumentException("A recoverable portrait file is outside its named product.", nameof(files));
            }
        }

        private static bool RequireRegularRecoveryFile(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    throw new IOException("An unexpected filesystem entry occupies a portrait recovery path.");
                return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        private static string? RecoveryDigest(string path)
            => RequireRegularRecoveryFile(path) ? ReadDigestOrAbsent(path) : null;

        private static FileStream OpenNewDurableFile(string path)
            => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);

        private static void CopyRecoveryImage(string sourcePath, string destination, string expectedDigest)
        {
            if (RecoveryDigest(sourcePath) != expectedDigest)
                throw new IOException("A portrait recovery source did not match its recorded bytes.");
            using (var source = File.OpenRead(sourcePath))
            using (var copy = OpenNewDurableFile(destination))
            {
                source.CopyTo(copy);
                copy.Flush(true);
            }
            if (RecoveryDigest(destination) != expectedDigest)
                throw new IOException("A staged portrait recovery image changed.");
        }

        private static bool DeleteRecoveryFile(string path, string? expected)
        {
            string? current = RecoveryDigest(path);
            if (current == null) return true;
            if (current != expected) return false;
            File.Delete(path);
            return true;
        }

        private static void TryDeletePreparedCopy(string path, string digest)
        {
            try { DeleteRecoveryFile(path, digest); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string PublishRecoveryMarker(string marker, string id, List<RecoveryFile> entries)
        {
            string stagedMarker = marker + "." + id + ".prepare";
            using (var stream = OpenNewDurableFile(stagedMarker))
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(RecoveryMarkerName);
                    writer.Write(id);
                    writer.Write(entries.Count);
                    foreach (var entry in entries)
                    {
                        writer.Write(entry.Name);
                        writer.Write(entry.Token);
                        writer.Write(entry.Before ?? "");
                        writer.Write(entry.After);
                    }
                }
                stream.Flush(true);
            }
            // Publish the phase atomically. An interrupted staging write is
            // never authority to change the current product.
            string digest = RecoveryDigest(stagedMarker)
                ?? throw new IOException("The staged portrait recovery marker is missing.");
            File.Move(stagedMarker, marker);
            return digest;
        }

        private static (string Id, List<RecoveryFile> Entries) ReadRecoveryMarker(string marker, HashSet<string> names)
        {
            using var stream = File.OpenRead(marker);
            if (stream.Length > MaxRecoveryMarkerBytes) throw new InvalidDataException("Oversized portrait recovery marker.");
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            string schema = reader.ReadString();
            string id = reader.ReadString();
            int count = reader.ReadInt32();
            if (schema != RecoveryMarkerName || !IsRecoveryId(id) || count <= 0 || count > names.Count)
                throw new InvalidDataException("Invalid portrait recovery marker.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<RecoveryFile>(count);
            for (int index = 0; index < count; index++)
            {
                string name = reader.ReadString();
                string token = reader.ReadString();
                string before = reader.ReadString();
                string after = reader.ReadString();
                if (!names.Contains(name) || !seen.Add(name) || !IsRecoveryId(token)
                    || (before.Length != 0 && !IsRecoveryDigest(before)) || !IsRecoveryDigest(after))
                    throw new InvalidDataException("Unexpected file in portrait recovery marker.");
                entries.Add(new RecoveryFile(name, token, before.Length == 0 ? null : before, after));
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected portrait recovery marker bytes.");
            return (id, entries);
        }

        private static bool IsRecoveryId(string value)
            => Guid.TryParseExact(value, "N", out var parsed) && value == parsed.ToString("N");

        private static bool IsRecoveryDigest(string value)
        {
            if (value.Length != 44) return false;
            try { var bytes = Convert.FromBase64String(value); return bytes.Length == 32 && Convert.ToBase64String(bytes) == value; }
            catch (FormatException) { return false; }
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
