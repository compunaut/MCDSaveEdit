using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using MCDSaveEdit.Data;
using MCDSaveEdit.Save.Models.Profiles;
using MCDSaveEdit.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MCDSaveEditTests
{
    [TestClass]
    [DoNotParallelize]
    public class LocalContentResolverTests
    {
        [STATestMethod]
        public void LimitedModeLoadsEveryEmbeddedItemCategoryWithoutGameFiles()
        {
            var resolver = new LocalContentResolver();
            (HashSet<string> items, string resource)[] categories = {
                (ItemDatabase.artifacts, "Artifacts.png"),
                (ItemDatabase.armor, "Armor.png"),
                (ItemDatabase.meleeWeapons, "MeleeWeapons.png"),
                (ItemDatabase.rangedWeapons, "RangedWeapons.png")
            };
            foreach (var (items, resource) in categories)
            {
                string itemType = "embedded-resource-test-" + Guid.NewGuid().ToString("N");
                items.Add(itemType);
                try
                {
                    AssertLoadedFrozenResource(resolver.imageSourceForItem(new Item { Type = itemType }), resource);
                }
                finally { items.Remove(itemType); }
            }
            AssertLoadedFrozenResource(resolver.imageSourceForItem("unknown-" + Guid.NewGuid()), "Unknown.png");
        }

        [STATestMethod]
        public void LimitedModeEnchantmentIconIsLoadedAndCanCrossThreads()
        {
            var resolver = new LocalContentResolver();
            var image = resolver.imageSourceForEnchantment(new Enchantment { Id = "embedded-resource-test" });
            AssertLoadedFrozenResource(image, "Enchantments.png");
            int width = Task.Run(() => image!.PixelWidth).GetAwaiter().GetResult();
            Assert.IsGreaterThan(0, width);
            Assert.IsNull(resolver.imageSourceForEnchantment(Constants.DEFAULT_ENCHANTMENT_ID));
        }

        private static void AssertLoadedFrozenResource(BitmapImage? image, string resource)
        {
            Assert.IsNotNull(image, resource);
            Assert.IsGreaterThan(0, image.PixelWidth, resource);
            Assert.IsGreaterThan(0, image.PixelHeight, resource);
            Assert.IsTrue(image.IsFrozen, resource);
            Assert.IsFalse(image.IsDownloading, resource);
            StringAssert.EndsWith(image.UriSource.AbsoluteUri, "/MCDSaveEdit;component/Images/" + resource);
            int stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
            image.CopyPixels(new byte[stride * image.PixelHeight], stride, 0);
        }
    }
}
