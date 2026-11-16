using System.Buffers.Binary;
using System.Text;

namespace WritingIDE.Core.Export;

/// <summary>One face (Regular, Bold Italic...) of a font, found in a .ttf, .otf or .ttc file.</summary>
public sealed record FontFace(string Path, int FaceIndex, string Family, string? TypographicFamily, int Weight, bool Bold, bool Italic, bool HasTrueTypeOutlines, ushort EmbeddingType) {
    /// <summary>
    /// The font's licence forbids embedding it in documents ("Restricted License" in its OS/2 table). Such fonts are
    /// never put into a PDF or EPUB.
    /// </summary>
    public bool IsEmbeddingRestricted => (EmbeddingType & 0x000F) == 0x0002;

    public bool IsCollection => Path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The faces an exporter uses for one family; a missing style is simulated from <see cref="Regular"/>.</summary>
public sealed record FontFamilyFaces(string Family, FontFace Regular, FontFace? Bold, FontFace? Italic, FontFace? BoldItalic) {
    public IEnumerable<FontFace> All => new[] { Regular, Bold, Italic, BoldItalic }.OfType<FontFace>().Distinct();

    public FontFace? Get(bool bold, bool italic) {
        return (bold, italic) switch {
            (true, true) => BoldItalic,
            (true, false) => Bold,
            (false, true) => Italic,
            _ => Regular
        };
    }
}

/// <summary>
/// Finds font files by family name: the Windows fonts folder, fonts installed for the current user, and fonts added
/// to WritingIDE (<see cref="AppFontsFolder"/>). Reads only each file's name, head and OS/2 tables.
/// </summary>
public sealed class FontCatalog {
    private static readonly Lock Gate = new();
    private static FontCatalog? _installed;
    private readonly Dictionary<string, List<FontFace>> _byFamily = new(StringComparer.OrdinalIgnoreCase);

    private FontCatalog(IEnumerable<FontFace> faces) {
        foreach (FontFace face in faces) {
            Add(face.Family, face);
            if ((face.TypographicFamily is not null) && !face.TypographicFamily.Equals(face.Family, StringComparison.OrdinalIgnoreCase)) {
                Add(face.TypographicFamily, face);
            }
        }
    }

    /// <summary>Fonts added in WritingIDE (Tools > Fonts), available only inside the program.</summary>
    public static string AppFontsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WritingIDE", "Fonts");

    public static IReadOnlyList<string> DefaultFolders => [
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
        AppFontsFolder
    ];

    /// <summary>The installed and added fonts, scanned once.</summary>
    public static FontCatalog Installed {
        get {
            lock (Gate) {
                return _installed ??= Scan(DefaultFolders);
            }
        }
    }

    public IEnumerable<string> Families => _byFamily.Keys;

    public static FontCatalog Scan(IEnumerable<string> folders) {
        List<FontFace> faces = [];
        foreach (string folder in folders.Where(Directory.Exists)) {
            foreach (string path in Directory.EnumerateFiles(folder)) {
                faces.AddRange(Read(path));
            }
        }
        return new FontCatalog(faces);
    }

    /// <summary>Forgets the scan, so the next use of <see cref="Installed"/> sees fonts added or removed since.</summary>
    public static void Refresh() {
        lock (Gate) {
            _installed = null;
        }
    }

    /// <summary>The faces of a family, or null when no file provides it.</summary>
    public FontFamilyFaces? Find(string family) {
        if (!_byFamily.TryGetValue(family.Trim(), out List<FontFace>? faces) || (faces.Count == 0)) {
            return null;
        }
        static FontFace? Best(IEnumerable<FontFace> candidates, int weight) {
            return candidates.OrderBy(face => Math.Abs(face.Weight - weight)).ThenBy(static face => face.IsCollection).FirstOrDefault();
        }
        FontFace? regular = Best(faces.Where(static face => !face.Italic && !face.Bold), 400) ?? Best(faces.Where(static face => !face.Italic), 400) ?? Best(faces, 400);
        return regular is null ? null : new FontFamilyFaces(
            family.Trim(),
            regular,
            Best(faces.Where(static face => !face.Italic && (face.Bold || (face.Weight >= 600))), 700),
            Best(faces.Where(static face => face.Italic && !face.Bold && (face.Weight < 600)), 400),
            Best(faces.Where(static face => face.Italic && (face.Bold || (face.Weight >= 600))), 700));
    }

    private void Add(string family, FontFace face) {
        if (!_byFamily.TryGetValue(family, out List<FontFace>? faces)) {
            faces = [];
            _byFamily[family] = faces;
        }
        faces.Add(face);
    }

