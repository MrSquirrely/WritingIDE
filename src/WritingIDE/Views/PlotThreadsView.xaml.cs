using System.Windows.Controls;
using System.Windows.Input;

using WritingIDE.Core.Analysis;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

public partial class PlotThreadsView : UserControl {
    public PlotThreadsView() {
        InitializeComponent();
    }

    private void OnMarkerDoubleClick(object sender, MouseButtonEventArgs e) {
        if ((((DataGrid)sender).SelectedItem is ThreadMarkerLocation location) && (DataContext is PlotThreadsDocumentViewModel model)) {
            model.OpenMarkerCommand.Execute(location);
        }
    }
}