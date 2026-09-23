using MintLauncher;
using MintLauncher.Operational;
using CmlLib.Core.Version;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;

var indicator = new NavigationIndicatorMotion();
indicator.SetInstant(3);
indicator.Retarget(121, 0, 270);
var halfway = indicator.ValueAt(90);
indicator.Retarget(3, 90, 270);
if (Math.Abs(indicator.ValueAt(90) - halfway) > 0.001)
    throw new InvalidOperationException("Navigation indicator jumped when direction reversed.");
var returning = indicator.ValueAt(180);
indicator.Retarget(239, 180, 270);
if (Math.Abs(indicator.ValueAt(180) - returning) > 0.001 ||
    Math.Abs(indicator.ValueAt(450) - 239) > 0.001 ||
    !indicator.IsCompleteAt(450))
    throw new InvalidOperationException("Rapid navigation retarget did not finish smoothly.");

using (var diagnostics = new MicrosoftAuthenticationDiagnosticsHandler(new FixedResponseHandler(
           HttpStatusCode.Forbidden,
           "{\"error\":\"ForbiddenOperationException\",\"errorMessage\":\"Invalid app registration; secret-token\"}")))
using (var client = new HttpClient(diagnostics))
{
    await client.PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox?code=private-code", null);
    var message = MicrosoftAuthenticationFailureMessage.Create(
        new CmlLib.Core.Auth.Microsoft.JEAuthException("403: Forbidden"), diagnostics.LastFailure);
    if (!message.Contains("Invalid app registration", StringComparison.Ordinal) ||
        message.Contains("secret-token", StringComparison.Ordinal) ||
        message.Contains("private-code", StringComparison.Ordinal))
        throw new InvalidOperationException("Microsoft authentication diagnostics lost the app error or exposed a secret.");
}

using (var diagnostics = new MicrosoftAuthenticationDiagnosticsHandler(new FixedResponseHandler(
           HttpStatusCode.Forbidden, "<html>Forbidden</html>")))
using (var client = new HttpClient(diagnostics))
{
    await client.GetAsync("https://api.minecraftservices.com/minecraft/profile");
    var message = MicrosoftAuthenticationFailureMessage.Create(
        new CmlLib.Core.Auth.Microsoft.JEAuthException("403: Forbidden"), diagnostics.LastFailure);
    if (!message.Contains("Minecraft 档案读取", StringComparison.Ordinal) ||
        message.Contains("需要提交应用 ID", StringComparison.Ordinal))
        throw new InvalidOperationException("A generic 403 was incorrectly diagnosed as app registration.");
}

if (LauncherWorkspace.ValidatePlayerName("Player") != "Player")
    throw new InvalidOperationException("Default offline name validation failed.");
if (LauncherWorkspace.ValidatePlayerName("Mint_Player_1234") != "Mint_Player_1234")
    throw new InvalidOperationException("Valid 16-character offline name was rejected.");

foreach (var invalidName in new[] { "ab", "abcdefghijklmnopq", "bad name", "薄荷玩家", " Player" })
{
    try
    {
        LauncherWorkspace.ValidatePlayerName(invalidName);
        throw new InvalidOperationException($"Invalid player name was accepted: {invalidName}");
    }
    catch (InvalidOperationException exception) when (exception.Message.Contains("玩家 ID"))
    {
    }
}

