using System.Windows;
using PhotoOrganizer.ViewModels;

namespace PhotoOrganizer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Subscribe to the drag-drop completed event if needed
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Load photos from saved directory if available
        if (DataContext is MainViewModel viewModel)
        {
            if (!string.IsNullOrEmpty(viewModel.DirectoryPath) &&
                System.IO.Directory.Exists(viewModel.DirectoryPath))
            {
                viewModel.LoadPhotosCommand.Execute(null);
            }
        }
    }
}
