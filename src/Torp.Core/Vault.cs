using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Torp.Core
{
    public sealed class Vault : IDisposable
    {
        public const int MaximumNoteBytes = 2 * 1024 * 1024;
        private readonly object gate = new object();
        private readonly FileStream lease;
        private bool disposed;
        public string Root { get; private set; }

        public Vault(string root)
        {
            if (String.IsNullOrWhiteSpace(root)) { throw new ArgumentException("Choose a vault folder."); }
            Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            RejectReparsePoints(Root);
            Directory.CreateDirectory(Root);
            string metadata = Path.Combine(Root, ".torp");
            RejectReparsePoints(metadata);
            Directory.CreateDirectory(metadata);
            string lockPath = Path.Combine(metadata, "session.lock");
            RejectReparsePoints(lockPath);
            try
            {
                lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException error)
            {
                throw new IOException("This vault is open in another Torp window, or its lock file is unavailable.", error);
            }
        }

        public static string NormalizePath(string relativePath)
        {
            if (String.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                throw new ArgumentException("Use a note path relative to the vault.");
            }
            string normalized = relativePath.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            foreach (string part in segments)
            {
                string deviceName = part.Split('.')[0].ToUpperInvariant();
                if (part.Length == 0 || part == "." || part == ".." || part[0] == '.' ||
                    part.Trim() != part || part.EndsWith(".", StringComparison.Ordinal) ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    deviceName == "CON" || deviceName == "PRN" || deviceName == "AUX" || deviceName == "NUL" ||
                    System.Text.RegularExpressions.Regex.IsMatch(deviceName, @"^(COM|LPT)[0-9¹²³]$"))
                {
                    throw new ArgumentException("Use ordinary folder and note names without reserved characters or dot segments.");
                }
            }
            if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { normalized += ".md"; }
            return normalized;
        }

        private string Resolve(string relativePath)
        {
            EnsureOpen();
            string path = Path.GetFullPath(Path.Combine(Root, NormalizePath(relativePath).Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) { throw new ArgumentException("The note must be inside the vault."); }
            // Leave room for sibling temporary files on XP's MAX_PATH filesystem APIs.
            if (path.Length > 210) { throw new PathTooLongException("Use a shorter vault or note path (maximum 210 characters)."); }
            RejectReparsePoints(path);
            return path;
        }

        private static void RejectReparsePoints(string path)
        {
            string current = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            while (!String.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Symbolic links and directory junctions are not supported inside vault paths.");
                }
                current = Path.GetDirectoryName(current);
            }
        }

        private string MetadataFolder(string name)
        {
            string path = Path.Combine(Root, ".torp", name);
            RejectReparsePoints(path);
            Directory.CreateDirectory(path);
            return path;
        }

        private static byte[] ReadBytes(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaximumNoteBytes) { throw new IOException("This note exceeds the 2 MB editing limit."); }
                byte[] bytes = new byte[(int)stream.Length];
                int position = 0;
                while (position < bytes.Length)
                {
                    int count = stream.Read(bytes, position, bytes.Length - position);
                    if (count == 0) { throw new IOException("The note changed while it was being read."); }
                    position += count;
                }
                return bytes;
            }
        }

        private static string Decode(byte[] bytes)
        {
            // Strict UTF-8 avoids silently replacing bytes in a non-text file.
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            string text = new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start);
            if (text.IndexOf('\0') >= 0) { throw new IOException("This file contains binary NUL characters and cannot be edited as Markdown."); }
            return text;
        }

        public NoteDocument Read(string relativePath)
        {
            lock (gate)
            {
                string normalized = NormalizePath(relativePath);
                byte[] bytes = ReadBytes(Resolve(normalized));
                return new NoteDocument(normalized, Decode(bytes), AtomicFile.Hash(bytes));
            }
        }

        public bool Exists(string relativePath)
        {
            lock (gate) { return File.Exists(Resolve(relativePath)); }
        }

        public NoteDocument Create(string relativePath, string text)
        {
            lock (gate)
            {
                string normalized = NormalizePath(relativePath);
                string path = Resolve(normalized);
                ValidateText(text);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // A sibling temporary plus non-overwriting move prevents partial or duplicate notes.
                AtomicFile.WriteNew(path, text);
                return Read(normalized);
            }
        }

        private static void ValidateText(string text)
        {
            if (text == null) { throw new ArgumentNullException("text"); }
            if (text.IndexOf('\0') >= 0) { throw new IOException("Markdown notes cannot contain binary NUL characters."); }
            if (new UTF8Encoding(false, true).GetByteCount(text) > MaximumNoteBytes)
            {
                throw new IOException("This note exceeds the 2 MB editing limit.");
            }
        }

        private void VerifyRevision(string path, string expectedRevision)
        {
            if (!File.Exists(path) || AtomicFile.Hash(ReadBytes(path)) != expectedRevision)
            {
                throw new VaultConflictException("The note changed outside Torp. Your edits are still here. Save a copy or reload the disk version.");
            }
        }

        private string HistoryFolder(string relativePath)
        {
            string key = AtomicFile.Hash(Encoding.UTF8.GetBytes(NormalizePath(relativePath).ToLowerInvariant()));
            return MetadataFolder(Path.Combine("history", key));
        }

        public NoteDocument Save(NoteDocument original, string text)
        {
            if (original == null) { throw new ArgumentNullException("original"); }
            lock (gate)
            {
                string path = Resolve(original.RelativePath);
                ValidateText(text);
                VerifyRevision(path, original.Revision);
                if (text == original.Text) { return original; }
                string backup = Path.Combine(HistoryFolder(original.RelativePath), UniqueStamp() + ".md");
                AtomicFile.Replace(path, text, backup, delegate { VerifyRevision(path, original.Revision); });
                // ReplaceFile retains the exact replaced bytes. Detect another atomic writer
                // in the remaining check/replace interval and expose its preserved version.
                if (AtomicFile.Hash(ReadBytes(backup)) != original.Revision)
                {
                    throw new VaultConflictException("Another program replaced the note during this save. Your edit is on disk and that program's version is preserved in .torp/history. Save a copy to resolve this conflict.");
                }
                return Read(original.RelativePath);
            }
        }

        public NoteDocument Rename(NoteDocument original, string newRelativePath)
        {
            lock (gate)
            {
                string source = Resolve(original.RelativePath);
                string target = Resolve(newRelativePath);
                VerifyRevision(source, original.Revision);
                if (String.Equals(source, target, StringComparison.Ordinal)) { return original; }
                if (String.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("For a case-only rename, first choose a different name.");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(source, target);
                return Read(newRelativePath);
            }
        }

        public void Trash(NoteDocument original)
        {
            lock (gate)
            {
                string source = Resolve(original.RelativePath);
                VerifyRevision(source, original.Revision);
                string folder = MetadataFolder(Path.Combine("trash", UniqueStamp()));
                string target = Path.Combine(folder, original.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (target.Length >= 250) { throw new PathTooLongException("This note path is too long to move into vault trash."); }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(source, target);
            }
        }

        public IList<TrashEntry> GetTrash()
        {
            lock (gate)
            {
                EnsureOpen();
                List<TrashEntry> entries = new List<TrashEntry>();
                foreach (string folder in Directory.GetDirectories(MetadataFolder("trash")))
                {
                    RejectReparsePoints(folder);
                    foreach (string file in EnumerateMarkdown(folder))
                    {
                        string relative = file.Substring(folder.Length + 1).Replace('\\', '/');
                        entries.Add(new TrashEntry(Path.GetFileName(folder), relative, Directory.GetCreationTimeUtc(folder)));
                    }
                }
                return entries.OrderByDescending(e => e.DeletedUtc).ToList();
            }
        }

        public NoteDocument Restore(TrashEntry entry)
        {
            lock (gate)
            {
                if (entry == null || !System.Text.RegularExpressions.Regex.IsMatch(entry.Id, @"^[0-9]{17}-[a-f0-9]{32}$"))
                {
                    throw new ArgumentException("Invalid trash entry.");
                }
                string target = Resolve(entry.RelativePath);
                string source = Path.Combine(MetadataFolder("trash"), entry.Id, NormalizePath(entry.RelativePath).Replace('/', Path.DirectorySeparatorChar));
                RejectReparsePoints(source);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(source, target);
                return Read(entry.RelativePath);
            }
        }

        public IList<string> GetHistory(string relativePath)
        {
            lock (gate)
            {
                Resolve(relativePath);
                return Directory.GetFiles(HistoryFolder(relativePath), "*.md").Select(Path.GetFileName).OrderByDescending(p => p).ToList();
            }
        }

        public string ReadHistory(string relativePath, string version)
        {
            lock (gate)
            {
                Resolve(relativePath);
                if (String.IsNullOrEmpty(version) || !System.Text.RegularExpressions.Regex.IsMatch(version, @"^[0-9]{17}-[a-f0-9]{32}\.md$"))
                {
                    throw new ArgumentException("Invalid history version.");
                }
                string path = Path.Combine(HistoryFolder(relativePath), version);
                RejectReparsePoints(path);
                return Decode(ReadBytes(path));
            }
        }

        public IList<string> GetPaths()
        {
            lock (gate)
            {
                EnsureOpen();
                return EnumerateMarkdown(Root.TrimEnd(Path.DirectorySeparatorChar))
                    .Select(p => p.Substring(Root.Length).Replace('\\', '/'))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        private static IEnumerable<string> EnumerateMarkdown(string folder)
        {
            RejectReparsePoints(folder);
            foreach (string file in Directory.GetFiles(folder))
            {
                if (!Path.GetFileName(file).StartsWith(".", StringComparison.Ordinal) &&
                    Path.GetExtension(file).Equals(".md", StringComparison.OrdinalIgnoreCase) &&
                    (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                {
                    yield return file;
                }
            }
            foreach (string directory in Directory.GetDirectories(folder))
            {
                if (!Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal) &&
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                {
                    foreach (string file in EnumerateMarkdown(directory)) { yield return file; }
                }
            }
        }

        private static string UniqueStamp()
        {
            return DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        }

        private void EnsureOpen()
        {
            if (disposed) { throw new ObjectDisposedException("Vault"); }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (!disposed) { disposed = true; lease.Dispose(); }
            }
        }
    }
}
