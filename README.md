# WritingIDE

An IDE for novelists, modelled on Visual Studio. C# / WPF on .NET 10, styled with [HandyControls](https://www.nuget.org/packages/HandyControls).

Runs on Windows 10 and 11 (.NET 10 does not support older versions). The app is per-monitor DPI aware, so text stays sharp on monitors with different scaling.

You create a **project** (`.novproj`), then add **chapters** (like source files) and **story elements** (like classes): characters, places, items, factions, events and lore. While you write, the editor gives you IntelliSense for your story elements and a grammar engine that reports problems the way a compiler does.

A project is either a **novel** or a **series**. A series is like a solution with several projects: it holds several **books**, each with its own chapters, and every book shares the same characters, places, lore and **Series Bible** (premise, tone, themes, world rules and plot plan). Create one with **File > New Project > Book Series**, or turn a novel into a series with **Project > Convert to Series**. Add books from the Project menu, the Add New Item dialog or by right-clicking the series; move chapters between books from their right-click menu or the Book box in Properties.

**View > Theme** switches between Light, Dark and Use Windows Setting (the default, which also follows Windows when you change it). The choice is saved per user. The palettes live in `src/WritingIDE/Themes/Light.xaml` and `Dark.xaml`, and every colour in the UI comes from them.

## Run

```bash
dotnet run --project src/WritingIDE
```

To open a project straight away, pass its path: `WritingIDE.exe path\to\MyNovel.novproj`. Use **File > New Project > Sample Novel** for "The Lighthouse Keeper", a short mystery that shows off every feature:

- **Chapter 1** is full of deliberate grammar mistakes (Error List, Ctrl+. fixes) and has margin comments and a plot-thread setup.
- **Chapter 2** has its POV set, a labelled scene break, a TODO and a `[TK]`, a "First draft" snapshot to compare with, and revision mode already on, so tracked changes show inline.
- **Chapter 3** has one deliberate continuity slip for each continuity rule (NW4002–NW4007, NW4009), a resolved comment, a bookmark and a first-person logbook scene.
- **Chapter 4** is a flashback, set in 1888, which the Timeline flags; it also trips NW4008, age from birth year.
- **Chapter 5** is only an outline: try right-click > Move to Story Bible.

The story elements have attributes and relationships, so the Relationship Map and Character Statistics have something to show. The three plot threads cover a thread never paid off, one paid off before its setup, and a clean one. The story bible holds world rules and a chapter plan, and one event is left unplaced for the Timeline to point out.

```bash
dotnet test
```

## Installers and updates

