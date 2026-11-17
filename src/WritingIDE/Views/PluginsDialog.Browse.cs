using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

using WritingIDE.Core.Plugins;
using WritingIDE.Services;

using HcMessageBox = HandyControl.Controls.MessageBox;

namespace WritingIDE.Views;

/// <summary>
/// The Plugin list tab: the plugins listed in the WritingIDE repository's GitHub discussions, with Install, Update and
/// Remove. Changes are staged and take effect the next time WritingIDE starts.
/// </summary>
public partial class PluginsDialog {
    private static readonly HttpClient CatalogHttp = CreateCatalogHttp();

    private readonly StackPanel _browseList = new();
    private readonly TextBlock _browseStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _restartNote = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold, Visibility = Visibility.Collapsed };
    private IReadOnlyList<PluginListing> _listings = [];
    private bool _browseLoaded;

    private static HttpClient CreateCatalogHttp() {
        HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60), MaxResponseContentBufferSize = PluginCatalog.MaxIndexBytes };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WritingIDE");
        return http;
    }

    private void InitializeBrowse() {
        _browseStatus.SetResourceReference(TextBlock.ForegroundProperty, "LabelBrush");
        _restartNote.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

        TextBlock intro = new() {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Text = "Plugins that other writers and developers have shared. Each one is a discussion on WritingIDE's GitHub page, where you can read about it and ask questions. "
                + "A plugin runs with the same rights as WritingIDE, so install only ones you trust."
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "LabelBrush");

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(DialogButton("Refresh", async () => await LoadListingsAsync()));
        buttons.Children.Add(DialogButton("Open the discussions", () => OpenLink(PluginCatalog.DiscussionsUrl)));

        DockPanel panel = new() { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(intro, Dock.Top);
        panel.Children.Add(intro);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        DockPanel.SetDock(_restartNote, Dock.Bottom);
        panel.Children.Add(_restartNote);
        DockPanel.SetDock(_browseStatus, Dock.Top);
        panel.Children.Add(_browseStatus);
        Border border = new() { BorderThickness = new Thickness(1), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _browseList } };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        panel.Children.Add(border);
        BrowseHost.Content = panel;

        // The list is fetched the first time the tab is shown, not when the dialog opens.
        Tabs.SelectionChanged += async (_, e) => {
            if (ReferenceEquals(e.OriginalSource, Tabs) && ReferenceEquals(Tabs.SelectedItem, BrowseTab) && !_browseLoaded) {
                await LoadListingsAsync();
            }
        };
    }

    private async Task LoadListingsAsync() {
        _browseLoaded = true;
        _browseStatus.Text = "Loading the plugin list...";
        _browseStatus.Visibility = Visibility.Visible;
        try {
            string json = await CatalogHttp.GetStringAsync(PluginCatalog.IndexUrl);
            _listings = PluginCatalog.Parse(json);
            _browseStatus.Text = _listings.Count == 0 ? "No plugins are listed yet." : "";
            _browseStatus.Visibility = _listings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException) {
            _listings = [];
            _browseStatus.Text = $"The plugin list could not be loaded. Check your internet connection and press Refresh. ({exception.Message})";
        }
        ShowListings();
    }

    private void ShowListings() {
        _browseList.Children.Clear();
        string root = ExtensionManager.Root;
        bool pending = false;
        foreach (PluginListing listing in _listings) {
            ExtensionInfo? installed = _shell.Extensions.Installed.FirstOrDefault(extension => extension.Id.Equals(listing.Id, StringComparison.OrdinalIgnoreCase));
            Version? installedVersion = installed is null ? null : (Version.TryParse(installed.Version, out Version? parsed) ? parsed : new Version(0, 0));
            string folder = installed is null ? PluginInstaller.FolderName(listing.Id) : Path.GetFileName(installed.Folder);
            PluginState state = PluginInstaller.GetState(listing, installedVersion, ExtensionManager.SdkVersion,
                PluginInstaller.IsStaged(root, listing.Id), PluginInstaller.IsMarkedForRemoval(root, folder));
            pending |= state is PluginState.InstallPending or PluginState.RemovalPending;
            _browseList.Children.Add(CreateListingRow(listing, state, installedVersion, folder));
        }
        _restartNote.Text = "Restart WritingIDE to finish installing or removing plugins.";
        _restartNote.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border CreateListingRow(PluginListing listing, PluginState state, Version? installedVersion, string folder) {
        StackPanel actions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        string status = state switch {
            PluginState.Installed => $"Installed ({installedVersion})",
            PluginState.UpdateAvailable => $"Installed ({installedVersion}); version {listing.Version} is available",
            PluginState.InstallPending => "Downloaded. It is installed when WritingIDE restarts.",
            PluginState.RemovalPending => "It is removed when WritingIDE restarts.",
            PluginState.NeedsNewerApp => $"Needs a newer WritingIDE (SDK {listing.Sdk}; this one has {ExtensionManager.SdkVersion.ToString(2)}). Update WritingIDE first.",
            _ => "Not installed"
        };
        switch (state) {
            case PluginState.NotInstalled:
                actions.Children.Add(DialogButton("Install", async () => await InstallAsync(listing), $"Install {listing.Name}"));
                break;
            case PluginState.UpdateAvailable:
                actions.Children.Add(DialogButton("Update", async () => await InstallAsync(listing), $"Update {listing.Name}"));
                actions.Children.Add(DialogButton("Remove", () => Remove(listing, folder), $"Remove {listing.Name}"));
                break;
            case PluginState.Installed:
                actions.Children.Add(DialogButton("Remove", () => Remove(listing, folder), $"Remove {listing.Name}"));
                break;
            case PluginState.RemovalPending:
                actions.Children.Add(DialogButton("Keep", () => {
                    PluginInstaller.CancelRemoval(ExtensionManager.Root, folder);
                    ShowListings();
                }, $"Keep {listing.Name}"));
                break;
        }

        StackPanel text = new();
        TextBlock title = new() { TextWrapping = TextWrapping.Wrap };
        title.Inlines.Add(new System.Windows.Documents.Run(listing.Name) { FontWeight = FontWeights.SemiBold, FontSize = 14 });
        System.Windows.Documents.Run byline = new($"   {listing.Version}{(listing.Author.Length > 0 ? $" by {listing.Author}" : "")}");
        byline.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "SubtleTextBrush");
        title.Inlines.Add(byline);
        text.Children.Add(title);
        if (listing.Description.Length > 0) {
            TextBlock description = new() { Text = listing.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            description.SetResourceReference(TextBlock.ForegroundProperty, "LabelBrush");
            text.Children.Add(description);
        }
        StackPanel footer = new() { Orientation = Orientation.Horizontal };
        TextBlock statusText = new() { Text = status, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, state is PluginState.InstallPending or PluginState.RemovalPending or PluginState.UpdateAvailable ? "AccentBrush" : "SubtleTextBrush");
        footer.Children.Add(statusText);
        if (listing.Discussion.Length > 0) {
            footer.Children.Add(LinkButton("Read about it", () => OpenLink(listing.Discussion), $"Read about {listing.Name}"));
        }
        if (listing.Homepage.Length > 0) {
            footer.Children.Add(LinkButton("Source", () => OpenLink(listing.Homepage), $"Source of {listing.Name}"));
        }
        text.Children.Add(footer);

        DockPanel row = new() { Margin = new Thickness(12, 10, 12, 10) };
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        row.Children.Add(text);
        Border border = new() { Child = row, BorderThickness = new Thickness(0, 0, 0, 1) };
        border.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
        return border;
    }

    private async Task InstallAsync(PluginListing listing) {
        string by = listing.Author.Length > 0 ? $" by {listing.Author}" : "";
        if (HcMessageBox.Show(this, $"Install '{listing.Name}' {listing.Version}{by}?\n\nA plugin is a program: it runs with the same rights as WritingIDE and can read and change your files. "
            + "Install it only if you trust where it comes from.", "Install plugin", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) {
            return;
        }
        _browseStatus.Text = $"Downloading {listing.Name}...";
        _browseStatus.Visibility = Visibility.Visible;
        try {
            using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WritingIDE");
            byte[] zip = await PluginInstaller.DownloadAsync(http, listing);
            await Task.Run(() => PluginInstaller.Stage(ExtensionManager.Root, listing, zip));
            _shell.Log($"Plugin '{listing.Name}' {listing.Version} downloaded from {listing.Download}; it is installed at the next start.");
            _browseStatus.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException) {
            _browseStatus.Text = $"'{listing.Name}' could not be installed: {exception.Message}";
            _shell.Log($"warning: plugin '{listing.Name}' could not be installed: {exception.Message}");
        }
        ShowListings();
    }

    private void Remove(PluginListing listing, string folder) {
        if (HcMessageBox.Show(this, $"Remove '{listing.Name}'? It stays until WritingIDE restarts. Its settings and data are kept.", "Remove plugin",
            MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) {
            return;
        }
        try {
            PluginInstaller.MarkForRemoval(ExtensionManager.Root, folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            _browseStatus.Text = $"'{listing.Name}' could not be marked for removal: {exception.Message}";
            _browseStatus.Visibility = Visibility.Visible;
        }
        ShowListings();
    }

    private static void OpenLink(string url) {
        if (PluginCatalog.IsHttps(url)) {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private Button DialogButton(string text, Action click, string? name = null) {
        Button button = new() { Content = text, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("DialogButton") };
        System.Windows.Automation.AutomationProperties.SetName(button, name ?? text);
        button.Click += (_, _) => click();
        return button;
    }

    private Button LinkButton(string text, Action click, string name) {
        Button button = new() { Content = text, Margin = new Thickness(14, 0, 0, 0), FontSize = 11, Style = (Style)FindResource("LinkButton") };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }
}