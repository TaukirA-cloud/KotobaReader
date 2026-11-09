using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KotobaReader.Core;

public sealed class ReaderSettings
{
    public bool RightToLeft { get; set; } = true;
    public string Fit { get; set; } = "Page";
    public string SourceLanguage { get; set; } = "ja";
    public string TargetLanguage { get; set; } = "en";
    public string OcrLayout { get; set; } = "Vertical";
    public string PythonPath { get; set; } = "";
    public string TesseractPath { get; set; } = "";
    public string ModelsPath { get; set; } = "";
    public string TessdataPath { get; set; } = "";
}

public sealed class BookEntry
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public int PageIndex { get; set; }
    public int PageCount { get; set; }
    public DateTimeOffset LastOpened { get; set; }
    public List<int> Bookmarks { get; set; } = [];
    public string Progress => $"{Math.Min(PageIndex + 1, PageCount)} / {PageCount} pages";
    public override string ToString() => Title;
}

public sealed class TranslationNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BookPath { get; set; } = "";
    public int PageIndex { get; set; }
    public string SourceLanguage { get; set; } = "ja";
    public string TargetLanguage { get; set; } = "en";
    public string Original { get; set; } = "";
    public string Translated { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public string Caption => $"Page {PageIndex + 1} · {SourceLanguage} → {TargetLanguage}";
}

public sealed class LibraryState
{
    public int Version { get; set; } = 1;
    public ReaderSettings Settings { get; set; } = new();
    public List<BookEntry> Books { get; set; } = [];
    public List<TranslationNote> Notes { get; set; } = [];
    public Dictionary<string, string> TranslationCache { get; set; } = [];

    public static string CacheKey(string text, string source, string target, string model)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { text, source, target, model }))));
}

public sealed class StateStore(string directory)
{
    private readonly string file = System.IO.Path.Combine(directory, "library.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string? RecoveryMessage { get; private set; }

    public LibraryState Load()
    {
        if (!File.Exists(file)) return new();
        try { return Read(file); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            RecoveryMessage = "Library data could not be read. A backup was used if available; original data is preserved.";
            var preserved = file + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Copy(file, preserved, true);
            try { return Read(file + ".bak"); }
            catch (Exception backupEx) when (backupEx is JsonException or InvalidDataException or IOException) { return new(); }
        }
    }

    private static LibraryState Read(string path)
    {
        var state = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Library is empty.");
        if (state.Version != 1 || state.Settings is null || state.Books is null || state.Notes is null || state.TranslationCache is null
            || state.Books.Any(b => b is null || string.IsNullOrWhiteSpace(b.Path) || b.Title is null || b.Bookmarks is null)
            || state.Notes.Any(n => n is null || n.Original is null || n.Translated is null || n.BookPath is null)
            || state.TranslationCache.Any(pair => pair.Value is null)) throw new InvalidDataException("Unsupported library format.");
        var languages = new[] { "ja", "en", "zh", "ko", "es", "fr", "de" };
        if (!languages.Contains(state.Settings.SourceLanguage)) state.Settings.SourceLanguage = "ja";
        if (!languages.Contains(state.Settings.TargetLanguage)) state.Settings.TargetLanguage = "en";
        if (!new[] { "Page", "Width", "Actual" }.Contains(state.Settings.Fit)) state.Settings.Fit = "Page";
        if (!new[] { "Auto", "Block", "Vertical" }.Contains(state.Settings.OcrLayout)) state.Settings.OcrLayout = "Vertical";
        state.Settings.PythonPath ??= "";
        state.Settings.TesseractPath ??= "";
        state.Settings.ModelsPath ??= "";
        state.Settings.TessdataPath ??= "";
        return state;
    }

    public void Save(LibraryState state)
    {
        Directory.CreateDirectory(directory);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, state, JsonOptions);
                stream.Flush(true);
            }
            if (File.Exists(file)) File.Replace(temporary, file, file + ".bak");
            else File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
