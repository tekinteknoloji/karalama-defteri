using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private void OpenPictureEditor(Window parent, StoredPicture picture, Action<BitmapSource> save)
    {
        var window = Owned("Resmi düzenle", 920, 650);
        window.Owner = parent;
        var root = new DockPanel { Margin = new Thickness(8) };
        var toolbar = new WrapPanel();
        Add(root, toolbar, Dock.Top);
        var tools = new ComboBox { ItemsSource = new[] { "Pikselleştir", "Bulanıklaştır", "Dikdörtgen", "Ok", "Kırp" }, SelectedIndex = 0, MinWidth = 148 };
        toolbar.Children.Add(tools);
        var help = new TextBlock { Text = "Resimde düzenlenecek alanı sürükleyerek seçin.", Margin = new Thickness(5) };
        Add(root, help, Dock.Top);
        var canvas = new Grid { ClipToBounds = true, Background = Theme.Brush("Surface") };
        var image = new Image { Stretch = Stretch.Uniform };
        var overlay = new Canvas { IsHitTestVisible = false };
        var selection = new System.Windows.Shapes.Rectangle { Stroke = Brushes.OrangeRed, StrokeThickness = 2, Visibility = Visibility.Collapsed };
        canvas.Children.Add(image);
        canvas.Children.Add(overlay);
        overlay.Children.Add(selection);
        root.Children.Add(canvas);
        window.Content = root;
        var current = Pictures.Decode(picture.Bytes);
        image.Source = current;
        var undo = new Stack<BitmapSource>();
        toolbar.Children.Add(Button("Geri al", () => { if (undo.TryPop(out var previous)) image.Source = current = previous; }));
        var apply = Button("Uygula", () => { if (undo.Count > 0) save(current); window.Close(); });
        apply.SetResourceReference(StyleProperty, "PrimaryButton");
        toolbar.Children.Add(apply);
        Point? start = null;
        Rect ImageRect()
        {
            var scale = Math.Min(canvas.ActualWidth / current.PixelWidth, canvas.ActualHeight / current.PixelHeight);
            var w = current.PixelWidth * scale;
            var h = current.PixelHeight * scale;
            return new Rect((canvas.ActualWidth - w) / 2, (canvas.ActualHeight - h) / 2, w, h);
        }
        Point Clamp(Point point, Rect rect) => new(Math.Clamp(point.X, rect.Left, rect.Right), Math.Clamp(point.Y, rect.Top, rect.Bottom));
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            var point = e.GetPosition(canvas);
            if (!ImageRect().Contains(point)) return;
            start = point;
            canvas.CaptureMouse();
        };
        canvas.MouseMove += (_, e) =>
        {
            if (start is not { } point) return;
            var area = new Rect(point, Clamp(e.GetPosition(canvas), ImageRect()));
            Canvas.SetLeft(selection, area.X); Canvas.SetTop(selection, area.Y);
            selection.Width = area.Width; selection.Height = area.Height;
            selection.Visibility = Visibility.Visible;
        };
        canvas.LostMouseCapture += (_, _) => { start = null; selection.Visibility = Visibility.Collapsed; };
        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (start is not { } point) return;
            var rect = ImageRect();
            var end = Clamp(e.GetPosition(canvas), rect);
            canvas.ReleaseMouseCapture();
            if ((end - point).Length < 4) return;
            Point Pixels(Point p) => new((p.X - rect.X) * current.PixelWidth / rect.Width, (p.Y - rect.Y) * current.PixelHeight / rect.Height);
            var next = MarkPicture(current, Pixels(point), Pixels(end), tools.SelectedIndex);
            if (ReferenceEquals(next, current)) return;
            undo.Push(current);
            image.Source = current = next;
        };
        window.ShowDialog();
    }

    private static BitmapSource MarkPicture(BitmapSource source, Point from, Point to, int tool)
    {
        var rect = new Rect(from, to);
        var x = (int)Math.Floor(rect.X);
        var y = (int)Math.Floor(rect.Y);
        var width = Math.Min(source.PixelWidth, (int)Math.Ceiling(rect.Right)) - x;
        var height = Math.Min(source.PixelHeight, (int)Math.Ceiling(rect.Bottom)) - y;
        if (tool == 4)
            return width > 0 && height > 0 ? new CroppedBitmap(source, new Int32Rect(x, y, width, height)) : source;
        var drawing = new DrawingVisual();
        var size = new Rect(0, 0, source.PixelWidth, source.PixelHeight);
        using (var ctx = drawing.RenderOpen())
        {
            ctx.DrawImage(source, size);
            if (tool <= 1 && width > 0 && height > 0)
            {
                var patch = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
                var destination = new Rect(x, y, width, height);
                if (tool == 0)
                {
                    var pixels = Pixelate(patch);
                    RenderOptions.SetBitmapScalingMode(drawing, BitmapScalingMode.NearestNeighbor);
                    ctx.DrawImage(pixels, destination);
                }
                else
                {
                    var visual = new DrawingVisual { Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 22 } };
                    using (var blur = visual.RenderOpen()) blur.DrawImage(patch, new Rect(0, 0, width, height));
                    var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    rendered.Render(visual);
                    ctx.DrawRectangle(Brushes.Black, null, destination);
                    ctx.DrawImage(rendered, destination);
                }
            }
            else if (tool == 2) ctx.DrawRectangle(null, new Pen(Brushes.Red, 4), rect);
            else if (tool == 3)
            {
                var pen = new Pen(Brushes.Red, 4);
                var direction = to - from;
                direction.Normalize();
                var side = new Vector(-direction.Y, direction.X);
                var tip = Math.Min(22, (to - from).Length / 2);
                ctx.DrawLine(pen, from, to);
                ctx.DrawLine(pen, to, to - direction * tip + side * (tip / 2));
                ctx.DrawLine(pen, to, to - direction * tip - side * (tip / 2));
            }
        }
        var result = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(drawing);
        return result;
    }

    private static BitmapSource Pixelate(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var format = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[width * height * 4];
        format.CopyPixels(pixels, width * 4, 0);
        const int block = 18;
        var smallWidth = (width + block - 1) / block;
        var smallHeight = (height + block - 1) / block;
        var output = new byte[smallWidth * smallHeight * 4];
        for (var y = 0; y < smallHeight; y++)
        for (var x = 0; x < smallWidth; x++)
        {
            var red = 0; var green = 0; var blue = 0; var count = 0;
            for (var row = y * block; row < Math.Min(height, (y + 1) * block); row++)
            for (var col = x * block; col < Math.Min(width, (x + 1) * block); col++)
            {
                var input = (row * width + col) * 4;
                blue += pixels[input]; green += pixels[input + 1]; red += pixels[input + 2]; count++;
            }
            var index = (y * smallWidth + x) * 4;
            output[index] = (byte)(blue / count);
            output[index + 1] = (byte)(green / count);
            output[index + 2] = (byte)(red / count);
            output[index + 3] = 255;
        }
        return BitmapSource.Create(smallWidth, smallHeight, 96, 96, PixelFormats.Bgra32, null, output, smallWidth * 4);
    }
}
