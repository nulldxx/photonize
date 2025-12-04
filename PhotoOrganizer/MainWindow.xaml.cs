using System.Windows;
using PhotoOrganizer.ViewModels;

namespace PhotoOrganizer;

public partial class MainWindow : Window
{
    public MainWindow(string? initialDirectory = null)
    {
        InitializeComponent();

        // Create ViewModel with initial directory if provided
        var viewModel = new MainViewModel(initialDirectory);
        DataContext = viewModel;

        // Subscribe to the loaded event
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Load photos from directory if available (either from command line or saved settings)
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
