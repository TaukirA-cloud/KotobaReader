using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KotobaReader.Services;

public static class DemoComic
{
    public static string Create(string directory)
    {
        Directory.CreateDirectory(directory);
        string[] phrases = ["こんにちは。", "これは本です。", "おはようございます。"];
        for (int page = 0; page < phrases.Length; page++)
        {
            var file = Path.Combine(directory, $"page{page + 1}.png");
            if (File.Exists(file)) continue;
            var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen())
            {
                var ink = new SolidColorBrush(Color.FromRgb(27, 38, 52));
                var paper = new SolidColorBrush(Color.FromRgb(246, 243, 232));
                var teal = new SolidColorBrush(Color.FromRgb(102, 195, 173));
                var pen = new Pen(ink, 5);
                draw.DrawRectangle(paper, null, new(0, 0, 1000, 1400));
                Text(draw, "THE LITTLE EXPLORER", 44, 50, 48, ink);
                Text(draw, $"AN ORIGINAL KOTOBA READER DEMO / CHAPTER {page + 1:00}", 16, 52, 111, ink);
                draw.DrawRectangle(teal, pen, new(50, 160, 900, 580));
                draw.DrawEllipse(paper, null, new(750, 285), 85, 85);
                for (int i = 0; i < 12; i++)
                {
                    int height = 80 + (i * 37 + page * 29) % 140;
                    draw.DrawRectangle(ink, null, new(55 + i * 75, 740 - height, 62, height));
                    draw.DrawRectangle(paper, null, new(67 + i * 75, 760 - height, 12, 16));
                }
                Robot(draw, new(340 + page * 60, 500), pen, paper, ink);
                draw.DrawRoundedRectangle(Brushes.White, pen, new(490, 345, 410, 120), 40, 40);
                Text(draw, phrases[page], phrases[page].Length > 8 ? 30 : 40, 525, 379, Brushes.Black, "Yu Gothic");
                draw.DrawRectangle(Brushes.White, pen, new(50, 780, 430, 470));
                draw.DrawRectangle(ink, pen, new(515, 780, 435, 470));
                Robot(draw, new(260, 1040), pen, paper, ink);
                Text(draw, "EVERY PAGE", 34, 555, 865, paper);
                Text(draw, "IS A NEW", 34, 555, 925, paper);
                Text(draw, "BEGINNING.", 34, 555, 985, teal);
                Text(draw, "Select the Japanese speech bubble.", 22, 55, 1300, ink);
                Text(draw, "Recognize. Edit. Translate. Keep exploring.", 22, 55, 1340, ink);
                Text(draw, $"{page + 1:00}", 26, 885, 1320, ink);
            }
            var bitmap = new RenderTargetBitmap(1000, 1400, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(file); encoder.Save(output);
        }
        return directory;
    }
    private static void Text(DrawingContext draw, string value, double size, double x, double y, Brush brush, string font = "Segoe UI")
        => draw.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(font), size, brush, 1), new(x, y));
    private static void Robot(DrawingContext draw, Point center, Pen pen, Brush paper, Brush ink)
    {
        draw.DrawRoundedRectangle(paper, pen, new(center.X - 75, center.Y, 150, 150), 24, 24);
        draw.DrawEllipse(paper, pen, new(center.X, center.Y - 45), 105, 80);
        draw.DrawEllipse(ink, null, new(center.X - 38, center.Y - 50), 12, 18);
        draw.DrawEllipse(ink, null, new(center.X + 38, center.Y - 50), 12, 18);
        draw.DrawLine(pen, new(center.X - 20, center.Y - 14), new(center.X + 20, center.Y - 14));
        draw.DrawLine(pen, new(center.X, center.Y - 125), new(center.X + 15, center.Y - 158));
        draw.DrawEllipse(paper, pen, new(center.X + 15, center.Y - 165), 10, 10);
        draw.DrawLine(pen, new(center.X - 45, center.Y + 150), new(center.X - 60, center.Y + 190));
        draw.DrawLine(pen, new(center.X + 45, center.Y + 150), new(center.X + 60, center.Y + 190));
    }
}
