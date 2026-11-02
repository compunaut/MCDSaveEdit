using FModel;
using MCDSaveEdit.Data;
using MCDSaveEdit.Services;
using MCDSaveEdit.ViewModels;
using PakReader;
using PakReader.Pak;
using PakReader.Parsers.Objects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
#nullable enable

namespace MCDSaveEdit
{
    public class AppModel
    {
        public MainViewModel mainModel = new MainViewModel();

        public void initPakReader()
        {
            Globals.Game = new FGame(EGame.MinecraftDungeons, EPakVersion.FNAME_BASED_COMPRESSION_METHOD);
        }

        public string? usableGameContentIfExists()
        {
#if DEBUG
            string? result = null;
            foreach (var path in contentPathsToCheck())
            {
                if (string.IsNullOrWhiteSpace(path) == false)
                {
                    if (Directory.Exists(path))
                    {
                        Console.WriteLine($"{path} exists");
                        result = result ?? path;
                    }
                    else
                    {
                        Console.WriteLine($"{path} does not exist");
                    }
                }
            }
            return result;
#else
            foreach(var path in contentPathsToCheck())
            {
                if (string.IsNullOrWhiteSpace(path) == false && Directory.Exists(path))
                {
                    return path;
                }
            }

            return null;
#endif
        }

        private IEnumerable<string?> contentPathsToCheck()
        {
            yield return RegistryTools.GetSetting(Constants.APPLICATION_NAME, Constants.PAK_FILE_LOCATION_REGISTRY_KEY, string.Empty);
            yield return Constants.STEAM_PAKS_FOLDER_PATH;
            yield return Constants.LAUNCHER_PAKS_FOLDER_PATH;
            yield return Constants.XBOX_PC_GAMES_PAKS_FOLDER_PATH;
            //yield return Constants.WINSTORE_PAKS_FOLDER_PATH_IF_EXISTS;
        }

        public static bool gameContentLoaded { get; private set; } = false;

        public async Task loadGameContentAsync(string paksFolderPath)
        {
            var pakIndex = await loadPakIndex(paksFolderPath);
            if (pakIndex == null)
            {
                throw new NullReferenceException($"PakIndex is null. Cannot Continue.");
            }

            var pakContentResolver = new PakContentResolver(pakIndex!, paksFolderPath);
            await pakContentResolver.loadPakFilesAsync(preloadBitmaps: false);
            ImageResolver.instance = pakContentResolver;
            LanguageResolver.instance = pakContentResolver;
            gameContentLoaded = true;

            RegistryTools.SaveSetting(Constants.APPLICATION_NAME, Constants.PAK_FILE_LOCATION_REGISTRY_KEY, paksFolderPath!);

            //Load language strings
            var lang = RegistryTools.GetSetting(Constants.APPLICATION_NAME, Constants.LANG_SPECIFIER_REGISTRY_KEY, Constants.DEFAULT_LANG_SPECIFIER);
            loadLanguageStrings(lang);
        }

        public static string currentLangSpecifier { get; private set; } = string.Empty;

        public static void loadLanguageStrings(string lang)
        {
            if(lang == currentLangSpecifier)
            {
                return;
            }

            R.unloadExternalStrings();

            Console.WriteLine($"Extracting '{lang}' language strings...");
            var stringLibrary = LanguageResolver.instance.loadLanguageStrings(lang);
            if (stringLibrary != null)
            {
                Console.WriteLine($"Loading '{lang}' language strings...");
                R.loadExternalStrings(stringLibrary);
            }

            currentLangSpecifier = lang;

            RegistryTools.SaveSetting(Constants.APPLICATION_NAME, Constants.LANG_SPECIFIER_REGISTRY_KEY, lang);
        }

        private Task<PakIndex?> loadPakIndex(string paksFolderPath)
        {
            var tcs = new TaskCompletionSource<PakIndex?>();
            Task.Run(() =>
            {
                try
                {
                    var filter = new PakFilter(new[] { Constants.PAKS_FILTER_STRING }, false);
                    var pakIndex = new PakIndex(path: paksFolderPath, cacheFiles: true, caseSensitive: true, filter: filter);
                    if (pakIndex.PakFileCount == 0)
                    {
                        throw new FileNotFoundException($"No files were found at {paksFolderPath}");
                    }
                    var success = unlockPakIndex(pakIndex);
                    if (success == false)
                    {
                        throw new InvalidOperationException($"Could not decrypt pak files at {paksFolderPath}");
                    }
                    var versionStr = R.formatMCD_VERSION(mainModel.detectedGameVersion ?? R.ERROR);
                    Console.WriteLine($"Detected Pak Files for {versionStr}");

                    tcs.SetResult(pakIndex);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Could not load Minecraft Dungeons Paks: {e}");
                    tcs.SetException(e);
                }
            });
            return tcs.Task;
        }

        private bool unlockPakIndex(PakIndex pakIndex)
        {
            var keys = GameContentKeys.Load();
            mainModel.detectedGameVersion = null;
            if (keys.Length == 0)
            {
                // Unencrypted indexes need no key. Do this only when none was supplied:
                // a supplied key may also be needed for encrypted payloads in such PAKs.
                pakIndex.UseKey((byte[])null!);
            }
            else
            {
                foreach (var key in keys)
                {
                    string hex = key.key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? key.key.Substring(2) : key.key;
                    byte[] keyBytes = hex.ToBytesKey();
                    if (keyBytes.Length != 32)
                        throw new InvalidOperationException("The game-content key must contain 32 bytes of hexadecimal data.");
                    // User-supplied keys have no associated GUID; try every pending PAK.
                    pakIndex.UseKey(keyBytes);
                    if (pakIndex.InitializedPakFileCount == pakIndex.PakFileCount)
                    {
                        mainModel.detectedGameVersion = key.versions;
                        return true;
                    }
                }
            }
            if (pakIndex.InitializedPakFileCount == pakIndex.PakFileCount)
                return true;
            int pending = pakIndex.PakFileCount - pakIndex.InitializedPakFileCount;
            throw new InvalidOperationException(keys.Length == 0
                ? $"{pending} game-content PAK file(s) could not be opened without a key. Supply a game-content key in Game Files settings, then retry. Save editing remains available without game images."
                : $"{pending} game-content PAK file(s) could not be opened with the supplied key. Check that the key matches this game installation. Unsupported or damaged PAKs can also prevent loading. Save editing remains available without game images.");
        }

        public void unloadGameContent()
        {
            //Clear the path saved in the registry
            RegistryTools.DeleteSetting(Constants.APPLICATION_NAME, Constants.PAK_FILE_LOCATION_REGISTRY_KEY);

            if (gameContentLoaded)
            {
                gameContentLoaded = false;
                var localContentResolver = new LocalContentResolver();
                ImageResolver.instance = localContentResolver;
                LanguageResolver.instance = localContentResolver;
                mainModel.detectedGameVersion = null;
            }

            if (!string.IsNullOrWhiteSpace(currentLangSpecifier))
            {
                R.unloadExternalStrings();
            }
        }

        private static string spaceOutWords(string input)
        {
            var output = new StringBuilder();
            for (int ii = 0; ii < input.Length; ii++)
            {
                var letter = input[ii];
                if (ii > 0 && char.IsUpper(letter) && output[output.Length] != ' ')
                {
                    output.Append(' ');
                }
                output.Append(letter);
            }
            return output.ToString();
        }

    }
}
