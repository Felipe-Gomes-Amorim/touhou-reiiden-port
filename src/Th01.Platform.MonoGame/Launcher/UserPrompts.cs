using System.Runtime.InteropServices;

namespace Th01.Platform.MonoGame.Launcher;

public static class UserPrompts
{
    public static IUserPrompt Create() => OperatingSystem.IsWindows() ? new WindowsPrompt() : new ConsolePrompt();

    /// <summary>True while Shift is held (Windows only). Holding it at startup asks for the game file again.</summary>
    public static bool ShiftHeld() => OperatingSystem.IsWindows() && (WindowsPrompt.GetAsyncKeyState(0x10) & 0x8000) != 0;
}

sealed class ConsolePrompt : IUserPrompt
{
    public string? PickGameFile()
    {
        Console.WriteLine("Enter the path of your Touhou Reiiden .hdi image (or a folder with the game files), empty to cancel:");
        string? line = Console.ReadLine()?.Trim().Trim('"');
        return string.IsNullOrEmpty(line) ? null : line;
    }

    public void ShowError(string message) => Console.Error.WriteLine(message);
}

/// <summary>Native Windows dialogs through comdlg32/user32, so the launcher needs no extra dependency.</summary>
sealed partial class WindowsPrompt : IUserPrompt
{
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint hWnd, string text, string caption, uint type);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileName(ref OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential)]
    struct OpenFileName
    {
        public int StructSize;
        public nint Owner, Instance, Filter, CustomFilter;
        public int MaxCustomFilter, FilterIndex;
        public nint File;
        public int MaxFile;
        public nint FileTitle;
        public int MaxFileTitle;
        public nint InitialDir, Title;
        public int Flags;
        public short FileOffset, FileExtension;
        public nint DefExt, CustData, Hook, TemplateName, Reserved;
        public int ReservedInt, FlagsEx;
    }

    const int PathMustExist = 0x800, FileMustExist = 0x1000, Explorer = 0x80000;

    public string? PickGameFile()
    {
        const int bufferChars = 2048;
        nint file = Marshal.AllocHGlobal(bufferChars * 2);
        nint filter = Marshal.StringToHGlobalUni("Touhou Reiiden disk image (*.hdi)\0*.hdi\0All files (*.*)\0*.*\0");
        nint title = Marshal.StringToHGlobalUni("Select your Touhou Reiiden .hdi file");
        try
        {
            Marshal.WriteInt16(file, 0);
            var ofn = new OpenFileName
            {
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Filter = filter,
                File = file,
                MaxFile = bufferChars,
                Title = title,
                Flags = PathMustExist | FileMustExist | Explorer,
            };
            return GetOpenFileName(ref ofn) ? Marshal.PtrToStringUni(file) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(file);
            Marshal.FreeHGlobal(filter);
            Marshal.FreeHGlobal(title);
        }
    }

    public void ShowError(string message) => MessageBox(0, message, "Touhou Reiiden (fan port)", 0x10 /* MB_ICONERROR */);
}
