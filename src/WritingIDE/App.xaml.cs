using System.Windows;
using System.Windows.Threading;

using WritingIDE.Infrastructure;
using WritingIDE.ViewModels;
using WritingIDE.Views;

namespace WritingIDE;

public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        MainViewModel viewModel = new();
        // Apply the saved theme before the first window is created, so nothing flashes in the wrong colours.
        AppTheme.Apply(AppTheme.Parse(viewModel.Settings.Theme));
        MainWindow window = new(viewModel);
        MainWindow = window;
        window.Show();

        string? projectPath = e.Args.FirstOrDefault(static arg => arg.EndsWith(Core.Persistence.ProjectSerializer.Extension, StringComparison.OrdinalIgnoreCase));
        if (projectPath is not null) {
            viewModel.OpenProject(projectPath);
        }
        else {
            viewModel.ShowStartPage();
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
        HandyControl.Controls.MessageBox.Show(e.Exception.Message, "WritingIDE - Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}