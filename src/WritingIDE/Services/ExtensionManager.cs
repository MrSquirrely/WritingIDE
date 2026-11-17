using System.Reflection;
using System.Runtime.Loader;
using System.Windows;

using WritingIDE.Core.Grammar;
using WritingIDE.Sdk;
using WritingIDE.ViewModels;

namespace WritingIDE.Services;

/// <summary>An installed extension and what happened when it loaded.</summary>
public sealed class ExtensionInfo(string folder, string assemblyPath) {
    public string Folder { get; } = folder;

    public string AssemblyPath { get; } = assemblyPath;

    public IWritingIdePlugin? Plugin { get; set; }

    public string Id => Plugin?.Id ?? Path.GetFileNameWithoutExtension(AssemblyPath);

    public string Name => Plugin?.Name ?? Path.GetFileName(Folder);

    public string Version => Plugin?.Version ?? "";

    public string Description => Plugin?.Description ?? "";

    public bool IsEnabled { get; set; } = true;

    public string Status { get; set; } = "";

    public List<GrammarRule> Rules { get; } = [];

    public List<IManuscriptExporter> Exporters { get; } = [];

    public List<PluginCommand> Commands { get; } = [];

    public List<PluginToolWindow> ToolWindows { get; } = [];

    public List<EditorCommand> EditorCommands { get; } = [];
}

/// <summary>
/// Loads extensions from %LocalAppData%\WritingIDE\Extensions\&lt;Name&gt;\*.dll. Each extension gets its own load
/// context, but shares WritingIDE's copy of the SDK and Core, so its types are the ones WritingIDE knows.
/// Extensions run with the same rights as WritingIDE: install only ones you trust.
/// </summary>
public sealed class ExtensionManager(MainViewModel shell) {
    public static readonly Version SdkVersion = new(1, 2, 0);

    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WritingIDE", "Extensions");

    public List<ExtensionInfo> Installed { get; } = [];

    public ProjectApi ProjectApi { get; } = new(shell);

    public IEnumerable<GrammarRule> Rules => Installed.Where(static extension => extension.IsEnabled).SelectMany(static extension => extension.Rules);

    public IEnumerable<IManuscriptExporter> Exporters => Installed.Where(static extension => extension.IsEnabled).SelectMany(static extension => extension.Exporters);

    public IEnumerable<PluginCommand> Commands => Installed.Where(static extension => extension.IsEnabled).SelectMany(static extension => extension.Commands);

    public IEnumerable<(ExtensionInfo Extension, PluginToolWindow Window)> ToolWindows => Installed.Where(static extension => extension.IsEnabled)
        .SelectMany(static extension => extension.ToolWindows.Select(window => (extension, window)));

    public IEnumerable<EditorCommand> EditorCommands => Installed.Where(static extension => extension.IsEnabled).SelectMany(static extension => extension.EditorCommands);

    public EditorApi EditorApi { get; } = new(shell);

    private MainViewModel Shell => shell;

    private Guid? _activeChapterId;

    public event EventHandler? ProjectOpened;

    public event EventHandler? ProjectClosed;

    public event EventHandler? ProjectSaved;

    public event EventHandler<ChapterEventArgs>? ActiveChapterChanged;

    public event EventHandler<ChapterEventArgs>? ChapterTextChanged;

    public event EventHandler<EditorSelectionEventArgs>? SelectionChanged;

    public event EventHandler<SprintEventArgs>? SprintFinished;

    public event EventHandler? DailyGoalReached;

    public void RaiseSprintFinished(int words, double minutes, bool completed) {
        Raise(SprintFinished, new SprintEventArgs(words, minutes, completed));
    }

    public void RaiseDailyGoalReached() {
        Raise(DailyGoalReached);
    }

    /// <summary>Called whenever the active tab changes; extensions only hear about it when the chapter is a different one.</summary>
    public void RaiseActiveChapterChanged(Guid? chapterId) {
        if (chapterId == _activeChapterId) {
            return;
        }
        _activeChapterId = chapterId;
        Raise(ActiveChapterChanged, new ChapterEventArgs(chapterId));
    }

    public void RaiseChapterTextChanged(Guid chapterId) {
        if (ChapterTextChanged is not null) {
            Raise(ChapterTextChanged, new ChapterEventArgs(chapterId));
        }
    }

    public void RaiseSelectionChanged(Guid chapterId, int caretOffset, int selectionStart, int selectionLength) {
        if (SelectionChanged is not null) {
            Raise(SelectionChanged, new EditorSelectionEventArgs(chapterId, caretOffset, selectionStart, selectionLength));
        }
    }

