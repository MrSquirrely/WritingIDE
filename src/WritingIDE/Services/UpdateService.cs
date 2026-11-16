using System.Reflection;
using System.Windows;
using System.Windows.Input;

using Velopack;
using Velopack.Sources;

using WritingIDE.Core;
using WritingIDE.Infrastructure;
using WritingIDE.ViewModels;

namespace WritingIDE.Services;

/// <summary>
/// Updates for the installed app (Velopack). WritingIDE checks quietly at startup, downloads a new version in the
/// background and installs it when WritingIDE closes, so nothing interrupts writing; Help > Restart to Update installs
/// it now. Nothing happens in a development build or when no update address was set at build time.
/// </summary>
public sealed class UpdateService : ObservableObject {
    private readonly MainViewModel _shell;
    private readonly UpdateManager? _manager;
    private UpdateInfo? _ready;
    private bool _restartAfterUpdate;

    public UpdateService(MainViewModel shell) {
        _shell = shell;
        string? feed = typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static attribute => attribute.Key == "UpdateUrl")?.Value;
        if (!string.IsNullOrWhiteSpace(feed)) {
            try {
                _manager = Uri.TryCreate(feed, UriKind.Absolute, out Uri? uri) && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                    ? new UpdateManager(new GithubSource(feed, null, false))
                    : new UpdateManager(feed);
            }
            catch (Exception exception) when (exception is ArgumentException or UriFormatException or InvalidOperationException) {
                _manager = null;
            }
        }
        CheckCommand = new RelayCommand(() => _ = CheckAsync(interactive: true), () => !IsBusy);
        RestartToUpdateCommand = new RelayCommand(RestartToUpdate, () => IsUpdateReady);
    }

    /// <summary>Installed with the installer (not a build run from Visual Studio) and an update address is set.</summary>
    public bool CanUpdate => _manager?.IsInstalled == true;

    public bool IsBusy {
        get;
        private set {
            SetProperty(ref field, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsUpdateReady => _ready is not null;

    public string Version => _manager?.IsInstalled == true ? _manager.CurrentVersion?.ToString() ?? "" : typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "";

    public ICommand CheckCommand { get; }

    public ICommand RestartToUpdateCommand { get; }

    /// <summary>Checks for a newer version and downloads it. Interactive checks report the outcome in a message.</summary>
    public async Task CheckAsync(bool interactive) {
        if (!CanUpdate) {
            if (interactive) {
                Say(_manager is null
                    ? "This copy of WritingIDE was built without an update address, so it cannot check for updates."
                    : "Updates work in the installed copy of WritingIDE (from Setup.exe), not in a development build.");
            }
            return;
        }
        if (IsBusy || (_ready is not null)) {
            if (interactive && (_ready is not null)) {
                Say($"WritingIDE {_ready.TargetFullRelease.Version} is ready. Use Help > Restart to Update, or it installs when you close WritingIDE.");
            }
            return;
        }
        IsBusy = true;
        try {
            UpdateInfo? update = await _manager!.CheckForUpdatesAsync();
            if (update is null) {
                if (interactive) {
                    Say($"WritingIDE {Version} is the latest version.");
                }
                return;
            }
            _shell.StatusText = $"Downloading WritingIDE {update.TargetFullRelease.Version}...";
            await _manager.DownloadUpdatesAsync(update, progress => Application.Current?.Dispatcher.BeginInvoke(() => _shell.StatusText = $"Downloading WritingIDE {update.TargetFullRelease.Version}: {progress}%"));
            _ready = update;
            OnPropertyChanged(nameof(IsUpdateReady));
            CommandManager.InvalidateRequerySuggested();
            _shell.StatusText = "Ready";
            _shell.Log($"WritingIDE {update.TargetFullRelease.Version} downloaded; it installs when WritingIDE closes (or Help > Restart to Update).");
            MainViewModel.Notify($"WritingIDE {update.TargetFullRelease.Version} is ready. It installs when you close WritingIDE, or use Help > Restart to Update.", MainViewModel.NotificationKind.Success);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) {
            _shell.Log($"warning: the update check failed: {exception.Message}");
            if (interactive) {
                Say($"Could not check for updates.\n\n{exception.Message}");
            }
        }
        finally {
            IsBusy = false;
        }
    }

    /// <summary>Closes WritingIDE the normal way (it offers to save) and restarts it once the update is installed.</summary>
    private void RestartToUpdate() {
        _restartAfterUpdate = true;
        Application.Current.MainWindow?.Close();
        // Closing was cancelled (the writer chose to keep working): install on a later exit, without a restart.
        if (Application.Current.MainWindow?.IsLoaded == true) {
            _restartAfterUpdate = false;
        }
    }

    /// <summary>Called as WritingIDE exits: hands a downloaded update to Velopack, which installs it once this process ends.</summary>
    public void ApplyOnExit() {
        if ((_manager is null) || (_ready is null)) {
            return;
        }
        try {
            _manager.WaitExitThenApplyUpdates(_ready.TargetFullRelease, silent: !_restartAfterUpdate, restart: _restartAfterUpdate);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) {
            // The update stays downloaded; the next check finds and installs it.
            _shell.Log($"warning: could not start the update: {exception.Message}");
        }
    }

    private static void Say(string message) {
        HandyControl.Controls.MessageBox.Show(Application.Current.MainWindow!, message, "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}