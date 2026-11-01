using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MultiPresence;

var root = Path.GetFullPath(args.Single());
var assembly = typeof(GameDetector).Assembly;
var scratch = Path.Combine(Path.GetTempPath(), "MultiPresenceChecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    var registry = assembly.GetType("MultiPresence.Runtime.GameIntegrationRegistry", throwOnError: true)!;
    var supported = (IReadOnlyCollection<string>)registry.GetProperty("SupportedGames")!.GetValue(null)!;
    string[] games = ["Cryo", "Disney Dreamlight Valley", "Kingdom Hearts 0.2 Birth by Sleep",
        "Legacy of Kain Soul Reaver 1", "Legacy of Kain Soul Reaver 2", "Minecraft Dungeons II",
        "Phasmophobia", "PokeOne", "Resident Evil Requiem", "Shadow Generations", "Vampire Crawlers",
        "Sekiro", "Bloodborne"];
    foreach (var game in games)
        Require(supported.Contains(game), $"Missing integration: {game}");

    var defaults = assembly.GetType("MultiPresence.Infrastructure.ConfigurationDefaults", true)!
        .GetMethod("CreateMissingFiles")!;
    defaults.Invoke(null, [scratch]);
    var configDirectory = Path.Combine(scratch, "Assets", "Config");
    foreach (var game in games)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(configDirectory, game + ".json")));
        Require(config.RootElement.GetProperty("Games").TryGetProperty(game, out _), $"Missing template: {game}");
    }
    var customFile = Path.Combine(configDirectory, "Sekiro.json");
    const string customized = "{\n  \"AppId\": \"123\", \"Games\": {}, \"custom\": true\n}\n";
    File.WriteAllText(customFile, customized);
    File.Delete(Path.Combine(configDirectory, "Cryo.json"));
    defaults.Invoke(null, [scratch]);
    Require(File.ReadAllText(customFile) == customized, "Startup overwrote a user's configuration.");
    Require(File.Exists(Path.Combine(configDirectory, "Cryo.json")), "Startup did not restore a missing template.");
    Console.WriteLine("PASS: 13 integrations, embedded templates, missing-file creation and customization preservation.");

    var manifestPath = Path.Combine(root, "MultiPresence", "Artwork", "sources.json");
    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    foreach (var asset in manifest.RootElement.GetProperty("assets").EnumerateArray())
    {
        var path = Path.Combine(Path.GetDirectoryName(manifestPath)!, asset.GetProperty("file").GetString()!);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Require(hash.Equals(asset.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), "Artwork hash mismatch.");
        var game = asset.GetProperty("game").GetString()!.StartsWith("Sekiro") ? "Sekiro" : "Bloodborne";
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "MultiPresence", "Config", game + ".json")));
        var template = config.RootElement.GetProperty("Games").GetProperty(game).GetProperty("Default");
        Require(template.GetProperty(asset.GetProperty("role").GetString()!).GetString() == asset.GetProperty("sourceUrl").GetString(),
            "Artwork URL does not match the credited original.");
    }
    Console.WriteLine("PASS: four original artwork hashes and configuration URLs.");

    var updater = assembly.GetType("MultiPresence.Infrastructure.GitHubUpdater", true)!;
    var getRelease = updater.GetMethod("GetRelease")!;
    JsonDocument Release(string owner) => JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        draft = false, prerelease = false, tag_name = "02.11.2026",
        assets = new[] { "MultiPresence.zip", "Updater.exe" }.Select(name => new
        {
            name, size = 100,
            browser_download_url = $"https://github.com/{owner}/MultiPresence/releases/download/02.11.2026/{name}"
        })
    }));
    using var forkRelease = Release("dizzi1222");
    Require(getRelease.Invoke(null, [forkRelease.RootElement, "01.11.2026"]) != null, "Fork release was rejected.");
    Require(getRelease.Invoke(null, [forkRelease.RootElement, "02.11.2026"]) == null, "Same-date release was accepted.");
    using var upstreamRelease = Release("Dekirai");
    var rejected = false;
    try { getRelease.Invoke(null, [upstreamRelease.RootElement, "01.11.2026"]); }
    catch (TargetInvocationException e) when (e.InnerException is InvalidDataException) { rejected = true; }
    Require(rejected, "The fork updater accepted an upstream binary that would remove fork features.");
    Console.WriteLine("PASS: fork update accepted, same version ignored, upstream download rejected.");
}
finally
{
    Directory.Delete(scratch, recursive: true);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
