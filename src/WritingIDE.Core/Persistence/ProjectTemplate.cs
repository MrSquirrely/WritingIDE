using WritingIDE.Core.Model;

namespace WritingIDE.Core.Persistence;

/// <summary>A "New Project" template, like the project templates in Visual Studio.</summary>
public sealed class ProjectTemplate(string title, string description, string glyph, Func<string, string, NovelProject> factory) {
    public string Title { get; } = title;

    public string Description { get; } = description;

    public string Glyph { get; } = glyph;

    public static IReadOnlyList<ProjectTemplate> All { get; } = [
        new("Empty Novel", "A blank manuscript with a single chapter. Add characters, places and chapters as you go.", "\uE8A5", CreateEmpty),
        new("Sample Novel", "\"The Lighthouse Keeper\": a short sample with characters, places and a chapter full of deliberate mistakes to show off IntelliSense and the grammar engine.", "\uE82D", CreateSample),
        new("Short Story", "A single-document short story with a protagonist and a setting ready to fill in.", "\uE70B", CreateShortStory),
        new("Book Series", "Several books that share one cast, world and story bible, like a solution with several projects. Starts with Book 1; add books as the series grows.", "\uE8F1", CreateSeries)
    ];

    public static ProjectTemplate Series => All.Single(static template => template.Title == "Book Series");

    private static NovelProject CreateSeries(string name, string author) {
        NovelProject project = new() { Name = name, Author = author, Kind = ProjectKind.Series, TargetWordCount = 300000 };
        Book first = new($"{name}, Book 1");
        project.Books.Add(first);
        project.Chapters.Add(new Chapter("Chapter 1") { BookId = first.Id });
        return project;
    }

    public NovelProject Create(string name, string author) {
        NovelProject project = factory(name, author);
        project.IsDirty = false;
        return project;
    }

    private static NovelProject CreateEmpty(string name, string author) {
        NovelProject project = new() { Name = name, Author = author };
        project.Chapters.Add(new Chapter("Chapter 1"));
        return project;
    }

    private static NovelProject CreateShortStory(string name, string author) {
        NovelProject project = new() { Name = name, Author = author, TargetWordCount = 7500, Genre = "Short fiction" };
        project.Chapters.Add(new Chapter(name) { Synopsis = "The whole story." });
        project.Entities.Add(StoryEntity.CreateWithDefaults(EntityKind.Character, "Protagonist"));
        project.Entities.Add(StoryEntity.CreateWithDefaults(EntityKind.Place, "Setting"));
        return project;
    }

    private static NovelProject CreateSample(string name, string author) {
        NovelProject project = new() {
            Name = name,
            Author = author,
            Genre = "Literary mystery",
            Synopsis = "A reclusive lighthouse keeper finds a brass compass that points to the night her brother vanished."
        };

        StoryEntity elena = StoryEntity.CreateWithDefaults(EntityKind.Character, "Elena Marsh");
        elena.Aliases = "Elena, Marsh";
        elena.Summary = "Keeper of the Greyhaven light for eleven years. Practical, guarded, quietly funny.";
        SetAttribute(elena, "Role", "Protagonist");
        SetAttribute(elena, "Age", "41");
        SetAttribute(elena, "Pronouns", "she/her");
        SetAttribute(elena, "Goal", "Learn what happened to her brother Tobias.");

        StoryEntity tobias = StoryEntity.CreateWithDefaults(EntityKind.Character, "Tobias Marsh");
        tobias.Aliases = "Tobias, Toby";
        tobias.Summary = "Elena's younger brother. Lost at sea during the Great Storm, or so everyone believes.";
        SetAttribute(tobias, "Role", "Missing person");
        SetAttribute(tobias, "Pronouns", "he/him");

        StoryEntity crane = StoryEntity.CreateWithDefaults(EntityKind.Character, "Silas Crane");
        crane.Aliases = "Crane, Silas";
        crane.Summary = "Harbourmaster of Port Ashby and senior member of the Harbor Guild.";
        SetAttribute(crane, "Role", "Antagonist");

        StoryEntity lighthouse = StoryEntity.CreateWithDefaults(EntityKind.Place, "Greyhaven Lighthouse");
        lighthouse.Aliases = "Greyhaven";
        lighthouse.Summary = "A white tower on a black rock, two miles off the coast.";
        SetAttribute(lighthouse, "Type", "Lighthouse");

        StoryEntity port = StoryEntity.CreateWithDefaults(EntityKind.Place, "Port Ashby");
        port.Summary = "A fishing town that smells of tar, salt and secrets.";
        SetAttribute(port, "Type", "Town");

        StoryEntity compass = StoryEntity.CreateWithDefaults(EntityKind.Item, "Brass Compass");
        compass.Aliases = "compass";
        compass.Summary = "Tobias's compass. Its needle points not north, but towards the storm.";

        StoryEntity guild = StoryEntity.CreateWithDefaults(EntityKind.Faction, "Harbor Guild");
        guild.Summary = "The merchants who own every boat, pier and debt in Port Ashby.";
        SetAttribute(guild, "Leader", "Silas Crane");

        StoryEntity storm = StoryEntity.CreateWithDefaults(EntityKind.Event, "Great Storm");
        storm.Summary = "The night, nine years ago, when the light went dark and Tobias vanished.";

        foreach (StoryEntity entity in new[] { elena, tobias, crane, lighthouse, port, compass, guild, storm }) {
            project.Entities.Add(entity);
        }

        project.Chapters.Add(new Chapter("Chapter 1 - The Light") {
            Synopsis = "Elena finds the compass on the night of the anniversary.",
            Text = """
                The wind had been rising since noon, and by dusk Elena Marsh could hear it singing in the the iron railings of Greyhaven Lighthouse.

                she climbed the hundred and twelve steps with a old lantern in one hand. Its a long way up when your tired, and she was very tired. Her knees ached. The lamp was lit by her every evening at six, the way her father had taught her.

                Tobias would of laughed at her. He always said she worried more then anyone he knew. "You'll loose your mind out here," he had told her, the week before the Great Storm.

                At the top she found it. A brass compass, green with age, sitting on the sill where no compass had any right to be. elena picked it up. The needle trembeled, then swung away from the north and pointed straight out to sea, towards Port Ashby.

                They was the only two people who knew about the compass. Him and her. And Tobias had been dead for nine years.

                "Who's there?" she whispered quietly.

                Nobody answered. Only the light turned, and turned, and turned, sweeping the black water like a slow and patient eye that had seen every ship and every storm and every drowned sailor that the sea had ever taken from the people of the coast who loved them.
                """
        });
        project.Chapters.Add(new Chapter("Chapter 2 - Port Ashby") {
            Synopsis = "Elena rows to town and confronts the harbourmaster.",
            Text = """
                Silas Crane was waiting on the pier when the boat came in, as if he had known.

                "Miss Marsh." He nodded his head. "Rough night for rowing."
                """
        });
        return project;
    }

    private static void SetAttribute(StoryEntity entity, string key, string value) {
        EntityAttribute? attribute = entity.Attributes.FirstOrDefault(a => a.Key == key);
        if (attribute is null) {
            entity.Attributes.Add(new EntityAttribute(key, value));
        }
        else {
            attribute.Value = value;
        }
    }
}