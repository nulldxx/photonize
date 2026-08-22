using System.IO;
using System.Windows;
using Photonize.Services;

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
            if (FFmpegLocator.Initialize())
            {
                Console.WriteLine($"FFmpeg found at: {FFmpegLocator.BinaryFolder}");
            }
            else
            {
                // FFmpeg not found - show warning message
                Console.WriteLine("FFmpeg not found. Video thumbnail extraction will not work.");

                var result = MessageBox.Show(
                    "FFmpeg is not installed on your system.\n\n" +
                    "Video files will be displayed but thumbnails, preview, Slice and Splice will not work.\n\n" +
                    "Would you like to download FFmpeg now?",
                    "FFmpeg Not Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "https://www.ffmpeg.org/download.html",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Could not open browser. Please visit:\nhttps://www.ffmpeg.org/download.html\n\nError: {ex.Message}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash - video features will be unavailable
            Console.WriteLine($"Failed to initialize FFmpeg: {ex.Message}");
            MessageBox.Show(
                $"Failed to initialize FFmpeg: {ex.Message}\n\n" +
                "Video features may not work properly.",
                "FFmpeg Initialization Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        return Task.CompletedTask;
    }
}
