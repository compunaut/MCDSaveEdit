using System;
using System.IO;
using System.Linq;
using System.Text;

namespace DungeonTools.Save.File {
    public static class SaveFileHandler {
        private static readonly byte[] Magic1 = {0x44, 0x30, 0x30, 0x31};
        private static readonly byte[] Magic2 = {0x00, 0x00, 0x00, 0x00};

        public static bool IsFileEncrypted(Stream stream) {
            if(!stream.CanRead) throw new ArgumentException();
            if(!stream.CanSeek) throw new ArgumentException();

            return StartsWithMagic(stream);
        }

        public static bool StartsWithMagic(Stream stream) {
            if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("A readable, seekable stream is required.");
            if (stream.Length - stream.Position < Magic1.Length + Magic2.Length) return false;
            long position = stream.Position;
            byte[] header = new byte[8];
            int offset = 0;
            while (offset < header.Length) {
                int read = stream.Read(header, offset, header.Length - offset);
                if (read == 0) { stream.Position = position; return false; }
                offset += read;
            }
            if (!Magic1.SequenceEqual(header.Take(4))) { stream.Position = position; return false; }
            if (!Magic2.SequenceEqual(header.Skip(4))) {
                stream.Position = position;
                throw new InvalidDataException("File started with magic but next 4 bytes were non-zero");
            }
            return true;
        }

        public static Stream RemoveTrailingZeroes(Stream stream) {
            using BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, true); // This is not text

            while(reader.BaseStream.Position < reader.BaseStream.Length) {
                if(reader.ReadByte() != 0) {
                    continue;
                }

                // Remove this zero and any after this. supposed to be text but the cipher adds some junk
                stream.SetLength(reader.BaseStream.Position - 1);
                break;
            }

            stream.Position = 0;
            return stream;
        }

        public static Stream PrependMagicToEncrypted(Stream stream) {
            MemoryStream output = new MemoryStream();
            output.Write(Magic1, 0, Magic1.Length);
            output.Write(Magic2, 0, Magic2.Length);
            stream.CopyTo(output);
            return output;
        }
    }
}
