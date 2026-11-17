using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using WritingIDE.Core.Plugins;

namespace WritingIDE.Core.Tests;

public sealed class PluginCatalogTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));

    private static byte[] Zip(params (string Name, string Content)[] files) {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            foreach ((string name, string content) in files) {
                using Stream entry = archive.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }
        return stream.ToArray();
    }

    private static PluginListing Listing(byte[] zip, string id = "com.example.map", string version = "1.0.0", string sdk = "1.2") {
        return new PluginListing(id, "Map", "A map.", "Someone", Version.Parse(version), Version.Parse(sdk), "https://example.com/map.zip",
            Convert.ToHexStringLower(SHA256.HashData(zip)), "", "");
    }

    [Fact]
    public void TheIndexKeepsOnlyCompleteAndSafeEntries() {
        string hash = new('a', 64);
        string json = $$"""
            { "plugins": [
              { "id": "com.example.map", "name": "Map Maker", "description": "Maps.", "author": "Ann", "version": "v1.2", "sdk": "1.2",
                "download": "https://example.com/map.zip", "sha256": "{{hash.ToUpperInvariant()}}", "homepage": "https://example.com", "discussion": "javascript:alert(1)" },
              { "id": "com.example.map", "name": "Map Maker (old)", "version": "1.0", "download": "https://example.com/old.zip", "sha256": "{{hash}}" },
              { "id": "com.example.http", "name": "Plain http", "version": "1.0", "download": "http://example.com/a.zip", "sha256": "{{hash}}" },
              { "id": "com.example.nohash", "name": "No checksum", "version": "1.0", "download": "https://example.com/a.zip" },
              { "id": "..\\escape", "name": "Bad id", "version": "1.0", "download": "https://example.com/a.zip", "sha256": "{{hash}}" },
              { "id": "com.example.noversion", "name": "No version", "download": "https://example.com/a.zip", "sha256": "{{hash}}" },
              { "id": "com.example.audio", "name": "Audio", "version": "2", "download": "https://example.com/audio.zip", "sha256": "{{hash}}" },
              "not an object"
            ] }
            """;

        IReadOnlyList<PluginListing> listings = PluginCatalog.Parse(json);

        Assert.Equal(["Audio", "Map Maker"], listings.Select(static listing => listing.Name));
        PluginListing map = listings[1];
        Assert.Equal(new Version(1, 2), map.Version);
        Assert.Equal(hash, map.Sha256);
        Assert.Equal("https://example.com", map.Homepage);
        // A link that is not https is dropped, not shown.
        Assert.Equal("", map.Discussion);
        Assert.Equal(new Version(2, 0), listings[0].Version);
        Assert.Equal(new Version(1, 0), listings[0].Sdk);
        Assert.Empty(PluginCatalog.Parse("not json"));
        Assert.Empty(PluginCatalog.Parse("[]"));
    }

    [Fact]
    public void ADownloadThatDoesNotMatchItsChecksumIsRefused() {
        byte[] zip = Zip(("Map.dll", "code"));
        PluginListing listing = Listing(zip);

        PluginInstaller.Verify(listing, zip);
        byte[] tampered = [.. zip, 0];
        Assert.Throws<InvalidDataException>(() => PluginInstaller.Verify(listing, tampered));
    }

    [Fact]
    public void AnInstallIsStagedAndPutInPlaceAtTheNextStart() {
        byte[] zip = Zip(("Map.dll", "version one"), ("data/readme.txt", "hello"));
        PluginListing listing = Listing(zip);

        PluginInstaller.Stage(_root, listing, zip);

        Assert.True(PluginInstaller.IsStaged(_root, listing.Id));
        Assert.False(Directory.Exists(Path.Combine(_root, "com.example.map")));
        IReadOnlyList<string> log = PluginInstaller.ApplyPending(_root);
        Assert.Equal("Installed the plugin in 'com.example.map'.", Assert.Single(log));
        Assert.Equal("version one", File.ReadAllText(Path.Combine(_root, "com.example.map", "Map.dll")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_root, "com.example.map", "data", "readme.txt")));
        Assert.False(PluginInstaller.IsStaged(_root, listing.Id));

        // An update replaces the folder, leftovers included.
        byte[] update = Zip(("Map.dll", "version two"));
        PluginInstaller.Stage(_root, Listing(update, version: "1.1.0"), update);
        PluginInstaller.ApplyPending(_root);
        Assert.Equal("version two", File.ReadAllText(Path.Combine(_root, "com.example.map", "Map.dll")));
        Assert.False(Directory.Exists(Path.Combine(_root, "com.example.map", "data")));
        Assert.Empty(PluginInstaller.ApplyPending(_root));
    }

    [Fact]
    public void ARemovalWaitsForTheNextStartAndCanBeTakenBack() {
        string folder = Path.Combine(_root, "com.example.map");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Map.dll"), "code");

        PluginInstaller.MarkForRemoval(_root, "com.example.map");
        Assert.True(PluginInstaller.IsMarkedForRemoval(_root, "com.example.map"));
        PluginInstaller.CancelRemoval(_root, "com.example.map");
        Assert.Empty(PluginInstaller.ApplyPending(_root));
        Assert.True(Directory.Exists(folder));

        PluginInstaller.MarkForRemoval(_root, "com.example.map");
        Assert.Equal("Removed the plugin in 'com.example.map'.", Assert.Single(PluginInstaller.ApplyPending(_root)));
        Assert.False(Directory.Exists(folder));
        Assert.False(PluginInstaller.IsMarkedForRemoval(_root, "com.example.map"));

        // Installing again cancels a removal that has not happened yet.
        Directory.CreateDirectory(folder);
        PluginInstaller.MarkForRemoval(_root, "com.example.map");
        byte[] zip = Zip(("Map.dll", "again"));
        PluginInstaller.Stage(_root, Listing(zip), zip);
        PluginInstaller.ApplyPending(_root);
        Assert.Equal("again", File.ReadAllText(Path.Combine(folder, "Map.dll")));
    }

    [Fact]
    public void AZipThatIsNotAPluginIsRefusedAndLeavesNothingBehind() {
        byte[] escaping = Zip(("../../evil.dll", "code"));
        byte[] noCode = Zip(("readme.txt", "just text"));

        Assert.Throws<InvalidDataException>(() => PluginInstaller.Stage(_root, Listing(escaping), escaping));
        Assert.Throws<InvalidDataException>(() => PluginInstaller.Stage(_root, Listing(noCode), noCode));
        Assert.ThrowsAny<Exception>(() => PluginInstaller.Stage(_root, Listing([1, 2, 3]), [1, 2, 3]));

        Assert.False(PluginInstaller.IsStaged(_root, "com.example.map"));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "evil.dll")));
        Assert.Empty(PluginInstaller.ApplyPending(_root));
    }

    [Fact]
    public void TheStateSaysWhatTheWriterCanDo() {
        PluginListing listing = Listing([1], version: "1.1.0", sdk: "1.2");
        Version sdk = new(1, 2, 0);

        Assert.Equal(PluginState.NotInstalled, PluginInstaller.GetState(listing, null, sdk, staged: false, markedForRemoval: false));
        Assert.Equal(PluginState.NeedsNewerApp, PluginInstaller.GetState(listing, null, new Version(1, 1, 0), staged: false, markedForRemoval: false));
        Assert.Equal(PluginState.UpdateAvailable, PluginInstaller.GetState(listing, new Version(1, 0, 0), sdk, staged: false, markedForRemoval: false));
        Assert.Equal(PluginState.Installed, PluginInstaller.GetState(listing, new Version(1, 1, 0), sdk, staged: false, markedForRemoval: false));
        // An update that needs a newer WritingIDE is not offered.
        Assert.Equal(PluginState.Installed, PluginInstaller.GetState(listing, new Version(1, 0, 0), new Version(1, 1, 0), staged: false, markedForRemoval: false));
        Assert.Equal(PluginState.InstallPending, PluginInstaller.GetState(listing, null, sdk, staged: true, markedForRemoval: false));
        Assert.Equal(PluginState.RemovalPending, PluginInstaller.GetState(listing, new Version(1, 1, 0), sdk, staged: false, markedForRemoval: true));
    }

    [Fact]
    public void AnIdBecomesOneFolderName() {
        Assert.Equal("com.example.map", PluginInstaller.FolderName("com.example.map"));
        Assert.Equal("_.._evil", PluginInstaller.FolderName(@"\..\evil"));
        Assert.Equal("_", PluginInstaller.FolderName("..."));
    }

    public void Dispose() {
        if (Directory.Exists(_root)) {
            Directory.Delete(_root, recursive: true);
        }
    }
}