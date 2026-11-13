using WritingIDE.Core.Import;
using WritingIDE.Core.Model;

namespace WritingIDE.Core.Persistence;

/// <summary>
/// "The Lighthouse Keeper", the Sample Novel template: a short mystery built to show every feature of WritingIDE.
/// Chapter 1 is full of deliberate grammar mistakes, chapter 3 of deliberate continuity slips (one for each NW4xxx
/// rule), chapter 4 is a flashback the timeline flags, chapter 5 is only an outline, and the project comes with
/// comments, a bookmark, plot threads in three different states, relationships, a snapshot and tracked changes.
/// </summary>
internal static class SampleNovel {
    private const string Chapter1 = """
        The wind had been rising since noon, and by dusk Elena Marsh could hear it singing in the the iron railings of Greyhaven Lighthouse.

        she climbed the hundred and twelve steps with a old lantern in one hand. Its a long way up when your tired, and she was very tired. Her knees ached. The lamp was lit by her every evening at six, the way her father had taught her.

        Tobias would of laughed at her. He always said she worried more then anyone he knew. "You'll loose your mind out here," he had told her, the week before the Great Storm.

        At the top she found it. A brass compass, green with age, sitting on the sill where no compass had any right to be. elena picked it up. The needle trembeled, then swung away from the north and pointed straight out to sea, towards Port Ashby.

        They was the only two people who knew about the compass. Him and her. And Tobias had been dead for nine years.

        "Who's there?" she whispered quietly.

        Nobody answered. Only the light turned, and turned, and turned, sweeping the black water like a slow and patient eye that had seen every ship and every storm and every drowned sailor that the sea had ever taken from the people of the coast who loved them.

        * * *

        TODO: decide whether Elena tells anyone about the compass.

        She did not sleep. At four she opened the keeper's log, wrote the date, *12 October*, and under it a single word: *Toby*.
        """;

    private const string Chapter2 = """
        Silas Crane was waiting on the pier when the boat came in, as if he had known.

        "Miss Marsh." He nodded his head. "Rough night for rowing."

        "Rougher for some," Elena said. She kept her hands in her coat, where the compass sat heavy against her hip. The compass needle swung towards the Guild warehouse and stayed there.

        "The Harbor Guild has been patient with Greyhaven," said Crane. "Patience costs money, Miss Marsh."

        "Then send me the bill," Elena said, and walked past him before he could see her hands shake.

        *** The warehouse

        The doors of the Guild warehouse were chained, but the chain was new and the lock was **old**. TODO: give Crane a tell when he lies.

        Inside, behind crates stamped with the Guild's anchor, she found a ship's bell with the name scraped off. It was the bell of the [TK], the boat Tobias had taken out the night of the Great Storm. Whoever had put out the light that night had wanted the boat to sink, and someone in Port Ashby had kept the bell.
        """;

    /// <summary>Chapter 2 as it was first drafted: the snapshot.</summary>
    private const string Chapter2FirstDraft = """
        Silas Crane was waiting on the pier when the boat came in, as if he had known.

        "Miss Marsh." He nodded his head. "Rough night for rowing."
        """;

    private const string Chapter3 = """
        Elean read the keeper's log until the candle guttered. Elena's blue eyes stung from the small, cramped hand.

        Elena, forty-five, was too old for this, she told herself, and Elena flicked his hair out of her face and kept reading.

        Tobias's daughter Elena, the entry for that week began, as if Harlan Pike had needed reminding who she was. Harlan Pike smiled up at the window from the pier below, though he had been in the churchyard since spring.

        In the margin, in a hand that was not Harlan's, someone had written: Crane's compass points home.

        *** From the logbook

        I am writing this by the lamp because I do not trust the lamp to stay lit. I know who comes up the stairs at night. I have heard my own name called from the water, and I have not answered.

        If anyone reads this, I put out the light myself, and I would do it again. Crane has been reading these pages; I have seen the corners turned down.
        """;

