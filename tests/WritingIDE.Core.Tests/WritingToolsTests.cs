using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Core.Persistence;
using WritingIDE.Core.Search;

namespace WritingIDE.Core.Tests;

public sealed class WordDiffTests {
    [Fact]
    public void MarksChangedWordsInChangedParagraphsOnly() {
        const string before = "The wolf ran.\n\nRowan waited by the gate.\n\nThe end.";
        const string after = "The wolf ran.\n\nRowan waited, shivering, by the old gate.\n\nThe end.";
        string diff = WordDiff.Render(before, after);
        Assert.Equal("@@ line 3 @@\nRowan waited{+, shivering, +}by the{+ old+} gate.", diff);
        Assert.DoesNotContain("The wolf ran", diff);
        Assert.DoesNotContain("The end", diff);
    }

    [Fact]
    public void ReplacementsAreShownAsRemovedThenAdded() {
        IReadOnlyList<DiffPiece> pieces = WordDiff.DiffWords("Kael smiled at her.", "Kael frowned at her.");
        Assert.Equal([new DiffPiece(DiffKind.Same, "Kael "), new DiffPiece(DiffKind.Removed, "smiled"), new DiffPiece(DiffKind.Added, "frowned"), new DiffPiece(DiffKind.Same, " at her.")], pieces);
        Assert.Equal("Kael [-smiled-]{+frowned+} at her.", WordDiff.RenderPieces(pieces));
    }

    [Fact]
    public void IdenticalTextsHaveNoDiffAndNewParagraphsAreAdded() {
        Assert.Equal("", WordDiff.Render("Same.\nText.", "Same.\r\nText."));
        Assert.Equal("@@ line 2 @@\n{+A new line.+}", WordDiff.Render("One.", "One.\nA new line."));
    }

    [Fact]
    public void DiffIndexesMatchesLongestCommonSubsequence() {
        string[] a = ["a", "b", "c", "a", "b", "b", "a"];
        string[] b = ["c", "b", "a", "b", "a", "c"];
        IReadOnlyList<(DiffKind Kind, int OldIndex, int NewIndex)> diff = WordDiff.DiffIndexes(a, b);
        Assert.Equal(4, diff.Count(static entry => entry.Kind == DiffKind.Same));
        Assert.Equal(a.Length, diff.Count(static entry => entry.Kind != DiffKind.Added));
        Assert.Equal(b.Length, diff.Count(static entry => entry.Kind != DiffKind.Removed));
    }
}

public sealed class SnapshotTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TakesListsLoadsAndDeletesSnapshotsNewestFirst() {
        Chapter chapter = new("Chapter 1");
        ChapterSnapshot first = SnapshotStore.Take(_directory, chapter, "First draft text.", "First draft")!;
        Assert.Null(SnapshotStore.Take(_directory, chapter, "First draft text.", "Again"));
        ChapterSnapshot second = SnapshotStore.Take(_directory, chapter, "Second draft, longer text.", "")!;

        IReadOnlyList<ChapterSnapshot> snapshots = SnapshotStore.List(_directory, chapter.Id);
        Assert.Equal([second.FilePath, first.FilePath], snapshots.Select(static snapshot => snapshot.FilePath));
        Assert.Equal("First draft", snapshots[1].DisplayName);
        Assert.Equal(3, snapshots[1].Words);
        Assert.Equal("Second draft, longer text.", SnapshotStore.LoadText(snapshots[0]));

        SnapshotStore.Delete(first);
        SnapshotStore.Delete(second);
        Assert.Empty(SnapshotStore.List(_directory, chapter.Id));
        Assert.False(Directory.Exists(Path.Combine(_directory, SnapshotStore.FolderName, chapter.Id.ToString("N"))));
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

public sealed class CommentTests {
    [Fact]
    public void ReanchorsToNearestOccurrenceOrMarksOrphaned() {
        ChapterComment comment = new() { Offset = 4, Length = 4, AnchorText = "gate" };
        Assert.True(CommentAnchoring.Reanchor("The gate.", comment));
        Assert.Equal(4, comment.Offset);

        Assert.True(CommentAnchoring.Reanchor("A gate. Then the old gate.", comment));
        Assert.Equal(2, comment.Offset);

        Assert.False(CommentAnchoring.Reanchor("No door here.", comment));
        Assert.True(comment.IsOrphaned);
        Assert.Equal(0, comment.Length);
    }