WritingIDE ships with [Velopack](https://velopack.io): a per-user installer (no administrator rights) with automatic updates.

```powershell
.\build\release.ps1 -Version 1.0.0 -UpdateUrl https://github.com/<owner>/<releases-repo>
```

The script publishes a self-contained, ReadyToRun build for x64 and ARM64 (the publish profiles `FolderProfile` and `FolderProfileArm64`), then runs Velopack's `vpk` tool, pinned in `dotnet-tools.json`. It writes these files to `artifacts\releases`:

- `WritingIDE.App-win-x64-Setup.exe` and `WritingIDE.App-win-arm64-Setup.exe`: the installers.
- `...-Portable.zip`: runs without installing.
- `...-full.nupkg` plus `releases.*.json`, `assets.*.json` and `RELEASES-*`: the update packages and feeds.

Upload all of them to the place `-UpdateUrl` names: the releases of a public GitHub repository (`dotnet vpk upload github ...`), or any HTTPS folder. The source repository can stay private.

- **The installer** puts WritingIDE in `%LocalAppData%\WritingIDE.App`, adds Start menu and desktop shortcuts, and registers `.novproj` files to open in WritingIDE. It needs no .NET install. The writer's own data stays in `%LocalAppData%\WritingIDE` and `%AppData%\WritingIDE` and survives uninstalling.
- **Updates**: the installed app checks quietly at startup and downloads a new version in the background. It installs when WritingIDE closes; **Help > Restart to Update** installs it straight away, after the usual offer to save. **Help > Check for Updates** checks on demand. Builds without `-UpdateUrl`, and builds run from Visual Studio, never check.
- **Smaller updates**: put the previous release in `artifacts\releases` before packing (`dotnet vpk download github ...`), and Velopack also creates delta packages.
- **Signing**: unsigned installers make Windows SmartScreen warn on first run. Pass `-AzureTrustedSignFile` (Azure Trusted Signing) to sign `WritingIDE.exe` and `Setup.exe`.

## Importing (with an AI's help)

**File > Import** (Ctrl+Shift+I, or the Import button on the toolbar and Start Page) opens a two-step dialog:

1. **Ask an AI.** Pick what the AI should do:
   - **Convert a story bible we made in the chat.** For a world bible written earlier in an AI conversation. Characters, places, factions, events and **lore** (magic systems, ranks, glossary terms) become story elements; premise, tone, themes and the plot plan go into the Story/Series Bible; planned chapters become **Outline** chapters, with any drafted key scenes copied into them.
   - **Convert a manuscript the AI wrote in the chat.** Every chapter, prologue and epilogue word for word, plus the characters and places in it.
   - **Convert my own draft or notes**, or **Develop a story idea**.
   - **Combine a whole series.** For several manuscripts and story bibles that belong to one series. Add the files (.txt, .md or Word .docx) or leave them in your AI chat. The AI merges every bible into one Series Bible, writes one entry per character, place and thing across all the sources (variant spellings become aliases, disagreements are kept as `Conflict:` notes, and each element gets a `First appears` book), and turns each manuscript into a book with its chapters word for word. Books that only exist as plans become Outline chapters, and what a bible says about one book goes into the Series Bible under that book. The import converts a novel project into a series.

   Tick what to include and, optionally, paste the material into the dialog instead of leaving it in the chat. WritingIDE builds a prompt that explains the import format, gives an example, and lists the books and story elements already in your project so the AI reuses their exact names. Send it in the same AI chat as the bible or manuscript (ChatGPT, Claude, Gemini or any other).
   Long bibles and manuscripts don't fit in one AI reply, so the prompt tells the AI to stop at a block boundary with `=== CONTINUE ===`. Import each part, then reply "continue" in the chat; importing again merges into what is already there.
   With an AI provider set up (AI > AI Settings), **Send to AI** sends the prompt from the dialog instead, answers "continue" for you until the reply is complete, and pastes the joined parts on the next tab. It needs the material in the prompt (added files or pasted text), because the provider has not seen your chat.
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

## Autosave and crash recovery

- **Crash recovery is always on.** While there are unsaved changes, WritingIDE snapshots the whole project, including every chapter's current text, every 30 seconds. It also takes a snapshot on an unexpected error and when Windows shuts down. Snapshots live in `%LocalAppData%\WritingIDE\Recovery`, never in your project folder. The next time you open the project, WritingIDE offers to recover the unsaved work, and you review it and Save to keep it. Saving, or choosing not to save on close, deletes the snapshot.
- **Autosave to disk** is optional: **Tools > Autosave to Disk** saves the project every 1, 2, 5 or 10 minutes.

## Find and Replace in Files

**Edit > Find in Files** (Ctrl+Shift+F) and **Replace in Files** (Ctrl+Shift+H) search the entire project, the current book or the current chapter. Options are Match case, Match whole word and regular expressions (`$1` works in replacements). Find All lists every match in Find Results, and double-clicking a row jumps to it. Replace All asks for confirmation first. In open chapters it can be undone with Ctrl+Z; closed chapters change when you save.

## Security notes

- A project file can only point its chapters at `.txt` or `.md` files inside the project folder (not in hidden folders such as `.git`). A shared or downloaded project that points elsewhere opens with a warning, and those chapters get new files inside the project when you save, so a project can never read, overwrite or delete files outside its folder.
- WritingIDE runs Git with `core.fsmonitor` turned off and starts `git.exe` by its full path, so a repository's own settings cannot run a program when WritingIDE checks its status. Git hooks still run when you commit, as they would on the command line.
- Extensions are ordinary .NET code with your rights: install only ones you trust. Only DLLs that reference the WritingIDE SDK are loaded as extensions.

## Git

The **Git Changes** tool window (bottom panel, or click the branch in the status bar) works like Visual Studio's:
- **Create Git Repository** for a project that isn't in one yet.
- A commit box with **Commit All** and **Commit All and Push**. Committing saves first, so the commit matches what you see.
- **Pull** (fast-forward only), **Push** (sets the upstream on the first push) and **Add remote** to connect GitHub or another host.
- A list of changed files shown by chapter name. Double-click one for a **word diff** against the last commit (`[-old-]{+new+}`, coloured), which is far more readable for prose than line diffs.
- The recent history. Double-click a commit (or right-click, **View Changes**) to open it in a tab: its message, the files it changed (named by chapter, with words added and removed), and a word diff of each, so you can read exactly what a commit did to the manuscript.

It uses the Git for Windows command line, and Git Credential Manager handles sign-in for push and pull.

## Writing tools

- **Distraction-free writing** (F11, View menu): full screen with only the chapter, in a centred column of larger text. The line you are typing stays in the middle of the screen (typewriter scrolling), and a faint word count sits in the corner. Esc or F11 returns.
- **Comments and margin notes**: select text and press Ctrl+Alt+M (or right-click > Add Comment). The commented text is shaded, and the comment card sits in a margin level with its line, where you edit it in place. Cards can be resolved (✓) or deleted. Comments follow their text as you edit, and are found again if the chapter changed while it was closed. **Edit > List All Comments** puts every open comment in Find Results. They are saved in the project file.
- **Snapshots (version history)**: **Take snapshot** in a chapter's Properties (Ctrl+Alt+S) saves the current text, with an optional name, under `Snapshots/` in the project folder. Each snapshot can be **compared** with the current text (a word diff that shows only changed paragraphs) or **restored**. Restoring snapshots the current text first, and Ctrl+Z undoes it.
- **Character and POV Statistics** (Analyze menu): for each character, mentions, chapters, POV chapters, dialogue lines and words spoken, and first and last appearance. Also a presence heat map (character × chapter) and each POV's share of the manuscript. A chapter's point of view is set in its Properties, or detected: first person when the narration is full of "I/me/my", otherwise the character named most outside dialogue. Speech is attributed to the name next to the quotation.
- **Timeline** (Analyze menu): chapters in reading order as cards, each with a story date edited in place, POV, words and synopsis. Events sit under the chapter that first mentions them (or the one in their `Chapter` attribute), and each character has a lane of dots wherever they appear. Dates such as "Day 3", "Year 2, Day 14", "12 May 1204" or "1204" are ordered, and a chapter set earlier than the one before it is flagged as a possible flashback.
- **Relationship Map** (Analyze menu): characters (and optionally factions, places and others) as nodes. Relationships you add on an element's page ("sister of", "rival of") are drawn as solid labelled lines. Elements that share paragraphs are joined by dashed lines; set how many shared paragraphs it takes. Drag nodes to arrange them (saved with the project), hover to highlight a node's links, double-click to open.

## Structure and revision

- **Scenes**: a line containing just `* * *`, `***`, `#`, `~~~` or `---` is a scene break. Insert one with Ctrl+Shift+Enter (Edit > Insert Scene Break). `*** At the gate` gives the scene a label. Scene breaks are dimmed in the editor, listed in the Outline with each scene's words and detected POV, and exported as `#` (manuscript format) or `* * *` (book).
- **Split and merge chapters**: Project > Split Chapter at Caret moves the text from the caret on into a new chapter right after (scene breaks at the cut are dropped). Merge with Next Chapter (Project menu, or right-click a chapter in Solution Explorer) appends the next chapter after a scene break. Comments, bookmarks and plot markers move with their text.
- **Revision mode (track changes)**: under Revision mode in a chapter's Properties, choose "Start tracking changes", or press the track-changes button on a snapshot to compare against it. From then on, inserted text is shaded green and deleted text stays visible, struck through in red, without being part of the chapter. Right-click a change to accept it (the change is kept and stops being shown) or reject it (the old text comes back; Ctrl+Z works). Accept all, Reject all and Next are in Properties. The baseline is kept in `Revisions/` in the project folder, so tracking survives restarts.
- **Plot threads and foreshadowing**: Analyze > Plot Threads lists every thread (a promise to the reader, such as "The compass points to where Tobias died"), with its status (open, resolved, abandoned). Select a passage in a chapter and right-click > Mark for Plot Thread > *thread* > Setup / Development / Payoff, or start a new thread from there. Marked passages get a purple dashed underline and a card in the margin. The tracker lists each thread's passages in reading order and warns about setups never paid off, payoffs with nothing set up, payoffs before their setup, threads marked resolved without a payoff, and abandoned threads with leftover passages.

## Writing habit and progress

- **Daily goal**: the status bar shows today's words against your goal ("Today 312 / 500") with a small bar; click it for the Writing Progress page. WritingIDE logs the words you add each day while the project is open. Cutting counts against the day, and importing or deleting a chapter does not count. The log is saved in the project file. Reaching the goal shows a notification, with your streak.
- **Writing Progress** (Analyze menu): today, your streak (days in a row that met the goal) and best streak, the manuscript against its target, and the deadline. Set a deadline and it tells you how many words a day get you there, your average on the days you write, and when you will finish at your recent pace. Below is a heat map of the last 26 weeks, one square per day, darker for bigger days. Put the page in the second pane (Window > Split) to watch it while you write.
- **Writing sprints**: F9 (Analyze > Start or Stop Writing Sprint) starts a timed burst of writing, 25 minutes unless you pick another length on the Writing Progress page. The status bar shows the clock and the words written so far; when the time is up you get a sound, a notification and the result.

## Planning

- **Corkboard** (Analyze menu): every chapter as an index card with its synopsis, status, words and point of view, grouped by book in a series. Drag a card by its coloured top edge to reorder chapters or move one to another book; type the synopsis on the card; change the status from the card.
- **Beat Sheet** (Analyze menu): choose a structure (Three-Act Structure, Save the Cat, Hero's Journey, Story Circle or Seven-Point Structure), then place each beat in a chapter and note how your story hits it. Each beat has a track from 0% to 100%: a line where the structure expects the beat, a bar for the chapter you put it in. Beats that land more than 7.5 points early or late, or out of order, are pointed out. Positions are measured against the words written so far. The sheet is saved in the project file.
- **Thread Chart** (Analyze menu): plot threads down the side, chapters across, with a dot wherever a thread has a marked passage (setup, development or payoff). A thread that goes quiet for three chapters or more between two of its passages is shaded, so you can see a subplot being forgotten.

## Navigation

- **Outline** (the tab next to Properties, or View > Outline): the current chapter's scenes, bookmarks, TODOs, comments and plot-thread passages in order. Click one to go there.
- **Second pane** (Window > Split: Open in Second Pane, or right-click a chapter or story element in Solution Explorer > Open in Second Pane): shows a document beside the document well. A chapter opened there is a second editor on the same text, so you can read chapter 3 while writing chapter 12, or keep the same chapter at two places. Story element pages and the bible can go there too.
- **Bookmarks**: Ctrl+K, Ctrl+K toggles a bookmark on the current line (or click the bookmark gutter at the far left). Ctrl+K, Ctrl+N / Ctrl+P move to the next / previous one, and Ctrl+K, Ctrl+L clears the chapter's bookmarks. View > Bookmarks lists them all. They are saved with the project.
- **Task List**: `TODO`, `FIXME`, `XXX` and `TK` ("to come") in the text are highlighted, and View > Task List collects them from the whole manuscript, as in Visual Studio. Write `TODO: check the date` or `[TK]`.

## Export

File > Export (Ctrl+Shift+E) exports the whole manuscript, one book of a series, or the current chapter:

| Format | What you get |
|---|---|
| Word (.docx) | Standard manuscript format (Times New Roman 12 pt, double spaced, 1" margins, "Surname / TITLE / page" header, title page with contact details and word count, chapters on new pages a third of the way down, `#` between scenes, END), or a typeset book layout. Chapters use Heading 1, so Word's navigation pane lists them. |
| E-book (.epub) | EPUB 3 with a table of contents (and an EPUB 2 one for older readers), for Kindle (Send to Kindle), Apple Books, Kobo and other e-readers. |
| PDF | Manuscript format on US Letter, or a 6 x 9 in book with justified text and page numbers. |
| Google Docs | A .docx that converts cleanly (chapters become the Google Docs outline). WritingIDE then shows the file in Explorer and opens Google Drive: choose New > File upload (or drag the file in), then open it with Google Docs. |
| Markdown | `#` book and `##` chapter headings; Import reads it back. |
| For an AI chat | One Markdown file with the story bible, every story element (attributes and relationships), plot threads, chapter synopses, story dates and POV, and optionally the full text, plus your instructions at the top. It is also copied to the clipboard, ready to paste into any AI chat. |

The export is offered under a name that says what is in it: one chapter is `Series - Book - Chapter Name` (`Novel - Chapter Name` in a novel), one book of a series is `Series - Book`, and everything else the project's name.

`*italic*`, `_italic_`, `**bold**` and `***bold italic***` in the manuscript become real italics and bold in DOCX, EPUB and PDF. Choose the **font** in the Export dialog (any installed font or one added to WritingIDE). PDF and EPUB embed it, unless its licence forbids embedding, in which case you get a note and the layout's own font; Word only names it. Extensions can add more formats.

## Formatting and fonts

- **Bold and italic**: Ctrl+B and Ctrl+I (or the toolbar buttons, Edit > Format, or the right-click menu) format the selection, or the word at the caret. Chapters stay plain text: formatting is stored as `**bold**` and `*italic*`, which the editor draws as real bold and italic with faded markers, and which Git, the grammar checker and every export understand. Selections widen to whole words, several paragraphs are formatted one by one, and pressing the shortcut again removes the formatting. **Clear Formatting** removes both. Only the markers change, so comments and tracked changes on the words stay put, and one Ctrl+Z undoes it.
- **Editor font**: pick any font and size from the toolbar, or open **Tools > Fonts** for a preview. Distraction-free mode uses the same font a little larger.
- **Your own fonts**: in Tools > Fonts, **Add font files...** copies `.ttf`, `.otf` or `.ttc` files into WritingIDE (`%LocalAppData%WritingIDEonts`) without installing them in windows. they appear in every font picker, work in the editor, and are embedded in pdf and epub exports. **remove** deletes an added font again.

import moved to ctrl+shift+i so that ctrl+i can be italic.

## AI tools

The AI menu works with free AI services. Set one up in AI > AI Settings:

| Provider | Cost | Notes |
|---|---|---|
| Google Gemini | Free tier ([get a key](https://aistudio.google.com/apikey)) | Very large context: whole books fit. The default. |
| Groq | Free tier ([get a key](https://console.groq.com/keys)) | Very fast; smaller context, so work chapter by chapter. |
| OpenRouter | Free models ([get a key](https://openrouter.ai/keys)) | One key, many models; "List models" shows only the free (`:free`) ones. |
| Ollama | Free, on your PC ([download](https://ollama.com/download)) | Private: nothing leaves your computer. No key. Run `ollama pull llama3.2` first. |
| Other | | Any OpenAI-compatible server (LM Studio, llama.cpp). |

- **Ask About Your Book**: a chat that has read the story bible, the story elements and the manuscript (or synopses plus the current chapter). Ask things like "When did Rowan last see the lighthouse?" or "Which threads are still open?".
- **Summarize Chapter** writes the chapter's synopsis (you confirm before it replaces one).
- **Beta Read Chapter** and **Check Continuity with AI** put the AI's notes in the comment margin, attached to the passages they are about. Resolve or delete them as you go.
- **Rewrite** (select text, right-click > AI): Tighten, Make More Vivid, Simplify, Fix Grammar, or your own instruction. The suggestion opens beside the original; edit it, then Replace (Ctrl+Z undoes it).

API keys are stored encrypted for your Windows account (DPAPI), never in plain text or in the project. Before WritingIDE sends manuscript text to an online provider for the first time, it asks. Free tiers may use what you send to improve their models, so check each provider's terms, or use Ollama to keep everything local.

## Extensions (SDK) and local API

WritingIDE is open to other code in two ways. Both are managed in Tools > Extensions and API.

### Writing an extension

An extension is a .NET 10 class library that references `src/WritingIDE.Sdk` and implements `IWritingIdePlugin`:

```csharp
public sealed class MyExtension : IWritingIdePlugin {
    public string Id => "com.example.myextension";
    public string Name => "My Extension";
    public string Version => "1.0.0";
    public string Description => "What it does.";

    public void Initialize(IPluginHost host) {
        host.RegisterRule(new MyGrammarRule());              // runs on every chapter, findings in the Error List
        host.RegisterExporter(new MyExporter());             // appears in File > Export
        host.RegisterCommand(new PluginCommand("Do Something", project => { /* IProjectApi */ }));  // Extensions menu
        host.RegisterToolWindow(new PluginToolWindow("sprint", "Sprint Timer", () => new SprintPanel(host)));  // Extensions menu, opens as a tab
        host.RegisterEditorCommand(new EditorCommand("Look Up Word", context => { /* context.Word, context.SelectedText */ }) {
            IsAvailable = static context => context.Word.Length > 0  // the chapter's right-click menu
        });
        host.ProjectOpened += (_, _) => host.Log($"Opened {host.Project.Name}");
        host.ChapterTextChanged += (_, e) => { /* every edit to an open chapter: keep it quick */ };
    }
}
```

What the host offers (SDK 1.2):

| Member | Does |
|---|---|
| `RegisterRule`, `RegisterExporter`, `RegisterCommand` | A grammar rule, an export format, a command in the Extensions menu |
| `RegisterToolWindow`, `ShowToolWindow` | A tool window of your own. It is listed in the Extensions menu, opens as a tab and can go in the second pane. `CreateContent` returns a WPF element, so the extension targets `net10.0-windows` with `<UseWPF>true</UseWPF>`. Use `SetResourceReference` with WritingIDE's brush keys (such as `TextBrush`, see `Themes/Light.xaml`) to follow the theme |
| `RegisterEditorCommand` | A command in the chapter editor's right-click menu. It receives an `EditorContext`: the chapter, the clicked offset, the selection, the word and the story element under the cursor. `IsAvailable` decides where it is listed |
| `Editor` (`IEditorApi`) | The active chapter, caret and selection; `ReplaceSelection` (undoable) and `Select` (opens a chapter and reveals a range) |
| `Storage` (`IPluginStorage`) | `GetSetting`/`SetSetting`: per-user settings, kept with a `DataDirectory` for your own files in `%LocalAppData%\WritingIDE\ExtensionData\<id>`. `GetProjectValue`/`SetProjectValue`: values saved in the `.novproj`, so they travel with the project |
| `ProjectOpened`, `ProjectClosed`, `ProjectSaved` | Project events |
| `SprintFinished`, `DailyGoalReached` | A writing sprint ended (with its words and minutes); today's words reached the daily goal |
| `ActiveChapterChanged`, `ChapterTextChanged`, `SelectionChanged` | Editor events, raised on the UI thread. The last two fire on every keystroke or caret move, so do the real work on a timer |
| `Log`, `Notify`, `SetStatusText` | The Output window, a notification, the status bar |

`IProjectApi` reads and edits the open project: chapters (text edits go through the editor, so the writer can undo them), story elements (summary, notes, attributes and relationships), comments, the Error List, `OpenEntity`, `Reanalyze` (check the open chapters again after a rule's settings change), the genre, the writing statistics (`GetWritingStats`: total and today's words, goal and streaks), and saving. Grammar rules derive from `WritingIDE.Core.Grammar.GrammarRule`, like the built-in ones. Build the extension, copy its DLL (and any dependencies it brings; not the SDK or Core) to `%LocalAppData%\WritingIDE\Extensions\<YourExtension>\`, and restart WritingIDE. `samples/SampleExtension` is a complete example: a rule flagging "very", a word-count command, a plain-text exporter, a "Writing Session" tool window that counts the words you write against a goal, and a right-click command that counts the words in the selection. Extensions run with WritingIDE's rights, so install only ones you trust, and switch any of them off in Tools > Extensions and API.

### Plugins in their own repository

[WritingIDE-Plugins](https://github.com/MrSquirrely/WritingIDE-Plugins) holds four extensions built on this SDK that go beyond what the main program does itself: **Ambient Soundscapes** (looping background sound that follows the place a chapter is set in), **Map Maker** (a map with a pin for each place, travel times, and a continuity rule for journeys that do not fit the map), **Gamification** (experience, levels and achievements) and **Genre Rule Packs** (rules and manuscript checks for romance, mystery, fantasy, science fiction and thrillers). Check it out next to this repository and run its `build\install.ps1`.

### Local REST API

Turn it on in Tools > Extensions and API > Local API. It listens on `http://localhost:5719/api` (the port is configurable), on this computer only.  Request bodies are limited to 16 MB, and unexpected errors return a generic message (the details go to the Output window).

| Request | Does |
|---|---|
| `GET /api/project` | Name, author, chapter count, words |
| `GET /api/chapters` | Chapters in reading order (id, title, book, status, synopsis, POV, story date, words) |
| `POST /api/chapters` | Add a chapter: `{ "title": "...", "text": "...", "bookId": null }` |
| `GET /api/chapters/{id}` | A chapter and its text |
| `PUT /api/chapters/{id}/text` | Replace its text (plain text or `{ "text": "..." }`); undoable in an open editor |
| `GET` / `POST /api/chapters/{id}/comments` | Comments, bookmarks and plot markers / add a comment `{ "offset": 0, "length": 5, "text": "..." }` |
| `GET` / `POST /api/entities` | Story elements / add one `{ "kind": "Character", "name": "...", "summary": "..." }` |
| `GET /api/diagnostics` | The Error List |
| `POST /api/save` | Save the project |
| `GET /api/openapi.json` | OpenAPI 3 description (no token needed), for Postman, Swagger UI and code generators |

```powershell
$h = @{ Authorization = "Bearer <token>" }
Invoke-RestMethod http://localhost:5719/api/chapters -Headers $h
```

## Plugins: Harper

**Tools > Plugins** turns on [Harper](https://github.com/Automattic/harper), Automattic's open-source grammar checker. WritingIDE runs Harper's language server, `harper-ls`, on your computer (text never leaves it) and adds its findings to the Error List and squiggles under rule ID `HARPER`. Harper's quick fixes appear in Ctrl+. like the built-in ones.

- **Uninstall** stops the plugin and deletes WritingIDE's copy of harper-ls. Copies installed elsewhere (PATH, Cargo) are left alone.
- **Download and install latest** fetches the official Windows build from Harper's GitHub releases into `%LocalAppData%\WritingIDE\Plugins\Harper`, and installs it only if it matches the SHA-256 checksum GitHub publishes for it. Alternatively, point the plugin at your own `harper-ls.exe`; it is also found on `PATH` and in `~/.cargo/bin`.
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
| CodeLens | Name lenses: above the first mention of each name in a chapter, its reference count, chapter count and when it was last seen (or "first appearance"). Click a part to act on it. View > Name Lenses |
| Code review comments | Comments in the margin (Ctrl+Alt+M) |
| Local history | Chapter snapshots |
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
| NW4002 | A misspelled story element name ("Rowen" for "Rowan"), with the fix | Warning |
| NW4003 | Eye or hair colour next to a character's name that contradicts their description or attributes | Warning |
| NW4004 | An age next to a character's name ("Rowan, forty-one,") that contradicts their Age attribute | Warning |
| NW4005 | A possessive pronoun in a self-directed gesture ("Wren tossed his hair") that contradicts their Pronouns attribute | Warning |
| NW4006 | A character who acts or speaks after the chapter named in their Died attribute (`Died: Chapter 12`); quoted dialogue and memories are ignored | Warning |
| NW4007 | An item called someone else's ("Wren's compass") when its Owner attribute names another character | Warning |
| NW4008 | An age that does not fit the character's Born year and the chapter's story date (born 1180, "forty" in Spring 1204) | Warning |
| NW4009 | A relationship in the text ("Kael's sister Wren") that contradicts the relationships defined on the characters' pages (Wren: daughter of Kael) | Warning |
| HARPER | Findings from the Harper plugin, when enabled (spelling counts as an error) | Warning |

To add a rule, derive from `GrammarRule` (or `PatternRule` for phrase tables) and add it to `GrammarEngine.Rules`.

## Layout

```
src/WritingIDE.Core     Model, persistence, import, grammar engine, analysis, exporters (DOCX, EPUB, PDF), AI clients and prompts (no UI dependencies)
src/WritingIDE.Sdk      The extension SDK: IWritingIdePlugin, IPluginHost, IProjectApi
src/WritingIDE          WPF shell (HandyControls): AvalonEdit-based editor, IntelliSense, squiggles, tool windows, dialogs, Harper plugin (LSP client), extension loader, local REST API
samples/                SampleExtension: a complete extension built on the SDK
tests/                  xUnit tests for the grammar engine, analysis, import, export, AI clients and persistence
```

A project is a folder that holds `Name.novproj` (JSON: metadata, story elements, dictionary, disabled rules) and `Manuscript/*.txt` (one plain-text file per chapter).
