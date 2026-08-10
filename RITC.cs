using EternalCycleServer;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Loaders;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Services.Ragfair;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Reflection;
using System.Text.RegularExpressions;
using EternalCycleServer;
using static EternalCycleServer.ContextManager;
using SPTarkov.Server.Core.Services.Server;
using SPTarkov.Server.Core.Models.Spt.Bundles;

namespace RITC
{

    /// <summary>
    /// This is the replacement for the former package.json data. This is required for all mods.
    ///
    /// This is where we define all the metadata associated with this mod.
    /// You don't have to do anything with it, other than fill it out.
    /// All properties must be overriden, properties you don't use may be left null.
    /// It is read by the mod loader when this mod is loaded.
    /// </summary>
    public record ModMetadata : IModMetadata
    {
        /// <summary>
        /// Any string can be used for a modId, but it should ideally be unique and not easily duplicated
        /// a 'bad' ID would be: "mymod", "mod1", "questmod"
        /// It is recommended (but not mandatory) to use the reverse domain name notation,
        /// see: https://docs.oracle.com/javase/tutorial/java/package/namingpkgs.html
        /// </summary>
        public string ModGuid { get; init; } = "com.hiddenhiragi.ritc";

        /// <summary>
        /// The name of your mod
        /// </summary>
        public  string Name { get; init; } = "RITC";

        /// <summary>
        /// Who created the mod (you!)
        /// </summary>
        public  string Author { get; init; } = "HiddenHiragi";

        /// <summary>
        /// A list of people who helped you create the mod
        /// </summary>
        public  List<string>? Contributors { get; init; }

        /// <summary>
        ///  The version of the mod, follows SEMVER rules (https://semver.org/)
        /// </summary>
        public  SemanticVersioning.Version Version { get; init; } = new("1.2.1");

        /// <summary>
        /// What version of SPT is your mod made for, follows SEMVER rules (https://semver.org/)
        /// </summary>
        public  SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");

        /// <summary>
        /// ModIds that you know cause problems with your mod
        /// </summary>
        public  List<string>? Incompatibilities { get; init; }

        /// <summary>
        /// ModIds your mod REQUIRES to function
        /// </summary>
        public  Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
{
    { "projectspark.hiddenhiragi.eternalcycleserver", new SemanticVersioning.Range(">=1.3.1") }
};

        /// <summary>
        /// Where to find your mod online
        /// </summary>
        public  string? Url { get; init; } = "https://github.com/sp-tarkov/server-mod-examples";

        /// <summary>
        /// Does your mod load bundles? (e.g. new weapon/armor mods)
        /// </summary>
        public  bool? IsBundleMod { get; init; } = false;

        /// <summary>
        /// What Licence does your mod use
        /// </summary>
        public  string? License { get; init; } = "MIT";

        public bool HasPrepatcher { get; init; } = false;
    }

