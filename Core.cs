using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Loaders;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Reflection;
using System.Text.RegularExpressions;
using VulcanCore;

namespace RITC;

/// <summary>
/// This is the replacement for the former package.json data. This is required for all mods.
///
/// This is where we define all the metadata associated with this mod.
/// You don't have to do anything with it, other than fill it out.
/// All properties must be overriden, properties you don't use may be left null.
/// It is read by the mod loader when this mod is loaded.
/// </summary>
public record ModMetadata : AbstractModMetadata
{
    /// <summary>
    /// Any string can be used for a modId, but it should ideally be unique and not easily duplicated
    /// a 'bad' ID would be: "mymod", "mod1", "questmod"
    /// It is recommended (but not mandatory) to use the reverse domain name notation,
    /// see: https://docs.oracle.com/javase/tutorial/java/package/namingpkgs.html
    /// </summary>
    public override string ModGuid { get; init; } = "com.hiddenhiragi.ritc";

    /// <summary>
    /// The name of your mod
    /// </summary>
    public override string Name { get; init; } = "RITC";

    /// <summary>
    /// Who created the mod (you!)
    /// </summary>
    public override string Author { get; init; } = "HiddenHiragi";

    /// <summary>
    /// A list of people who helped you create the mod
    /// </summary>
    public override List<string>? Contributors { get; init; }

    /// <summary>
    ///  The version of the mod, follows SEMVER rules (https://semver.org/)
    /// </summary>
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");

    /// <summary>
    /// What version of SPT is your mod made for, follows SEMVER rules (https://semver.org/)
    /// </summary>
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");

    /// <summary>
    /// ModIds that you know cause problems with your mod
    /// </summary>
    public override List<string>? Incompatibilities { get; init; }

    /// <summary>
    /// ModIds your mod REQUIRES to function
    /// </summary>
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
{
    { "com.hiddenhiragi.vulcancore", new SemanticVersioning.Range(">=1.0.1") }
};

    /// <summary>
    /// Where to find your mod online
    /// </summary>
    public override string? Url { get; init; } = "https://github.com/sp-tarkov/server-mod-examples";

    /// <summary>
    /// Does your mod load bundles? (e.g. new weapon/armor mods)
    /// </summary>
    public override bool? IsBundleMod { get; init; } = false;

    /// <summary>
    /// What Licence does your mod use
    /// </summary>
    public override string? License { get; init; } = "MIT";
}

