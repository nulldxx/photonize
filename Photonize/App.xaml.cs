using System.IO;
using System.Windows;
using FFMpegCore;

namespace Photonize;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize FFmpeg for video thumbnail extraction
        await InitializeFFmpegAsync();

        // Parse command line arguments
        string? initialDirectory = null;
        List<string>? filesToExport = null;

        for (int i = 0; i < e.Args.Length; i++)
        {
            if ((e.Args[i] == "-d" || e.Args[i] == "--directory") && i + 1 < e.Args.Length)
            {
                initialDirectory = e.Args[i + 1];
            }
            else if (e.Args[i] == "-e" || e.Args[i] == "--export-webp")
            {
                // Collect all remaining arguments as file paths
                filesToExport = new List<string>();
                for (int j = i + 1; j < e.Args.Length; j++)
                {
                    if (File.Exists(e.Args[j]))
                    {
                        filesToExport.Add(e.Args[j]);
                    }
                }
                break;
            }
        }

        // Create and show main window
        var mainWindow = new MainWindow(initialDirectory, filesToExport);
        mainWindow.Show();
    }

    private Task InitializeFFmpegAsync()
    {
        try
        {
            // Try common FFmpeg installation locations
            var possiblePaths = new[]
            {
                // Bundled with application
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg"),
                // User's local app data
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Photonize", "ffmpeg"),
                // System-wide Program Files installation
                @"C:\Program Files\ffmpeg\bin",
                @"C:\ffmpeg\bin",
                // Chocolatey installation path
                @"C:\ProgramData\chocolatey\lib\ffmpeg\tools\ffmpeg\bin"
            };

            string? foundPath = null;
            foreach (var path in possiblePaths)
            {
                var ffmpegExe = Path.Combine(path, "ffmpeg.exe");
                if (File.Exists(ffmpegExe))
                {
                    foundPath = path;
                    break;
                }
            }

            if (foundPath != null)
            {
                // Configure FFMpegCore to use the found FFmpeg binaries
                GlobalFFOptions.Configure(options => options.BinaryFolder = foundPath);
                Console.WriteLine($"FFmpeg found at: {foundPath}");
            }
            else
            {
                // FFmpeg not found - video features will be unavailable
                Console.WriteLine("FFmpeg not found. Video thumbnail extraction will not work.");
                Console.WriteLine("To enable video support, install FFmpeg and place it in one of these locations:");
                foreach (var path in possiblePaths)
                {
                    Console.WriteLine($"  - {path}");
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash - video features will be unavailable
            Console.WriteLine($"Failed to initialize FFmpeg: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
