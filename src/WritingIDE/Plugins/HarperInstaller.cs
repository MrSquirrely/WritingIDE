using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace WritingIDE.Plugins;

/// <summary>Downloads the official harper-ls Windows build from Harper's GitHub releases into WritingIDE's plugin folder, checking it against the SHA-256 checksum GitHub publishes.</summary>
public static class HarperInstaller {
    private const string LatestReleaseApi = "https://api.github.com/repos/Automattic/harper/releases/latest";

    public sealed record InstallResult(string ExecutablePath, string Version);

    /// <summary>True when WritingIDE's own plugin folder holds a harper-ls installation.</summary>
    public static bool IsInstalled => Directory.Exists(HarperPlugin.InstallDirectory)
        && Directory.EnumerateFiles(HarperPlugin.InstallDirectory, "harper-ls.exe", SearchOption.AllDirectories).Any();

    /// <summary>The installed release tag, if known.</summary>
    public static string? InstalledVersion {
        get {
            string file = Path.Combine(HarperPlugin.InstallDirectory, "VERSION.txt");
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
    }

    /// <summary>
    /// Deletes WritingIDE's copy of harper-ls (%LocalAppData%\WritingIDE\Plugins\Harper). Copies installed elsewhere
    /// (PATH, Cargo) are never touched. Stop the plugin first; deletion retries briefly while the process exits.
    /// </summary>
    public static async Task UninstallAsync() {
        string target = HarperPlugin.InstallDirectory;
        for (int attempt = 1; Directory.Exists(target); attempt++) {
            try {
                Directory.Delete(target, recursive: true);
            }
            catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && (attempt < 10)) {
                await Task.Delay(300).ConfigureAwait(false);
            }
        }
    }

    public static async Task<InstallResult> InstallLatestAsync(IProgress<string> progress, CancellationToken cancellationToken = default) {
        using HttpClient http = new();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WritingIDE-HarperPlugin");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        progress.Report("Looking up the latest Harper release...");
        using JsonDocument release = JsonDocument.Parse(await http.GetStringAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false));
        string version = release.RootElement.GetProperty("tag_name").GetString() ?? "latest";
        string architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x86_64";
        string assetName = $"harper-ls-{architecture}-pc-windows-msvc.zip";
        JsonElement? asset = release.RootElement.GetProperty("assets").EnumerateArray()
            .Where(candidate => candidate.GetProperty("name").GetString() == assetName)
            .Cast<JsonElement?>()
            .FirstOrDefault();
        string? downloadUrl = asset?.GetProperty("browser_download_url").GetString();
        if ((downloadUrl is null) || !Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? downloadUri) || (downloadUri.Scheme != Uri.UriSchemeHttps)
            || !downloadUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Harper {version} has no {assetName} download on github.com.");
        }
        // GitHub publishes each release file's SHA-256 ("sha256:<hex>"); the program is only installed when it matches.
        string? digest = asset!.Value.TryGetProperty("digest", out JsonElement digestElement) ? digestElement.GetString() : null;
        if ((digest is null) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"GitHub did not publish a checksum for {assetName}, so it cannot be verified. Install harper-ls yourself and choose its location instead.");
        }

        progress.Report($"Downloading {assetName} ({version})...");
        string zipPath = Path.Combine(Path.GetTempPath(), $"writingide-{Guid.NewGuid():N}-{assetName}");
        try {
            await using (Stream download = await http.GetStreamAsync(downloadUri, cancellationToken).ConfigureAwait(false))
            await using (FileStream file = File.Create(zipPath)) {
                await download.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }
            progress.Report("Verifying the download...");
            string actual;
            await using (FileStream file = File.OpenRead(zipPath)) {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
            }
            if (!actual.Equals(digest["sha256:".Length..], StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"The downloaded {assetName} does not match the checksum GitHub published for it, so it was not installed.");
            }

            progress.Report("Unpacking...");
            string target = HarperPlugin.InstallDirectory;
            if (Directory.Exists(target)) {
                Directory.Delete(target, recursive: true);
            }
            Directory.CreateDirectory(target);
            ZipFile.ExtractToDirectory(zipPath, target);
            string executable = Directory.EnumerateFiles(target, "harper-ls.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidOperationException("The download did not contain harper-ls.exe.");
            File.WriteAllText(Path.Combine(target, "VERSION.txt"), version);
            progress.Report($"Installed Harper {version}.");
            return new InstallResult(executable, version);
        }
        finally {
            if (File.Exists(zipPath)) {
                File.Delete(zipPath);
            }
        }
    }
}