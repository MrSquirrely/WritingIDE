using System.Runtime.InteropServices;

using Microsoft.Win32;

using WritingIDE.Core.Persistence;

namespace WritingIDE.Services;

/// <summary>
/// Opens .novproj files in WritingIDE from Explorer: registered for the current user (HKCU\Software\Classes, so no
/// administrator rights) when the installer runs and after each update, and removed again on uninstall.
/// </summary>
public static class FileAssociation {
    public const string ProgId = "WritingIDE.Project";
    private const string ClassesKey = @"Software\Classes";
    private const int ShcneAssocChanged = 0x08000000;

    /// <summary>Points .novproj at the installed WritingIDE.</summary>
    public static void Register() {
        string? executable = Environment.ProcessPath;
        if (executable is null) {
            return;
        }
        // Velopack runs the app from ...\WritingIDE.App\current\, which an update replaces; its launcher one folder up
        // stays put and passes the file on, so Explorer uses that (as the shortcuts do).
        string? folder = Path.GetDirectoryName(executable);
        if ((folder is not null) && Path.GetFileName(folder).Equals("current", StringComparison.OrdinalIgnoreCase)) {
            string launcher = Path.Combine(Path.GetDirectoryName(folder)!, Path.GetFileName(executable));
            if (File.Exists(launcher)) {
                executable = launcher;
            }
        }
        using RegistryKey classes = Registry.CurrentUser.CreateSubKey(ClassesKey);
        using (RegistryKey extension = classes.CreateSubKey(ProjectSerializer.Extension)) {
            extension.SetValue("", ProgId);
            using RegistryKey openWith = extension.CreateSubKey("OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
        using (RegistryKey progId = classes.CreateSubKey(ProgId)) {
            progId.SetValue("", "WritingIDE Project");
            using (RegistryKey icon = progId.CreateSubKey("DefaultIcon")) {
                icon.SetValue("", $"\"{executable}\",0");
            }
            using RegistryKey command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{executable}\" \"%1\"");
        }
        NotifyShell();
    }

    /// <summary>Removes the file type, but only the parts that still point at WritingIDE.</summary>
    public static void Unregister() {
        using RegistryKey? classes = Registry.CurrentUser.OpenSubKey(ClassesKey, writable: true);
        if (classes is null) {
            return;
        }
        classes.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
        using (RegistryKey? extension = classes.OpenSubKey(ProjectSerializer.Extension, writable: true)) {
            if (extension is not null) {
                using (RegistryKey? openWith = extension.OpenSubKey("OpenWithProgids", writable: true)) {
                    openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
                }
                if (string.Equals(extension.GetValue("") as string, ProgId, StringComparison.Ordinal)) {
                    extension.DeleteValue("", throwOnMissingValue: false);
                }
            }
        }
        using (RegistryKey? extension = classes.OpenSubKey(ProjectSerializer.Extension)) {
            bool empty = (extension is not null) && (extension.ValueCount == 0)
                && extension.GetSubKeyNames().All(name => name.Equals("OpenWithProgids", StringComparison.OrdinalIgnoreCase) && (extension.OpenSubKey(name)?.ValueCount ?? 0) == 0);
            if (empty) {
                classes.DeleteSubKeyTree(ProjectSerializer.Extension, throwOnMissingSubKey: false);
            }
        }
        NotifyShell();
    }

    /// <summary>Tells Explorer to pick up the new icon and "Open" command.</summary>
    private static void NotifyShell() {
        SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}