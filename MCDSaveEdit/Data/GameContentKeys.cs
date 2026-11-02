using System;

namespace MCDSaveEdit.Data
{
    public static class GameContentKeys
    {
        // Only needed for installed game asset access, never for save editing.
        // Do not log the value or compile a user's key into distributed binaries.
        public static AesKey[] Load()
        {
            string? value = Environment.GetEnvironmentVariable("MCD_PAK_AES_KEY")?.Trim();
            if (string.IsNullOrEmpty(value)) return Array.Empty<AesKey>();
            string hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.Substring(2) : value;
            if (hex.Length != 64 || !System.Linq.Enumerable.All(hex, Uri.IsHexDigit))
                throw new InvalidOperationException("MCD_PAK_AES_KEY must contain a 32-byte hexadecimal game-content key.");
            return new[] { new AesKey(hex, "user supplied") };
        }
    }
}
