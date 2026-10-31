using System.Text.Json;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Modding;

/// <summary>One installed mod as the Mods window shows it.</summary>
public sealed record ModEntry(
    string Key, string Name, string Description, string Contents, bool IsArchive);

/// <summary>
/// settings.json <c>ActiveMods</c> lists the mod folders (and script
/// archives) that load. Every loader that walks the mods folder asks this
/// catalog instead of only checking that its own files exist, so a mod can be
/// switched off without deleting it.
///
/// The list in force at the first query is kept for the whole process: mods
/// load once at startup, and a scanner that runs lazily later (the arena
/// registry, for one) must see the same set as everything else. Changes from
/// the Mods window are saved immediately and take effect on restart.
/// </summary>
public static class ModCatalog
{
    static readonly object _gate = new();
    // Null until first queried; then the active set for this process.
    static HashSet<string>? _active;
    static IReadOnlyList<ModEntry>? _described;

    /// <summary>
    /// Mod folders (and script archives) directly under a mods root. Folders
    /// starting with '.' are runtime-owned, such as the script-mod build cache.
    /// </summary>
    public static IEnumerable<string> InstalledMods(string modsRoot)
    {
        if (!Directory.Exists(modsRoot)) return [];
        return Directory.EnumerateDirectories(modsRoot)
            .Where(path => !Path.GetFileName(path).StartsWith('.'))
            .Concat(Directory.EnumerateFiles(modsRoot, "*.zip"))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Releases before this list was honoured saved it as an empty, unused
    /// array, so an empty list there cannot mean "no mods". Version 1 marks a
    /// list initialised from the installed mods; from then on it is the
    /// player's choice, and a mod added later stays off until enabled.
    /// Returns true when <paramref name="game"/> changed and must be saved.
    /// </summary>
    public static bool InitializeActiveMods(GameConfig game)
    {
        if (game.ModsVersion >= 1) return false;
        game.ActiveMods = InstalledMods(Runtime.ModsDirectory)
            .Select(path => Path.GetFileName(path)!).ToList();
        game.ModsVersion = 1;
        return true;
    }

    /// <summary>Whether a mod folder or archive loads in this process.</summary>
    public static bool IsEnabled(string modPath) =>
        Active().Contains(Path.GetFileName(Path.GetFullPath(modPath).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

    /// <summary>The enabled mod folders under a mods root, in load order.</summary>
    public static IEnumerable<string> EnabledDirectories(string modsRoot) =>
        InstalledMods(modsRoot).Where(path => Directory.Exists(path) && IsEnabled(path));

    /// <summary>
    /// True when an explicitly configured path points into a disabled mod,
    /// so a saved package path cannot bypass ActiveMods.
    /// </summary>
    public static bool IsInsideDisabledMod(string path)
    {
        string root = Path.GetFullPath(Runtime.ModsDirectory) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        string folder = full[root.Length..].Split(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return folder.Length != 0 && !IsEnabled(Path.Combine(root, folder));
    }

    public static bool IsLoaded(string key) => Active().Contains(key);

    /// <summary>The saved choice, which applies on next launch.</summary>
    public static bool IsEnabledNextLaunch(string key)
    {
        var game = ConfigManager.Game;
        return game.ModsVersion < 1 ||
            game.ActiveMods.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    public static bool RestartRequired =>
        Describe().Any(mod => IsLoaded(mod.Key) != IsEnabledNextLaunch(mod.Key));

    /// <summary>Why the last settings write failed, for the Mods window.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// Saves one mod's choice. Called from UI handlers, so a write failure (a
    /// read-only install folder, say) is reported, never thrown.
    /// </summary>
    public static bool SetEnabled(string key, bool enabled)
    {
        lock (_gate)
        {
            Apply(key, enabled);
            Console.WriteLine($"[Mods] ActiveMods: {key} {(enabled ? "on" : "off")} (applies after restart)");
            return Save();
        }
    }

    /// <summary>Saves the state of this process back, cancelling pending changes.</summary>
    public static bool UndoChanges()
    {
        lock (_gate)
        {
            foreach (var mod in Describe()) Apply(mod.Key, IsLoaded(mod.Key));
            return Save();
        }
    }

    static void Apply(string key, bool enabled)
    {
        var game = ConfigManager.Game;
        InitializeActiveMods(game);
        var active = game.ActiveMods
            .Where(existing => !existing.Equals(key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (enabled) active.Add(key);
        game.ActiveMods = active.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static bool Save()
    {
        try { ConfigManager.SaveGame(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Could not save settings.json: {ex.Message}";
            Console.Error.WriteLine($"[Mods] {LastError}");
            return false;
        }
        LastError = "";
        return true;
    }

    static HashSet<string> Active()
    {
        lock (_gate)
        {
            if (_active != null) return _active;
            var game = ConfigManager.Game;
            // Without loaded settings (tools, contract tests) every mod loads,
            // matching what the first launch of a new install records.
            IEnumerable<string> keys = game.ModsVersion < 1
                ? InstalledMods(Runtime.ModsDirectory).Select(path => Path.GetFileName(path)!)
                : game.ActiveMods;
            _active = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            var installed = InstalledMods(Runtime.ModsDirectory)
                .Select(path => Path.GetFileName(path)!).ToArray();
            string Join(IEnumerable<string> list) =>
                list.Any() ? string.Join(',', list) : "(none)";
            if (installed.Length != 0)
                Console.WriteLine(
                    $"[Mods] ActiveMods active={Join(installed.Where(_active.Contains))} " +
                    $"inactive={Join(installed.Where(key => !_active.Contains(key)))}");
            return _active;
        }
    }

    /// <summary>
    /// Name, description and contents of each installed mod. Counting a large
    /// texture pack is slow, so this is computed once per process.
    /// </summary>
    public static IReadOnlyList<ModEntry> Describe()
    {
        lock (_gate)
        {
            return _described ??= InstalledMods(Runtime.ModsDirectory)
                .Select(DescribeOne).ToArray();
        }
    }

    static ModEntry DescribeOne(string path)
    {
        string key = Path.GetFileName(path);
        if (File.Exists(path))
            return new ModEntry(key, Humanize(Path.GetFileNameWithoutExtension(path)), "", "script archive", true);

        string name = "", description = "";
        foreach (string metadata in new[] { "mod.json", "manifest.json" })
        {
            string file = Path.Combine(path, metadata);
            if (!File.Exists(file)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(file));
                if (name.Length == 0 && document.RootElement.TryGetProperty("name", out var n))
                    name = n.GetString() ?? "";
                if (description.Length == 0 && document.RootElement.TryGetProperty("description", out var d))
                    description = d.GetString() ?? "";
            }
            catch (JsonException) { }
        }
        string readme = Path.Combine(path, "README.md");
        if (description.Length == 0 && File.Exists(readme))
            description = File.ReadLines(readme)
                .Select(line => line.Trim().TrimStart('#').Trim())
                .FirstOrDefault(line => line.Length != 0) ?? "";
        if (name.Length == 0) name = Humanize(key);
        return new ModEntry(key, name, description, DescribeContents(path), false);
    }

    // Generic inventory of what a mod supplies, from the layout the loaders use.
    static string DescribeContents(string path)
    {
        var parts = new List<string>();
        int Count(string directory, string pattern) => Directory.Exists(Path.Combine(path, directory))
            ? Directory.EnumerateFiles(Path.Combine(path, directory), pattern, SearchOption.AllDirectories).Count()
            : 0;
        int textures = Count("images", "*.dds");
        if (textures != 0) parts.Add($"{textures:N0} textures");
        if (Directory.Exists(Path.Combine(path, "loading_cards"))) parts.Add("loading cards");
        if (File.Exists(Path.Combine(path, "VEHICLES.V8R"))) parts.Add("vehicles");
        if (File.Exists(Path.Combine(path, "ARENAS.V8R"))) parts.Add("arenas");
        if (File.Exists(Path.Combine(path, "quests.json"))) parts.Add("quests");
        int files = Count("files", "*");
        if (files != 0) parts.Add($"{files:N0} game file{(files == 1 ? "" : "s")}");
        if (Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories).Any()) parts.Add("scripts");
        return string.Join(", ", parts);
    }

    static string Humanize(string key) => string.Join(' ',
        key.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