    [Fact]
    public void CommentsPovStoryDateAndRelationshipsSurviveSaveAndLoad() {
        string directory = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));
        try {
            NovelProject project = ProjectSerializer.Create(directory, "Roundtrip", "", ProjectTemplate.All[0]);
            Chapter chapter = project.Chapters[0];
            chapter.Text = "Kael opened the gate.";
            chapter.PointOfView = "Kael";
            chapter.StoryDate = "Day 3";
            chapter.Comments.Add(new ChapterComment { Offset = 16, Length = 4, AnchorText = "gate", Text = "Which gate?", Author = "me", IsResolved = true });
            StoryEntity kael = new(EntityKind.Character, "Kael") { MapX = 10, MapY = 20 };
            StoryEntity wren = new(EntityKind.Character, "Wren");
            wren.Relationships.Add(new EntityRelationship(kael.Id, "sister of"));
            project.Entities.Add(kael);
            project.Entities.Add(wren);
            ProjectSerializer.Save(project);

            NovelProject loaded = ProjectSerializer.Load(project.FilePath);
            Chapter loadedChapter = loaded.Chapters[0];
            Assert.Equal("Kael", loadedChapter.PointOfView);
            Assert.Equal("Day 3", loadedChapter.StoryDate);
            ChapterComment comment = Assert.Single(loadedChapter.Comments);
            Assert.Equal(("gate", "Which gate?", true, 16), (comment.AnchorText, comment.Text, comment.IsResolved, comment.Offset));
            Assert.Equal((10.0, 20.0), (loaded.Entities.First(e => e.Name == "Kael").MapX, loaded.Entities.First(e => e.Name == "Kael").MapY));
            EntityRelationship relationship = Assert.Single(loaded.Entities.First(e => e.Name == "Wren").Relationships);
            Assert.Equal((kael.Id, "sister of"), (relationship.TargetId, relationship.Label));
            Assert.False(loaded.IsDirty);

            loadedChapter.Comments[0].Text = "Edited";
            Assert.True(loaded.IsDirty);
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }
}

public sealed class CharacterStatisticsTests {
    private static (StoryEntity Kael, StoryEntity Wren, List<StoryEntity> All) Cast() {
        StoryEntity kael = new(EntityKind.Character, "Kael");
        StoryEntity wren = new(EntityKind.Character, "Wren");
        return (kael, wren, [kael, wren, new StoryEntity(EntityKind.Place, "Ashford")]);
    }

    [Fact]
    public void CountsMentionsDialogueAndDetectsPointOfView() {
        (StoryEntity kael, StoryEntity wren, List<StoryEntity> all) = Cast();
        Chapter one = new("One");
        Chapter two = new("Two");
        Chapter three = new("Three") { PointOfView = "wren" };
        const string textOne = "Kael walked to Ashford. Kael saw Wren at the gate.\n\"You're late,\" Wren said.\nKael shrugged. \"The road was long.\"";
        const string textTwo = "I woke early. I could hear my sister downstairs, and I wanted to leave before she saw me.";
        const string textThree = "Kael was gone.";

        CharacterReport report = CharacterStatistics.Compute([new(one, textOne), new(two, textTwo), new(three, textThree)], all);

        CharacterStats kaelStats = report.Characters.Single(stat => stat.Character == kael);
        Assert.Equal(4, kaelStats.Mentions);
        Assert.Equal(2, kaelStats.Chapters);
        Assert.Equal([3, 0, 1], kaelStats.MentionsPerChapter);
        Assert.Equal((0, 2), (kaelStats.FirstChapterIndex, kaelStats.LastChapterIndex));
        Assert.Equal(1, kaelStats.DialogueLines);
        Assert.Equal(1, kaelStats.PovChapters);

        CharacterStats wrenStats = report.Characters.Single(stat => stat.Character == wren);
        Assert.Equal((1, 2), (wrenStats.DialogueLines, wrenStats.DialogueWords));
        Assert.Equal(1, wrenStats.PovChapters);

        Assert.Equal(["Kael", ChapterPointOfView.FirstPerson, "Wren"], report.Chapters.Select(static pov => pov.PointOfView));
        Assert.Equal([true, true, false], report.Chapters.Select(static pov => pov.IsDetected));
        Assert.DoesNotContain(report.Characters, stat => stat.Character.Name == "Ashford");
    }

