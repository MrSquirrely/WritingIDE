using Velopack;

using WritingIDE.Services;

namespace WritingIDE;

/// <summary>
/// The entry point. Velopack runs first: when the installer, an update or the uninstaller starts WritingIDE to run a
/// hook, the hook runs (registering or removing the .novproj file type) and the process exits. Otherwise the WPF app
/// starts as usual.
/// </summary>
public static class Program {
    [STAThread]
    public static void Main() {
        VelopackApp.Build()
            .OnAfterInstallFastCallback(static _ => FileAssociation.Register())
            .OnAfterUpdateFastCallback(static _ => FileAssociation.Register())
            .OnBeforeUninstallFastCallback(static _ => FileAssociation.Unregister())
            .Run();

        App app = new();
        app.InitializeComponent();
        app.Run();
    }
}