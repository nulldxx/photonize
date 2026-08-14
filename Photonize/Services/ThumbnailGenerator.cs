using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFMpegCore;
using FFMpegCore.Exceptions;

namespace Photonize.Services;

public class ThumbnailGenerator
{
    private static readonly string[] ImageExtensions =
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp", ".dng"
    };

    /// <summary>
    /// Raw formats, shown from the JPEG preview embedded in the file rather than by
    /// demosaicing the sensor data. A subset of <see cref="ImageExtensions"/>.
    /// </summary>
    private static readonly string[] RawExtensions = { ".dng" };

    private static readonly string[] VideoExtensions = { ".mp4", ".mov", ".m4v" };

    /// <summary>Every extension the grid enumerates, images and videos together.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions =
        ImageExtensions.Concat(VideoExtensions).ToArray();

    public static bool IsImageFile(string filePath) => HasExtension(filePath, ImageExtensions);

    public static bool IsRawFile(string filePath) => HasExtension(filePath, RawExtensions);

    public static bool IsVideoFile(string filePath) => HasExtension(filePath, VideoExtensions);

    private static bool HasExtension(string filePath, string[] extensions) =>
        extensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);

    public async Task<BitmapSource?> GenerateThumbnailAsync(string filePath, int maxSize)
    {
        // Route to appropriate thumbnail generator based on file type
        if (IsVideoFile(filePath))
        {
            return await GenerateVideoThumbnailAsync(filePath, maxSize);
        }

        var thumbnail = await GenerateImageThumbnailAsync(filePath, maxSize);

        // A raw file that neither the embedded preview nor a WIC codec could read gets a
        // placeholder rather than the permanently blank tile other failures leave behind.
        if (thumbnail == null && IsRawFile(filePath))
        {
            return GenerateBrokenImageThumbnail(maxSize);
        }

        return thumbnail;
    }

    private async Task<BitmapSource?> GenerateImageThumbnailAsync(string filePath, int maxSize)
    {
        return await Task.Run(() => Decode(filePath, maxSize));
    }

    public async Task<BitmapSource?> GeneratePreviewAsync(string filePath)
    {
        return await Task.Run(() => Decode(filePath, 0));
    }

    /// <summary>
    /// Decodes an image for display, scaling during decode when a width is given.
    /// </summary>
    /// <param name="decodePixelWidth">Target width for a grid tile, or 0 for full size.</param>
    private static BitmapSource? Decode(string filePath, int decodePixelWidth)
    {
        if (!File.Exists(filePath))
            return null;

        if (IsRawFile(filePath))
        {
            var preview = DecodeRawPreview(filePath, decodePixelWidth);
            if (preview != null)
                return preview;

            // No usable embedded preview. Fall through and let whichever DNG codec is
            // registered on this machine have a go at the file itself.
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
            if (decodePixelWidth > 0)
                bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze(); // Make it cross-thread accessible

            return bitmap;
        }
        catch (Exception ex)
        {
            // Log error or handle corrupted images
            Console.WriteLine($"Error loading {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Decodes a raw file from the JPEG preview embedded in it — the same image
    /// Explorer and Lightroom's grid show — which costs a JPEG decode rather than a
    /// demosaic of the full sensor. Returns null when the file holds no preview we can
    /// use (an uncompressed one, or none at all), leaving the caller to fall back to WIC.
    /// </summary>
    private static BitmapSource? DecodeRawPreview(string filePath, int decodePixelWidth)
    {
        try
        {
            DngPreview? preview;
            using (var file = File.OpenRead(filePath))
            {
                preview = DngPreviewExtractor.ExtractLargestPreview(file);
            }

            if (preview == null)
                return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
            if (decodePixelWidth > 0)
                bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.StreamSource = new MemoryStream(preview.JpegBytes);
            bitmap.EndInit();
            bitmap.Freeze();

            return ApplyOrientation(bitmap, preview.Orientation);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading embedded preview from {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Applies a TIFF/EXIF orientation value (1-8). A DNG stores its preview in the raw
    /// image's own orientation, so a portrait frame arrives sideways without this — for
    /// ordinary JPEGs WIC has already done it by the time the bitmap exists.
    /// </summary>
    private static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        static Transform Combine(Transform first, Transform second)
        {
            var group = new TransformGroup();
            group.Children.Add(first);
            group.Children.Add(second);
            return group;
        }

        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => Combine(new RotateTransform(90), new ScaleTransform(-1, 1)),
            6 => new RotateTransform(90),
            7 => Combine(new RotateTransform(270), new ScaleTransform(-1, 1)),
            8 => new RotateTransform(270),
            _ => null
        };

        if (transform == null)
            return source;

        var transformed = new TransformedBitmap(source, transform);
        transformed.Freeze();
        return transformed;
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

        return RenderToBitmapImage(visual, size);
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

        return RenderToBitmapImage(visual, size);
    }

    /// <summary>
    /// Tile for an image that could not be decoded. Raw files are what usually land
    /// here: a DNG with no embedded preview, on a machine with no DNG codec registered.
    /// </summary>
    private BitmapImage GenerateBrokenImageThumbnail(int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // Draw background
            var bgBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)); // Dark gray
            context.DrawRectangle(bgBrush, null, new Rect(0, 0, size, size));

            // Picture frame outline
            var iconBrush = new SolidColorBrush(Color.FromRgb(180, 60, 60)); // Red
            var iconPen = new Pen(iconBrush, 2);
            var frameRect = new Rect(size * 0.2, size * 0.25, size * 0.6, size * 0.5);
            context.DrawRectangle(null, iconPen, frameRect);

            // A hill and a sun, the usual shorthand for "a picture"
            var contentBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            context.DrawEllipse(contentBrush, null, new Point(size * 0.35, size * 0.4), size * 0.05, size * 0.05);
            var hill = new StreamGeometry();
            using (var figure = hill.Open())
            {
                figure.BeginFigure(new Point(size * 0.22, size * 0.73), true, true);
                figure.LineTo(new Point(size * 0.45, size * 0.5), true, false);
                figure.LineTo(new Point(size * 0.78, size * 0.73), true, false);
            }
            hill.Freeze();
            context.DrawGeometry(contentBrush, null, hill);

            // Draw X to indicate broken/error
            var xPen = new Pen(iconBrush, 3);
            context.DrawLine(xPen, new Point(size * 0.35, size * 0.35), new Point(size * 0.65, size * 0.65));
            context.DrawLine(xPen, new Point(size * 0.65, size * 0.35), new Point(size * 0.35, size * 0.65));
        }

        return RenderToBitmapImage(visual, size);
    }

    /// <summary>Rasterises a drawn placeholder into a frozen, cross-thread-safe bitmap.</summary>
    private static BitmapImage RenderToBitmapImage(DrawingVisual visual, int size)
    {
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

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
