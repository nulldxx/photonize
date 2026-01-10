using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFMpegCore;
using FFMpegCore.Exceptions;

namespace Photonize.Services;

public class ThumbnailGenerator
{
    private static readonly string[] SupportedExtensions =
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp",
        ".mp4", ".mov", ".m4v"
    };

    public static bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension == ".jpg" || extension == ".jpeg" || extension == ".png" ||
               extension == ".bmp" || extension == ".gif" || extension == ".tiff" ||
               extension == ".tif" || extension == ".webp";
    }

    public static bool IsVideoFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension == ".mp4" || extension == ".mov" || extension == ".m4v";
    }

    public async Task<BitmapImage?> GenerateThumbnailAsync(string filePath, int maxSize)
    {
        // Route to appropriate thumbnail generator based on file type
        if (IsVideoFile(filePath))
        {
            return await GenerateVideoThumbnailAsync(filePath, maxSize);
        }
        else
        {
            return await GenerateImageThumbnailAsync(filePath, maxSize);
        }
    }

    private async Task<BitmapImage?> GenerateImageThumbnailAsync(string filePath, int maxSize)
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

    public async Task<List<string>> GetMediaFilesAsync(string directoryPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                    return new List<string>();

                return Directory.GetFiles(directoryPath)
                    .Where(f => IsImageFile(f) || IsVideoFile(f))
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

    private async Task<BitmapImage> GenerateVideoThumbnailAsync(string filePath, int maxSize)
    {
        try
        {
            if (!File.Exists(filePath))
                return GenerateBrokenVideoThumbnail(maxSize);

            // Analyze the video to get duration and dimensions
            var mediaInfo = await FFProbe.AnalyseAsync(filePath);
            var duration = mediaInfo.Duration;

            // Extract frame at 1 second, or earlier if video is shorter
            var captureTime = duration > TimeSpan.FromSeconds(1)
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromMilliseconds(100);

            // Get video dimensions and calculate thumbnail size preserving aspect ratio
            var videoStream = mediaInfo.PrimaryVideoStream;
            int thumbnailWidth, thumbnailHeight;
            
            if (videoStream != null && videoStream.Width > 0 && videoStream.Height > 0)
            {
                double aspectRatio = (double)videoStream.Width / videoStream.Height;
                
                if (aspectRatio > 1) // Landscape
                {
                    thumbnailWidth = maxSize;
                    thumbnailHeight = (int)(maxSize / aspectRatio);
                }
                else // Portrait or square
                {
                    thumbnailWidth = (int)(maxSize * aspectRatio);
                    thumbnailHeight = maxSize;
                }
            }
            else
            {
                // Fallback to square if dimensions can't be determined
                thumbnailWidth = maxSize;
                thumbnailHeight = maxSize;
            }

            // Create a temporary file for the thumbnail
            var tempFile = Path.Combine(Path.GetTempPath(), $"thumb_{Guid.NewGuid()}.png");

            try
            {
                // Extract frame to temporary file with proper aspect ratio
                await FFMpeg.SnapshotAsync(filePath, tempFile, new System.Drawing.Size(thumbnailWidth, thumbnailHeight), captureTime);

                // Load the temporary file into a BitmapImage
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(tempFile, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze(); // Make it cross-thread accessible

                return bitmap;
            }
            finally
            {
                // Clean up temporary file
                try
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (FFMpegException ffmpegEx)
        {
            Console.WriteLine($"FFmpeg error for {filePath}: {ffmpegEx.Message}");
            return GenerateBrokenVideoThumbnail(maxSize);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating video thumbnail for {filePath}: {ex.Message}");
            return GenerateBrokenVideoThumbnail(maxSize);
        }
    }

    private BitmapImage GenerateBrokenVideoThumbnail(int size)
    {
        // Create a visual representation of a broken video file
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // Draw background
            var bgBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)); // Dark gray
            context.DrawRectangle(bgBrush, null, new Rect(0, 0, size, size));

            // Draw film strip icon outline
            var iconBrush = new SolidColorBrush(Color.FromRgb(180, 60, 60)); // Red
            var iconPen = new Pen(iconBrush, 2);

            // Film strip frame
            var frameRect = new Rect(size * 0.2, size * 0.25, size * 0.6, size * 0.5);
            context.DrawRectangle(null, iconPen, frameRect);

            // Film strip holes (top and bottom)
            var holeBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            for (int i = 0; i < 4; i++)
            {
                double x = size * (0.25 + i * 0.15);
                // Top holes
                context.DrawRectangle(holeBrush, null, new Rect(x, size * 0.15, size * 0.08, size * 0.06));
                // Bottom holes
                context.DrawRectangle(holeBrush, null, new Rect(x, size * 0.79, size * 0.08, size * 0.06));
            }

            // Draw X to indicate broken/error
            var xPen = new Pen(iconBrush, 3);
            context.DrawLine(xPen, new Point(size * 0.35, size * 0.35), new Point(size * 0.65, size * 0.65));
            context.DrawLine(xPen, new Point(size * 0.65, size * 0.35), new Point(size * 0.35, size * 0.65));
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
