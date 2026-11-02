using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MCDSaveEdit.Save.Models.Profiles;
using MCDSaveEdit.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MCDSaveEditTests
{
    [TestClass]
    public class MainViewModelSafetyTests
    {
        [TestMethod]
        public async Task LanguageViewReloadRetainsOriginalStaleSaveBaseline()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "hero.json");
                const string original = "{\"items\":[],\"currency\":[],\"xp\":1}";
                const string external = "{\"items\":[],\"currency\":[],\"xp\":999}";
                File.WriteAllText(path, original);
                using var input = new MemoryStream(Encoding.UTF8.GetBytes(original));
                var profile = await ProfileParser.Read(input);
                var model = new MainViewModel();
                model.setProfile(path, profile!);
                File.WriteAllText(path, external);

                // MainWindow's language event passes the existing profile through
                // App.onReload to this exact public method, without rereading disk.
                model.setProfile(path, model.profileModel.profile.value!);
                await Assert.ThrowsExactlyAsync<IOException>(() => model.handleFileSaveAsync(path, profile!));
                Assert.AreEqual(external, File.ReadAllText(path));
                Assert.AreEqual(1, Directory.GetFiles(directory).Length);
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
