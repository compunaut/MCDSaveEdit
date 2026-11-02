using System;

namespace MCDSaveEdit.Data
{
    public static class GameContentKeys
    {
        private static string? sessionKey;

        public static bool HasConfiguredKey => !string.IsNullOrWhiteSpace(sessionKey ?? Environment.GetEnvironmentVariable("MCD_PAK_AES_KEY"));

        // Kept in memory only; never saved to the registry, disk, or environment.
        public static void SetSessionKey(string? value)
        {
            sessionKey = Normalize(value);
        }

        // Only needed for installed game asset access, never for save editing.
        // Do not log the value or compile a user's key into distributed binaries.
        public static AesKey[] Load()
        {
            string? value = Normalize(sessionKey ?? Environment.GetEnvironmentVariable("MCD_PAK_AES_KEY"));
            return value == null ? Array.Empty<AesKey>() : new[] { new AesKey(value, "user supplied") };
        }

        private static string? Normalize(string? value)
        {
            value = value?.Trim();
            if (string.IsNullOrEmpty(value)) return null;
            string hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.Substring(2) : value;
            if (hex.Length != 64 || !System.Linq.Enumerable.All(hex, Uri.IsHexDigit))
                throw new InvalidOperationException("Enter a game-content AES key containing 64 hexadecimal characters (optional 0x prefix), or correct MCD_PAK_AES_KEY.");
            return hex;
        }
    }
}
