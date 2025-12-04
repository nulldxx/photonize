using System.Windows;

namespace PhotoOrganizer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Parse command line arguments
        string? initialDirectory = null;
        for (int i = 0; i < e.Args.Length; i++)
        {
            if ((e.Args[i] == "-d" || e.Args[i] == "--directory") && i + 1 < e.Args.Length)
            {
                initialDirectory = e.Args[i + 1];
                break;
            }
        }

        // Create and show main window with initial directory
        var mainWindow = new MainWindow(initialDirectory);
        mainWindow.Show();
    }
}
