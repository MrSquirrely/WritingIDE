using WritingIDE.Core.Analysis;
using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;

namespace WritingIDE.Core.Tests;

public sealed class GrammarEngineTests {
    private readonly GrammarEngine _engine = new();

    [Theory]
    [InlineData("She climbed the the stairs.", "NW1002")]
    [InlineData("He ate a apple.", "NW1003")]
    [InlineData("It took an hour and a unicorn.", null)]
    [InlineData("They was late.", "NW1004")]
    [InlineData("He don't care.", "NW1004")]
    [InlineData("the sun rose.", "NW1005")]
    [InlineData("Then i left.", "NW1005")]
    [InlineData("I could of gone.", "NW1006")]
    [InlineData("She is taller then him.", "NW1006")]
    [InlineData("Its a long way.", "NW1006")]
    [InlineData("It is hard when your tired.", "NW1006")]
    [InlineData("You'll loose your mind.", "NW1006")]
    [InlineData("Wait , what?", "NW2001")]
    [InlineData("Wait,what?", "NW2001")]
    [InlineData("The lamp was lit by her.", "NW3001")]
    [InlineData("She was very tired.", "NW3002")]
    [InlineData("He nodded his head.", "NW3005")]
    [InlineData("\"Go,\" she said quietly.", "NW3006")]
    [InlineData("It was crystal clear.", "NW3004")]
    public void DetectsExpectedRule(string text, string? expectedRule) {
        IReadOnlyList<GrammarDiagnostic> diagnostics = _engine.Check(text);
        if (expectedRule is null) {
            Assert.DoesNotContain(diagnostics, d => d.RuleId is "NW1003");
        }
        else {
            Assert.Contains(diagnostics, d => d.RuleId == expectedRule);
        }
    }

    [Theory]
    [InlineData("\"Help!\" she cried. The door opened.")]
    [InlineData("Mr. Crane waited. He was patient.")]
    [InlineData("She paused... and then she left.")]
    public void DoesNotFlagCorrectCapitalization(string text) {
        IReadOnlyList<GrammarDiagnostic> diagnostics = _engine.Check(text);
        Assert.DoesNotContain(diagnostics, d => d.RuleId == "NW1005");
    }

    [Fact]
    public void ArticleFixSuggestsAn() {
        GrammarDiagnostic diagnostic = Assert.Single(_engine.Check("She held a old lantern."), d => d.RuleId == "NW1003");
        Assert.Equal("an", diagnostic.Fixes[0].Replacement);
        Assert.Equal(9, diagnostic.Offset);
    }

    [Fact]
    public void ConfusedWordFixPreservesCase() {
        GrammarDiagnostic diagnostic = Assert.Single(_engine.Check("Could of been worse."), d => d.RuleId == "NW1006");
        Assert.Equal("Could have", diagnostic.Fixes[0].Replacement);
    }

    [Fact]
    public void EntityCasingSuggestsProperName() {
        StoryEntity elena = new(EntityKind.Character, "Elena Marsh") { Aliases = "Elena" };
        GrammarOptions options = new(new EntityIndex([elena]), new HashSet<string>(), new HashSet<string>());
        IReadOnlyList<GrammarDiagnostic> diagnostics = _engine.Check("Then elena smiled.", options);
        GrammarDiagnostic diagnostic = Assert.Single(diagnostics, d => d.RuleId == "NW4001");
        Assert.Equal("Elena", diagnostic.Fixes[0].Replacement);
    }

    [Fact]
    public void DisabledRulesAreSkipped() {
        GrammarOptions options = new(EntityIndex.Empty, new HashSet<string>(), new HashSet<string> { "NW1002" });
        Assert.DoesNotContain(_engine.Check("the the", options), d => d.RuleId == "NW1002");
    }

    [Fact]
    public void FindsEntityMentionsIncludingAliasesAndPossessives() {
        StoryEntity elena = new(EntityKind.Character, "Elena Marsh") { Aliases = "Elena" };
        StoryEntity port = new(EntityKind.Place, "Port Ashby");
        EntityIndex index = new([elena, port]);
        IReadOnlyList<EntityMention> mentions = index.FindMentions("Elena Marsh sailed to Port Ashby. Elena's boat leaked.");
        Assert.Equal(["Elena Marsh", "Port Ashby", "Elena"], mentions.Select(m => m.MatchedName));
    }

    [Fact]
    public void RenameReplacesWholeWordsOnly() {
        string result = ManuscriptTools.ReplaceWholeWord("Tom met Tomas. Tom's hat.", "Tom", "Ben");
        Assert.Equal("Ben met Tomas. Ben's hat.", result);
    }

    [Fact]
    public void StatisticsCountWordsAndSentences() {
        TextStatistics statistics = TextStatistics.Compute("One two three. Four five!\n\nSix.");
        Assert.Equal(6, statistics.Words);
        Assert.Equal(3, statistics.Sentences);
        Assert.Equal(2, statistics.Paragraphs);
    }
}