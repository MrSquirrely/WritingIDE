using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using WritingIDE.Core.Grammar;
using WritingIDE.Sdk;

namespace SampleExtension;

/// <summary>A sample extension: a grammar rule, a menu command, an export format, a tool window and an editor command.</summary>
public sealed class SampleExtension : IWritingIdePlugin {
    public string Id => "com.example.writingide.sample";

    public string Name => "Sample Extension";

    public string Version => "1.0.0";

    public string Description => "Flags 'very', counts words per chapter and per session, and exports plain text.";

    public void Initialize(IPluginHost host) {
        host.RegisterRule(new VeryRule());
        host.RegisterCommand(new PluginCommand("Word Count per Chapter", project => {
            foreach (ChapterInfo chapter in project.GetChapters()) {
                host.Log($"{chapter.Title}: {chapter.Words:N0} words");
            }
            host.Notify("Word counts written to the Output window.");
        }) { Description = "Lists every chapter's word count in the Output window." });
        host.RegisterExporter(new PlainTextExporter());
        // A tool window: listed in the Extensions menu, opens as a tab. The tracker counts whether or not it is open.
        SessionTracker tracker = new(host);
        host.RegisterToolWindow(new PluginToolWindow("session", "Writing Session", () => new SessionPanel(host, tracker)) {
            Description = "Words written this session, against a goal."
        });
        // A right-click command in the chapter editor, offered only when text is selected.
        host.RegisterEditorCommand(new EditorCommand("Count Words in Selection", context =>
            host.Notify($"{SessionTracker.Words().Count(context.SelectedText):N0} words selected.")) {
            Description = "Counts the words in the selected text.",
            IsAvailable = static context => context.SelectionLength > 0
        });
        host.ProjectOpened += (_, _) => host.Log($"Sample Extension: '{host.Project.Name}' opened with {host.Project.GetChapters().Count} chapters.");
    }
}

/// <summary>SAMPLE001: "very" is usually a weak intensifier.</summary>
public sealed partial class VeryRule() : GrammarRule("SAMPLE001", "Avoid 'very'", RuleCategory.Style, DiagnosticSeverity.Message,
    "'Very' usually weakens the word it props up: 'very tired' reads better as 'exhausted'.") {
    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (Match match in Very().Matches(context.Text)) {
            yield return Report(match.Index, match.Length, "Consider a stronger word instead of 'very ...'.");
        }
    }

    [GeneratedRegex(@"\b[Vv]ery\b")]
    private static partial Regex Very();
}

/// <summary>Exports the manuscript as plain text, one chapter after another.</summary>
public sealed class PlainTextExporter : IManuscriptExporter {
    public string Name => "Plain text (sample extension)";

    public string FileExtension => ".txt";

    public void Export(string path, IProjectApi project) {
        StringBuilder builder = new();
        builder.AppendLine(project.Name.ToUpperInvariant()).AppendLine();
        foreach (ChapterInfo chapter in project.GetChapters()) {
            builder.AppendLine(chapter.Title).AppendLine().AppendLine(project.GetChapterText(chapter.Id).Trim()).AppendLine();
        }
        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }
}