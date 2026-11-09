using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KotobaReader.Core;
using KotobaReader.Services;
using Microsoft.Win32;

namespace KotobaReader;

public partial class MainWindow : Window
{
    private readonly ReaderViewModel vm = new();
    private readonly string root = FindRoot();
    private readonly string dataDirectory;
    private readonly StateStore store;
    private readonly LibraryState state;
    private readonly bool restoreSession;
    private EngineClient engine;
    private ComicDocument? comic;
    private BookEntry? currentBook;
    private int pageIndex;
    private CancellationTokenSource? pageCancellation;
    private CancellationTokenSource? operationCancellation;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private bool ready, syncing, closing, dragging, focusMode;
    private Point selectionStart;
    private Rect selectionPixels;
    private double zoom = 1;
    private long pageEpoch;
    private GridLength previousLibrary, previousStudio;
    private WindowState previousWindowState;
    private WindowStyle previousWindowStyle;

    public MainWindow() : this(restoreSession: true) { }

    public MainWindow(bool restoreSession)
    {
        this.restoreSession = restoreSession;
        dataDirectory = Environment.GetEnvironmentVariable("KOTOBA_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KotobaReader");
        store = new(dataDirectory);
        try { state = store.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state = new();
            vm.Status = "Library could not be loaded: " + ex.Message;
        }
        engine = new(root);
        InitializeComponent();
        DataContext = vm;
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(ReaderViewModel.Original)) vm.Translated = "";
        };
        Choose(FitMode, state.Settings.Fit);
        Choose(SourceLanguage, state.Settings.SourceLanguage);
        Choose(TargetLanguage, state.Settings.TargetLanguage);
        Choose(OcrLayout, state.Settings.OcrLayout);
        RightToLeft.IsChecked = state.Settings.RightToLeft;
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveState(); };
        ready = true;
        RefreshLibrary();
        if (store.RecoveryMessage is not null) vm.Status = store.RecoveryMessage;
    }

    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (Directory.Exists(Path.Combine(folder.FullName, "src", "KotobaReader"))
                && Directory.Exists(Path.Combine(folder.FullName, "worker"))) return folder.FullName;
        return AppContext.BaseDirectory;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!restoreSession) return;
        var argument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
        if (argument == "--demo") await OpenDemoAsync();
        else if (!string.IsNullOrWhiteSpace(argument)) await OpenComicAsync(argument);
        else if (state.Books.OrderByDescending(b => b.LastOpened).FirstOrDefault() is { } last
            && (File.Exists(last.Path) || Directory.Exists(last.Path))) await OpenComicAsync(last.Path);
    }

    private async void OpenFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a comic image folder" };
        if (dialog.ShowDialog(this) == true) await OpenComicAsync(dialog.FolderName);
    }

    private async void OpenArchive(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Comic archives (*.cbz;*.zip)|*.cbz;*.zip", Title = "Open a comic" };
        if (dialog.ShowDialog(this) == true) await OpenComicAsync(dialog.FileName);
    }

    private async Task OpenComicAsync(string path)
    {
        pageCancellation?.Cancel();
        operationCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        pageCancellation = cancellation;
        long epoch = ++pageEpoch;
        vm.Status = "Opening comic…";
        try
        {
            var document = await Task.Run(() => ComicDocument.Open(path, cancellation.Token), cancellation.Token);
            if (epoch != pageEpoch || closing) return;
            comic = document;
            currentBook = state.Books.FirstOrDefault(b => string.Equals(b.Path, document.Path, StringComparison.OrdinalIgnoreCase));
            if (currentBook is null)
            {
                currentBook = new() { Path = document.Path, Title = document.Title };
                state.Books.Add(currentBook);
            }
            currentBook.LastOpened = DateTimeOffset.Now;
            currentBook.PageCount = document.Pages.Count;
            currentBook.Bookmarks.RemoveAll(p => p < 0 || p >= document.Pages.Count);
            pageIndex = Math.Clamp(currentBook.PageIndex, 0, document.Pages.Count - 1);
            vm.Title = document.Title;
            vm.BookOpen = true;
            syncing = true;
            PagesList.ItemsSource = document.Pages;
            syncing = false;
            vm.Original = vm.Translated = "";
            RefreshLibrary();
            RefreshNotes();
            await ShowPageAsync(pageIndex);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (epoch == pageEpoch) vm.Status = "Could not open comic: " + ex.Message; }
        finally { if (ReferenceEquals(pageCancellation, cancellation)) pageCancellation = null; cancellation.Dispose(); }
    }

    private async Task ShowPageAsync(int index)
    {
        if (comic is null || currentBook is null || closing) return;
        var document = comic;
        pageCancellation?.Cancel();
        operationCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        pageCancellation = cancellation;
        long epoch = ++pageEpoch;
        pageIndex = Math.Clamp(index, 0, document.Pages.Count - 1);
        int requested = pageIndex;
        ClearSelection();
        vm.Image = null;
        vm.Original = vm.Translated = "";
        vm.PageLabel = $"PAGE {pageIndex + 1} OF {document.Pages.Count}  /  {document.Pages[pageIndex].Name}";
        JumpBox.Text = (pageIndex + 1).ToString();
        vm.Progress = (pageIndex + 1) * 100d / document.Pages.Count;
        vm.Status = "Loading page…";
        syncing = true;
        PagesList.SelectedIndex = pageIndex;
        syncing = false;
        RefreshBookmarks();
        try
        {
            var image = await Task.Run(() => PageDecoder.Load(document, requested, cancellation.Token), cancellation.Token);
            if (epoch != pageEpoch || closing) return;
            vm.Image = image;
            currentBook.PageIndex = pageIndex;
            ResizePage();
            ReaderScroll.ScrollToTop();
            ReaderScroll.ScrollToLeftEnd();
            vm.Status = "Drag over dialogue to recognize it, or use arrow keys to read.";
            RefreshLibrary();
            ScheduleSave();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (epoch == pageEpoch && !closing) vm.Status = $"Page {requested + 1} could not be displayed: {ex.Message}. You can continue to the next page."; }
        finally { if (ReferenceEquals(pageCancellation, cancellation)) pageCancellation = null; cancellation.Dispose(); }
    }

    private async void Previous(object sender, RoutedEventArgs e) => await MoveAsync(-1);
    private async void Next(object sender, RoutedEventArgs e) => await MoveAsync(1);
    private Task MoveAsync(int delta) => comic is null ? Task.CompletedTask
        : ShowPageAsync(Math.Clamp(pageIndex + delta, 0, comic.Pages.Count - 1));

    private async void JumpToPage(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || comic is null) return;
        e.Handled = true;
        if (int.TryParse(JumpBox.Text, out int page) && page >= 1 && page <= comic.Pages.Count)
            await ShowPageAsync(page - 1);
        else vm.Status = $"Enter a page number from 1 to {comic.Pages.Count}.";
    }

    private async void SelectPage(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && PagesList.SelectedIndex >= 0) await ShowPageAsync(PagesList.SelectedIndex);
    }

    private void RefreshLibrary()
    {
        if (!ready) return;
        var selected = LibraryList.SelectedItem;
        var search = LibrarySearch.Text.Trim();
        LibraryList.ItemsSource = state.Books.Where(b => b.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.LastOpened).ToList();
        LibraryList.SelectedItem = selected;
    }
    private void SearchLibrary(object sender, TextChangedEventArgs e) => RefreshLibrary();
    private async void OpenLibraryBook(object sender, MouseButtonEventArgs e)
    {
        if (LibraryList.SelectedItem is BookEntry book) await OpenComicAsync(book.Path);
    }
    private async void LibraryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && LibraryList.SelectedItem is BookEntry book) { e.Handled = true; await OpenComicAsync(book.Path); }
    }
    private void RemoveBook(object sender, RoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is not BookEntry book) { vm.Status = "Select a library entry to remove."; return; }
        state.Books.Remove(book);
        if (ReferenceEquals(currentBook, book))
        {
            ++pageEpoch; pageCancellation?.Cancel(); operationCancellation?.Cancel();
            comic = null; currentBook = null; vm.BookOpen = false; vm.Image = null;
            vm.Title = "Your next chapter starts here"; vm.PageLabel = "NO COMIC OPEN"; vm.Progress = 0;
            PagesList.ItemsSource = null; BookmarksList.ItemsSource = null;
            vm.Original = vm.Translated = ""; ClearSelection(); RefreshNotes();
        }
        RefreshLibrary(); ScheduleSave();
        vm.Status = "Library entry removed. Comic files and saved notes were kept.";
    }

    private sealed record BookmarkItem(int Index) { public override string ToString() => $"Page {Index + 1}"; }
    private void RefreshBookmarks()
    {
        syncing = true;
        BookmarksList.ItemsSource = currentBook?.Bookmarks.Order().Select(p => new BookmarkItem(p)).ToList();
        syncing = false;
        vm.Bookmark = currentBook?.Bookmarks.Contains(pageIndex) == true ? "★ Bookmarked" : "☆ Bookmark";
    }
    private void ToggleBookmark(object sender, RoutedEventArgs e)
    {
        if (currentBook is null) return;
        if (!currentBook.Bookmarks.Remove(pageIndex)) currentBook.Bookmarks.Add(pageIndex);
        RefreshBookmarks(); ScheduleSave();
    }
    private async void SelectBookmark(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && BookmarksList.SelectedItem is BookmarkItem bookmark) await ShowPageAsync(bookmark.Index);
    }

    private static string SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
    private static void Choose(ComboBox box, string tag)
    {
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == tag) ?? box.Items[0];
    }
    private void PreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        state.Settings.RightToLeft = RightToLeft.IsChecked == true; ScheduleSave();
    }
    private void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        operationCancellation?.Cancel();
        state.Settings.SourceLanguage = SelectedTag(SourceLanguage);
        state.Settings.TargetLanguage = SelectedTag(TargetLanguage);
        vm.Translated = ""; ScheduleSave();
    }
    private void LayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        operationCancellation?.Cancel();
        state.Settings.OcrLayout = SelectedTag(OcrLayout); ScheduleSave();
    }
    private void FitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        state.Settings.Fit = SelectedTag(FitMode); zoom = 1; ResizePage(); ScheduleSave();
    }
    private void ReaderResized(object sender, SizeChangedEventArgs e) { if (ready) ResizePage(); }
    private void ResizePage()
    {
        if (vm.Image is not { } image || ReaderScroll.ActualWidth <= 0) return;
        var width = Math.Max(50, ReaderScroll.ActualWidth - 22);
        var height = Math.Max(50, ReaderScroll.ActualHeight - 22);
        double scale = state.Settings.Fit switch
        {
            "Width" => width / image.PixelWidth,
            "Actual" => 1,
            _ => Math.Min(width / image.PixelWidth, height / image.PixelHeight)
        };
        scale *= zoom;
        PageSurface.Width = image.PixelWidth * scale;
        PageSurface.Height = image.PixelHeight * scale;
        ZoomLabel.Text = $"{scale * 100:0}%";
        DrawSelection();
    }
    private void ZoomIn(object sender, RoutedEventArgs e) { zoom = Math.Min(zoom * 1.2, 6); ResizePage(); }
    private void ZoomOut(object sender, RoutedEventArgs e) { zoom = Math.Max(zoom / 1.2, .2); ResizePage(); }
    private void ReaderWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), .2, 6); ResizePage();
    }

    private void SelectionModeChanged(object sender, RoutedEventArgs e)
    {
        if (SelectionCanvas is not null) SelectionCanvas.Cursor = SelectionMode.IsChecked == true ? Cursors.Cross : Cursors.Arrow;
    }
    private Point PixelPosition(Point point)
    {
        if (vm.Image is not { } image || PageSurface.Width <= 0 || PageSurface.Height <= 0) return new();
        return new(Math.Clamp(point.X / PageSurface.Width * image.PixelWidth, 0, image.PixelWidth),
            Math.Clamp(point.Y / PageSurface.Height * image.PixelHeight, 0, image.PixelHeight));
    }
    private void SelectionStart(object sender, MouseButtonEventArgs e)
    {
        if (vm.Image is null || SelectionMode.IsChecked != true || vm.Busy) return;
        selectionStart = PixelPosition(e.GetPosition(PageSurface));
        selectionPixels = Rect.Empty; dragging = true;
        SelectionCanvas.CaptureMouse(); e.Handled = true;
    }
    private void SelectionMove(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        selectionPixels = new Rect(selectionStart, PixelPosition(e.GetPosition(PageSurface)));
        DrawSelection();
    }
    private async void SelectionEnd(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        selectionPixels = new Rect(selectionStart, PixelPosition(e.GetPosition(PageSurface)));
        dragging = false; SelectionCanvas.ReleaseMouseCapture(); e.Handled = true;
        if (selectionPixels.Width < 12 || selectionPixels.Height < 12) { ClearSelection(); return; }
        DrawSelection();
        await RecognizeAsync(false);
    }
    private void SelectionLost(object sender, MouseEventArgs e) => dragging = false;
    private void ClearSelection()
    {
        dragging = false;
        SelectionCanvas.ReleaseMouseCapture();
        selectionPixels = Rect.Empty; SelectionRectangle.Visibility = Visibility.Collapsed;
        SelectionHint.Text = "No selection yet";
    }
    private void DrawSelection()
    {
        if (vm.Image is not { } image || selectionPixels.IsEmpty) { SelectionRectangle.Visibility = Visibility.Collapsed; return; }
        double scaleX = PageSurface.Width / image.PixelWidth, scaleY = PageSurface.Height / image.PixelHeight;
        Canvas.SetLeft(SelectionRectangle, selectionPixels.X * scaleX);
        Canvas.SetTop(SelectionRectangle, selectionPixels.Y * scaleY);
        SelectionRectangle.Width = selectionPixels.Width * scaleX;
        SelectionRectangle.Height = selectionPixels.Height * scaleY;
        SelectionRectangle.Visibility = Visibility.Visible;
        SelectionHint.Text = $"Selection: {selectionPixels.Width:0} × {selectionPixels.Height:0} px";
    }

    private async Task RunOperationAsync(string status, Func<CancellationToken, Task> action)
    {
        if (vm.Busy) { vm.Status = "An engine task is running. Cancel it before starting another."; return; }
        using var cancellation = new CancellationTokenSource();
        long epoch = pageEpoch;
        operationCancellation = cancellation; vm.Busy = true; vm.Status = status;
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { if (!closing && epoch == pageEpoch) vm.Status = "Engine task canceled."; }
        catch (Exception ex) { if (!closing && epoch == pageEpoch) vm.Status = ex.Message; }
        finally
        {
            if (ReferenceEquals(operationCancellation, cancellation)) operationCancellation = null;
            vm.Busy = false;
        }
    }
    private void CancelOperation(object sender, RoutedEventArgs e) => operationCancellation?.Cancel();
    private async void RecognizeSelection(object sender, RoutedEventArgs e) => await RecognizeAsync(false);
    private async void RecognizePage(object sender, RoutedEventArgs e) => await RecognizeAsync(true);

    private async Task RecognizeAsync(bool wholePage)
    {
        if (vm.Image is not { } image) { vm.Status = "Open a page before recognizing text."; return; }
        if (!wholePage && selectionPixels.IsEmpty) { vm.Status = "Drag a box over a speech bubble first."; return; }
        long epoch = pageEpoch;
        var originalBefore = vm.Original;
        var crop = wholePage ? new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight)
            : new Int32Rect((int)selectionPixels.X, (int)selectionPixels.Y,
                Math.Min((int)Math.Ceiling(selectionPixels.Width), image.PixelWidth - (int)selectionPixels.X),
                Math.Min((int)Math.Ceiling(selectionPixels.Height), image.PixelHeight - (int)selectionPixels.Y));
        if (crop.Width < 1 || crop.Height < 1) return;
        await RunOperationAsync("Recognizing text locally…", async token =>
        {
            var temporary = Path.Combine(Path.GetTempPath(), "kotoba-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                var cropped = new CroppedBitmap(image, crop);
                cropped.Freeze();
                await Task.Run(() =>
                {
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(cropped));
                    using var output = File.Create(temporary); encoder.Save(output);
                }, token);
                var result = await engine.RequestAsync("ocr", state.Settings, new()
                {
                    ["image"] = temporary, ["source"] = state.Settings.SourceLanguage,
                    ["layout"] = wholePage ? "Auto" : state.Settings.OcrLayout
                }, token);
                token.ThrowIfCancellationRequested();
                if (epoch != pageEpoch || closing) return;
                if (vm.Original != originalBefore) { vm.Status = "OCR finished; result discarded because you edited the text."; return; }
                vm.Original = result.GetProperty("text").GetString() ?? ""; vm.Translated = "";
                StudioTabs.SelectedIndex = 0;
                vm.Status = string.IsNullOrWhiteSpace(vm.Original) ? "No text found. Try a tighter selection or another text layout."
                    : "Text recognized. Correct any mistakes, then translate offline.";
            }
            finally { try { File.Delete(temporary); } catch (IOException) { } }
        });
    }

    private async Task<JsonElement> DiagnoseAsync(CancellationToken token)
    {
        var result = await engine.RequestAsync("diagnose", state.Settings, cancellation: token);
        int models = result.GetProperty("models").GetArrayLength();
        int languages = result.GetProperty("languages").GetArrayLength();
        var warnings = result.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
        vm.EngineStatus = $"{models} translation model(s) · {languages} OCR language(s)";
        if (warnings.Length > 0) vm.EngineStatus += "\n" + string.Join("\n", warnings);
        return result;
    }
    private async void CheckEngine(object sender, RoutedEventArgs e)
        => await RunOperationAsync("Checking local engine…", async token =>
        {
            await DiagnoseAsync(token); vm.Status = "Engine check complete. See Translation Studio for details.";
        });

    private async void TranslateText(object sender, RoutedEventArgs e)
    {
        string text = vm.Original.Trim(), source = state.Settings.SourceLanguage, target = state.Settings.TargetLanguage;
        if (text.Length == 0) { vm.Status = "Recognize dialogue or type text to translate."; return; }
        long epoch = pageEpoch;
        await RunOperationAsync("Translating on your CPU…", async token =>
        {
            // Recheck model identity before every request so changed models cannot reuse stale cache entries.
            var report = await DiagnoseAsync(token);
            var model = report.GetProperty("models").EnumerateArray().FirstOrDefault(m =>
                m.GetProperty("source").GetString() == source && m.GetProperty("target").GetString() == target);
            var modelId = source == target ? "identity" : model.ValueKind == JsonValueKind.Undefined ? "missing" : model.GetProperty("id").GetString()!;
            string key = LibraryState.CacheKey(text, source, target, modelId);
            bool cached = state.TranslationCache.TryGetValue(key, out string? translated);
            string? warning = null;
            if (!cached)
            {
                var result = await engine.RequestAsync("translate", state.Settings, new()
                    { ["text"] = text, ["source"] = source, ["target"] = target, ["model"] = modelId == "missing" ? null : modelId }, token);
                translated = result.GetProperty("text").GetString() ?? "";
                if (result.TryGetProperty("warning", out var warningValue)) warning = warningValue.GetString();
                if (state.TranslationCache.Count >= 1500) state.TranslationCache.Clear();
                if (string.IsNullOrWhiteSpace(warning)) { state.TranslationCache[key] = translated; ScheduleSave(); }
            }
            token.ThrowIfCancellationRequested();
            if (epoch != pageEpoch || vm.Original.Trim() != text || closing)
            { vm.Status = "Translation finished; result discarded because the page or text changed."; return; }
            vm.Translated = translated!;
            vm.Status = !string.IsNullOrWhiteSpace(warning) ? warning : cached ? "Translation loaded from local cache. Save a note to keep it with this page."
                : "Translated offline. Save a note to keep it with this page.";
        });
    }

    private void SaveNote(object sender, RoutedEventArgs e)
    {
        if (comic is null || string.IsNullOrWhiteSpace(vm.Original) || string.IsNullOrWhiteSpace(vm.Translated))
        { vm.Status = "Open a comic and translate some text before saving a note."; return; }
        state.Notes.Add(new()
        {
            BookPath = comic.Path, PageIndex = pageIndex, SourceLanguage = state.Settings.SourceLanguage,
            TargetLanguage = state.Settings.TargetLanguage, Original = vm.Original.Trim(), Translated = vm.Translated.Trim()
        });
        RefreshNotes(); ScheduleSave(); vm.Status = "Translation note saved with this page.";
    }
    private void RefreshNotes()
        => NotesList.ItemsSource = state.Notes.Where(n => comic is not null && string.Equals(n.BookPath, comic.Path, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.PageIndex).ThenByDescending(n => n.Created).ToList();
    private async void SelectNote(object sender, SelectionChangedEventArgs e)
    {
        if (NotesList.SelectedItem is not TranslationNote note || vm.Busy) return;
        await ShowPageAsync(note.PageIndex);
        if (comic?.Path != note.BookPath || closing) return;
        Choose(SourceLanguage, note.SourceLanguage); Choose(TargetLanguage, note.TargetLanguage);
        vm.Original = note.Original; vm.Translated = note.Translated;
    }
    private void DeleteNote(object sender, RoutedEventArgs e)
    {
        if (NotesList.SelectedItem is not TranslationNote note) return;
        state.Notes.Remove(note); RefreshNotes(); ScheduleSave(); vm.Status = "Note deleted.";
    }
    private void CopyTranslation(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(vm.Translated)) return;
        try { Clipboard.SetText(vm.Translated); vm.Status = "Translation copied."; }
        catch (System.Runtime.InteropServices.COMException) { vm.Status = "Clipboard is busy. Try again."; }
    }
    private void ExportNotes(object sender, RoutedEventArgs e)
    {
        if (comic is null) return;
        var notes = state.Notes.Where(n => string.Equals(n.BookPath, comic.Path, StringComparison.OrdinalIgnoreCase)).OrderBy(n => n.PageIndex).ToList();
        if (notes.Count == 0) { vm.Status = "Save at least one translation note before exporting."; return; }
        var dialog = new SaveFileDialog { Filter = "Markdown (*.md)|*.md|JSON (*.json)|*.json", FileName = "KotobaReader-notes.md" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (dialog.FilterIndex == 2) File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(notes, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            else
            {
                var output = new StringBuilder("# KotobaReader translation notes\n\n");
                foreach (var note in notes)
                {
                    output.AppendLine($"## Page {note.PageIndex + 1} · {note.SourceLanguage} → {note.TargetLanguage}\n");
                    foreach (var line in note.Original.Split('\n')) output.AppendLine("> " + line);
                    output.AppendLine();
                    foreach (var line in note.Translated.Split('\n')) output.AppendLine("> " + line);
                    output.AppendLine();
                }
                File.WriteAllText(dialog.FileName, output.ToString(), Encoding.UTF8);
            }
            vm.Status = "Notes exported.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { vm.Status = "Export failed: " + ex.Message; }
    }

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        if (vm.Busy) { vm.Status = "Cancel the engine task before changing its settings."; return; }
        var dialog = new SettingsWindow(state.Settings, root, dataDirectory) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        state.Settings.PythonPath = dialog.PythonPath;
        state.Settings.TesseractPath = dialog.TesseractPath;
        state.Settings.ModelsPath = dialog.ModelsPath;
        state.Settings.TessdataPath = dialog.TessdataPath;
        engine.Dispose(); engine = new(root);
        vm.EngineStatus = "Settings saved. Check engine to verify dependencies."; ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (!ready || closing) return;
        saveTimer.Stop(); saveTimer.Start();
    }
    private bool SaveState()
    {
        try { store.Save(state); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { vm.Status = "Could not save library: " + ex.Message; return false; }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        { e.Handled = true; await OpenComicAsync(paths[0]); }
    }
    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { operationCancellation?.Cancel(); ClearSelection(); if (focusMode) ToggleFocus(); return; }
        if (e.Key == Key.F11) { ToggleFocus(); e.Handled = true; return; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.O) { OpenArchive(this, new()); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox or ComboBox || e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return;
        if (comic is null || Keyboard.Modifiers != ModifierKeys.None) return;
        int delta = e.Key switch { Key.Right => 1, Key.Left => -1, Key.PageDown => 1, Key.PageUp => -1, _ => 0 };
        if (delta != 0)
        {
            e.Handled = true;
            if (e.Key is Key.Left or Key.Right && RightToLeft.IsChecked == true) delta = -delta;
            await MoveAsync(delta);
        }
        else if (e.Key == Key.Home) { e.Handled = true; await ShowPageAsync(0); }
        else if (e.Key == Key.End) { e.Handled = true; await ShowPageAsync(comic.Pages.Count - 1); }
        else if (e.Key == Key.B) { e.Handled = true; ToggleBookmark(this, new()); }
    }
    private void ToggleFocus()
    {
        focusMode = !focusMode;
        if (focusMode)
        {
            previousLibrary = LibraryColumn.Width; previousStudio = TranslationColumn.Width;
            previousWindowStyle = WindowStyle; previousWindowState = WindowState;
            LibraryColumn.MinWidth = TranslationColumn.MinWidth = 0;
            LibraryColumn.Width = TranslationColumn.Width = new(0);
            WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized;
        }
        else
        {
            LibraryColumn.Width = previousLibrary; TranslationColumn.Width = previousStudio;
            LibraryColumn.MinWidth = 170; TranslationColumn.MinWidth = 280;
            WindowStyle = previousWindowStyle; WindowState = previousWindowState;
        }
        Dispatcher.BeginInvoke(ResizePage, DispatcherPriority.Loaded);
    }

    private async void OpenDemo(object sender, RoutedEventArgs e) => await OpenDemoAsync();
    private async Task OpenDemoAsync()
    {
        try
        {
            var path = DemoComic.Create(Path.Combine(dataDirectory, "Demo", "The Little Explorer"));
            Choose(OcrLayout, "Block"); Choose(SourceLanguage, "ja"); Choose(TargetLanguage, "en");
            await OpenComicAsync(path);
            vm.Status = "Original demo: select the Japanese dialogue. Use Single text block layout for these horizontal bubbles.";
        }
        catch (Exception ex) { vm.Status = "Could not create demo: " + ex.Message; }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        saveTimer.Stop();
        if (!SaveState() && MessageBox.Show(this, "Library changes could not be saved. Close anyway?", "Unsaved library", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        { e.Cancel = true; return; }
        closing = true; ++pageEpoch;
        pageCancellation?.Cancel(); operationCancellation?.Cancel(); engine.Dispose();
    }
}
