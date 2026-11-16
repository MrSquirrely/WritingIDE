using System.Windows;
using System.Windows.Threading;

using WritingIDE.Infrastructure;
using WritingIDE.ViewModels;
using WritingIDE.Views;

namespace WritingIDE;

public partial class App : Application {
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // Last chance on a fatal crash from any thread: save unsaved work for recovery.
        AppDomain.CurrentDomain.UnhandledException += (_, _) => TryWriteRecoverySnapshot();

        MainViewModel viewModel = new();
        _viewModel = viewModel;
        // Apply the saved theme before the first window is created, so nothing flashes in the wrong colours.
        AppTheme.Apply(AppTheme.Parse(viewModel.Settings.Theme));
        MainWindow window = new(viewModel);
        MainWindow = window;
        window.Show();
        // A quiet check: a newer version downloads in the background and installs when WritingIDE closes.
        _ = viewModel.Updates.CheckAsync(interactive: false);

        string? projectPath = e.Args.FirstOrDefault(static arg => arg.EndsWith(Core.Persistence.ProjectSerializer.Extension, StringComparison.OrdinalIgnoreCase));
        if (projectPath is not null) {
            viewModel.OpenProject(projectPath);
        }
        else {
            viewModel.ShowStartPage();
        }
    }

    /// <summary>Hands a downloaded update to the installer, which applies it once WritingIDE has exited.</summary>
    protected override void OnExit(ExitEventArgs e) {
        _viewModel?.Updates.ApplyOnExit();
        base.OnExit(e);
    }

    /// <summary>Windows is shutting down or the user is signing out: keep a recovery snapshot of unsaved work.</summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e) {
        TryWriteRecoverySnapshot();
        base.OnSessionEnding(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
        TryWriteRecoverySnapshot();
        HandyControl.Controls.MessageBox.Show(
            $"{e.Exception.Message}\n\nYour unsaved work has been kept for recovery.",
            "WritingIDE - Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void TryWriteRecoverySnapshot() {
        try {
            if (Dispatcher.CheckAccess()) {
                _viewModel?.WriteRecoverySnapshot();
            }
            else {
                Dispatcher.Invoke(() => _viewModel?.WriteRecoverySnapshot(), DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception) {
            // Crash path: never throw from here.
        }
    }
}