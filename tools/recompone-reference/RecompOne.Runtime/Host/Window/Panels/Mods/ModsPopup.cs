using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;

namespace RecompOne.Runtime.Modding;

internal sealed class ModsPopup : IPanel
{
    static readonly Vector4 Warning = new(1f, 0.8f, 0.25f, 1f);
    static readonly Vector4 Failure = new(1f, 0.45f, 0.35f, 1f);

    public string Name => "Mods";
    public bool IsOpen { get; set; }
    // Starts closed on every launch, including after its own Restart button.
    public bool RestoresOpenState => false;

    public void Draw()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(new Vector2(1040, 380), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(vp.GetCenter(), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        var mods = ModCatalog.Describe();
        ImGui.TextUnformatted($"{mods.Count} mod(s) installed");
        ImGui.SameLine();
        ImGui.TextDisabled($"  {Runtime.ModsDirectory}");
        ImGui.Separator();
        ImGui.Spacing();

        if (mods.Count == 0)
        {
            ImGui.TextDisabled("No mods installed. Put mod folders into the mods folder and restart.");
        }
        else if (ImGui.BeginTable("##mods-table", 5,
                     ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Mod");
            ImGui.TableSetupColumn("Folder");
            ImGui.TableSetupColumn("Contents");
            // Sized for the longest status; fitting the first frame's
            // "Loaded" cut off "Off after restart".
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed,
                ImGui.CalcTextSize("Off after restart").X + 8);
            ImGui.TableHeadersRow();

            foreach (var mod in mods)
            {
                bool loaded = ModCatalog.IsLoaded(mod.Key);
                bool enabled = ModCatalog.IsEnabledNextLaunch(mod.Key);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Checkbox($"##mod-{mod.Key}", ref enabled))
                    ModCatalog.SetEnabled(mod.Key, enabled);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(mod.Name);
                if (ImGui.IsItemHovered() && mod.Description.Length != 0)
                    ImGui.SetTooltip(mod.Description);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(mod.Key);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(mod.Contents);
                ImGui.TableNextColumn();
                string status = (loaded, enabled) switch
                {
                    (true, true) => "Loaded",
                    (false, false) => "Disabled",
                    (false, true) => "On after restart",
                    (true, false) => "Off after restart",
                };
                if (loaded == enabled) ImGui.TextUnformatted(status);
                else ImGui.TextColored(Warning, status);
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        if (ModCatalog.RestartRequired)
        {
            ImGui.TextColored(Warning,
                "Mod changes apply after a restart. An unfinished match is lost.");
            if (ImGui.Button("Restart now", new Vector2(140, 0))) GameRestart.Restart();
            ImGui.SameLine();
            if (ImGui.Button("Undo changes", new Vector2(140, 0))) ModCatalog.UndoChanges();
        }
        else
        {
            ImGui.TextDisabled("Mods load at startup. Changes here are saved to settings.json (ActiveMods).");
        }
        foreach (string error in new[] { ModCatalog.LastError, GameRestart.LastError })
            if (error.Length != 0) ImGui.TextColored(Failure, error);

        DrawScriptMods();

        IsOpen = open;
        ImGui.End();
    }

    // C# script mods report their own metadata once compiled and loaded.
    static void DrawScriptMods()
    {
        var scripts = ModLoader.LoadedMods;
        if (scripts.Count == 0) return;
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted($"{scripts.Count} script mod(s) loaded");
        if (!ImGui.BeginTable("##script-mods-table", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("Id");
        ImGui.TableSetupColumn("Version");
        ImGui.TableSetupColumn("Author");
        ImGui.TableHeadersRow();
        foreach (var mod in scripts)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(mod.Name);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(mod.Id);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(mod.Version);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(mod.Author);
        }
        ImGui.EndTable();
    }
}