var fixtureParent = Path.Combine(Path.GetTempPath(), "MintLauncher-test-" + Guid.NewGuid().ToString("N"));
var fixtureRoot = Path.Combine(fixtureParent, ".minecraft");
try
{
    var preferencesPath = Path.Combine(fixtureParent, "workspace.json");
    new WorkspacePreferences
    {
        PlayerName = "Player",
        GameDirectory = fixtureRoot,
        SelectedGameId = "mint-fabric-0.19.3-1.21.11",
        SelectedMicrosoftUuid = "0123456789abcdef0123456789abcdef"
    }.SaveTo(preferencesPath);
    var reloadedPreferences = WorkspacePreferences.LoadFrom(preferencesPath);
    if (reloadedPreferences.GameDirectory != fixtureRoot ||
        reloadedPreferences.SelectedGameId != "mint-fabric-0.19.3-1.21.11" ||
        reloadedPreferences.SelectedMicrosoftUuid != "0123456789abcdef0123456789abcdef")
        throw new InvalidOperationException("Selected game was not preserved across preference reload.");

    var versionDir = Path.Combine(fixtureRoot, "versions", "1.21.1");
    Directory.CreateDirectory(versionDir);
    File.WriteAllText(Path.Combine(versionDir, "1.21.1.json"), "{\"id\":\"1.21.1\",\"type\":\"release\"}");
    if (LauncherWorkspace.ValidateImportedDirectory(fixtureParent) != fixtureRoot)
        throw new InvalidOperationException("Nested .minecraft import failed.");
    if (LauncherWorkspace.ScanInstalled(fixtureRoot).Single().Id != "1.21.1")
        throw new InvalidOperationException("Installed version scan failed.");
    if (LauncherWorkspace.ResolveRunDirectory(fixtureRoot, "1.21.1") != fixtureRoot)
        throw new InvalidOperationException("Shared game directory fallback failed.");
    var hmclSettings = Path.Combine(versionDir, ".hmcl", "config");
    Directory.CreateDirectory(hmclSettings);
    File.WriteAllText(Path.Combine(hmclSettings, "instance-game-settings.json"),
        "{\"overrideProperties\":[\"runningDirectory\"],\"runningDirectory\":\"\"}");
    if (LauncherWorkspace.ResolveRunDirectory(fixtureRoot, "1.21.1") != versionDir)
        throw new InvalidOperationException("HMCL isolated game directory was not preserved.");
    var modsDir = Path.Combine(versionDir, "mods");
    Directory.CreateDirectory(modsDir);
    File.WriteAllBytes(Path.Combine(modsDir, "example.jar"), []);
    try
    {
        await new LauncherWorkspace().LaunchAsync(fixtureRoot, "1.21.1", "Player",
            new Progress<LaunchPreparationProgress>(), CancellationToken.None);
        throw new InvalidOperationException("Vanilla metadata with isolated MODs was launched silently.");
    }
    catch (MissingModLoaderException exception) when (exception.Message.Contains("MOD 文件还在"))
    {
        if (exception.VersionDirectory != versionDir)
            throw new InvalidOperationException("Loader recovery should point to the affected version directory.");
    }

    var fabricJar = Path.Combine(modsDir, "fabric-example.jar");
    using (var zip = ZipFile.Open(fabricJar, ZipArchiveMode.Create))
    using (var entry = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
        entry.Write("{\"id\":\"example\"}");
    var logsDir = Path.Combine(versionDir, "logs");
    Directory.CreateDirectory(logsDir);
    File.WriteAllText(Path.Combine(logsDir, "latest.log"), "Loading Minecraft 1.21.1 with Fabric Loader 0.19.3\n");
    var detection = FabricRecovery.Detect("1.21.1", versionDir);
    if (detection is null || detection.GameVersion != "1.21.1" || detection.LoaderVersion != "0.19.3")
        throw new InvalidOperationException("Imported Fabric instance was not identified from its mods and log.");
    var profileId = "mint-fabric-0.19.3-1.21.1";
    var profileJson = FabricRecovery.BuildProfile(
        "{\"id\":\"fabric-loader-0.19.3-1.21.1\",\"inheritsFrom\":\"1.21.1\",\"mainClass\":\"net.fabricmc.loader.impl.launch.knot.KnotClient\",\"libraries\":[{\"name\":\"net.fabricmc:fabric-loader:0.19.3\"}]}",
        profileId, "1.21.1", "0.19.3");
    using (var parsed = JsonVersionParser.ParseFromJsonString(profileJson, new JsonVersionParserOptions()) as JsonVersion)
        if (parsed?.Id != profileId || parsed.InheritsFrom != "1.21.1" ||
            parsed.MainClass != "net.fabricmc.loader.impl.launch.knot.KnotClient" || parsed.Libraries.Count != 1)
            throw new InvalidOperationException("Generated Fabric profile cannot be read by the launch engine.");
    var profileDir = Path.Combine(fixtureRoot, "versions", profileId);
    Directory.CreateDirectory(profileDir);
    File.WriteAllText(Path.Combine(profileDir, profileId + ".json"), profileJson);
    if (LauncherWorkspace.ResolveRunDirectory(fixtureRoot, profileId) != versionDir)
        throw new InvalidOperationException("Recovered profile did not retain its HMCL instance directory.");
    if (!LauncherWorkspace.ScanInstalled(fixtureRoot).Any(game => game.Id == profileId && game.Description.Contains("Fabric 0.19.3")))
        throw new InvalidOperationException("Recovered profile is not visible with a readable library label.");
    var originalMetadata = File.ReadAllText(Path.Combine(versionDir, "1.21.1.json"));
    var resolvedProfile = await LauncherWorkspace.CreateLocalFirstLauncher(fixtureRoot, profileId).GetVersionAsync(profileId);
    if (resolvedProfile.MainClass != "net.fabricmc.loader.impl.launch.knot.KnotClient" ||
        resolvedProfile.ParentVersion?.Id != "1.21.1" ||
        File.ReadAllText(Path.Combine(versionDir, "1.21.1.json")) != originalMetadata)
        throw new InvalidOperationException("Local-first launch resolution changed imported metadata or lost its Fabric parent.");
    using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(versionDir, "1.21.1.json"))))
        if (document.RootElement.TryGetProperty("mintSourceVersion", out _))
            throw new InvalidOperationException("Original imported metadata was modified.");

    if (args.Contains("--fabric-install-smoke"))
    {
        var smokeRoot = Path.Combine(fixtureParent, "fabric-smoke", ".minecraft");
        var smokeSource = Path.Combine(smokeRoot, "versions", "1.21.11");
        Directory.CreateDirectory(smokeSource);
        var sourceFile = Path.Combine(smokeSource, "1.21.11.json");
        File.WriteAllText(sourceFile, "{\"id\":\"1.21.11\",\"type\":\"release\"}");
        using var smokeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var installedId = await FabricRecovery.CreateAndInstallAsync(
            smokeRoot, "1.21.11", new FabricRecovery.Detection("1.21.11", "0.19.3"),
            LauncherWorkspace.CreateLocalFirstLauncher(smokeRoot, "1.21.11"),
            new Progress<LaunchPreparationProgress>(), smokeTimeout.Token);
        var loaderJar = Path.Combine(smokeRoot, "libraries", "net", "fabricmc", "fabric-loader", "0.19.3", "fabric-loader-0.19.3.jar");
        if (installedId != "mint-fabric-0.19.3-1.21.11" || !File.Exists(loaderJar) ||
            File.ReadAllText(sourceFile) != "{\"id\":\"1.21.11\",\"type\":\"release\"}" ||
            !File.Exists(sourceFile + ".mint-backup"))
            throw new InvalidOperationException("Fabric install smoke test did not preserve source metadata or download its loader.");
        Console.WriteLine("PASS: Fabric dependencies installed beside the imported version without changing its JSON.");
    }
}
finally
{
    var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
    if (Path.GetDirectoryName(Path.GetFullPath(fixtureParent)) != temp)
        throw new InvalidOperationException("Unsafe test fixture cleanup path.");
    Directory.Delete(fixtureParent, recursive: true);
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var fabricApi = new HttpClient();
var officialProfile = await fabricApi.GetStringAsync(
    "https://meta.fabricmc.net/v2/versions/loader/1.21.11/0.19.3/profile/json", timeout.Token);
var recoveredProfile = FabricRecovery.BuildProfile(officialProfile, "mint-fabric-0.19.3-1.21.11", "1.21.11", "0.19.3");
using (var profile = JsonVersionParser.ParseFromJsonString(recoveredProfile, new JsonVersionParserOptions()) as JsonVersion)
    if (profile?.InheritsFrom != "1.21.11" || profile.MainClass != "net.fabricmc.loader.impl.launch.knot.KnotClient" ||
        profile.Libraries.Count < 2)
        throw new InvalidOperationException("Current official Fabric profile cannot be adapted for an imported game.");
var root = Path.Combine(Path.GetTempPath(), "MintLauncher-catalog-check");
var releases = await new LauncherWorkspace().GetReleasesAsync(root, timeout.Token);
if (releases.Count == 0 || releases.Any(game => string.IsNullOrWhiteSpace(game.Id)))
    throw new InvalidOperationException("Minecraft release catalog was empty or malformed.");

Console.WriteLine($"PASS: player name, PCL/HMCL import, local-first metadata, Fabric detection/profile; {releases.Count} Minecraft releases; latest={releases[0].Id}");

internal sealed class FixedResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
}