    [Fact]
    public void FindsStraightAndCurlyQuotations() {
        Assert.Equal([(1, 7), (20, 26)], CharacterStatistics.FindQuotations("\"Hello,\" she said. \u201CHello.\u201D"));
        Assert.Equal([(1, 5)], CharacterStatistics.FindQuotations("\"Open\nNext paragraph"));
    }
}

public sealed class StoryTimeTests {
    [Theory]
    [InlineData("Day 3", 3)]
    [InlineData("day 14", 14)]
    [InlineData("Year 2, Day 14", 379)]
    [InlineData("Week 2", 7)]
    [InlineData("1204", 1204)]
    [InlineData("Spring 1204", 1204)]
    public void ParsesRelativeAndNumericDates(string value, double expected) {
        Assert.Equal(expected, StoryTime.TryGetSortKey(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("The next morning")]
    [InlineData("Between 3 and 4")]
    public void LeavesVagueDatesUnordered(string value) {
        Assert.Null(StoryTime.TryGetSortKey(value));
    }

    [Fact]
    public void CalendarDatesCompareChronologically() {
        Assert.True(StoryTime.TryGetSortKey("2024-05-12") < StoryTime.TryGetSortKey("2024-06-01"));
    }
}

public sealed class RelationshipGraphTests {
    [Fact]
    public void CombinesExplicitRelationshipsAndCoOccurrence() {
        StoryEntity kael = new(EntityKind.Character, "Kael");
        StoryEntity wren = new(EntityKind.Character, "Wren");
        StoryEntity rowan = new(EntityKind.Character, "Rowan");
        StoryEntity mira = new(EntityKind.Character, "Mira");
        wren.Relationships.Add(new EntityRelationship(kael.Id, "sister of"));
        kael.Relationships.Add(new EntityRelationship(wren.Id, "brother of"));
        string[] texts = ["Kael and Rowan argued.\nRowan left. Kael followed.\nKael met Rowan again.\nMira and Kael talked."];

        IReadOnlyList<GraphEdge> edges = RelationshipGraph.Build([kael, wren, rowan, mira], texts, minimumCoOccurrences: 2);

        GraphEdge family = Assert.Single(edges, edge => edge.Labels.Count > 0);
        Assert.Equal(["Kael: brother of", "Wren: sister of"], family.Labels.Order());
        GraphEdge friends = Assert.Single(edges, edge => !edge.IsExplicit);
        Assert.Equal(3, friends.CoOccurrences);
        Assert.Equal(2, edges.Count);
        Assert.Single(RelationshipGraph.Build([kael, wren, rowan, mira], texts, minimumCoOccurrences: 0));
    }

    [Fact]
    public void LayoutKeepsPinnedNodesAndSpreadsOthers() {
        StoryEntity pinned = new(EntityKind.Character, "Pinned") { MapX = 0.25, MapY = 0.5 };
        StoryEntity a = new(EntityKind.Character, "A");
        StoryEntity b = new(EntityKind.Character, "B");
        Dictionary<Guid, (double X, double Y)> layout = RelationshipGraph.Layout([pinned, a, b], [], 800, 600);
        Assert.Equal((0.25, 0.5), layout[pinned.Id]);
        (double ax, double ay) = layout[a.Id];
        (double bx, double by) = layout[b.Id];
        Assert.True(Math.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by))) > 0.15);
        Assert.InRange(ax, 0, 1);
        Assert.InRange(by, 0, 1);
    }
}