using System.Reflection;
using System.Windows.Controls;

namespace WritingIDE.Views;

/// <summary>Help > About, shown as a HandyControls Dialog overlay on the main window.</summary>
public partial class AboutView : UserControl {
    public AboutView(string? harperStatus) {
        InitializeComponent();
        Assembly assembly = typeof(AboutView).Assembly;
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString()
            ?? "1.0";
        VersionText.Text = $"Version {version}  \u00B7  .NET {Environment.Version}";
        // No HandyControls version here: its assemblies report an older version than the NuGet package they ship in.
        CreditsText.Text = $"Built with AvalonEdit and HandyControls. Grammar plugin: Harper by Automattic ({harperStatus ?? "not running"}).";
    }
}