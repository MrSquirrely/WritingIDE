using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using Microsoft.Win32;

using WritingIDE.Infrastructure;
using WritingIDE.Plugins;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>Tools > Plugins: enable, locate, install and test the Harper grammar checker.</summary>
public partial class PluginsDialog : HandyControl.Controls.Window {
    private const string TestSentence = "Their going to the libary tomorow, and they has an big plan to bring there freinds.";

    private readonly MainViewModel _shell;
    private readonly HarperPlugin _harper;
    private readonly bool _ready;

    public PluginsDialog(MainViewModel shell) {
        InitializeComponent();
        _shell = shell;
        _harper = shell.Harper;
        EnabledSwitch.IsChecked = _harper.Settings.Enabled;
        PathBox.Text = _harper.Settings.ExecutablePath;
        DialectBox.ItemsSource = HarperPlugin.Dialects;
        DialectBox.SelectedItem = _harper.Settings.Dialect;
        _harper.PropertyChanged += OnHarperPropertyChanged;
        Closed += (_, _) => _harper.PropertyChanged -= OnHarperPropertyChanged;
        _ready = true;
        UpdateStatus();
        InitializeExtensionsAndApi();
        InitializeBrowse();
    }

    private void OnHarperPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        Dispatcher.BeginInvoke(UpdateStatus);
    }

    private void UpdateStatus() {
        string? found = _harper.ResolveExecutable();
        if (_harper.IsRunning) {
            StatusText.Text = _harper.Status;
            StatusDot.Foreground = Visuals.CreateBrush("#388A34");
        }
        else if (found is null) {
            StatusText.Text = string.IsNullOrWhiteSpace(_harper.Settings.ExecutablePath)
                ? "harper-ls is not installed. Use \"Download and install latest\", or install it yourself and choose its location."
                : $"No harper-ls.exe at '{_harper.Settings.ExecutablePath}'.";
            StatusDot.Foreground = Visuals.CreateBrush("#C88A00");
        }
        else {
            StatusText.Text = _harper.Settings.Enabled ? _harper.Status : $"Disabled. Found {found}";
            StatusDot.Foreground = _harper.Status.StartsWith("Could not", StringComparison.Ordinal) ? Visuals.ErrorBrush : Visuals.CreateBrush("#9A9A9A");
        }
        TestButton.IsEnabled = _harper.IsRunning;
        UninstallButton.IsEnabled = HarperInstaller.IsInstalled;
        InstallButton.ToolTip = HarperInstaller.InstalledVersion is string version ? $"Installed: {version}. Click to update to the latest release." : null;
    }

    private async void OnUninstall(object sender, RoutedEventArgs e) {
        string version = HarperInstaller.InstalledVersion ?? "harper-ls";
        if (HandyControl.Controls.MessageBox.Show(this, $"Uninstall {version}? This stops the Harper plugin and deletes {HarperPlugin.InstallDirectory}.",
            "Uninstall Harper", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) {
            return;
        }
        UninstallButton.IsEnabled = false;
        try {
            _harper.Settings.Enabled = false;
            EnabledSwitch.IsChecked = false;
            await _shell.ApplyPluginSettingsAsync();
            await HarperInstaller.UninstallAsync();
            TestOutput.Text = $"Uninstalled {version}.";
            _shell.Log($"Uninstalled Harper ({version}) from {HarperPlugin.InstallDirectory}.");
            string? other = HarperPlugin.FindExecutable();
            if (other is not null) {
                TestOutput.Text += $" Another copy is still available at {other}; WritingIDE did not install it, so it was left alone.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            TestOutput.Text = $"Could not remove every file: {exception.Message}";
        }
        finally {
            UpdateStatus();
        }
    }

    private async void OnEnabledChanged(object sender, RoutedEventArgs e) {
        if (!_ready) {
            return;
        }
        _harper.Settings.Enabled = EnabledSwitch.IsChecked == true;
        await ApplyAsync();
    }

    private async void OnPathChanged(object sender, RoutedEventArgs e) {
        string path = PathBox.Text.Trim();
        if (path == _harper.Settings.ExecutablePath) {
            return;
        }
        _harper.Settings.ExecutablePath = path;
        await ApplyAsync();
    }

    private async void OnDialectChanged(object sender, SelectionChangedEventArgs e) {
        if (!_ready || (DialectBox.SelectedItem is not string dialect) || (dialect == _harper.Settings.Dialect)) {
            return;
        }
        _harper.Settings.Dialect = dialect;
        await ApplyAsync();
    }

    private async void OnBrowse(object sender, RoutedEventArgs e) {
        OpenFileDialog dialog = new() { Title = "Locate harper-ls", Filter = "harper-ls|harper-ls.exe|Programs (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true) {
            PathBox.Text = dialog.FileName;
            _harper.Settings.ExecutablePath = dialog.FileName;
            await ApplyAsync();
        }
    }

    private async void OnDetect(object sender, RoutedEventArgs e) {
        PathBox.Text = "";
        _harper.Settings.ExecutablePath = "";
        string? found = HarperPlugin.FindExecutable();
        TestOutput.Text = found is null ? "harper-ls was not found automatically." : $"Found {found}";
        await ApplyAsync();
    }

    private async void OnInstall(object sender, RoutedEventArgs e) {
        InstallButton.IsEnabled = false;
        Progress<string> progress = new(message => TestOutput.Text = message);
        try {
            await _harper.StopAsync();
            HarperInstaller.InstallResult result = await HarperInstaller.InstallLatestAsync(progress);
            PathBox.Text = "";
            _harper.Settings.ExecutablePath = "";
            _harper.Settings.Enabled = true;
            EnabledSwitch.IsChecked = true;
            TestOutput.Text = $"Installed Harper {result.Version} to {result.ExecutablePath}.";
            _shell.Log($"Installed Harper {result.Version} to {result.ExecutablePath}.");
            await ApplyAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException or TaskCanceledException) {
            TestOutput.Text = $"Install failed: {exception.Message}";
        }
        finally {
            InstallButton.IsEnabled = true;
        }
    }

    private async void OnTest(object sender, RoutedEventArgs e) {
        TestButton.IsEnabled = false;
        TestOutput.Text = $"Checking: {TestSentence}";
        try {
            Stopwatch stopwatch = Stopwatch.StartNew();
            IReadOnlyList<HarperLint> lints = await Task.Run(() => _harper.Lint(TestSentence, CancellationToken.None));
            StringBuilder report = new();
            report.AppendLine($"Checked: {TestSentence}");
            report.AppendLine($"Harper found {lints.Count} issue(s) in {stopwatch.ElapsedMilliseconds} ms:");
            foreach (HarperLint lint in lints) {
                string fixes = lint.Fixes.Count > 0 ? $"  \u2192 {string.Join(" | ", lint.Fixes.Take(3).Select(static fix => fix.Replacement))}" : "";
                report.AppendLine($"  '{TestSentence.Substring(lint.Offset, lint.Length)}': {lint.Message}{fixes}");
            }
            TestOutput.Text = report.ToString();
        }
        finally {
            TestButton.IsEnabled = _harper.IsRunning;
        }
    }

    private void OnOpenProject(object sender, RoutedEventArgs e) {
        Process.Start(new ProcessStartInfo(HarperPlugin.ProjectUrl) { UseShellExecute = true });
    }

    private async Task ApplyAsync() {
        StatusText.Text = _harper.Settings.Enabled ? "Starting harper-ls..." : "Stopping...";
        await _shell.ApplyPluginSettingsAsync();
        UpdateStatus();
    }

    // ------------------------------------------------------------------
    // Extensions and the local API
    // ------------------------------------------------------------------

    private void InitializeExtensionsAndApi() {
        ExtensionsFolderText.Text = Services.ExtensionManager.Root;
        ExtensionsList.ItemsSource = _shell.Extensions.Installed;
        NoExtensionsText.Visibility = _shell.Extensions.Installed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApiSwitch.IsChecked = _shell.Settings.Api.Enabled;
        ApiPortBox.Text = _shell.Settings.Api.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UpdateApi();
    }

    private void OnOpenExtensionsFolder(object sender, RoutedEventArgs e) {
        Directory.CreateDirectory(Services.ExtensionManager.Root);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Services.ExtensionManager.Root}\"") { UseShellExecute = true });
    }

    private void OnExtensionToggled(object sender, RoutedEventArgs e) {
        if ((sender is ToggleButton { Tag: Services.ExtensionInfo extension } toggle)) {
            _shell.Extensions.SetEnabled(extension, toggle.IsChecked == true);
            ExtensionsList.Items.Refresh();
        }
    }

    private void OnApiToggled(object sender, RoutedEventArgs e) {
        _shell.Settings.Api.Enabled = ApiSwitch.IsChecked == true;
        _shell.Settings.Save();
        if (_shell.Settings.Api.Enabled) {
            _shell.LocalApi.Start();
        }
        else {
            _shell.LocalApi.Stop();
        }
        UpdateApi();
    }

    private void OnApiPortChanged(object sender, RoutedEventArgs e) {
        if (int.TryParse(ApiPortBox.Text, out int port) && (port is > 1024 and < 65536) && (port != _shell.Settings.Api.Port)) {
            _shell.Settings.Api.Port = port;
            _shell.Settings.Save();
            if (_shell.Settings.Api.Enabled) {
                _shell.LocalApi.Start();
            }
        }
        ApiPortBox.Text = _shell.Settings.Api.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UpdateApi();
    }

    private void OnCopyToken(object sender, RoutedEventArgs e) {
        if (_shell.Settings.Api.Token.Length > 0) {
            Clipboard.SetText(_shell.Settings.Api.Token);
            MainViewModel.Notify("Token copied.");
        }
    }

    private void OnNewToken(object sender, RoutedEventArgs e) {
        _shell.Settings.Api.Token = Services.LocalApiServer.NewToken();
        _shell.Settings.Save();
        UpdateApi();
    }

    private void UpdateApi() {
        if (_shell.Settings.Api.Token.Length == 0) {
            _shell.Settings.Api.Token = Services.LocalApiServer.NewToken();
            _shell.Settings.Save();
        }
        ApiTokenBox.Text = _shell.Settings.Api.Token;
        ApiStatusText.Text = _shell.LocalApi.IsRunning ? $"\u25CF {_shell.LocalApi.Status}" : (_shell.Settings.Api.Enabled ? _shell.LocalApi.Status : "Off. Switch it on to accept requests.");
        ApiExampleBox.Text = $"curl -H \"Authorization: Bearer {_shell.Settings.Api.Token}\" {_shell.LocalApi.BaseUrl}/chapters\n\n"
            + $"Invoke-RestMethod {_shell.LocalApi.BaseUrl}/project -Headers @{{ Authorization = \"Bearer {_shell.Settings.Api.Token}\" }}";
    }
}