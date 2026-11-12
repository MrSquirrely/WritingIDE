using System.Text;

using WritingIDE.Core.Model;

namespace WritingIDE.Core.Import;

public enum ImportPromptMode {
    /// <summary>A story bible (world, cast, plot plan) that already exists in the AI chat or is pasted.</summary>
    ConvertBible,

    /// <summary>A manuscript the AI already wrote in the chat (or that is pasted): chapters kept word for word.</summary>
    ConvertManuscript,

    /// <summary>Convert an existing draft, outline or notes without rewriting the prose.</summary>
    ConvertMyText,

    /// <summary>Develop new material from a story idea.</summary>
    DevelopMyIdea
}

public sealed record ImportPromptOptions(
    ImportPromptMode Mode,
    bool IncludeChapters,
    IReadOnlySet<EntityKind> Kinds,
    int ChapterCount,
    string Material,
    IReadOnlyList<StoryEntity> ExistingEntities) {
    /// <summary>The project is a series, so books are part of the format.</summary>
    public bool IsSeries { get; init; }

    /// <summary>Books already in the series, so the AI reuses their titles.</summary>
    public IReadOnlyList<string> ExistingBooks { get; init; } = [];

    /// <summary>Bible mode: turn the bible's plot plan into Outline chapters.</summary>
    public bool IncludeOutlineChapters { get; init; } = true;

    /// <summary>Bible mode: copy the bible's premise, tone, themes and world rules into the story notes.</summary>
    public bool IncludeStoryNotes { get; init; } = true;
}

/// <summary>Builds a prompt the user can give to any AI chat so it answers in the WritingIDE import format.</summary>
public static class ImportPrompt {
    public const string MaterialPlaceholder = "[PASTE YOUR DRAFT, NOTES OR STORY IDEA HERE]";

