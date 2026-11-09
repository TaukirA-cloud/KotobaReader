using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KotobaReader;
using KotobaReader.Core;
using KotobaReader.Services;

internal static class Program
{
    private static int result;
    private static int passed;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object[] arguments)
        => instance.GetType().GetMethod(name, Private)!.Invoke(instance, arguments);
    private static Task Async(object instance, string name, params object[] arguments) => (Task)Call(instance, name, arguments)!;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }

    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/smoke");
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("KOTOBA_DATA_DIR", Path.Combine(output, "library-" + Guid.NewGuid().ToString("N")));
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
            new Uri("/KotobaReader;component/Theme.xaml", UriKind.Relative)));
        app.Dispatcher.InvokeAsync(async () =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow(restoreSession: false) { Left = -18000, Top = -18000, Width = 1480, Height = 940 };
                window.Show();
                await Async(window, "OpenDemoAsync");
                await Task.Delay(300);
                var vm = Field<ReaderViewModel>(window, "vm");
                Check(vm.Image is { PixelWidth: 1000, PixelHeight: 1400 }, "Demo opens through the actual reader and image decoder");
                Check(Field<ComicDocument>(window, "comic").Pages.Count == 3, "Demo has three pages");
                Call(window, "ToggleBookmark", window, new RoutedEventArgs());
                Check(Field<BookEntry>(window, "currentBook").Bookmarks.Contains(0), "Bookmark action changes persistent book state");
                await Async(window, "ShowPageAsync", 1);
                Check(Field<BookEntry>(window, "currentBook").PageIndex == 1, "Reader records successfully decoded page position");
                var first = Async(window, "ShowPageAsync", 0);
                var latest = Async(window, "ShowPageAsync", 2);
                await Task.WhenAll(first, latest);
                Check(Field<int>(window, "pageIndex") == 2 && Field<BookEntry>(window, "currentBook").PageIndex == 2,
                    "Rapid page requests retain the newest decoded page");
                await Async(window, "ShowPageAsync", 0);
                vm.Original = "こんにちは。"; vm.Translated = "Hello.";
                Call(window, "SaveNote", window, new RoutedEventArgs());
                Check(Field<LibraryState>(window, "state").Notes.Count == 1, "Translation notes attach to the current book and page");
                Call(window, "SaveState");
                var persisted = new StateStore(Environment.GetEnvironmentVariable("KOTOBA_DATA_DIR")!).Load();
                Check(persisted.Notes.Count == 1 && persisted.Books.Single().Bookmarks.Contains(0), "Reader saves notes and bookmarks durably");

                // Exercise reverse-drag crop geometry at the current display scale.
                var surface = (Grid)window.FindName("PageSurface");
                Call(window, "ResizePage");
                var point = (Point)Call(window, "PixelPosition", new Point(surface.Width / 2, surface.Height / 2))!;
                Check(Math.Abs(point.X - 500) < 1 && Math.Abs(point.Y - 700) < 1, "Display coordinates map to OCR pixels independently of zoom");
                var before = surface.Width;
                Call(window, "ZoomIn", window, new RoutedEventArgs());
                Check(surface.Width > before, "Zoom increases rendered page size");
                Call(window, "ZoomOut", window, new RoutedEventArgs());

                if (Environment.GetEnvironmentVariable("KOTOBA_INTEGRATION") == "1")
                {
                    var engine = Field<EngineClient>(window, "engine");
                    var settings = Field<LibraryState>(window, "state").Settings;
                    var report = await engine.RequestAsync("diagnose", settings);
                    Check(report.GetProperty("models").GetArrayLength() > 0, "Real engine discovers installed translation model");
                    var image = vm.Image!;
                    var crop = new CroppedBitmap(image, new(520, 375, 300, 65));
                    string cropPath = Path.Combine(output, "dialogue.png");
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(crop));
                    using (var file = File.Create(cropPath)) encoder.Save(file);
                    var recognized = await engine.RequestAsync("ocr", settings,
                        new() { ["image"] = cropPath, ["source"] = "ja", ["layout"] = "Block" });
                    string original = recognized.GetProperty("text").GetString()!;
                    Check(new string(original.Where(char.IsLetter).ToArray()) == "こんにちは", "Real Tesseract recognizes Japanese dialogue from the comic without stray panel marks");
                    window.GetType().GetField("selectionPixels", Private)!.SetValue(window, new Rect(520, 375, 300, 65));
                    Call(window, "DrawSelection");
                    await Async(window, "RecognizeAsync", false);
                    Check(vm.Original.Contains("こんにちは"), "Reader selection action runs OCR through the UI workflow");
                    var translated = await engine.RequestAsync("translate", settings,
                        new() { ["text"] = original, ["source"] = "ja", ["target"] = "en" });
                    string translation = translated.GetProperty("text").GetString()!;
                    Check(translation.Contains("Hello", StringComparison.OrdinalIgnoreCase), "Real CPU model correctly translates the Japanese greeting into English");
                    Console.WriteLine($"OCR: {original}\nTranslation: {translation}");
                    vm.Original = original; vm.Translated = translation;
                    Call(window, "CheckEngine", window, new RoutedEventArgs());
                    while (vm.Busy) await Task.Delay(30);
                    Call(window, "TranslateText", window, new RoutedEventArgs());
                    while (vm.Busy) await Task.Delay(30);
                    Check(Field<LibraryState>(window, "state").TranslationCache.Count > 0, "Translation UI populates its local cache");
                    Call(window, "TranslateText", window, new RoutedEventArgs());
                    while (vm.Busy) await Task.Delay(30);
                    Check(vm.Status.Contains("cache"), "Repeated UI translation uses cached result");
                    Call(window, "SaveNote", window, new RoutedEventArgs());
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
                    bool canceled = false;
                    try
                    {
                        await engine.RequestAsync("translate", settings,
                            new() { ["text"] = string.Join("\n", Enumerable.Repeat("これは本です。", 100)),
                                ["source"] = "ja", ["target"] = "en" }, cancellation.Token);
                    }
                    catch (OperationCanceledException) { canceled = true; }
                    Check(canceled, "Cancel interrupts a real inference request");
                    var recovered = await engine.RequestAsync("diagnose", settings);
                    Check(recovered.GetProperty("models").GetArrayLength() > 0, "Engine restarts cleanly after cancellation without consuming a stale response");
                }
                await Task.Delay(100);
                window.UpdateLayout();
                var content = (FrameworkElement)window.Content;
                var capture = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                capture.Render(content);
                var screenshot = new PngBitmapEncoder(); screenshot.Frames.Add(BitmapFrame.Create(capture));
                using (var file = File.Create(Path.Combine(output, "reader.png"))) screenshot.Save(file);
                Check(File.Exists(Path.Combine(output, "reader.png")), "Reader renders screenshot artifact");
                Console.WriteLine($"\n{passed} Windows UI smoke checks passed. Artifacts: {output}");
            }
            catch (Exception ex) { result = 1; Console.Error.WriteLine(ex); }
            finally { window?.Close(); app.Shutdown(); }
        });
        app.Run();
        return result;
    }
}