// We want to load after PreSptModLoader is complete, so we set our type priority to that, plus 1.
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class Core(
    ISptLogger<VulcanCore.VulcanCore> logger,
    DatabaseService databaseService,
    CustomItemService customItemService,
    ModHelper modHelper,
    JsonUtil jsonutil,
    ICloner cloner,
    ConfigServer configServer,
    ImageRouter imageRouter
    ) // We inject a logger for use inside our class, it must have the class inside the diamond <> brackets
    : IOnLoad // Implement the IOnLoad interface so that this mod can do something on server load
{
    public static string modPath = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    public static string author = "<color=#B0E0E6>RITC</color>";
    public static Dictionary<string, Package> PackagePath = new Dictionary<string, Package>();
    public async Task OnLoad()
    {

        new RagfairLoadPatch().Enable();
        var pkgpath = System.IO.Path.Combine(modPath, "package/");
        var testpkg = System.IO.Path.Combine(modPath, "示例包/");
        //LoadPackage(testpkg, logger, databaseService, customItemService, modHelper, jsonutil, cloner, configServer, imageRouter);
        // We can access the logger and call its methods to log to the server window and the server log file
        string[] directories = Directory.GetDirectories(pkgpath);
        foreach(var key in directories)
        {
            LoadPackage(key, logger, databaseService, customItemService, modHelper, jsonutil, cloner, configServer, imageRouter);
        }
        var bundleHashCacheService = ServiceLocator.ServiceProvider.GetService<BundleHashCacheService>();
        var bundleLoader = ServiceLocator.ServiceProvider.GetService<BundleLoader>();
        await LoadAllBundleFromPack(bundleHashCacheService, bundleLoader, jsonutil);

        // Inform the server our mod has finished doing work
        //return Task.CompletedTask;
    }
    public static void LoadPackage(
        string pkgpath, 
        ISptLogger<VulcanCore.VulcanCore> logger,
        DatabaseService databaseService,
        CustomItemService customItemService,
        ModHelper modHelper,
        JsonUtil jsonUtil,
        ICloner cloner,
        ConfigServer configServer,
        ImageRouter imageRouter
        )
    {
        //var databaseService = ServiceLocator.ServiceProvider.GetService<DatabaseService>();

        var bundleHashCacheService = ServiceLocator.ServiceProvider.GetService<BundleHashCacheService>();
        var bundleLoader = ServiceLocator.ServiceProvider.GetService<BundleLoader>();

        var package = VulcanUtil.LoadJsonCFromPath<Package>(System.IO.Path.Combine(pkgpath, "package.jsonc"));
        if (package.IsActive)
        {
            var name = package.Name;
            var version = package.Version;
            var creator = $"<color=#B0E0E6>RITC拓展 - {name}</color>";
            var imagepath = System.IO.Path.Combine(pkgpath, "res/");
            var traderimgpath = System.IO.Path.Combine(imagepath, "avatar/");
            var questimgpath = System.IO.Path.Combine(imagepath, "image/");
            var iconimgpath = System.IO.Path.Combine(imagepath, "icon/");

            var questData = modHelper.GetJsonDataFromFile<Dictionary<string, CustomQuest>>(pkgpath, "questdata/init.jsonc");
            var recipeData = modHelper.GetJsonDataFromFile<Dictionary<string, CustomRecipeData>>(pkgpath, "recipe/craft.jsonc");
            var scavCaseData = modHelper.GetJsonDataFromFile<Dictionary<string, CustomScavCaseRecipeData>>(pkgpath, "recipe/scavcase.jsonc");
            var achievementData = modHelper.GetJsonDataFromFile<List<CustomAchievementData>>(pkgpath, "questdata/achievement.jsonc");
            VulcanLog.Log($"加载拓展包: {name}, 版本{version}", logger);
            PackagePath.TryAdd(pkgpath, package);
            ImageUtils.RegisterFolderImageRoute("/files/quest/icon/", questimgpath, imageRouter);
            ImageUtils.RegisterFolderImageRoute("/files/icon/", iconimgpath, imageRouter);
            ItemUtils.InitItem(System.IO.Path.Combine(pkgpath, "items/"), creator, author, logger, databaseService, jsonUtil, cloner, configServer);
            ItemUtils.InitDrawPool(System.IO.Path.Combine(pkgpath, "pool/"));
            TraderUtils.InitTraders(System.IO.Path.Combine(pkgpath, "traderdata/trader/"), traderimgpath, 100, 3600, 3600, false, creator, author, configServer, jsonUtil, modHelper, databaseService, cloner, imageRouter);
            QuestUtils.InitQuestData(System.IO.Path.Combine(pkgpath, "questdata/init/"), databaseService, modHelper, cloner, logger);
            QuestUtils.InitQuestData(questData, databaseService, cloner, logger);
            QuestUtils.InitQuestLogicTreeData(System.IO.Path.Combine(pkgpath, "questdata/logic/"), databaseService, modHelper, cloner);

            QuestUtils.InitQuestRewards(System.IO.Path.Combine(pkgpath, "questdata/reward/"), databaseService, modHelper, cloner, logger);

            AssortUtils.InitAssortData(System.IO.Path.Combine(pkgpath, "traderdata/assort/"), databaseService, modHelper, cloner, logger);
            RecipeUtils.InitRecipeData(System.IO.Path.Combine(pkgpath, "recipe/craft/"), databaseService, modHelper, cloner);
            RecipeUtils.InitRecipeData(recipeData, databaseService, cloner);
            RecipeUtils.InitScavCaseRecipeData(System.IO.Path.Combine(pkgpath, "recipe/scavcase/"), databaseService, modHelper, cloner);
            RecipeUtils.InitScavCaseRecipeData(scavCaseData, databaseService, cloner);
            PresetUtils.InitPresetData(System.IO.Path.Combine(pkgpath, "preset/"), databaseService, modHelper, cloner, logger);
            AchievementUtils.InitAchievementData(System.IO.Path.Combine(pkgpath, "questdata/achievement/"), databaseService, modHelper, cloner, logger);
            AchievementUtils.InitAchievementData(achievementData, databaseService, cloner, logger);
            CustomizationUtils.InitCustomiaztionData(System.IO.Path.Combine(pkgpath, "customization/"), databaseService, modHelper, cloner);
            SuitUtils.InitCustomSuitData(System.IO.Path.Combine(pkgpath, "traderdata/suit/"), databaseService, modHelper, cloner);
            LocaleUtils.InitQuestLocale(System.IO.Path.Combine(pkgpath, "locale/quest/"), creator, author, databaseService, modHelper);
            LocaleUtils.InitLocaleText(System.IO.Path.Combine(pkgpath, "locale/text/"), databaseService, modHelper);


        }
    }
    public async Task LoadAllBundleFromPack(BundleHashCacheService bundleHashCacheService, BundleLoader bundleLoader, JsonUtil jsonUtil)
    {
        foreach(var kvp in PackagePath)
        {
            //Console.WriteLine($"加载Bundle: {kvp.Key}: {kvp.Value.Name}");
            await LoadBundlesAsync(kvp.Key, kvp.Value.Name, bundleHashCacheService, bundleLoader, jsonUtil);
        }
    }
    public async Task LoadBundlesAsync(string modPath, string packname, BundleHashCacheService bundleHashCacheService, BundleLoader bundleLoader, JsonUtil jsonUtil)
    {
        //Console.WriteLine($"读取缓存");
        await bundleHashCacheService.HydrateCache();

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
                logger.Warning($"在拓展包{packname}中找不到指定的资源文件:  {bundleManifest.Key}");
                continue;
            }

            var bundleHash = await bundleHashCacheService.CalculateMatchAndStoreHash(bundleLocalPath);

            bundleLoader.AddBundle(bundleManifest.Key, new BundleInfo(relativeModPath, bundleManifest, bundleHash));
        }

        await bundleHashCacheService.WriteCache();
    }
}
