using System.Diagnostics;
using System.Globalization;

namespace RecompOne.Runtime.Host;

/// <summary>
/// Relaunches the game with the same executable, arguments and working
/// directory, for settings that only load at startup (ActiveMods).
/// </summary>
public static class GameRestart
{
    const string PredecessorVariable = "RECOMPONE_RESTART_AFTER_PID";

    /// <summary>Why the last restart attempt failed, for the Mods window.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// Starts the new instance, then asks the host to quit through its normal
    /// close path so view and game settings are saved as on any exit. Called
    /// from UI handlers, so a failure is reported, never thrown: an exception
    /// escaping a render callback ends the process.
    /// </summary>
    public static bool Restart()
    {
        try
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The game executable path is unknown.");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };
            foreach (string argument in Environment.GetCommandLineArgs().Skip(1))
                start.ArgumentList.Add(argument);
            start.Environment[PredecessorVariable] =
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            using var next = Process.Start(start)
                ?? throw new InvalidOperationException("Windows did not start the game.");
            Console.WriteLine($"[Host] restarting: new pid={next.Id}");
            LastError = "";
            HostWindow.RequestQuit();
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"The game could not be restarted: {ex.Message}";
            Console.Error.WriteLine($"[Host] restart failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Called before anything opens a shared file. The new instance would
    /// otherwise truncate v8_latest.log while the old one is still writing it
    /// and saving memory cards and settings on its way out.
    /// </summary>
    public static void WaitForPredecessor()
    {
        string? value = Environment.GetEnvironmentVariable(PredecessorVariable);
        if (value == null) return;
        // Not inherited by anything this instance starts.
        Environment.SetEnvironmentVariable(PredecessorVariable, null);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
            return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(TimeSpan.FromSeconds(15));
        }
        catch (ArgumentException) { } // already gone
        catch (InvalidOperationException) { }
    }
}
