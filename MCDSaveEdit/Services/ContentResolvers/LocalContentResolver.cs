using MCDSaveEdit.Data;
using MCDSaveEdit.Interfaces;
using MCDSaveEdit.Save.Models.Enums;
using MCDSaveEdit.Save.Models.Profiles;
using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
#nullable enable

namespace MCDSaveEdit.Services
{
    public class LocalContentResolver: IImageResolver, ILanguageResolver
    {
        static LocalContentResolver()
        {
            // WPF registers its application pack handler in Application's type
            // initializer. A library/test host may not have created an app yet.
            _ = System.Windows.Application.Current;
        }

        // Reference this assembly explicitly: the host can also be a test runner.
        private const string IMAGES_URI = "pack://application:,,,/MCDSaveEdit;component/Images/";

        public string? path { get { return null; } }
        public IReadOnlyCollection<string> localizationOptions { get { return new List<string>(); } }

        public Dictionary<string, Dictionary<string, string>>? loadLanguageStrings(string langSpecifier) { return null; }

        public BitmapImage? imageSource(string path)
        {
            return null;
        }

        private BitmapImage? tryBitmapImageForResource(string fileName)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(IMAGES_URI + fileName, UriKind.Absolute);
                image.EndInit();
                // Resolve embedded bytes now; frozen images remain usable by the
                // UI after background preload has finished on another thread.
                image.Freeze();
                return image;
            }
            catch (Exception e)
            {
                EventLogger.logError($"Error loading embedded bitmap {fileName}");
                Console.Write(e);
                //Debug.Assert(false);
                return null;
            }
        }

        #region Items

        public BitmapImage? imageSourceForItem(Item item)
        {
            return imageSourceForItem(item.Type);
        }

        public BitmapImage? imageSourceForItem(string itemType)
        {
            var itemTypeStr = folderNameForItemType(itemType);
            return tryBitmapImageForResource(itemTypeStr + ".png");
        }

        private string folderNameForItemType(string type)
        {
            if (ItemDatabase.artifacts.Contains(type))
            {
                return "Artifacts";
            }
            if (ItemDatabase.armor.Contains(type))
            {
                return "Armor";
            }
            if (ItemDatabase.meleeWeapons.Contains(type))
            {
                return "MeleeWeapons";
            }
            if (ItemDatabase.rangedWeapons.Contains(type))
            {
                return "RangedWeapons";
            }

            return "Unknown";
        }

        private string imageNameForItem(Item item)
        {
            var itemName = item.Type;
            //var encodedString = Uri.EscapeDataString(stringFromItemName(itemName));
            return string.Format("T_{0}_Icon_inventory.png", itemName);
        }

        #endregion

        #region Raritys

        public BitmapImage? imageSourceForRarity(Rarity rarity)
        {
            return null;
            //var filename = imageNameFromRarity(rarity);
            //if(filename == null) { return null; }
            //var path = Path.Combine(IMAGES_URI, "UI", "ItemRarity", filename);
            //var uri = new Uri(path);
            //return tryBitmapImageForUri(uri);
        }

        private string? imageNameFromRarity(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Common: return "drops_items_frame.png";
                case Rarity.Rare: return "drops_rare_frame.png";
                case Rarity.Unique: return "drops_unique_frame.png";
            }
            throw new NotImplementedException();
        }

        #endregion

        #region Enchantments

        public BitmapImage? imageSourceForEnchantment(Enchantment enchantment)
        {
            return imageSourceForEnchantment(enchantment.Id);
        }

        public BitmapImage? imageSourceForEnchantment(string enchantment)
        {
            var enchantmentId = enchantment;
            if (enchantmentId == Constants.DEFAULT_ENCHANTMENT_ID)
            {
                return null;
            }
            return tryBitmapImageForResource("Enchantments.png");
        }

        #endregion

    }
}
