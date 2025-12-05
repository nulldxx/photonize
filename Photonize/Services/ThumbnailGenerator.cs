using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photonize.Services;

public class ThumbnailGenerator
{
    private static readonly string[] SupportedExtensions =
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp"
    };

    public static bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    public async Task<BitmapImage?> GenerateThumbnailAsync(string filePath, int maxSize)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
                bitmap.DecodePixelWidth = maxSize;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze(); // Make it cross-thread accessible

                return bitmap;
            }
            catch (Exception ex)
            {
                // Log error or handle corrupted images
                Console.WriteLine($"Error loading thumbnail for {filePath}: {ex.Message}");
                return null;
            }
        });
    }

    public async Task<BitmapImage?> GeneratePreviewAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze(); // Make it cross-thread accessible

                return bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading preview for {filePath}: {ex.Message}");
                return null;
            }
        });
    }

    public async Task<List<string>> GetImageFilesAsync(string directoryPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                    return new List<string>();

                return Directory.GetFiles(directoryPath)
                    .Where(IsImageFile)
                    .OrderBy(f => f)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning directory {directoryPath}: {ex.Message}");
                return new List<string>();
            }
        });
    }

    public BitmapImage GenerateFolderThumbnail(int size)
    {
        // Create a visual representation of a folder
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // Draw folder background (main body)
            var folderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow/gold color
            var folderPen = new Pen(new SolidColorBrush(Color.FromRgb(200, 150, 0)), 2);

            // Folder tab (top part)
            var tabRect = new Rect(size * 0.1, size * 0.2, size * 0.4, size * 0.15);
            context.DrawRoundedRectangle(folderBrush, folderPen, tabRect, 3, 3);

            // Folder body (main part)
            var bodyRect = new Rect(size * 0.1, size * 0.3, size * 0.8, size * 0.5);
            context.DrawRoundedRectangle(folderBrush, folderPen, bodyRect, 5, 5);

            // Draw a darker overlay for 3D effect
            var overlayBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
            var overlayRect = new Rect(size * 0.1, size * 0.55, size * 0.8, size * 0.25);
            context.DrawRectangle(overlayBrush, null, overlayRect);
        }

        // Render to bitmap
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        // Convert to BitmapImage
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        return bitmapImage;
    }
}
