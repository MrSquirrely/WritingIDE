# WritingIDE

An IDE for novelists, modelled on Visual Studio. C# / WPF on .NET 10, styled with [HandyControls](https://www.nuget.org/packages/HandyControls).

You create a **project** (`.novproj`), then add **chapters** (like source files) and **story elements** (like classes): characters, places, items, factions, events and lore. While you write, the editor gives you IntelliSense for your story elements and a grammar engine that reports problems the way a compiler does.

A project is either a **novel** or a **series**. A series is like a solution with several projects: it holds several **books**, each with its own chapters, and every book shares the same characters, places, lore and **Series Bible** (premise, tone, themes, world rules and plot plan). Create one with **File > New Project > Book Series**, or turn a novel into a series with **Project > Convert to Series**. Add books from the Project menu, the Add New Item dialog or by right-clicking the series; move chapters between books from their right-click menu or the Book box in Properties.

**View > Theme** switches between Light, Dark and Use Windows Setting (the default, which also follows Windows when you change it). The choice is saved per user. The palettes live in `src/WritingIDE/Themes/Light.xaml` and `Dark.xaml`, and every colour in the UI comes from them.

## Run

```bash
dotnet run --project src/WritingIDE
```

To open a project straight away, pass its path: `WritingIDE.exe path\to\MyNovel.novproj`. Use **File > New Project > Sample Novel** for a demo project with deliberate mistakes in it.

```bash
dotnet test
```

## Importing (with an AI's help)

**File > Import** (Ctrl+I, or the Import button on the toolbar and Start Page) opens a two-step dialog:

1. **Ask an AI.** Pick what the AI should do:
   - **Convert a story bible we made in the chat.** For a world bible written earlier in an AI conversation. Characters, places, factions, events and **lore** (magic systems, ranks, glossary terms) become story elements; premise, tone, themes and the plot plan go into the Story/Series Bible; planned chapters become **Outline** chapters, with any drafted key scenes copied into them.
   - **Convert a manuscript the AI wrote in the chat.** Every chapter, prologue and epilogue word for word, plus the characters and places in it.
   - **Convert my own draft or notes**, or **Develop a story idea**.

   Tick what to include and, optionally, paste the material into the dialog instead of leaving it in the chat. WritingIDE builds a prompt that explains the import format, gives an example, and lists the books and story elements already in your project so the AI reuses their exact names. Send it in the same AI chat as the bible or manuscript (ChatGPT, Claude, Gemini or any other).
   Long bibles and manuscripts don't fit in one AI reply, so the prompt tells the AI to stop at a block boundary with `=== CONTINUE ===`. Import each part, then reply "continue" in the chat; importing again merges into what is already there.
2. **Paste the reply and preview.** Paste the AI's answer, or open a `.txt`/`.md` file. A live preview shows each book, chapter and story element, whether it is **New**, an **Update** to an existing one, or **Skip**, plus any parse warnings. Choose what happens to items that already exist (update/merge, skip, or add as new) and, in a series, which book receives chapters that don't name one. If the text contains books and the project is a novel, you can convert it into a series as part of the import.

A Markdown manuscript such as an AI chat export also imports directly, with no AI round trip. A single `# Title` over `## Chapter` headings becomes one book with its chapters; several `#` headings become several books. Title-page lines between the book heading and the first chapter are skipped.

The format is plain text, so you can also write it by hand:

```
=== CHARACTER: Elena Marsh ===
Aliases: Elena, Marsh
Summary: Keeper of the Greyhaven light.
Role: Protagonist
Age: 41
Notes:
Lost her brother in the Great Storm.

=== CHAPTER: Chapter 1 - The Light ===
Synopsis: Elena finds a strange compass.
Status: Draft
---
The wind had been rising since noon...
```

Block types are `NOVEL` or `SERIES` (with a multi-line `Notes:` field for the bible), `BOOK` (chapter blocks after it belong to it), `CHAPTER`, `CHARACTER`, `PLACE`, `ITEM`, `FACTION`, `EVENT`, `LORE` and `CONTINUE`. Common synonyms work too, such as `LOCATION`, `ORGANIZATION`, `CONCEPT` or `MAGIC`. The parser also accepts Markdown bullets and bold field names and `### TYPE: Name` headers, and it ignores code fences and chatter before the first block. Text with no blocks is imported as a plain manuscript, split at Markdown headings or lines like `Chapter 3` and `Prologue`.

## Plugins: Harper

