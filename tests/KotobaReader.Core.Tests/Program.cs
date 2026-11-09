using System.IO.Compression;
using System.Text.Json;
using KotobaReader.Core;

var root = Path.Combine(Path.GetTempPath(), "kotoba-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine($"FAIL {name}: {ex}"); failed++; }
}
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
string Folder(string name) { var path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
string Archive(string name, params (string name, byte[] data)[] pages)
{
    var path = Path.Combine(root, name + ".cbz");
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var page in pages) { using var output = archive.CreateEntry(page.name).Open(); output.Write(page.data); }
    return path;
}

try
{
    Test("Natural numeric ordering across chapters and arbitrarily large integers", () =>
    {
        string[] input = ["chapter10/page1.png", "chapter2/page10.png", "chapter2/page2.png", "chapter2/page999999999999999999999999999.png"];
        Array.Sort(input, NaturalOrder.Instance);
        Assert(input.SequenceEqual(new[] { "chapter2/page2.png", "chapter2/page10.png", "chapter2/page999999999999999999999999999.png", "chapter10/page1.png" }));
    });
    Test("Natural comparer is antisymmetric and transitive with leading zeroes", () =>
    {
        string[] values = ["a0", "a00", "a000", "a01", "a1", "a2", "A10", "a", "a2b", "a0000000003", "z"];
        foreach (var a in values) foreach (var b in values)
            Assert(Math.Sign(NaturalOrder.Instance.Compare(a, b)) == -Math.Sign(NaturalOrder.Instance.Compare(b, a)));
        var sorted = values.Order(NaturalOrder.Instance).ToList();
        for (int i = 0; i < sorted.Count; i++) for (int j = i; j < sorted.Count; j++)
            Assert(NaturalOrder.Instance.Compare(sorted[i], sorted[j]) <= 0);
    });
    Test("Folder import includes nested supported images and ignores unrelated files", () =>
    {
        var folder = Folder("nested"); Directory.CreateDirectory(Path.Combine(folder, "chapter2"));
        File.WriteAllBytes(Path.Combine(folder, "chapter2/page2.PNG"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(folder, "chapter2/page10.jpg"), [4, 5]);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "ignore");
        var comic = ComicDocument.Open(folder);
        Assert(comic.Pages.Count == 2 && comic.Pages[0].Name.EndsWith("page2.PNG"));
        Assert(comic.ReadPage(1).SequenceEqual(new byte[] { 4, 5 }));
    });
    Test("CBZ reads pages directly without extracting unsafe paths", () =>
    {
        var file = Archive("safe", ("../outside.png", [1, 2]), ("page2.PNG", [3, 4]), ("notes.txt", [9]), ("__MACOSX/test.png", [9]));
        var comic = ComicDocument.Open(file);
        Assert(comic.Pages.Count == 2);
        Assert(comic.ReadPage(0).SequenceEqual(new byte[] { 1, 2 }));
        Assert(!File.Exists(Path.Combine(root, "outside.png")));
    });
    Test("Duplicate CBZ page keys are rejected", () => Throws<InvalidDataException>(() =>
        ComicDocument.Open(Archive("duplicate", ("page.png", [1]), ("page.png", [2])))));
    Test("Empty comics are rejected", () => Throws<InvalidDataException>(() => ComicDocument.Open(Folder("empty"))));
    Test("Truncated archives fail safely", () =>
    {
        var file = Path.Combine(root, "broken.cbz"); File.WriteAllText(file, "not a ZIP");
        Throws<InvalidDataException>(() => ComicDocument.Open(file));
    });
    Test("Page bounds are enforced", () =>
    {
        var comic = ComicDocument.Open(Archive("bounds", ("page.png", [1])));
        Throws<ArgumentOutOfRangeException>(() => comic.ReadPage(-1));
        Throws<ArgumentOutOfRangeException>(() => comic.ReadPage(1));
    });
    Test("Oversized compressed pages are rejected before expansion", () =>
    {
        var file = Path.Combine(root, "oversized.cbz");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        using (var output = zip.CreateEntry("large.png").Open())
        {
            var zeros = new byte[1024 * 1024];
            for (int i = 0; i < 65; i++) output.Write(zeros);
        }
        var comic = ComicDocument.Open(file);
        Throws<InvalidDataException>(() => comic.ReadPage(0));
    });
    Test("Canceled imports do not produce a partial document", () =>
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Throws<OperationCanceledException>(() => ComicDocument.Open(Archive("cancel", ("page.png", [1])), cancel.Token));
    });
    Test("Library round trip preserves bookmarks, settings, notes, and cache", () =>
    {
        var store = new StateStore(Folder("state")); var state = new LibraryState();
        state.Settings.RightToLeft = false;
        state.Books.Add(new() { Path = "C:/漫画.cbz", Title = "漫画", PageIndex = 8, PageCount = 12, Bookmarks = [2, 8] });
        state.Notes.Add(new() { Original = "こんにちは", Translated = "Hello", PageIndex = 8 });
        state.TranslationCache["key"] = "cached"; store.Save(state);
        var restored = store.Load();
        Assert(restored.Books.Single().Bookmarks.SequenceEqual(new[] { 2, 8 }));
        Assert(restored.Notes.Single().Original == "こんにちは" && restored.TranslationCache["key"] == "cached");
        Assert(!restored.Settings.RightToLeft);
    });
    Test("Corrupt primary library recovers backup and preserves damaged bytes", () =>
    {
        var folder = Folder("recover"); var store = new StateStore(folder);
        var state = new LibraryState(); state.Settings.SourceLanguage = "ko"; store.Save(state);
        state.Settings.SourceLanguage = "en"; store.Save(state);
        File.WriteAllText(Path.Combine(folder, "library.json"), "{broken");
        var recovered = store.Load();
        Assert(recovered.Settings.SourceLanguage == "ko");
        Assert(Directory.EnumerateFiles(folder, "*.damaged-*").Any());
        Assert(store.RecoveryMessage is not null);
    });
    Test("Malformed library collections recover instead of crashing", () =>
    {
        var folder = Folder("null-state");
        File.WriteAllText(Path.Combine(folder, "library.json"), "{\"Books\":null}");
        Assert(new StateStore(folder).Load().Books.Count == 0);
    });
    Test("Translation cache separates languages, models, and ambiguous concatenations", () =>
    {
        var key = LibraryState.CacheKey("こんにちは", "ja", "en", "model1");
        Assert(key != LibraryState.CacheKey("こんにちは", "ja", "en", "model2"));
        Assert(key != LibraryState.CacheKey("こんにちは", "ja", "fr", "model1"));
        Assert(LibraryState.CacheKey("ab", "c", "d", "e") != LibraryState.CacheKey("a", "bc", "d", "e"));
    });
}
finally { Directory.Delete(root, recursive: true); }
Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
