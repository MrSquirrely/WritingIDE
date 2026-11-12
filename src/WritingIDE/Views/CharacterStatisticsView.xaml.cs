using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using WritingIDE.ViewModels;

namespace WritingIDE.Views;

public partial class CharacterStatisticsView : UserControl {
    public CharacterStatisticsView() {
        InitializeComponent();
    }

    private CharacterStatisticsDocumentViewModel? ViewModel => DataContext as CharacterStatisticsDocumentViewModel;

    private void OnCharacterDoubleClick(object sender, MouseButtonEventArgs e) {
        if (((DataGrid)sender).SelectedItem is CharacterRow row) {
            ViewModel?.OpenEntityCommand.Execute(row);
        }
    }

    private void OnPovDoubleClick(object sender, MouseButtonEventArgs e) {
        if (((DataGrid)sender).SelectedItem is PovRow row) {
            ViewModel?.OpenChapterCommand.Execute(row);
        }
    }

    private void OnCellClicked(object sender, MouseButtonEventArgs e) {
        if ((sender as FrameworkElement)?.DataContext is PresenceCell cell) {
            ViewModel?.OpenChapterCommand.Execute(cell);
        }
    }
}