**Tools > Plugins** turns on [Harper](https://github.com/Automattic/harper), Automattic's open-source grammar checker. WritingIDE runs Harper's language server, `harper-ls`, on your computer (text never leaves it) and adds its findings to the Error List and squiggles under rule ID `HARPER`. Harper's quick fixes appear in Ctrl+. like the built-in ones.

- **Uninstall** stops the plugin and deletes WritingIDE's copy of harper-ls. Copies installed elsewhere (PATH, Cargo) are left alone.
- **Download and install latest** fetches the official Windows build from Harper's GitHub releases into `%LocalAppData%\WritingIDE\Plugins\Harper`. Alternatively, point the plugin at your own `harper-ls.exe`; it is also found on `PATH` and in `~/.cargo/bin`.
- Story element names and project-dictionary words are never reported as misspelled. Findings that a built-in rule already reported are dropped, and Harper's long-sentence check is switched off because NW3003 covers it.
- Chapters are checked paragraph by paragraph and cached, so after the first pass only edited paragraphs go back to Harper. Background analysis when a project opens skips plugins; open chapters and **Analyze Manuscript** (F6) include them.
- A project can switch Harper off like any rule in **Tools > Options > Grammar Rules**. The plugin's own on/off switch is a per-user setting.

## Visual Studio concepts mapped to a novel

| Visual Studio | WritingIDE |
|---|---|
| Solution Explorer | Project tree: Story Bible, Manuscript (or one node per book in a series), Characters, Places, Items, Factions, Events, Lore |
| Solution with several projects | A series with several books sharing one cast, world and bible |
| Add New Item (Ctrl+Shift+A) | Add a chapter or a story element from templates |
| Class designer | Story element editor: name, aliases, summary, key/value attributes, notes, and the chapters it appears in |
| IntelliSense (`obj.`) | `person.` / `character.` / `char.` lists characters, `place.` / `location.` / `loc.` places, `item.` / `object.` items, `faction.` / `group.` / `org.` factions, `event.` events. Ctrl+Space lists everything. Typing the start of a known name offers to complete it |
| Semantic highlighting | Every mention of a story element is coloured by its kind |
| Quick Info | Hover a name for its summary and attributes, or a squiggle for the diagnostic |
| Light bulb (Ctrl+.) | Quick fixes, "Add to dictionary", "Suppress rule", "Create story element from this word" |
| Go To Definition (F12, Ctrl+Click) | Opens the story element |
| Find All References (Shift+F12) | Every mention, in Find Results |
| Rename (F2) | Renames the element and, optionally, every mention in the manuscript |
| Build (F6 / Ctrl+Shift+B) | Analyze Manuscript: runs every rule on every chapter and writes a build log to Output |
| Error List | Errors, warnings and messages with filters and a scope (entire manuscript or current chapter); double-click to jump |
| Analyzers / rule sets | Tools > Options: switch rules on or off per project; edit the project dictionary |

## Grammar engine

`WritingIDE.Core` has a tokenizer, a sentence splitter (handles abbreviations, initials, ellipses and dialogue tags such as `"Help!" she cried.`) and a set of rules. Each rule has a diagnostic ID:

| ID | Rule | Severity |
|---|---|---|
| NW1001 | Spelling (Windows spell checker; story element names and the project dictionary are always accepted) | Error |
| NW1002 | Repeated word ("the the") | Error |
| NW1003 | a / an by sound ("an hour", "a unicorn", "an FBI agent") | Warning |
| NW1004 | Subject-verb agreement ("they was", "he don't") | Warning |
| NW1005 | Sentence capitalization, lowercase "i" | Warning |
| NW1006 | Commonly confused words (could of, then/than, its/it's, your/you're, loose/lose, ...) | Warning |
| NW2001 | Punctuation spacing | Warning |
| NW2002 | Unbalanced quotes and brackets (multi-paragraph dialogue is allowed) | Warning |
| NW3001 | Passive voice | Message |
| NW3002 | Weak intensifiers, with stronger words suggested ("very tired" becomes "exhausted") | Message |
| NW3003 | Long sentences (over 40 words) | Message |
| NW3004 | Clichés | Message |
| NW3005 | Redundant phrases ("nodded his head", "in order to") | Message |
| NW3006 | Adverbs in dialogue tags ("she said quietly") | Message |
| NW4001 | Story element written in the wrong case ("elena" for "Elena") | Warning |
| HARPER | Findings from the Harper plugin, when enabled (spelling counts as an error) | Warning |

To add a rule, derive from `GrammarRule` (or `PatternRule` for phrase tables) and add it to `GrammarEngine.Rules`.

## Layout

```
src/WritingIDE.Core     Model, persistence, import parser and AI prompt, tokenizer, grammar engine, statistics (no UI dependencies)
src/WritingIDE          WPF shell (HandyControls): AvalonEdit-based editor, IntelliSense, squiggles, tool windows, dialogs, Harper plugin (LSP client)
tests/                  xUnit tests for the grammar engine, analysis, import and persistence
```

A project is a folder that holds `Name.novproj` (JSON: metadata, story elements, dictionary, disabled rules) and `Manuscript/*.txt` (one plain-text file per chapter).