    // We want to load after PreSptModLoader is complete, so we set our type priority to that, plus 1.
    [Injectable(TypePriority = OnLoadOrder.Preload + 1)]
    public class RITC(
        CustomItemService customItemService,
        ModHelper modHelper,
        ItemHelper itemHelper,
        JsonUtil jsonUtil,
        ICloner cloner,
        ConfigServer configServer,
        ImageRouter imageRouter,
        PresetHelper presetHelper,
        RagfairOfferService ragfairOfferService,
        RagfairController ragfairController,
        TemplateTable templateTable,
        LocaleTable localeTable,
        GlobalTable globalTable,
        TradersTable tradersTable,
        HideoutTable hideoutTable,
        LocationTable locationTable,
        HandbookHelper handbookHelper,
        BundleHashCacheService bundleHashCacheService,
        BundleLoader bundleLoader
        ) // We inject a logger for use inside our class, it must have the class inside the diamond <> brackets
        : IOnLoad // Implement the IOnLoad interface so that this mod can do something on server load
    {
        public static string modPath = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static string author = "<color=#B0E0E6>RITC</color>";
        public static Dictionary<string, Package> PackagePath = new Dictionary<string, Package>();
        public async Task OnLoadAsync(CancellationToken cancellationToken)
        {
            var pkgpath = System.IO.Path.Combine(modPath, "package/");
            var testpkg = System.IO.Path.Combine(modPath, "示例包/");
            //LoadPackage(testpkg, logger, databaseService, customItemService, modHelper, jsonutil, cloner, configServer, imageRouter);
            // We can access the logger and call its methods to log to the server window and the server log file
            string[] directories = Directory.GetDirectories(pkgpath);
            foreach (var key in directories)
            {
                EventManager.DataLoadEvent.PreDataLoadEvent += (context) =>
                {
                    try
                    {
                        LoadPackage(key, context, bundleHashCacheService, bundleLoader);
                        LoadAllBundleFromPack(bundleHashCacheService, bundleLoader, jsonUtil);
                    }
                    catch (Exception ex)
                    {
                    }
                };
            }
            //Utils.commonLogger.Success("妈的有病啊? 没见过后加载啊?");
            EventManager.OnAfterModLoadedEvent += (context) =>
            {
                Utils.commonLogger.Info($"共加载了{PackagePath.Keys.Count}个扩展包");
                foreach (var kvp in PackagePath)
                {
                    Utils.commonLogger.Info($"扩展包: {kvp.Value.Name}");
                    Utils.commonLogger.Info($"版本: {kvp.Value.Version}");
                    Utils.commonLogger.Info($"{kvp.Value.Description}");
                }
            };

            // Inform the server our mod has finished doing work
            return;
        }

        public static void LoadPackage(string pkgpath, LoadModContext context, BundleHashCacheService bundleHashCacheService, BundleLoader bundleLoader)
        {
            //var databaseService = ServiceLocator.ServiceProvider.GetService<DatabaseService>();


            var package = Utils.LoadJsonCFromPath<Package>(System.IO.Path.Combine(pkgpath, "package.jsonc"));
            var datapath = Path.Combine(pkgpath, "packdata/");
            if (package.IsActive)
            {
                var name = package.Name;
                var version = package.Version;
                var creator = $"<color=#B0E0E6>RITC扩展包 - {name}</color>";

                //捋一捋
                //缺了兑换码, sloticon示例, 标靶示例, 装修示例, 弹挂布局示例
                //诶呦, 遥遥无期哟
                //得从隔壁火神重工扒拉点资源过来
                //反正我把重复警告杀了
                //那么我就得做自定义icon的slot, 一个弹挂甲(包含预设), 一把武器
                //一套兑换码, 一个狗牌, 一个地板, 一个靶子
                //还有各种示例实例
                //比如新增的标签系统
                //卡池
                //乱七八糟
                //仿制钥匙明天还得搓了
                //当地时间07.11.2026 23:18
                //明天开搞。
                //next day...
                //内置一下武器组标签, 扫手册
                //然后把示例武器组写一下
                //当地时间07.12.2026 18:12
                //差装修, 标靶
                //武器组
                //衣服!!
                //当地时间07.12.2026 22:18
                //装修, 衣服, 应该没了
                //都是不好搞定的
                //我要让自己休息休息....压力好大

                Utils.commonLogger.Info($"加载拓展包: {name}, 版本{version}");
                PackagePath.TryAdd(pkgpath, package);
                ItemUtils.RegisterItem(datapath, "items/", creator, author);
                ItemUtils.RegisterDrawPool(datapath, "pool/");
                ItemUtils.RegisterDrawPool(datapath, "drawpool.jsonc");

                ItemTagUtils.RegisterItemTag(datapath, "itemtag.jsonc");
                GiftCodeUtils.RegisterGiftCode(datapath, "giftcode.jsonc");
                GiftCodeUtils.RegisterGiftCode(datapath, "giftcode/");

                TraderUtils.RegisterTrader(datapath, "traderdata/trader/", "res/avatar/", creator, author);
                AssortUtils.RegisterAssort(datapath, "traderdata/assort/");
                SuitUtils.RegisterSuit(datapath, "traderdata/suit/");

                QuestUtils.RegisterQuest(datapath, "questdata/init/", "res/questimage/");
                QuestUtils.RegisterQuest(datapath, "questdata/init.jsonc", "res/questimage/");
                QuestUtils.RegisterQuestLogicTree(datapath, "questdata/logic/");
                QuestUtils.RegisterQuestRewards(datapath, "questdata/reward/");
                QuestZoneUtils.RegisterQuestZones(datapath, "questdata/zone/");

                AchievementUtils.RegisterAchievement(datapath, "questdata/achievement/", "res/achievement/");
                AchievementUtils.RegisterAchievement(datapath, "questdata/achievement.jsonc", "res/achievement/");

                RecipeUtils.RegisterRecipe(datapath, "recipe/craft/");
                RecipeUtils.RegisterRecipe(datapath, "recipe/craft.jsonc");
                RecipeUtils.RegisterScavCaseRecipe(datapath, "recipe/scavcase/");
                RecipeUtils.RegisterScavCaseRecipe(datapath, "recipe/scavcase.jsonc");
                RecipeUtils.RegisterCultistCircleRecipe(datapath, "recipe/circle/");
                RecipeUtils.RegisterCultistCircleRecipe(datapath, "recipe/circle.jsonc");

                PresetUtils.RegisterPreset(datapath, "preset/");

                CustomizationUtils.RegisterCustomization(datapath, "customization/normal/", "res/customization/");
                CustomizationUtils.RegisterHideoutCustomization(datapath, "customization/hideout/");

                LocaleUtils.RegisterQuestLocale(datapath, "locale/quest/", creator, author);
                LocaleUtils.RegisterLocaleText(datapath, "locale/text/");
                DialogueUtils.RegisterDialogue(datapath, "locale/dialogue/");

                ResourceUtils.RegisterRigLayoutResource(datapath, "res/riglayout/");
                ResourceUtils.RegisterSlotIconResource(datapath, "res/sloticon/");

            }
        }
        public async Task LoadAllBundleFromPack(BundleHashCacheService bundleHashCacheService, BundleLoader bundleLoader, JsonUtil jsonUtil)
        {
            foreach (var kvp in PackagePath)
            {
                //Console.WriteLine($"加载Bundle: {kvp.Key}: {kvp.Value.Name}");
                await LoadBundlesAsync(kvp.Key, kvp.Value.Name, bundleHashCacheService, bundleLoader, jsonUtil);
            }
        }
        public async Task LoadBundlesAsync(string modPath, string packname, BundleHashCacheService bundleHashCacheService, BundleLoader bundleLoader, JsonUtil jsonUtil)
        {
            //Console.WriteLine($"读取缓存");
            await bundleHashCacheService.HydrateCacheAsync();

            //不对
            //var modPath = mod.GetModPath();
            //Console.WriteLine($"{modPath}");
            var testpath = Path.Join(modPath, "bundles.json");
            //Console.WriteLine($"{testpath}");
            var modBundles = await jsonUtil.DeserializeFromFileAsync<BundleManifest>(
                testpath
            );

            var bundleManifests = modBundles?.Manifest ?? [];
            var relativeModPath = modPath.Replace('\\', '/');
            //在这藏着呢, 草, 内置了一个拼接路径
            //神秘
            var match = Regex.Match(relativeModPath, @"user/.*");
            if (match.Success)
            {
                relativeModPath = match.Groups[0].Value;
            }

            foreach (var bundleManifest in bundleManifests)
            {

                //Console.WriteLine($"{relativeModPath}");

                //Console.WriteLine($"{Directory.GetCurrentDirectory()}"); 
                var bundleLocalPath = Path.Join(relativeModPath, "bundles", bundleManifest.Key).Replace('\\', '/');

                //Console.WriteLine($"{bundleLocalPath}");
                if (!File.Exists(bundleLocalPath))
                {
                    Utils.commonLogger.Warn($"在拓展包{packname}中找不到指定的资源文件:  {bundleManifest.Key}");
                    //logger.Warning($"在拓展包{packname}中找不到指定的资源文件:  {bundleManifest.Key}");
                    continue;
                }

                var bundleHash = await bundleHashCacheService.CalculateHashAsync(bundleLocalPath);

                bundleLoader.AddBundle(bundleManifest.Key, new BundleInfo
                {
                    ModPath = relativeModPath,
                    Bundle = bundleManifest,
                    Crc = bundleHash
                });
            }

            await bundleHashCacheService.WriteCacheAsync();
        }
    }
}