    public static string Build(ImportPromptOptions options) {
        StringBuilder prompt = new();
        List<EntityKindInfo> kinds = EntityKindInfo.All.Where(info => options.Kinds.Contains(info.Kind)).ToList();
        string elementList = string.Join(", ", kinds.Select(static info => info.PluralName.ToLowerInvariant()));
        bool chapters = options.IncludeChapters || (options.Mode == ImportPromptMode.ConvertManuscript);
        bool hasMaterial = !string.IsNullOrWhiteSpace(options.Material);
        string projectWord = options.IsSeries ? "SERIES" : "NOVEL";

        List<string> types = [];
        if ((options.Mode == ImportPromptMode.ConvertBible) && options.IncludeStoryNotes) {
            types.Add(projectWord);
        }
        types.AddRange(kinds.Select(static info => info.DisplayName.ToUpperInvariant()));
        if (chapters && options.IsSeries) {
            types.Add("BOOK");
        }
        if (chapters) {
            types.Add("CHAPTER");
        }

        prompt.AppendLine("You are helping me move my writing into WritingIDE, a novel-writing application with an importer.");
        prompt.AppendLine();
        prompt.AppendLine(BuildTask(options, elementList, chapters, hasMaterial, projectWord));
        prompt.AppendLine();
        prompt.AppendLine("Reply with ONLY the formatted text described below: no introduction, no explanations, no summaries of what you did and no Markdown code fences.");
        prompt.AppendLine();

        prompt.AppendLine("FORMAT");
        prompt.AppendLine("Every item starts with a header line of the form:");
        prompt.AppendLine("=== TYPE: Name ===");
        prompt.AppendLine($"where TYPE is one of: {string.Join(", ", types)}.");
        prompt.AppendLine("Every field is one \"Key: value\" line. Tables in the source become one field per row or plain lines inside Notes.");
        prompt.AppendLine();
        if (types.Contains(projectWord)) {
            prompt.AppendLine($"{projectWord} block (exactly one, first):");
            prompt.AppendLine("- Author:, Genre:, Synopsis: (the logline), Target words: (a number). Each optional.");
            prompt.AppendLine("- Notes: on its own line, then everything that is about the story as a whole and is not a single character, place, item, faction, event or lore entry: premise, tone, themes, heat level, the relationship or plot arc, rules of the central secret, story hooks for later. Keep the bible's own wording and section titles as plain lines.");
            prompt.AppendLine();
        }
        if (kinds.Count > 0) {
            prompt.AppendLine($"Story element blocks ({string.Join(", ", kinds.Select(static info => info.DisplayName.ToUpperInvariant()))}) contain:");
            prompt.AppendLine("- Aliases: other names used for it in the text (nicknames, first names, surnames, titles), comma separated. Omit if none.");
            prompt.AppendLine("- Summary: one or two sentences.");
            prompt.AppendLine("- Any number of \"Attribute: value\" lines. Suggested attributes:");
            foreach (EntityKindInfo info in kinds) {
                prompt.AppendLine($"    {info.DisplayName.ToUpperInvariant()}: {string.Join(", ", info.DefaultAttributes)}{AttributeHint(info.Kind)}");
            }
            prompt.AppendLine("- Notes: on its own line, followed by free-form paragraphs (backstory, habits, history, details). Notes must be the last field of the block.");
            if (options.Kinds.Contains(EntityKind.Lore)) {
                prompt.AppendLine("LORE blocks hold worldbuilding that is not a character, place, item, faction or event: magic systems, ranks, laws, currencies, customs, monster types, technology and glossary terms. One block per concept or glossary term.");
            }
            prompt.AppendLine();
        }
        if (chapters && options.IsSeries) {
            prompt.AppendLine("BOOK blocks start a book of the series. Every CHAPTER block belongs to the most recent BOOK block above it.");
            prompt.AppendLine("- Synopsis: one or two sentences. Optional.");
            if (options.ExistingBooks.Count > 0) {
                prompt.AppendLine($"- Books that already exist (reuse these exact titles): {string.Join("; ", options.ExistingBooks)}.");
            }
            prompt.AppendLine();
        }
        if (chapters) {
            prompt.AppendLine("CHAPTER blocks contain:");
            prompt.AppendLine("- Synopsis: one sentence. Optional.");
            prompt.AppendLine("- Status: one of Outline, Draft, Revised, Final. Optional.");
            prompt.AppendLine("- A line containing only ---");
            prompt.AppendLine("- Then the chapter text as plain prose: one paragraph per line with a blank line between paragraphs and straight double quotes for dialogue. No Markdown headings or bullet lists inside the text. *Italics* may stay.");
            prompt.AppendLine("Prologues, interludes and epilogues are CHAPTER blocks too (for example \"=== CHAPTER: Prologue: The Color of Silver ===\").");
            prompt.AppendLine();
        }

        prompt.AppendLine("RULES");
        prompt.AppendLine("- Spell every name exactly the same way everywhere. A name used in the chapter text must be the Name or one of the Aliases of its block.");
        if (kinds.Count > 0) {
            prompt.AppendLine("- Give every story element that matters its own block, and put story element blocks before BOOK and CHAPTER blocks.");
        }
        if (chapters) {
            prompt.AppendLine("- Keep chapters in reading order.");
        }
        foreach (string rule in ModeRules(options, chapters)) {
            prompt.AppendLine($"- {rule}");
        }
        if (options.ExistingEntities.Count > 0) {
            prompt.AppendLine("- These story elements already exist in my project. Reuse their exact names; only add a block for one of them if you have new details:");
            foreach (StoryEntity entity in options.ExistingEntities.OrderBy(static entity => entity.Kind).ThenBy(static entity => entity.Name)) {
                string aliases = entity.AliasList.Count > 0 ? $" (also: {string.Join(", ", entity.AliasList)})" : "";
                prompt.AppendLine($"    {entity.KindInfo.DisplayName.ToUpperInvariant()}: {entity.Name}{aliases}");
            }
        }
        if (IsLong(options.Mode)) {
            prompt.AppendLine("- If everything does not fit in one reply, stop after a complete block and write the line \"=== CONTINUE ===\" as the very last line. When I answer \"continue\", carry on exactly where you stopped" +
                (options.IsSeries && chapters ? ", starting with the current BOOK header line again" : "") + ". Never skip or summarise anything to make it fit.");
        }
        prompt.AppendLine();

        prompt.AppendLine("EXAMPLE");
        prompt.AppendLine(BuildExample(options, kinds, chapters, types.Contains(projectWord), projectWord));
        prompt.AppendLine();

        if (hasMaterial) {
            prompt.AppendLine(options.Mode switch {
                ImportPromptMode.ConvertBible => "MY STORY BIBLE",
                ImportPromptMode.ConvertManuscript => "MY MANUSCRIPT",
                ImportPromptMode.DevelopMyIdea => "MY STORY IDEA",
                _ => "MY MATERIAL"
            });
            prompt.Append(options.Material.Trim());
        }
        else if (options.Mode is ImportPromptMode.ConvertMyText or ImportPromptMode.DevelopMyIdea) {
            prompt.AppendLine(options.Mode == ImportPromptMode.DevelopMyIdea ? "MY STORY IDEA" : "MY MATERIAL");
            prompt.Append(MaterialPlaceholder);
        }
        else {
            prompt.Append(options.Mode == ImportPromptMode.ConvertBible
                ? "Use the story bible from earlier in this conversation."
                : "Use the manuscript from earlier in this conversation, from its first page to its last.");
        }
        return prompt.ToString().TrimEnd();
    }

