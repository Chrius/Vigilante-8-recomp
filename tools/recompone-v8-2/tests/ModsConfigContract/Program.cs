using RecompOne.Runtime.Config;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Modding;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
}
string scratch = Path.Combine(Path.GetTempPath(), $"mods-config-contract-{Environment.ProcessId}");
string user = Path.Combine(scratch, "user"), mods = Path.Combine(scratch, "mods");
string settings = Path.Combine(user, "settings.json");
foreach (string folder in new[] { "alpha", "beta", ".cache" })
    Directory.CreateDirectory(Path.Combine(mods, folder));
File.WriteAllBytes(Path.Combine(mods, "script.zip"), []);
Environment.SetEnvironmentVariable("RECOMPONE_USER_DATA_DIR", user);
Environment.SetEnvironmentVariable("RECOMPONE_MOD_DIR", mods);
try
{
    // A new install records every installed mod as active; dot folders are
    // runtime-owned, not mods.
    ConfigManager.Load();
    Check(ConfigManager.Game.ModsVersion == 1, "fresh install marked");
    Check(ConfigManager.Game.ActiveMods.SequenceEqual(["alpha", "beta", "script.zip"]),
        "fresh install: all mods active: " + string.Join(',', ConfigManager.Game.ActiveMods));
    Check(File.ReadAllText(settings).Contains("\"script.zip\""), "fresh list saved to settings.json");

    // A new install starts fullscreen with high-res textures, and the first
    // saved interface.ini records both.
    Check(ConfigManager.View.Fullscreen && ConfigManager.View.HighResolutionTextures, "fresh view defaults on");
    ConfigManager.SaveView([]);
    string created = File.ReadAllText(Path.Combine(user, "interface.ini"));
    Check(created.Contains("Fullscreen=True") && created.Contains("HighResolutionTextures=True"),
        "created interface.ini records the defaults");
    File.Delete(Path.Combine(user, "interface.ini"));

    // Releases that never honoured the list saved it empty; that must not
    // switch every mod off.
    File.WriteAllText(settings, """{ "ActiveMods": [] }""");
    ConfigManager.Load();
    Check(ConfigManager.Game.ActiveMods.Count == 3 && ConfigManager.Game.ModsVersion == 1,
        "legacy empty list migrated to all mods");

    // Once marked, the list is the player's choice; a mod added later stays off.
    Directory.CreateDirectory(Path.Combine(mods, "gamma"));
    File.WriteAllText(settings, """{ "ActiveMods": ["alpha", "script.zip"], "ModsVersion": 1 }""");
    ConfigManager.Load();
    Check(ConfigManager.Game.ActiveMods.SequenceEqual(["alpha", "script.zip"]), "player's list kept");
    Check(ModCatalog.IsEnabled(Path.Combine(mods, "alpha")), "listed mod loads");
    Check(!ModCatalog.IsEnabled(Path.Combine(mods, "beta")), "unlisted mod is off");
    Check(!ModCatalog.IsEnabled(Path.Combine(mods, "gamma")), "newly added mod stays off");
    Check(ModCatalog.EnabledDirectories(mods).Select(Path.GetFileName).SequenceEqual(["alpha"]),
        "loaders see only active folders");

    // Changes are saved at once but apply on next launch.
    Check(ModCatalog.SetEnabled("beta", true) && ModCatalog.SetEnabled("ALPHA", false), "changes saved");
    Check(!ModCatalog.IsEnabled(Path.Combine(mods, "beta")) && ModCatalog.IsEnabled(Path.Combine(mods, "alpha")),
        "this run keeps its startup set");
    Check(ConfigManager.Game.ActiveMods.SequenceEqual(["beta", "script.zip"]), "case-insensitive replace, no duplicates");
    Check(File.ReadAllText(settings).Contains("\"beta\"") && ModCatalog.RestartRequired, "pending change on disk");
    Check(ModCatalog.UndoChanges() && !ModCatalog.RestartRequired &&
        ConfigManager.Game.ActiveMods.SequenceEqual(["alpha", "script.zip"]), "undo restores the startup set");

    // A window that opts out never reopens from interface.ini, and its stale
    // open flag is dropped on the next save.
    File.WriteAllLines(Path.Combine(user, "interface.ini"),
        ["[RecompOne]", "Panels.Remembered=True", "Panels.Fresh=True"]);
    ConfigManager.Load();
    var remembered = new Panel("Remembered", true);
    var fresh = new Panel("Fresh", false);
    ConfigManager.ApplyViewToPanels([remembered, fresh]);
    Check(remembered.IsOpen && !fresh.IsOpen, "only remembering panels reopen");
    fresh.IsOpen = true;
    ConfigManager.SaveView([remembered, fresh]);
    string ini = File.ReadAllText(Path.Combine(user, "interface.ini"));
    Check(ini.Contains("Panels.Remembered=True") && !ini.Contains("Panels.Fresh"), "opt-out panel not saved");

    Console.WriteLine($"[ModsConfigContract] PASS checks={checks}");
    return 0;
}
finally
{
    Environment.SetEnvironmentVariable("RECOMPONE_USER_DATA_DIR", null);
    Environment.SetEnvironmentVariable("RECOMPONE_MOD_DIR", null);
    Directory.Delete(scratch, true);
}

sealed class Panel(string name, bool restores) : IPanel
{
    public string Name => name;
    public bool IsOpen { get; set; }
    public void Draw() { }
    public bool RestoresOpenState => restores;
}
