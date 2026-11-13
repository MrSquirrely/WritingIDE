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
}

/// <summary>
/// Loads extensions from %LocalAppData%\WritingIDE\Extensions\&lt;Name&gt;\*.dll. Each extension gets its own load
/// context, but shares WritingIDE's copy of the SDK and Core, so its types are the ones WritingIDE knows.
/// Extensions run with the same rights as WritingIDE: install only ones you trust.
/// </summary>
public sealed class ExtensionManager(MainViewModel shell) {
    public static readonly Version SdkVersion = new(1, 0, 0);

    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WritingIDE", "Extensions");

    public List<ExtensionInfo> Installed { get; } = [];

    public ProjectApi ProjectApi { get; } = new(shell);

    public IEnumerable<GrammarRule> Rules => Installed.Where(static extension => extension.IsEnabled).SelectMany(static extension => extension.Rules);

    public IEnumerable<IManuscriptExporter> Exporters => Installed.Where(static extension => extension.IsEnabled).SelectMany(static extension => extension.Exporters);

    public IEnumerable<PluginCommand> Commands => Installed.Where(static extension => extension.IsEnabled).SelectMany(static extension => extension.Commands);

    public event EventHandler? ProjectOpened;

    public event EventHandler? ProjectClosed;

    public event EventHandler? ProjectSaved;

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
                Load(folder, dll);
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
                    extension.Status = $"Loaded: {extension.Rules.Count} rule(s), {extension.Exporters.Count} exporter(s), {extension.Commands.Count} command(s)";
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

    private sealed class Host(ExtensionManager manager, ExtensionInfo extension) : IPluginHost {
        public Version SdkVersion => ExtensionManager.SdkVersion;

        public IProjectApi Project => manager.ProjectApi;

        public void RegisterRule(GrammarRule rule) {
            extension.Rules.Add(new ExtensionRule(rule));
        }

        public void RegisterExporter(IManuscriptExporter exporter) {
            extension.Exporters.Add(exporter);
        }

        public void RegisterCommand(PluginCommand command) {
            extension.Commands.Add(command);
        }

        public void Log(string message) {
            Application.Current.Dispatcher.BeginInvoke(() => manager.ShellLog($"[{extension.Name}] {message}"));
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
    }

    private void ShellLog(string message) {
        shell.Log(message);
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