    private static bool IsLong(ImportPromptMode mode) {
        return mode is ImportPromptMode.ConvertManuscript or ImportPromptMode.ConvertBible or ImportPromptMode.ConvertMyText;
    }

    private static string BuildTask(ImportPromptOptions options, string elementList, bool chapters, bool hasMaterial, string projectWord) {
        string source(string what) => hasMaterial ? $"the {what} below" : $"the {what} we created earlier in this conversation";
        StringBuilder task = new();
        switch (options.Mode) {
            case ImportPromptMode.ConvertBible:
                task.Append($"Convert {source("story bible")} into the WritingIDE import format.");
                if (elementList.Length > 0) {
                    task.Append($" Give each of the {elementList} it describes its own block, keeping all of the bible's details.");
                }
                if (options.IncludeStoryNotes) {
                    task.Append($" Put the premise, tone, themes and plot plan into the {projectWord} block's Notes.");
                }
                if (chapters) {
                    task.Append(" Turn the bible's planned chapters, acts or key scenes into CHAPTER blocks with Status: Outline whose text lists the planned beats; when the bible contains a drafted scene, include the draft in full in the chapter where it is placed and mark that chapter Status: Draft.");
                }
                break;
            case ImportPromptMode.ConvertManuscript:
                task.Append($"Convert {source("manuscript")} into the WritingIDE import format. Every chapter, prologue and epilogue becomes a CHAPTER block containing its text exactly as written: do not rewrite, shorten, summarise, correct or skip a single sentence.");
                if (options.IsSeries) {
                    task.Append(" Start with a BOOK block for the book the manuscript belongs to.");
                }
                if (elementList.Length > 0) {
                    task.Append($" Before the chapters, add blocks for the {elementList} that appear in it, using only details the text gives.");
                }
                break;
            case ImportPromptMode.ConvertMyText:
                task.Append($"Convert {(hasMaterial ? "my material below (a draft, outline or notes)" : "my material (pasted at the end of this message)")} into the WritingIDE import format.");
                if (chapters) {
                    task.Append(" Split the manuscript into chapters and keep my prose exactly as written: do not rewrite, shorten, summarise or correct it.");
                }
                if (elementList.Length > 0) {
                    task.Append($" Extract the {elementList} that appear in it, using only details the text gives or clearly implies.");
                }
                break;
            default:
                task.Append("Develop my story idea into the WritingIDE import format.");
                if (elementList.Length > 0) {
                    task.Append($" Create the {elementList} the story needs, with vivid, specific details.");
                }
                if (chapters) {
                    int count = Math.Max(1, options.ChapterCount);
                    task.Append($" Then write {count} chapter{(count == 1 ? "" : "s")} of polished prose that use{(count == 1 ? "s" : "")} those story elements.");
                }
                break;
        }
        return task.ToString();
    }