    /// <summary>Whether an editor command wants to be listed here. A command whose check fails is left out.</summary>
    public bool IsAvailable(EditorCommand command, EditorContext context) {
        try {
            return command.IsAvailable?.Invoke(context) ?? true;
        }
        catch (Exception exception) {
            shell.Log($"warning: extension command '{command.Title}' failed: {exception.Message}");
            return false;
        }
    }

    /// <summary>Runs an extension's command; a failure is reported, not thrown.</summary>
    public void Run(string title, Action action) {
        try {
            action();
        }
        catch (Exception exception) {
            shell.Log($"warning: extension command '{title}' failed: {exception.Message}");
            MainViewModel.Notify($"'{title}' failed: {exception.Message}", MainViewModel.NotificationKind.Error);
        }
    }

    public void RaiseProjectOpened() {
        Raise(ProjectOpened);
    }

    public void RaiseProjectClosed() {
        Raise(ProjectClosed);
    }

    public void RaiseProjectSaved() {
        Raise(ProjectSaved);
    }

    public void LoadAll() {
        Installed.Clear();
        if (!Directory.Exists(Root)) {
            return;
        }
        foreach (string folder in Directory.EnumerateDirectories(Root)) {
            foreach (string dll in Directory.EnumerateFiles(folder, "*.dll")) {
                if (Path.GetFileName(dll).StartsWith("WritingIDE.", StringComparison.OrdinalIgnoreCase)) {
                    // A stray copy of the SDK or Core: WritingIDE's own is used.
                    continue;
                }
                // Only assemblies built against the SDK are extensions; the rest are their dependencies, which the
                // extension's load context finds when it needs them (loading them here would load them twice).
                if (ReferencesSdk(dll)) {
                    Load(folder, dll);
                }
            }
        }
    }

