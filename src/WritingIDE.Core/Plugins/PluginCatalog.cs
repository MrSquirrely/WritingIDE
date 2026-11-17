using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WritingIDE.Core.Plugins;

/// <summary>A plugin in the plugin list: what it is and where to get it.</summary>
/// <param name="Id">The plugin's own id (<c>IWritingIdePlugin.Id</c>), such as "com.example.mapmaker".</param>
/// <param name="Sdk">The SDK version the plugin needs.</param>
/// <param name="Download">An https link to a zip with the plugin's files.</param>
/// <param name="Sha256">The zip's SHA-256, in hex. The download is refused when it does not match.</param>
/// <param name="Homepage">The plugin's source or home page; may be empty.</param>
/// <param name="Discussion">The GitHub discussion that lists the plugin; may be empty.</param>
public sealed record PluginListing(string Id, string Name, string Description, string Author, Version Version, Version Sdk,
    string Download, string Sha256, string Homepage, string Discussion);

/// <summary>
/// The plugin list. Plugins are listed as GitHub discussions on the WritingIDE repository; a workflow there turns the
/// approved ones into one JSON file, which is what WritingIDE reads.
/// </summary>
public static partial class PluginCatalog {
    /// <summary>The index built from the plugin discussions.</summary>
    public const string IndexUrl = "https://raw.githubusercontent.com/MrSquirrely/WritingIDE/plugin-index/plugins.json";

    /// <summary>Where plugins are listed and discussed.</summary>
    public const string DiscussionsUrl = "https://github.com/MrSquirrely/WritingIDE/discussions";