    private static IEnumerable<string> ModeRules(ImportPromptOptions options, bool chapters) {
        switch (options.Mode) {
            case ImportPromptMode.ConvertBible:
                yield return "Do not invent anything the bible does not say. Copy draft scenes word for word.";
                yield return "Glossary terms, ranks, magic schools and monster types become LORE blocks (or the matching type when one fits better).";
                break;
            case ImportPromptMode.ConvertManuscript:
                yield return "Chapter text must be identical to the manuscript. Remove only Markdown heading lines, which become the CHAPTER header instead.";
                yield return "Front matter such as a foreword or an in-world excerpt also becomes a CHAPTER block, with its title as the name.";
                break;
            case ImportPromptMode.ConvertMyText:
                yield return "Do not invent facts that are not in my material.";
                break;
        }
        if (chapters && !options.IsSeries) {
            yield return "Do not use BOOK blocks.";
        }
    }

    private static string AttributeHint(EntityKind kind) {
        return kind switch {
            EntityKind.Character => ", Species, Rank, Fighting style, Habits, Wants, Needs, Flaw",
            EntityKind.Place => ", Smells and sounds, Who goes there",
            EntityKind.Lore => ", Cost, Who knows",
            _ => ""
        };
    }

    private static string BuildExample(ImportPromptOptions options, List<EntityKindInfo> kinds, bool chapters, bool novelBlock, string projectWord) {
        StringBuilder example = new();
        if (novelBlock) {
            example.AppendLine($"=== {projectWord}: The Lighthouse Keeper ===");
            example.AppendLine("Genre: Literary mystery");
            example.AppendLine("Synopsis: A lighthouse keeper finds a compass that points to the night her brother vanished.");
            example.AppendLine("Notes:");
            example.AppendLine("Tone: quiet, salt-stained, hopeful.");
            example.AppendLine("Themes: grief; what the sea keeps.");
            example.AppendLine();
        }
        if (kinds.Any(static info => info.Kind == EntityKind.Character)) {
            example.AppendLine("=== CHARACTER: Elena Marsh ===");
            example.AppendLine("Aliases: Elena, Marsh");
            example.AppendLine("Summary: Keeper of the Greyhaven light for eleven years; practical and guarded.");
            example.AppendLine("Role: Protagonist");
            example.AppendLine("Age: 41");
            example.AppendLine("Notes:");
            example.AppendLine("Lost her brother Tobias in the Great Storm nine years ago.");
            example.AppendLine();
        }
        if (kinds.Any(static info => info.Kind == EntityKind.Place)) {
            example.AppendLine("=== PLACE: Greyhaven Lighthouse ===");
            example.AppendLine("Aliases: Greyhaven");
            example.AppendLine("Summary: A white tower on a black rock, two miles off the coast.");
            example.AppendLine("Type: Lighthouse");
            example.AppendLine();
        }
        if (kinds.Any(static info => info.Kind == EntityKind.Lore)) {
            example.AppendLine("=== LORE: Tide-reading ===");
            example.AppendLine("Summary: Keepers' folk magic for reading storms in the swell.");
            example.AppendLine("Category: Magic");
            example.AppendLine("Cost: Leaves the reader seasick for a day.");
            example.AppendLine();
        }
        if (kinds.Any(static info => info.Kind == EntityKind.Item)) {
            example.AppendLine("=== ITEM: Brass Compass ===");
            example.AppendLine("Summary: A compass whose needle points towards the storm instead of north.");
            example.AppendLine("Owner: Tobias Marsh");
            example.AppendLine();
        }
        if (chapters) {
            if (options.IsSeries) {
                example.AppendLine("=== BOOK: The Lighthouse Keeper ===");
                example.AppendLine("Synopsis: Book one of the Greyhaven series.");
                example.AppendLine();
            }
            bool outline = (options.Mode == ImportPromptMode.ConvertBible) && options.IncludeOutlineChapters;
            example.AppendLine("=== CHAPTER: Chapter 1 - The Light ===");
            example.AppendLine("Synopsis: Elena finds a strange compass on the anniversary of the storm.");
            example.AppendLine(outline ? "Status: Outline" : "Status: Draft");
            example.AppendLine("---");
            if (outline) {
                example.AppendLine("Beat 1: Elena climbs the tower at dusk.");
                example.AppendLine();
                example.AppendLine("Beat 2: She finds the compass on the sill.");
            }
            else {
                example.AppendLine("The wind had been rising since noon, and by dusk Elena could hear it singing in the railings of Greyhaven.");
                example.AppendLine();
                example.AppendLine("\"Who's there?\" she whispered.");
            }
        }
        return example.ToString().TrimEnd();
    }
}