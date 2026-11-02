using DungeonTools.Save.File;
using MCDSaveEdit.Services;
using System;
using System.IO;
using System.Threading.Tasks;
#nullable enable

namespace MCDSaveEdit.Logic
{
    public static class FileProcessHelper
    {
        private const long ProfileSizeLimit = 64 * 1024 * 1024 + 16;

        public static async ValueTask ProcessFile(FileInfo file, bool overwrite)
        {
            if (!file.Exists)
            {
                EventLogger.logError($"File \"{file.FullName}\" could not be found and has been skipped.");
                return;
            }

            using FileStream inputStream = file.OpenRead();
            if (inputStream.Length > ProfileSizeLimit) throw new InvalidDataException("Save exceeds the supported size limit.");
            bool encrypted = SaveFileHandler.IsFileEncrypted(inputStream);

            using Stream? processed = encrypted ? await Decrypt(inputStream) : await Encrypt(inputStream);
            if (processed == null)
            {
                EventLogger.logError($"Content of file \"{file.Name}\" could not be converted to a supported format.");
                return;
            }

            processed.Seek(0, SeekOrigin.Begin);
            string outputFile = GetOutputFilePath(file, encrypted, overwrite);
            await AtomicFileWriter.WriteAsync(processed, outputFile, overwrite);
        }

        public static async ValueTask<Stream?> Decrypt(Stream data)
        {
            Stream decrypted = await AesEncryptionProvider.DecryptAsync(data);
            Stream result = SaveFileHandler.RemoveTrailingZeroes(decrypted);
            result.Seek(0, SeekOrigin.Begin);
            return result;
        }

        public static async ValueTask<Stream?> Encrypt(Stream data)
        {
            using Stream encrypted = await AesEncryptionProvider.EncryptAsync(data);
            Stream result = SaveFileHandler.PrependMagicToEncrypted(encrypted);
            result.Seek(0, SeekOrigin.Begin);
            return result;
        }

        private static string GetOutputFilePath(FileInfo fileInfo, bool isEncrypted, bool overwrite)
        {
            string targetExtension = fileInfo.Extension.ToUpperInvariant() switch
            {
                "" => isEncrypted ? ".json" : "", // Special case for Switch which has no file extension
                _ => isEncrypted ? ".json" : ".dat",
            };

            string idealFileName = $"{Path.GetFileNameWithoutExtension(fileInfo.Name)}{targetExtension}";
            if (string.Equals(fileInfo.Name, idealFileName, StringComparison.OrdinalIgnoreCase))
            {
                idealFileName = $"{Path.GetFileNameWithoutExtension(idealFileName)}_{(isEncrypted ? "Decrypted" : "Encrypted")}{targetExtension}";
            }

            string outFileName = Path.Combine(fileInfo.DirectoryName, idealFileName);
            if (overwrite || !File.Exists(outFileName))
            {
                return outFileName;
            }

            int fileNumber = 1;
            while (File.Exists(outFileName))
            {
                outFileName = Path.Combine(fileInfo.DirectoryName!, $"{Path.GetFileNameWithoutExtension(idealFileName)}_{fileNumber++}{targetExtension}");
            }

            return outFileName;
        }

    }
}
