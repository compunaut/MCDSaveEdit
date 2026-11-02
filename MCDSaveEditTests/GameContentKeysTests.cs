using System;
using MCDSaveEdit.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MCDSaveEditTests
{
    [TestClass]
    [DoNotParallelize]
    public class GameContentKeysTests
    {
        [TestMethod]
        public void SessionKeyOverridesEnvironmentWithoutPersistingAndCanBeCleared()
        {
            string? previous = Environment.GetEnvironmentVariable("MCD_PAK_AES_KEY");
            try
            {
                GameContentKeys.SetSessionKey(null);
                Environment.SetEnvironmentVariable("MCD_PAK_AES_KEY", null);
                Assert.IsFalse(GameContentKeys.HasConfiguredKey);
                Assert.AreEqual(0, GameContentKeys.Load().Length);
                string environmentKey = new string('1', 64);
                string enteredKey = new string('2', 64);
                Environment.SetEnvironmentVariable("MCD_PAK_AES_KEY", environmentKey);
                GameContentKeys.SetSessionKey("  0x" + enteredKey + "  ");
                Assert.IsTrue(GameContentKeys.HasConfiguredKey);
                Assert.AreEqual(enteredKey, GameContentKeys.Load()[0].key);
                Assert.AreEqual(environmentKey, Environment.GetEnvironmentVariable("MCD_PAK_AES_KEY"));
                GameContentKeys.SetSessionKey(null);
                Assert.AreEqual(environmentKey, GameContentKeys.Load()[0].key);
            }
            finally
            {
                GameContentKeys.SetSessionKey(null);
                Environment.SetEnvironmentVariable("MCD_PAK_AES_KEY", previous);
            }
        }

        [TestMethod]
        public void InvalidKeyDoesNotReplaceValidSessionKeyOrExposeInput()
        {
            try
            {
                string key = new string('3', 64);
                GameContentKeys.SetSessionKey(key);
                const string invalid = "invalid-test-key";
                var error = Assert.ThrowsExactly<InvalidOperationException>(() => GameContentKeys.SetSessionKey(invalid));
                Assert.IsFalse(error.Message.Contains(invalid));
                Assert.AreEqual(key, GameContentKeys.Load()[0].key);
            }
            finally { GameContentKeys.SetSessionKey(null); }
        }
    }
}
