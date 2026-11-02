using MCDSaveEdit.Data;
using System.Threading.Tasks;

namespace MCDSaveEdit.Services
{
    // Personal fork: offline by default. No upstream release lookup or analytics
    // remote configuration. A fork-owned update endpoint can be added later.
    public class Config
    {
        public static Config instance = new Config();
        public bool isConfigsReady { get; protected set; } = true;
        public bool showInventoryIndexOrEquipmentSlot => Constants.IS_DEBUG;
        public bool showIDsInSelectionWindow => Constants.IS_DEBUG;
        public Task downloadAsync() => Task.CompletedTask;
        public string versionLabel() => "Personal fork (offline)";
        public bool isNewBetaVersionAvailable() => false;
        public bool isNewStableVersionAvailable() => false;
    }
}
