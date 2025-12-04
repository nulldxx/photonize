using System.IO;
using System.Windows.Media.Imaging;

namespace Photonize.Services;

public class ThumbnailGenerator
{
    private static readonly string[] SupportedExtensions =
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif"
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
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
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
}
