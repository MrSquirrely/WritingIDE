using System.Windows;
using System.Windows.Input;

using WritingIDE.Core.Text;
using WritingIDE.Editor;
using WritingIDE.Infrastructure;
using WritingIDE.Services;
using WritingIDE.Views;

namespace WritingIDE.ViewModels;

/// <summary>Bold and italic, and the editor font (the toolbar pickers and Tools > Fonts).</summary>
public sealed partial class MainViewModel {
    public ICommand BoldCommand { get; private set; } = null!;

    public ICommand ItalicCommand { get; private set; } = null!;

    public ICommand ClearFormattingCommand { get; private set; } = null!;

    public ICommand FontsCommand { get; private set; } = null!;

    /// <summary>Checking for, downloading and installing new versions of the installed app.</summary>
    public UpdateService Updates { get; private set; } = null!;

    public IReadOnlyList<FontChoice> FontChoices => FontLibrary.Choices;

    public IReadOnlyList<double> FontSizes => FontLibrary.Sizes;

    /// <summary>The editor font in the toolbar picker.</summary>
    public FontChoice? EditorFont {
        get => FontChoices.FirstOrDefault(static choice => choice.Name.Equals(FontLibrary.EditorFamilyName, StringComparison.OrdinalIgnoreCase));
        set {
            if (value is not null) {
                FontLibrary.SetEditorFont(value.Name, FontLibrary.EditorSize);
            }
        }
    }

    /// <summary>The editor font size in the toolbar picker (type any size from 6 to 96).</summary>
    public double EditorFontSize {
        get => FontLibrary.EditorSize;
        set => FontLibrary.SetEditorFont(FontLibrary.EditorFamilyName, value);
    }

    private void InitializeFormatting() {
        FontLibrary.Initialize(Settings);
        FontLibrary.Changed += () => {
            OnPropertyChanged(nameof(FontChoices));
            OnPropertyChanged(nameof(EditorFont));
            OnPropertyChanged(nameof(EditorFontSize));
        };
        BoldCommand = new RelayCommand(() => ApplyFormat(FormatCommand.Bold), () => FocusedEditor is not null);
        ItalicCommand = new RelayCommand(() => ApplyFormat(FormatCommand.Italic), () => FocusedEditor is not null);
        ClearFormattingCommand = new RelayCommand(() => ApplyFormat(FormatCommand.Clear), () => FocusedEditor is not null);
        FontsCommand = new RelayCommand(ShowFonts);
        Updates = new UpdateService(this);
    }

    /// <summary>The manuscript editor the writer was last typing in (the tab or the split pane).</summary>
    private ManuscriptEditor? FocusedEditor {
        get {
            ManuscriptEditor? recent = Documents.OfType<ChapterDocumentViewModel>()
                .Concat(SecondaryDocument is ChapterDocumentViewModel secondary ? [secondary] : [])
                .SelectMany(static document => document.AllEditors)
                .Where(static editor => editor.IsVisible)
                .MaxBy(static editor => editor.LastFocused);
            return (recent is not null) && (recent.LastFocused != default) ? recent : ActiveChapter?.Editor;
        }
    }

    private void ApplyFormat(FormatCommand command) {
        FocusedEditor?.ApplyFormat(command);
    }

    private void ShowFonts() {
        FontsDialog dialog = new() { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }
}