using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace KotobaReader;

public sealed class ReaderViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
    private string title = "Your next chapter starts here";
    public string Title { get => title; set => Set(ref title, value); }
    private string status = "Open a comic, drop one here, or explore the original demo.";
    public string Status { get => status; set => Set(ref status, value); }
    private string pageLabel = "NO COMIC OPEN";
    public string PageLabel { get => pageLabel; set => Set(ref pageLabel, value); }
    private string source = "";
    public string Original { get => source; set => Set(ref source, value); }
    private string translated = "";
    public string Translated { get => translated; set => Set(ref translated, value); }
    private string engineStatus = "Engine not checked";
    public string EngineStatus { get => engineStatus; set => Set(ref engineStatus, value); }
    private BitmapSource? image;
    public BitmapSource? Image { get => image; set => Set(ref image, value); }
    private bool busy;
    public bool Busy { get => busy; set => Set(ref busy, value); }
    private bool bookOpen;
    public bool BookOpen { get => bookOpen; set => Set(ref bookOpen, value); }
    private double progress;
    public double Progress { get => progress; set => Set(ref progress, value); }
    private string bookmark = "☆ Bookmark";
    public string Bookmark { get => bookmark; set => Set(ref bookmark, value); }
}
