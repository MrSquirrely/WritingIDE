using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;

using WritingIDE.ViewModels;

namespace WritingIDE.Views;

public partial class AiChatView : UserControl {
    public AiChatView() {
        InitializeComponent();
        DataContextChanged += (_, e) => {
            if (e.NewValue is AiChatDocumentViewModel model) {
                model.Messages.CollectionChanged += OnMessagesChanged;
            }
        };
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        Dispatcher.BeginInvoke(Scroller.ScrollToEnd);
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e) {
        if ((e.Key == Key.Enter) && (DataContext is AiChatDocumentViewModel model) && model.SendCommand.CanExecute(null)) {
            model.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}