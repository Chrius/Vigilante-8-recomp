using System.Runtime.InteropServices;
using System.Text;
using RecompOne.Runtime.Host;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
}

// The setup window's former declaration. Its StringBuilder field cannot be
// marshalled inside a struct, so sizing it throws: that exception escaped the
// render callback and closed the game when "Choose BIN / CUE..." was clicked.
bool legacyThrew = false;
try { _ = Marshal.SizeOf<LegacyOpenFileName>(); }
catch (ArgumentException) { legacyThrew = true; }
Check(legacyThrew, "legacy StringBuilder declaration is rejected by the marshaller");

// OPENFILENAMEW is 152 bytes on x64; comdlg32 validates lStructSize.
Check(NativeFileDialog.StructSize == 152, $"OPENFILENAMEW size {NativeFileDialog.StructSize} != 152");

Console.WriteLine($"[FileDialogContract] PASS checks={checks}");
return 0;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct LegacyOpenFileName
{
    public int Size; public nint Owner, Instance; public string Filter;
    public nint CustomFilter; public int MaxCustomFilter, FilterIndex;
    public StringBuilder File; public int MaxFile; public nint FileTitle;
    public int MaxFileTitle; public string? InitialDir, Title; public int Flags;
    public short FileOffset, FileExtension; public nint DefaultExtension, CustomData, Hook, TemplateName, Reserved;
    public int Reserved2, FlagsEx;
}
