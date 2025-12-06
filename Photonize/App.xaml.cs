using System.IO;
using System.Windows;

namespace Photonize;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Debug: Show what arguments were received
        MessageBox.Show(
            $"Received {e.Args.Length} argument(s):\n\n{string.Join("\n", e.Args)}",
            "Debug - Command Line Arguments",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

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
}