    /// <summary>The largest index WritingIDE will read.</summary>
    public const int MaxIndexBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Reads the index. Entries that are incomplete or unsafe (no https download, no checksum, an id that is not a
    /// plain name) are left out, and of two entries with the same id the newer version wins.
    /// </summary>
    public static IReadOnlyList<PluginListing> Parse(string json) {
        List<PluginListing> listings = [];
        try {
            using JsonDocument document = JsonDocument.Parse(json);
            if ((document.RootElement.ValueKind != JsonValueKind.Object) || !document.RootElement.TryGetProperty("plugins", out JsonElement plugins) || (plugins.ValueKind != JsonValueKind.Array)) {
                return listings;
            }
            foreach (JsonElement entry in plugins.EnumerateArray()) {
                if (TryRead(entry) is PluginListing listing) {
                    listings.Add(listing);
                }
            }
        }
        catch (JsonException) {
            return [];
        }
        return listings
            .GroupBy(static listing => listing.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(static listing => listing.Version).First())
            .OrderBy(static listing => listing.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PluginListing? TryRead(JsonElement entry) {
        if (entry.ValueKind != JsonValueKind.Object) {
            return null;
        }
        string Text(string name, int limit) {
            string value = entry.TryGetProperty(name, out JsonElement property) && (property.ValueKind == JsonValueKind.String) ? property.GetString()!.Trim() : "";
            return value.Length > limit ? value[..limit] : value;
        }
        string id = Text("id", 100);
        string name = Text("name", 80);
        string download = Text("download", 500);
        string sha256 = Text("sha256", 64).ToLowerInvariant();
        if (!IdPattern().IsMatch(id) || (name.Length == 0) || !IsHttps(download) || !Sha256Pattern().IsMatch(sha256)
            || !Version.TryParse(Normalize(Text("version", 30)), out Version? version)) {
            return null;
        }
        Version sdk = Version.TryParse(Normalize(Text("sdk", 30)), out Version? parsed) ? parsed : new Version(1, 0);
        string homepage = Text("homepage", 500);
        string discussion = Text("discussion", 500);
        return new PluginListing(id, name, Text("description", 600), Text("author", 80), version, sdk, download, sha256,
            IsHttps(homepage) ? homepage : "", IsHttps(discussion) ? discussion : "");
    }

    /// <summary>"1" and "v1.2" as versions <see cref="Version"/> can read.</summary>
    private static string Normalize(string version) {
        version = version.TrimStart('v', 'V');
        return version.Contains('.') ? version : version + ".0";
    }

    public static bool IsHttps(string url) {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps) && (uri.Host.Length > 0);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{2,99}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();
}

/// <summary>What the plugin list shows for a plugin.</summary>
public enum PluginState {
    NotInstalled,
    Installed,
    UpdateAvailable,

    /// <summary>Downloaded; it is put in place the next time WritingIDE starts.</summary>
    InstallPending,

    /// <summary>It is removed the next time WritingIDE starts.</summary>
    RemovalPending,

    /// <summary>It needs a newer WritingIDE than this one.</summary>
    NeedsNewerApp
}

/// <summary>
/// Installs plugins from the plugin list into the Extensions folder. WritingIDE loads extensions when it starts and
/// keeps their files open, so installs, updates and removals are staged and carried out at the next start, before
/// anything is loaded.
/// </summary>
public static class PluginInstaller {
    /// <summary>The largest plugin download WritingIDE accepts.</summary>
    public const int MaxDownloadBytes = 64 * 1024 * 1024;

    private const long MaxExtractedBytes = 256L * 1024 * 1024;
    private const int MaxEntries = 2000;
    private const string StagedFolder = ".staged";
    private const string RemoveFolder = ".remove";

    /// <summary>The folder a plugin is installed in: its id as one folder name.</summary>
    public static string FolderName(string id) {
        char[] invalid = Path.GetInvalidFileNameChars();
        string name = new string(id.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim(' ', '.');
        return name.Length == 0 ? "_" : name;
    }

    /// <summary>Downloads a plugin's zip and checks it against the listing's checksum.</summary>
    /// <exception cref="InvalidDataException">The download is too large or is not the file the listing describes.</exception>
    public static async Task<byte[]> DownloadAsync(HttpClient http, PluginListing listing, CancellationToken cancellationToken = default) {
        if (!PluginCatalog.IsHttps(listing.Download)) {
            throw new InvalidDataException("The plugin's download link is not an https address.");
        }
        using HttpResponseMessage response = await http.GetAsync(listing.Download, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxDownloadBytes) {
            throw new InvalidDataException("The plugin is larger than WritingIDE accepts (64 MB).");
        }
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0) {
            if (buffer.Length + read > MaxDownloadBytes) {
                throw new InvalidDataException("The plugin is larger than WritingIDE accepts (64 MB).");
            }
            buffer.Write(chunk, 0, read);
        }
        byte[] data = buffer.ToArray();
        Verify(listing, data);
        return data;
    }

    /// <exception cref="InvalidDataException">The data's SHA-256 is not the one in the listing.</exception>
    public static void Verify(PluginListing listing, byte[] data) {
        string actual = Convert.ToHexStringLower(SHA256.HashData(data));
        if (!actual.Equals(listing.Sha256, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException("The download does not match the checksum in the plugin list, so it was not installed.");
        }
    }

    /// <summary>
    /// Unpacks a verified zip into the staging area. <see cref="ApplyPending"/> moves it into place at the next start.
    /// </summary>
    /// <exception cref="InvalidDataException">The zip is not a plugin: no DLL, a path that leaves the folder, or too much data.</exception>
    public static void Stage(string extensionsRoot, PluginListing listing, byte[] zip) {
        string folder = FolderName(listing.Id);
        string staged = Path.Combine(extensionsRoot, StagedFolder, folder);
        if (Directory.Exists(staged)) {
            Directory.Delete(staged, recursive: true);
        }
        Directory.CreateDirectory(staged);
        try {
            string root = Path.GetFullPath(staged) + Path.DirectorySeparatorChar;
            using ZipArchive archive = new(new MemoryStream(zip), ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxEntries) {
                throw new InvalidDataException("The plugin's zip has too many files.");
            }
            long total = 0;
            bool hasDll = false;
            foreach (ZipArchiveEntry entry in archive.Entries) {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) {
                    continue;
                }
                string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidDataException("The plugin's zip contains a file path that leaves its folder.");
                }
                total += entry.Length;
                if (total > MaxExtractedBytes) {
                    throw new InvalidDataException("The plugin's zip unpacks to more than WritingIDE accepts (256 MB).");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using Stream source = entry.Open();
                using FileStream destination = File.Create(target);
                // Copy with a limit of its own: a zip can lie about its entries' sizes.
                CopyLimited(source, destination, MaxExtractedBytes);
                hasDll |= target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            }
            if (!hasDll) {
                throw new InvalidDataException("The plugin's zip contains no DLL.");
            }
            // A plugin that was going to be removed is being installed again.
            CancelRemoval(extensionsRoot, folder);
        }
        catch {
            Directory.Delete(staged, recursive: true);
            throw;
        }
    }

    private static void CopyLimited(Stream source, Stream destination, long limit) {
        byte[] chunk = new byte[81920];
        long copied = 0;
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0) {
            copied += read;
            if (copied > limit) {
                throw new InvalidDataException("The plugin's zip unpacks to more than WritingIDE accepts (256 MB).");
            }
            destination.Write(chunk, 0, read);
        }
    }

    /// <summary>Marks an installed plugin's folder to be deleted at the next start, and drops any staged update for it.</summary>
    public static void MarkForRemoval(string extensionsRoot, string folderName) {
        folderName = Path.GetFileName(folderName);
        string staged = Path.Combine(extensionsRoot, StagedFolder, folderName);
        if (Directory.Exists(staged)) {
            Directory.Delete(staged, recursive: true);
        }
        Directory.CreateDirectory(Path.Combine(extensionsRoot, RemoveFolder));
        File.WriteAllText(Path.Combine(extensionsRoot, RemoveFolder, folderName), "");
    }

    /// <summary>Takes back <see cref="MarkForRemoval"/>.</summary>
    public static void CancelRemoval(string extensionsRoot, string folderName) {
        string marker = Path.Combine(extensionsRoot, RemoveFolder, Path.GetFileName(folderName));
        // File.Delete throws when the folder itself is missing.
        if (File.Exists(marker)) {
            File.Delete(marker);
        }
    }

    public static bool IsStaged(string extensionsRoot, string id) {
        return Directory.Exists(Path.Combine(extensionsRoot, StagedFolder, FolderName(id)));
    }

    public static bool IsMarkedForRemoval(string extensionsRoot, string folderName) {
        return File.Exists(Path.Combine(extensionsRoot, RemoveFolder, Path.GetFileName(folderName)));
    }

    /// <summary>
    /// Carries out staged removals and installs. Call it before any extension is loaded. Returns a line for the Output
    /// window for each thing it did or could not do.
    /// </summary>
    public static IReadOnlyList<string> ApplyPending(string extensionsRoot) {
        List<string> log = [];
        string removals = Path.Combine(extensionsRoot, RemoveFolder);
        if (Directory.Exists(removals)) {
            foreach (string marker in Directory.EnumerateFiles(removals).ToList()) {
                string name = Path.GetFileName(marker);
                try {
                    string target = Path.Combine(extensionsRoot, name);
                    if (Directory.Exists(target)) {
                        Directory.Delete(target, recursive: true);
                        log.Add($"Removed the plugin in '{name}'.");
                    }
                    File.Delete(marker);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                    log.Add($"warning: the plugin in '{name}' could not be removed: {exception.Message}");
                }
            }
        }
        string staged = Path.Combine(extensionsRoot, StagedFolder);
        if (Directory.Exists(staged)) {
            foreach (string source in Directory.EnumerateDirectories(staged).ToList()) {
                string name = Path.GetFileName(source);
                try {
                    string target = Path.Combine(extensionsRoot, name);
                    if (Directory.Exists(target)) {
                        Directory.Delete(target, recursive: true);
                    }
                    Directory.Move(source, target);
                    log.Add($"Installed the plugin in '{name}'.");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                    log.Add($"warning: the plugin in '{name}' could not be installed: {exception.Message}");
                }
            }
        }
        return log;
    }

    /// <summary>What the plugin list should say about a listing.</summary>
    /// <param name="installed">The installed plugin's version, or null when it is not installed.</param>
    public static PluginState GetState(PluginListing listing, Version? installed, Version appSdk, bool staged, bool markedForRemoval) {
        if (staged) {
            return PluginState.InstallPending;
        }
        if (markedForRemoval) {
            return PluginState.RemovalPending;
        }
        if (installed is not null) {
            return listing.Version > installed ? (listing.Sdk > appSdk ? PluginState.Installed : PluginState.UpdateAvailable) : PluginState.Installed;
        }
        return listing.Sdk > appSdk ? PluginState.NeedsNewerApp : PluginState.NotInstalled;
    }
}