using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Torp.Core
{
    internal static class AtomicFile
    {
        internal static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
            }
        }

        internal static void Write(string path, string text, string backup)
        {
            Write(path, text, backup, false, null);
        }

        internal static void Replace(string path, string text, string backup, Action verifyBeforeCommit)
        {
            Write(path, text, backup, false, verifyBeforeCommit);
        }

        internal static void WriteNew(string path, string text)
        {
            Write(path, text, null, true, null);
        }

        private static void Write(string path, string text, string backup, bool createOnly, Action verifyBeforeCommit)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(path), ".torp-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(text);
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (verifyBeforeCommit != null) { verifyBeforeCommit(); }
                if (!createOnly && File.Exists(path))
                {
                    File.Replace(temporary, path, backup);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