    private void Load(string folder, string dll) {
        try {
            ExtensionLoadContext context = new(dll);
            Assembly assembly = context.LoadFromAssemblyPath(dll);
            IEnumerable<Type> types;
            try {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception) {
                types = exception.Types.OfType<Type>();
            }
            foreach (Type type in types.Where(static type => typeof(IWritingIdePlugin).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })) {
                ExtensionInfo extension = new(folder, dll);
                Installed.Add(extension);
                try {
                    extension.Plugin = (IWritingIdePlugin)Activator.CreateInstance(type)!;
                    extension.IsEnabled = !shell.Settings.DisabledExtensions.Contains(extension.Id, StringComparer.OrdinalIgnoreCase);
                    if (!extension.IsEnabled) {
                        extension.Status = "Disabled";
                        continue;
                    }
                    extension.Plugin.Initialize(new Host(this, extension));
                    extension.Status = $"Loaded: {extension.Rules.Count} rule(s), {extension.Exporters.Count} exporter(s), {extension.Commands.Count + extension.EditorCommands.Count} command(s), {extension.ToolWindows.Count} tool window(s)";
                    shell.Log($"Extension '{extension.Name}' {extension.Version} loaded from {folder}.");
                }
                catch (Exception exception) {
                    extension.IsEnabled = false;
                    extension.Status = $"Failed to start: {exception.Message}";
                    shell.Log($"warning: extension in {folder} failed to start: {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or FileNotFoundException) {
            shell.Log($"warning: {Path.GetFileName(dll)} in {folder} is not a WritingIDE extension ({exception.Message}).");
        }
    }

    /// <summary>Reads the assembly's references without loading it: true when it references WritingIDE.Sdk.</summary>
    private static bool ReferencesSdk(string dll) {
        try {
            using FileStream stream = File.OpenRead(dll);
            using System.Reflection.PortableExecutable.PEReader reader = new(stream);
            if (!reader.HasMetadata) {
                return false;
            }
            System.Reflection.Metadata.MetadataReader metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(reader);
            return metadata.AssemblyReferences.Any(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name).Equals("WritingIDE.Sdk", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException) {
            return false;
        }
    }

    /// <summary>Turns an extension on or off (takes effect after a restart; rules switch immediately).</summary>
    public void SetEnabled(ExtensionInfo extension, bool enabled) {
        shell.Settings.DisabledExtensions.RemoveAll(id => string.Equals(id, extension.Id, StringComparison.OrdinalIgnoreCase));
        if (!enabled) {
            shell.Settings.DisabledExtensions.Add(extension.Id);
        }
        shell.Settings.Save();
        extension.IsEnabled = enabled && (extension.Plugin is not null) && extension.Status.StartsWith("Loaded", StringComparison.Ordinal);
        extension.Status = enabled ? (extension.IsEnabled ? extension.Status : "Enabled: restart WritingIDE to load it") : "Disabled";
        shell.ApplyPluginRules();
    }

    private void Raise(EventHandler? handler) {
        if (handler is null) {
            return;
        }
        foreach (EventHandler callback in handler.GetInvocationList().Cast<EventHandler>()) {
            try {
                callback(this, EventArgs.Empty);
            }
            catch (Exception exception) {
                shell.Log($"warning: an extension's event handler failed: {exception.Message}");
            }
        }
    }

    private void Raise<T>(EventHandler<T>? handler, T e) {
        if (handler is null) {
            return;
        }
        foreach (EventHandler<T> callback in handler.GetInvocationList().Cast<EventHandler<T>>()) {
            try {
                callback(this, e);
            }
            catch (Exception exception) {
                shell.Log($"warning: an extension's event handler failed: {exception.Message}");
            }
        }
    }

    private sealed class Host(ExtensionManager manager, ExtensionInfo extension) : IPluginHost {
        public Version SdkVersion => ExtensionManager.SdkVersion;

        public IProjectApi Project => manager.ProjectApi;

        public IEditorApi Editor => manager.EditorApi;

        public IPluginStorage Storage => field ??= new PluginStorage(manager.Shell, extension.Id);

        public void RegisterRule(GrammarRule rule) {
            extension.Rules.Add(new ExtensionRule(rule));
        }

        public void RegisterExporter(IManuscriptExporter exporter) {
            extension.Exporters.Add(exporter);
        }

        public void RegisterCommand(PluginCommand command) {
            extension.Commands.Add(command);
        }

        public void RegisterToolWindow(PluginToolWindow window) {
            ArgumentException.ThrowIfNullOrEmpty(window.Id);
            if (extension.ToolWindows.Any(existing => string.Equals(existing.Id, window.Id, StringComparison.OrdinalIgnoreCase))) {
                throw new ArgumentException($"A tool window with the id '{window.Id}' is already registered.");
            }
            extension.ToolWindows.Add(window);
        }

        public void ShowToolWindow(string id) {
            PluginToolWindow window = extension.ToolWindows.FirstOrDefault(window => string.Equals(window.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"No tool window with the id '{id}' is registered.");
            Application.Current.Dispatcher.BeginInvoke(() => manager.Shell.OpenPluginToolWindow(extension, window));
        }

        public void RegisterEditorCommand(EditorCommand command) {
            extension.EditorCommands.Add(command);
        }

        public void SetStatusText(string text) {
            Application.Current.Dispatcher.BeginInvoke(() => manager.Shell.StatusText = text);
        }

        public void Log(string message) {
            Application.Current.Dispatcher.BeginInvoke(() => manager.Shell.Log($"[{extension.Name}] {message}"));
        }

        public void Notify(string message) {
            Application.Current.Dispatcher.BeginInvoke(() => MainViewModel.Notify($"{extension.Name}: {message}"));
        }

        public event EventHandler? ProjectOpened {
            add => manager.ProjectOpened += value;
            remove => manager.ProjectOpened -= value;
        }

        public event EventHandler? ProjectClosed {
            add => manager.ProjectClosed += value;
            remove => manager.ProjectClosed -= value;
        }

        public event EventHandler? ProjectSaved {
            add => manager.ProjectSaved += value;
            remove => manager.ProjectSaved -= value;
        }

        public event EventHandler<ChapterEventArgs>? ActiveChapterChanged {
            add => manager.ActiveChapterChanged += value;
            remove => manager.ActiveChapterChanged -= value;
        }

        public event EventHandler<ChapterEventArgs>? ChapterTextChanged {
            add => manager.ChapterTextChanged += value;
            remove => manager.ChapterTextChanged -= value;
        }

        public event EventHandler<EditorSelectionEventArgs>? SelectionChanged {
            add => manager.SelectionChanged += value;
            remove => manager.SelectionChanged -= value;
        }

        public event EventHandler<SprintEventArgs>? SprintFinished {
            add => manager.SprintFinished += value;
            remove => manager.SprintFinished -= value;
        }

        public event EventHandler? DailyGoalReached {
            add => manager.DailyGoalReached += value;
            remove => manager.DailyGoalReached -= value;
        }
    }

    /// <summary>Wraps an extension's rule so the Error List and Options show it as a plugin rule.</summary>
    private sealed class ExtensionRule(GrammarRule inner) : GrammarRule(inner.Id, inner.Title, inner.Category, inner.Severity, inner.Description) {
        public override bool IsPlugin => true;

        public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
            return inner.Analyze(context);
        }
    }

    /// <summary>Loads an extension and its private dependencies; WritingIDE's own assemblies (SDK, Core, WPF) are shared.</summary>
    private sealed class ExtensionLoadContext(string mainAssemblyPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible: false) {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName) {
            if ((assemblyName.Name?.StartsWith("WritingIDE.", StringComparison.OrdinalIgnoreCase) ?? false)
                || Default.Assemblies.Any(assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName))) {
                return null;
            }
            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}