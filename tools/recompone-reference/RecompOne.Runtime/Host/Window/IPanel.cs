namespace RecompOne.Runtime.Host.Window;

public interface IPanel
{
    string Name { get; }
    bool IsOpen { get; set; }
    void Draw();

    /// <summary>
    /// False for windows that must start closed on every launch instead of
    /// reopening because they were open when the game last closed.
    /// </summary>
    bool RestoresOpenState => true;
}
