using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace PakReader
{
    static class AESDecryptor
    {
        // AES block size in bits (legacy callers use this value as such).
        public const int BLOCK_SIZE = 128;

        public static int FindKey(byte[] data, IList<byte[]> keys)
        {
            if (data == null || data.Length < 16) return -1;
            int length = Math.Min(data.Length / 16 * 16, 1024);
            byte[] sample = new byte[length];
            Array.Copy(data, sample, length);
            for (int i = 0; i < keys.Count; i++)
            {
                byte[] block;
                try { block = DecryptAES(sample, keys[i]); }
                catch (CryptographicException) { continue; }
                int count = BitConverter.ToInt32(block, 0);
                long end = count < 0 ? 4 + (-(long)count - 1) * 2 : 4L + count - 1;
                if (count == 0 || count < -512 || count > 512 || end < 4 || end >= block.Length)
                    continue;
                if (block[(int)end] == 0 && (count >= 0 || (end + 1 < block.Length && block[(int)end + 1] == 0)))
                    return i;
            }
            return -1;
        }

        public static byte[] DecryptAES(byte[] data, byte[] key)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(key);
            if (data.Length % 16 != 0) throw new CryptographicException("Invalid AES block length.");
            using var cipher = Aes.Create();
            cipher.Key = key;
            return cipher.DecryptEcb(data, PaddingMode.None);
        }
    }
}