    private const string Chapter4 = """
        The storm came in at dusk, nine years before Elena found the compass.

        Tobias Marsh stood at the rail of the Greyhaven gallery with his collar up. Below, the boats of the Harbor Guild ran for Port Ashby. Silas Crane, fifty-nine, watched them come in from the end of the pier.

        Someone climbed the hundred and twelve steps after midnight and put out the lamp.

        By morning the sea was flat and grey, and Tobias was gone.
        """;

    private const string Bible = """
        Premise: a lighthouse keeper finds her missing brother's compass, and it will not point north.

        Tone: quiet, salt-worn, a little uncanny. The sea is a character; nobody says what they mean.

        World rules:
        - The compass points towards whatever its owner most wants to find. Nobody knows this yet.
        - The Harbor Guild owns every boat, pier and debt in Port Ashby.

        This sample shows WritingIDE's features: open each chapter and the Error List, Analyze > Timeline, Character and POV Statistics, Relationship Map and Plot Threads, and the Snapshots and Revision mode sections of Chapter 2's Properties.
        """;

    public static NovelProject Create(string name, string author) {
        NovelProject project = new() {
            Name = name,
            Author = author,
            Genre = "Literary mystery",
            Synopsis = "A reclusive lighthouse keeper finds a brass compass that points to the night her brother vanished.",
            TargetWordCount = 70000,
            Notes = Bible + "\n\n" + ImportApplier.FormatChapterPlan("Chapter 6 - Low Tide", null,
                "Elena takes the logbook to the magistrate in the next town. He already knows Crane.", "Open on the coach road. End with the magistrate's ring: the Guild's anchor.")
        };

        StoryEntity elena = Character("Elena Marsh", "Elena, Marsh", "Keeper of the Greyhaven light for eleven years. Practical, guarded, quietly funny.",
            ("Role", "Protagonist"), ("Age", "41"), ("Pronouns", "she/her"), ("Eyes", "grey"), ("Hair", "dark"), ("Goal", "Learn what happened to her brother Tobias."));
        StoryEntity tobias = Character("Tobias Marsh", "Tobias, Toby", "Elena's younger brother. Lost at sea during the Great Storm, or so everyone believes.",
            ("Role", "Missing person"), ("Pronouns", "he/him"), ("Eyes", "grey"));
        StoryEntity crane = Character("Silas Crane", "Crane, Silas", "Harbourmaster of Port Ashby and senior member of the Harbor Guild.",
            ("Role", "Antagonist"), ("Pronouns", "he/him"), ("Born", "1840"));
        StoryEntity harlan = Character("Harlan Pike", "Harlan", "Keeper of Greyhaven before Elena; taught her the light. Buried last spring.",
            ("Role", "Mentor (deceased)"), ("Pronouns", "he/him"), ("Died", "Chapter 2"));

        StoryEntity lighthouse = Entity(EntityKind.Place, "Greyhaven Lighthouse", "Greyhaven", "A white tower on a black rock, two miles off the coast.", ("Type", "Lighthouse"));
        StoryEntity port = Entity(EntityKind.Place, "Port Ashby", "", "A fishing town that smells of tar, salt and secrets.", ("Type", "Town"));
        StoryEntity compass = Entity(EntityKind.Item, "Brass Compass", "compass", "Tobias's compass. Its needle points not north, but towards the storm.",
            ("Type", "Heirloom"), ("Owner", "Tobias Marsh"));
        StoryEntity guild = Entity(EntityKind.Faction, "Harbor Guild", "Guild", "The merchants who own every boat, pier and debt in Port Ashby.", ("Leader", "Silas Crane"));
        StoryEntity storm = Entity(EntityKind.Event, "Great Storm", "", "The night, nine years ago, when the light went dark and Tobias vanished.", ("Date", "October 1888"));
        StoryEntity reckoning = Entity(EntityKind.Event, "The Reckoning", "", "Elena confronts Crane with the logbook and the bell.", ("Date", "15 October 1897"), ("Chapter", "Chapter 5"));
        StoryEntity fire = Entity(EntityKind.Event, "The Harbor Fire", "", "A fire the Guild started to cover its books. Not in the manuscript yet, so the timeline lists it as unplaced.", ("Date", "1885"));
        StoryEntity log = Entity(EntityKind.Lore, "The Keeper's Log", "keeper's log, logbook", "Every keeper writes the weather, the ships and the lamp hours in it. Some write more.", ("Category", "Object and custom"));

        elena.Relationships.Add(new EntityRelationship(tobias.Id, "sister of"));
        tobias.Relationships.Add(new EntityRelationship(elena.Id, "brother of"));
        harlan.Relationships.Add(new EntityRelationship(elena.Id, "mentor of"));
        crane.Relationships.Add(new EntityRelationship(guild.Id, "leader of"));
        crane.Relationships.Add(new EntityRelationship(tobias.Id, "creditor of"));

        foreach (StoryEntity entity in new[] { elena, tobias, crane, harlan, lighthouse, port, compass, guild, storm, reckoning, fire, log }) {
            project.Entities.Add(entity);
        }

        Chapter one = new("Chapter 1 - The Light") {
            Synopsis = "Elena finds the compass on the night of the anniversary. (Deliberate grammar mistakes: see the Error List.)",
            StoryDate = "12 October 1897",
            // Tobias is named more often than Elena here, so detection would guess him: set it (chapters 3 and 4 show detection).
            PointOfView = "Elena Marsh",
            Text = Chapter1
        };
        Chapter two = new("Chapter 2 - Port Ashby") {
            Synopsis = "Elena rows to town, faces the harbourmaster, and finds the bell of Tobias's boat in the Guild warehouse.",
            StoryDate = "13 October 1897",
            PointOfView = "Elena Marsh",
            Status = ChapterStatus.Revised,
            Text = Chapter2,
            TrackRevisions = true
        };
        Chapter three = new("Chapter 3 - The Logbook") {
            Synopsis = "Elena reads Harlan's log; a scene from the log itself. (Deliberate continuity slips: one for each continuity rule.)",
            StoryDate = "13 October 1897",
            Text = Chapter3
        };
        Chapter four = new("Chapter 4 - The Night of the Storm") {
            Synopsis = "Nine years earlier: the storm, the dark lamp, and the last night anyone saw Tobias. (Set years earlier: the timeline flags it.)",
            StoryDate = "October 1888",
            Text = Chapter4
        };
        Chapter five = new("Chapter 5 - The Reckoning") {
            Synopsis = "Elena confronts Crane in the warehouse with the logbook and the bell. Not written yet: right-click it in Solution Explorer > Move to Story Bible.",
            Status = ChapterStatus.Outline
        };
        foreach (Chapter chapter in new[] { one, two, three, four, five }) {
            project.Chapters.Add(chapter);
        }

        // Comments, a bookmark and plot-thread passages.
        Mark(one, "the the", MarkerKind.Comment, "Deliberate typo: put the caret on it and press Ctrl+. to fix it.");
        Mark(one, "like a slow and patient eye", MarkerKind.Comment, "Lovely image, but the sentence runs on (see NW3003).");
        ChapterComment resolved = Mark(three, "the candle guttered", MarkerKind.Comment, "Check how long a ship's candle burns.");
        resolved.IsResolved = true;
        Mark(three, "I am writing this by the lamp", MarkerKind.Bookmark, "");

        PlotThread compassThread = new("The compass points to the truth") {
            Description = "The compass never points north; it points at whatever its owner most wants to find. Still open: it needs a payoff."
        };
        PlotThread lightThread = new("Who put out the light?") {
            Description = "Someone put out the Greyhaven lamp the night of the storm. Its payoff comes before its setup (chapter 4 is a flashback), so the tracker warns."
        };
        PlotThread craneThread = new("Crane knew she was coming") {
            Description = "Crane is always one step ahead because he has been reading the keeper's log.",
            Status = PlotThreadStatus.Resolved
        };
        project.PlotThreads.Add(compassThread);
        project.PlotThreads.Add(lightThread);
        project.PlotThreads.Add(craneThread);
        Thread(one, "The needle trembeled, then swung away from the north and pointed straight out to sea", compassThread, ThreadRole.Setup);
        Thread(two, "The compass needle swung towards the Guild warehouse and stayed there", compassThread, ThreadRole.Development);
        Thread(three, "I put out the light myself", lightThread, ThreadRole.Payoff);
        Thread(four, "Someone climbed the hundred and twelve steps after midnight and put out the lamp", lightThread, ThreadRole.Setup);
        Thread(two, "as if he had known", craneThread, ThreadRole.Setup);
        Thread(three, "Crane has been reading these pages", craneThread, ThreadRole.Payoff);
        return project;
    }

