using System.Windows;
using KotobaReader.Core;

namespace KotobaReader;

public partial class SettingsWindow : Window
{
    public string PythonPath => PythonBox.Text.Trim().Trim('"');
    public string TesseractPath => TesseractBox.Text.Trim().Trim('"');
    public string ModelsPath => ModelsBox.Text.Trim().Trim('"');
    public string TessdataPath => TessdataBox.Text.Trim().Trim('"');
    public SettingsWindow(ReaderSettings settings, string root, string dataDirectory)
    {
        InitializeComponent();
        PythonBox.Text = settings.PythonPath; TesseractBox.Text = settings.TesseractPath;
        ModelsBox.Text = settings.ModelsPath; TessdataBox.Text = settings.TessdataPath;
        SetupText.Text = $"Run scripts/setup-engine.ps1 in {root}. Install Tesseract and its jpn, jpn_vert, and eng language files. Full instructions: docs/SETUP.md. Downloading models requires internet once; reading and translation then work offline.";
        StorageText.Text = dataDirectory;
    }
    private void Save(object sender, RoutedEventArgs e) => DialogResult = true;
}
