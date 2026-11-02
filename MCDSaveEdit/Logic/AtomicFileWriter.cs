using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MCDSaveEditTests")]

namespace MCDSaveEdit.Logic
{
    /// <summary>Stages a complete save beside its destination before publishing it.</summary>
    public static class AtomicFileWriter
    {
        public static Task<FileSnapshot> WriteAsync(Stream source, string destination, bool overwrite, FileSnapshot? expected = null)
            => WriteAsync(source, destination, overwrite, expected, (temporary, path, backup) => File.Replace(temporary, path, backup));

        internal static async Task<FileSnapshot> WriteAsync(Stream source, string destination, bool overwrite,
            FileSnapshot? expected, Action<string, string, string> replace)
        {
            string path = Path.GetFullPath(destination);
            expected ??= FileSnapshot.Capture(path);
            string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (source.CanSeek) source.Position = 0;
                    await source.CopyToAsync(output);
                    output.Flush(flushToDisk: true);
                }
                FileSnapshot written = FileSnapshot.Capture(temporary);
                // Detect external edits both before staging (the caller's opened snapshot)
                // and during it. This comparison is not a filesystem compare-and-swap:
                // another process can still replace the file between this check and commit.
                expected.AssertUnchanged(path);
                if (overwrite && File.Exists(path))
                {
                    // A unique backup survives subsequent saves. Never truncate the original
                    // or emulate replacement with delete+move if the filesystem rejects it.
                    string backup = path + $".{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}.{Guid.NewGuid():N}.bak";
                    replace(temporary, path, backup);
                }
                else
                {
                    // Also protects against another writer claiming a selected filename.
                    File.Move(temporary, path);
                }
                return written;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    /// <summary>A content baseline for rejecting stale saves after external changes.</summary>
    public sealed class FileSnapshot
    {
        private readonly byte[]? digest;
        private FileSnapshot(byte[]? digest) { this.digest = digest; }

        public static FileSnapshot Capture(string path)
        {
            try
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length > 64 * 1024 * 1024 + 16)
                    throw new InvalidDataException("Save exceeds the supported size limit.");
                return new FileSnapshot(SHA256.HashData(input));
            }
            catch (FileNotFoundException) { return new FileSnapshot(null); }
        }

        public void AssertUnchanged(string path)
        {
            var current = Capture(path);
            bool matches = digest == null ? current.digest == null
                : current.digest != null && CryptographicOperations.FixedTimeEquals(digest, current.digest);
            if (!matches)
                throw new IOException("The save changed outside the editor. Reopen it or save to a different filename to avoid overwriting newer progress.");
        }
    }

}
