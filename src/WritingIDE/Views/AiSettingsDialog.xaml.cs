using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

using WritingIDE.Core.Ai;
using WritingIDE.Plugins;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>AI > AI Settings: provider, key (encrypted), model and endpoint, with a connection test.</summary>
public partial class AiSettingsDialog : HandyControl.Controls.Window {
    private readonly MainViewModel _shell;
    private AiProviderKind _shown;
    private bool _ready;

    public AiSettingsDialog(MainViewModel shell) {
        InitializeComponent();
        _shell = shell;
        ProviderBox.ItemsSource = AiProviderInfo.All;
        ProviderBox.SelectedItem = AiProviderInfo.Get(shell.Settings.Ai.Provider);
        _ready = true;
        Show(shell.Settings.Ai.Provider);
    }

    private AiProviderInfo Info => (AiProviderInfo)ProviderBox.SelectedItem;

    private void Show(AiProviderKind kind) {
        _shown = kind;
        AiProviderInfo info = AiProviderInfo.Get(kind);
        AiProviderSettings settings = _shell.Settings.Ai.For(kind);
        NotesText.Text = info.Notes;
        KeyLink.Content = info.KeyUrl.Length == 0 ? "" : (info.NeedsKey ? $"Get a free key: {info.KeyUrl}" : $"Download: {info.KeyUrl}");
        KeyLink.Visibility = info.KeyUrl.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        KeyPanel.Visibility = info.Kind == AiProviderKind.Ollama ? Visibility.Collapsed : Visibility.Visible;
        KeyBox.Password = settings.GetKey();
        ModelBox.ItemsSource = null;
        ModelBox.Text = settings.Model;
        EndpointBox.Text = settings.Endpoint;
    }

    /// <summary>Keeps what was typed for a provider before switching to another.</summary>
    private void Store(AiProviderKind kind) {
        AiProviderSettings settings = _shell.Settings.Ai.For(kind);
        settings.SetKey(KeyBox.Password);
        settings.Model = ModelBox.Text.Trim();
        settings.Endpoint = EndpointBox.Text.Trim().Length > 0 ? EndpointBox.Text.Trim() : AiProviderInfo.Get(kind).Endpoint;
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e) {
        if (!_ready) {
            return;
        }
        Store(_shown);
        Show(Info.Kind);
    }

    private AiConnection CurrentConnection() {
        return new AiConnection(Info.Kind, EndpointBox.Text.Trim(), ModelBox.Text.Trim(), KeyBox.Password.Trim());
    }

    private async void OnListModels(object sender, RoutedEventArgs e) {
        ResultText.Text = "Asking for the list of models...";
        try {
            IReadOnlyList<string> models = await _shell.Ai.CreateClient(CurrentConnection()).ListModelsAsync();
            string current = ModelBox.Text;
            ModelBox.ItemsSource = Info.Kind == AiProviderKind.OpenRouter ? models.Where(static model => model.EndsWith(":free", StringComparison.Ordinal)).ToList() : models;
            ModelBox.Text = current;
            ResultText.Text = models.Count == 0 ? "The provider returned no models." : $"{((IList<string>)ModelBox.ItemsSource).Count} model(s) available{(Info.Kind == AiProviderKind.OpenRouter ? " for free" : "")}. Open the list to choose.";
        }
        catch (AiException exception) {
            ResultText.Text = exception.Message;
        }
    }

    private async void OnTest(object sender, RoutedEventArgs e) {
        ResultText.Text = $"Testing {Info.DisplayName}...";
        try {
            string reply = await _shell.Ai.CreateClient(CurrentConnection()).CompleteAsync([AiMessage.User("Reply with the single word: ready")]);
            ResultText.Text = $"\u2714 {Info.DisplayName} answered: \u201C{reply.Trim()}\u201D";
        }
        catch (AiException exception) {
            ResultText.Text = exception.Message;
        }
    }

    private void OnOpenKeyPage(object sender, RoutedEventArgs e) {
        if (Info.KeyUrl.Length > 0) {
            Process.Start(new ProcessStartInfo(Info.KeyUrl) { UseShellExecute = true });
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) {
        Store(Info.Kind);
        _shell.Settings.Ai.Provider = Info.Kind;
        _shell.Settings.Save();
        MainViewModel.Notify($"AI tools will use {Info.DisplayName}.", MainViewModel.NotificationKind.Success);
        DialogResult = true;
    }
}