    /// <summary>After the first save: a snapshot of Chapter 2's first draft, and the baseline its tracked changes are shown against.</summary>
    public static void Initialize(NovelProject project) {
        Chapter two = project.Chapters.First(static chapter => chapter.Title.StartsWith("Chapter 2", StringComparison.Ordinal));
        // Same line endings as the chapter, so comparisons show only real changes.
        string newline = two.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        SnapshotStore.Take(project.DirectoryPath, two, Chapter2FirstDraft.Replace("\r\n", "\n").Replace("\n", newline), "First draft");
        // The revision baseline is the chapter as it stood before the last pass: the changes show inline.
        string baseline = two.Text
            .Replace("Rough night for rowing.", "Bad night for rowing.", StringComparison.Ordinal)
            .Replace(" before he could see her hands shake", "", StringComparison.Ordinal)
            .Replace("the chain was new and the lock was **old**", "the chain was old", StringComparison.Ordinal);
        RevisionStore.Save(project.DirectoryPath, two, baseline);
    }

    private static StoryEntity Character(string name, string aliases, string summary, params (string Key, string Value)[] attributes) {
        return Entity(EntityKind.Character, name, aliases, summary, attributes);
    }

    private static StoryEntity Entity(EntityKind kind, string name, string aliases, string summary, params (string Key, string Value)[] attributes) {
        StoryEntity entity = StoryEntity.CreateWithDefaults(kind, name);
        entity.Aliases = aliases;
        entity.Summary = summary;
        foreach ((string key, string value) in attributes) {
            EntityAttribute? attribute = entity.Attributes.FirstOrDefault(a => a.Key == key);
            if (attribute is null) {
                entity.Attributes.Add(new EntityAttribute(key, value));
            }
            else {
                attribute.Value = value;
            }
        }
        return entity;
    }

    private static ChapterComment Mark(Chapter chapter, string anchor, MarkerKind kind, string text) {
        int offset = chapter.Text.IndexOf(anchor, StringComparison.Ordinal);
        if (offset < 0) {
            throw new InvalidOperationException($"Sample text is missing \"{anchor}\".");
        }
        ChapterComment marker = new() {
            Kind = kind,
            Offset = offset,
            Length = anchor.Length,
            AnchorText = anchor,
            // A bookmark's text is the line it marks, as when one is set in the editor.
            Text = kind == MarkerKind.Bookmark ? anchor : text,
            Author = "WritingIDE sample",
            CreatedUtc = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc)
        };
        chapter.Comments.Add(marker);
        return marker;
    }

    private static void Thread(Chapter chapter, string passage, PlotThread thread, ThreadRole role) {
        ChapterComment marker = Mark(chapter, passage, MarkerKind.PlotThread, "");
        marker.ThreadId = thread.Id;
        marker.ThreadRole = role;
    }
}