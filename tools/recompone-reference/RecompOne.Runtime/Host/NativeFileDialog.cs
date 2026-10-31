using System.Runtime.InteropServices;

namespace RecompOne.Runtime.Host;

/// <summary>
/// Win32 open-file dialog shared by the setup window and the guest-vehicle
/// panel. The dialog runs on its own STA thread so ImGui hosts keep rendering
/// and pumping messages while it is open; callers poll the returned task.
/// </summary>
public static class NativeFileDialog
{
    const int OfnNoChangeDir = 0x00000008;
    const int OfnPathMustExist = 0x00000800;
    const int OfnFileMustExist = 0x00001000;
    const int PathCapacity = 32768;

    // OPENFILENAMEW. Every string is an unmanaged pointer: the marshaller
    // refuses a StringBuilder field inside a struct ("Struct or class fields
    // cannot be of type StringBuilder"), which made the previous declarations
    // throw before any dialog opened, and the filter's embedded NULs must
    // reach comdlg32 intact.
    [StructLayout(LayoutKind.Sequential)]
    struct OpenFileName
    {
        public int Size;
        public nint Owner, Instance, Filter, CustomFilter;
        public int MaxCustomFilter, FilterIndex;
        public nint File;
        public int MaxFile;
        public nint FileTitle;
        public int MaxFileTitle;
        public nint InitialDir, Title;
        public int Flags;
        public short FileOffset, FileExtension;
        public nint DefaultExtension, CustomData, Hook, TemplateName, Reserved;
        public int Reserved2, FlagsEx;
    }

    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetOpenFileName(ref OpenFileName value);

    [DllImport("comdlg32.dll")]
    static extern uint CommDlgExtendedError();

    [DllImport("user32.dll")]
    static extern nint GetActiveWindow();

    /// <summary>Native size of the dialog structure (152 bytes on x64).</summary>
    public static int StructSize => Marshal.SizeOf<OpenFileName>();

    /// <summary>
    /// The calling thread's active window, for use as the dialog owner. Only
    /// pass an owner whose thread keeps pumping messages until the task
    /// completes: the dialog disables it with a cross-thread SendMessage.
    /// </summary>
    public static nint ActiveWindow() => GetActiveWindow();

    /// <summary>
    /// Shows the dialog. Completes with the selected path, or null when the
    /// user cancels; faults when comdlg32 reports an error.
    /// </summary>
    public static Task<string?> OpenAsync(
        string title, string filter, string? initialDirectory = null, nint owner = 0)
    {
        var result = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // The shell's dialog and its namespace extensions require STA; the
        // host's main thread is MTA.
        var thread = new Thread(() =>
        {
            try { result.SetResult(Open(title, filter, initialDirectory, owner)); }
            catch (Exception ex) { result.SetException(ex); }
        })
        { IsBackground = true, Name = "Open file dialog" };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    static string? Open(string title, string filter, string? initialDirectory, nint owner)
    {
        nint file = 0, filterText = 0, titleText = 0, directoryText = 0;
        try
        {
            file = Marshal.AllocHGlobal(PathCapacity * sizeof(char));
            Marshal.WriteInt16(file, 0);
            filterText = Marshal.StringToHGlobalUni(filter);
            titleText = Marshal.StringToHGlobalUni(title);
            if (initialDirectory != null)
                directoryText = Marshal.StringToHGlobalUni(initialDirectory);
            var value = new OpenFileName
            {
                Size = Marshal.SizeOf<OpenFileName>(),
                Owner = owner,
                Filter = filterText,
                FilterIndex = 1,
                File = file,
                MaxFile = PathCapacity,
                InitialDir = directoryText,
                Title = titleText,
                Flags = OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir,
            };
            if (GetOpenFileName(ref value))
                return Marshal.PtrToStringUni(file);
            uint error = CommDlgExtendedError();
            if (error != 0)
                throw new InvalidOperationException(
                    $"The Windows file dialog failed (error 0x{error:X4}).");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(file);
            Marshal.FreeHGlobal(filterText);
            Marshal.FreeHGlobal(titleText);
            Marshal.FreeHGlobal(directoryText);
        }
    }
}
