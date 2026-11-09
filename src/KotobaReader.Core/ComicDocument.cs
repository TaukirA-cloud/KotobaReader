using System.IO.Compression;

namespace KotobaReader.Core;

public sealed record ComicPage(string Key, string Name);

public sealed class ComicDocument
{
    public const int MaxPages = 20_000;
    public const long MaxPageBytes = 64 * 1024 * 1024;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
    public string Path { get; }
    public string Title => System.IO.Path.GetFileNameWithoutExtension(Path.TrimEnd('\\', '/'));
    public bool IsArchive { get; }
    public IReadOnlyList<ComicPage> Pages { get; }

    private ComicDocument(string path, bool archive, List<ComicPage> pages)
        => (Path, IsArchive, Pages) = (path, archive, pages.AsReadOnly());

    public static ComicDocument Open(string path, CancellationToken token = default)
    {
        path = System.IO.Path.GetFullPath(path);
        bool archive = !Directory.Exists(path);
        var pages = new List<ComicPage>();
        if (archive)
        {
            if (!new[] { ".cbz", ".zip" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                throw new NotSupportedException("Open an image folder, CBZ, or ZIP comic archive.");
            using var zip = ZipFile.OpenRead(path);
            if (zip.Entries.Count > MaxPages * 2) throw new InvalidDataException("Archive contains too many entries.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.Name.Length == 0 || !IsImage(entry.Name) || entry.FullName.StartsWith("__MACOSX/")) continue;
                if (!keys.Add(entry.FullName)) throw new InvalidDataException("Archive has duplicate page names.");
                pages.Add(new(entry.FullName, entry.FullName));
            }
        }
        else
        {
            // Ignore inaccessible subfolders, but don't follow directory junctions or symlinks.
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System };
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                token.ThrowIfCancellationRequested();
                if (IsImage(file)) pages.Add(new(file, System.IO.Path.GetRelativePath(path, file)));
                if (pages.Count > MaxPages) throw new InvalidDataException($"A comic may contain at most {MaxPages:N0} pages.");
            }
        }
        if (pages.Count == 0) throw new InvalidDataException("No supported images were found in this comic.");
        if (pages.Count > MaxPages) throw new InvalidDataException("Comic contains too many pages.");
        pages.Sort((a, b) => NaturalOrder.Instance.Compare(a.Name, b.Name));
        return new(path, archive, pages);
    }

    public byte[] ReadPage(int index, CancellationToken token = default)
    {
        if ((uint)index >= (uint)Pages.Count) throw new ArgumentOutOfRangeException(nameof(index));
        using var zip = IsArchive ? ZipFile.OpenRead(Path) : null;
        var entry = zip?.GetEntry(Pages[index].Key);
        if (IsArchive && entry is null) throw new InvalidDataException("Page is missing; reopen the comic.");
        var size = entry?.Length ?? new FileInfo(Pages[index].Key).Length;
        if (size <= 0 || size > MaxPageBytes) throw new InvalidDataException("Page is empty or exceeds 64 MB.");
        using var input = entry is null ? File.OpenRead(Pages[index].Key) : entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            if (output.Length + read > MaxPageBytes) throw new InvalidDataException("Expanded page exceeds 64 MB.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static bool IsImage(string path) => Extensions.Contains(System.IO.Path.GetExtension(path));
}
