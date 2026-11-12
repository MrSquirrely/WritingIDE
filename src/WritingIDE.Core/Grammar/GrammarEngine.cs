using WritingIDE.Core.Analysis;
using WritingIDE.Core.Grammar.Rules;

namespace WritingIDE.Core.Grammar;

/// <summary>The result of analysing one document.</summary>
public sealed record AnalysisResult(
    IReadOnlyList<GrammarDiagnostic> Diagnostics,
    IReadOnlyList<EntityMention> Mentions,
    TextStatistics Statistics);

/// <summary>Runs every enabled grammar rule ("analyzer") over a document, like the C# compiler's analyzers.</summary>
public sealed class GrammarEngine(ISpellingProvider? spelling = null) {
    private IReadOnlyList<GrammarRule> _pluginRules = [];

    /// <summary>Built-in rules followed by the rules contributed by enabled plugins.</summary>
    public IReadOnlyList<GrammarRule> Rules => [.. BuiltInRules, .. _pluginRules];

    public IReadOnlyList<GrammarRule> PluginRules => _pluginRules;

    /// <summary>Replaces the plugin rules (thread-safe: analyses in flight keep the list they started with).</summary>
    public void SetPluginRules(IEnumerable<GrammarRule> rules) {
        _pluginRules = rules.ToList();
    }

    public IReadOnlyList<GrammarRule> BuiltInRules { get; } = [
        new SpellingRule(),
        new RepeatedWordRule(),
        new ArticleRule(),
        new SubjectVerbAgreementRule(),
        new CapitalizationRule(),
        new ConfusedWordsRule(),
        new PunctuationSpacingRule(),
        new BalancedPunctuationRule(),
        new PassiveVoiceRule(),
        new WeakWordsRule(),
        new LongSentenceRule(),
        new ClicheRule(),
        new RedundancyRule(),
        new DialogueTagAdverbRule(),
        new EntityCasingRule(),
        new NameTypoRule(),
        new AppearanceContinuityRule(),
        new AgeContinuityRule(),
        new PronounContinuityRule(),
        new DeathContinuityRule()
    ];

    public ISpellingProvider? Spelling { get; } = spelling;

    /// <summary>Raised (on the analysis thread) when a rule throws; the rule is skipped for that run.</summary>
    public event EventHandler<(GrammarRule Rule, Exception Exception)>? RuleFailed;

    public GrammarRule? FindRule(string id) {
        return Rules.FirstOrDefault(rule => rule.Id == id);
    }

    public IReadOnlyList<GrammarDiagnostic> Check(string text, GrammarOptions? options = null, CancellationToken cancellationToken = default) {
        AnalysisContext context = new(text, options ?? GrammarOptions.Default, Spelling, cancellationToken);
        return RunRules(context);
    }

    /// <summary>Like <see cref="Analyze"/>, but returns null instead of throwing when <paramref name="cancellationToken"/> is cancelled.</summary>
    public AnalysisResult? TryAnalyze(string text, GrammarOptions options, CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return null;
        }
        try {
            return Analyze(text, options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return null;
        }
    }

    /// <summary>Full analysis: diagnostics, story element mentions and statistics, sharing one tokenization.</summary>
    public AnalysisResult Analyze(string text, GrammarOptions options, CancellationToken cancellationToken = default) {
        AnalysisContext context = new(text, options, Spelling, cancellationToken);
        IReadOnlyList<GrammarDiagnostic> diagnostics = RunRules(context);
        IReadOnlyList<EntityMention> mentions = options.Entities.FindMentions(text);
        TextStatistics statistics = TextStatistics.Compute(text, context.Tokens, context.Sentences);
        return new AnalysisResult(diagnostics, mentions, statistics);
    }

    private List<GrammarDiagnostic> RunRules(AnalysisContext context) {
        List<GrammarDiagnostic> diagnostics = [];
        HashSet<(string, int, int)> seen = [];
        List<GrammarDiagnostic> builtInFindings = [];
        foreach (GrammarRule rule in Rules) {
            if (context.Options.DisabledRules.Contains(rule.Id) || (rule.IsPlugin && !context.Options.RunPlugins)) {
                continue;
            }
            context.CancellationToken.ThrowIfCancellationRequested();
            try {
                foreach (GrammarDiagnostic diagnostic in rule.Analyze(context)) {
                    if ((diagnostic.Length == 0) || !seen.Add((diagnostic.RuleId, diagnostic.Offset, diagnostic.Length))) {
                        continue;
                    }
                    if (rule.IsPlugin) {
                        // A plugin finding on text a built-in rule already flagged (spelling, "the the") is a duplicate.
                        if (builtInFindings.Any(existing => (existing.Offset < diagnostic.End) && (diagnostic.Offset < existing.End))) {
                            continue;
                        }
                    }
                    else if (diagnostic.Severity != DiagnosticSeverity.Message) {
                        // Style messages often span whole sentences, so they never hide plugin findings.
                        builtInFindings.Add(diagnostic);
                    }
                    diagnostics.Add(diagnostic);
                }
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception exception) {
                RuleFailed?.Invoke(this, (rule, exception));
            }
        }
        diagnostics.Sort(static (a, b) => a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset) : b.Severity.CompareTo(a.Severity));
        return diagnostics;
    }
}