    /// <summary>Every face in a font file; nothing for files that are not fonts or cannot be read.</summary>
    public static IReadOnlyList<FontFace> Read(string path) {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".ttf" or ".otf" or ".ttc")) {
            return [];
        }
        try {
            // Only the headers and three small tables are read, so scanning a large fonts folder stays quick.
            using FileStream stream = File.OpenRead(path);
            byte[] Bytes(long offset, int length) {
                if ((offset < 0) || (length < 0) || (offset + length > stream.Length)) {
                    throw new IOException("The font file is truncated.");
                }
                byte[] buffer = new byte[length];
                stream.Position = offset;
                stream.ReadExactly(buffer);
                return buffer;
            }
            byte[] header = Bytes(0, 12);
            if (Encoding.ASCII.GetString(header, 0, 4) == "ttcf") {
                int count = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8)), 64);
                byte[] offsets = Bytes(12, count * 4);
                List<FontFace> faces = [];
                for (int index = 0; index < count; index++) {
                    FontFace? face = ReadFace(Bytes, path, index, BinaryPrimitives.ReadUInt32BigEndian(offsets.AsSpan(index * 4)));
                    if (face is not null) {
                        faces.Add(face);
                    }
                }
                return faces;
            }
            FontFace? single = ReadFace(Bytes, path, 0, 0);
            return single is null ? [] : [single];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException or IndexOutOfRangeException or ArgumentException) {
            return [];
        }
    }

    /// <summary>
    /// A standalone font file for one face: the file itself, or for a face inside a .ttc collection, a new .ttf built
    /// from that face's tables (PDF and EPUB need single fonts).
    /// </summary>
    /// <exception cref="InvalidDataException">The file is damaged (a table lies outside it).</exception>
    public static byte[] ExtractFace(FontFace face) {
        byte[] data = File.ReadAllBytes(face.Path);
        if (!face.IsCollection) {
            return data;
        }
        // Every offset comes from the file itself, so each one is checked before it is used.
        void Require(long start, long length) {
            if ((start < 0) || (length < 0) || (start + length > data.Length)) {
                throw new InvalidDataException($"The font file '{Path.GetFileName(face.Path)}' is damaged.");
            }
        }
        Require(12 + (face.FaceIndex * 4L), 4);
        long offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12 + (face.FaceIndex * 4)));
        Require(offset, 12);
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan((int)offset + 4));
        Require(offset + 12, 16L * tableCount);
        int headerLength = 12 + (16 * tableCount);
        List<(byte[] Record, int Offset, int Length)> tables = [];
        long total = headerLength;
        for (int index = 0; index < tableCount; index++) {
            int record = (int)offset + 12 + (index * 16);
            long tableOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8));
            long tableLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 12));
            Require(tableOffset, tableLength);
            tables.Add((data[record..(record + 16)], (int)tableOffset, (int)tableLength));
            total += (tableLength + 3) & ~3;
        }
        // One face never needs more than the whole collection; a damaged file could otherwise ask for gigabytes.
        if (total > data.Length + headerLength) {
            throw new InvalidDataException($"The font file '{Path.GetFileName(face.Path)}' is damaged.");
        }
        byte[] output = new byte[total];
        Array.Copy(data, offset, output, 0, 12);
        int position = headerLength;
        for (int index = 0; index < tables.Count; index++) {
            (byte[] record, int tableOffset, int length) = tables[index];
            Array.Copy(record, 0, output, 12 + (index * 16), 16);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(12 + (index * 16) + 8), (uint)position);
            Array.Copy(data, tableOffset, output, position, length);
            position += (length + 3) & ~3;
        }
        return output;
    }

    private static FontFace? ReadFace(Func<long, int, byte[]> read, string path, int index, long offset) {
        byte[] header = read(offset, 12);
        uint version = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (version is not (0x00010000 or 0x4F54544F or 0x74727565)) {
            return null;
        }
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        byte[] directory = read(offset + 12, tableCount * 16);
        Dictionary<string, (long Offset, int Length)> tables = new(StringComparer.Ordinal);
        for (int table = 0; table < tableCount; table++) {
            int record = table * 16;
            tables[Encoding.ASCII.GetString(directory, record, 4)] = (BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(record + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(record + 12)));
        }
        if (!tables.TryGetValue("name", out (long Offset, int Length) name)) {
            return null;
        }
        byte[] names = read(name.Offset, name.Length);
        string? family = ReadName(names, 1);
        if (string.IsNullOrWhiteSpace(family)) {
            return null;
        }
        string? typographic = ReadName(names, 16);
        int weight = 400;
        bool bold = false;
        bool italic = false;
        ushort embedding = 0;
        if (tables.TryGetValue("OS/2", out (long Offset, int Length) os2) && (os2.Length >= 64)) {
            byte[] table = read(os2.Offset, 64);
            weight = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(4));
            embedding = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(8));
            ushort selection = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(62));
            italic = (selection & 0x0001) != 0;
            bold = (selection & 0x0020) != 0;
        }
        else if (tables.TryGetValue("head", out (long Offset, int Length) head) && (head.Length >= 46)) {
            ushort macStyle = BinaryPrimitives.ReadUInt16BigEndian(read(head.Offset + 44, 2));
            bold = (macStyle & 0x0001) != 0;
            italic = (macStyle & 0x0002) != 0;
            weight = bold ? 700 : 400;
        }
        return new FontFace(path, index, family.Trim(), typographic?.Trim(), weight, bold, italic, tables.ContainsKey("glyf"), embedding);
    }

    /// <summary>A name from the 'name' table, preferring Windows English, then any Windows entry, then Macintosh Roman.</summary>
    private static string? ReadName(byte[] data, int nameId) {
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2));
        int strings = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        string? windows = null;
        string? mac = null;
        for (int index = 0; index < count; index++) {
            int record = 6 + (index * 12);
            ushort platform = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record));
            ushort language = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 4));
            if (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 6)) != nameId) {
                continue;
            }
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 8));
            int start = strings + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 10));
            if (platform == 3) {
                string value = Encoding.BigEndianUnicode.GetString(data, start, length);
                if (language == 0x0409) {
                    return value;
                }
                windows ??= value;
            }
            else if ((platform == 1) && (mac is null)) {
                mac = Encoding.Latin1.GetString(data, start, length);
            }
        }
        return windows ?? mac;
    }
}