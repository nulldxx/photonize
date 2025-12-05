using System.Linq;
using System.Windows;
using Photonize.ViewModels;

namespace Photonize;

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

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        // Check if the dragged data contains files
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        // Check if the dragged data contains files
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // Get the dropped file paths
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            if (files != null && files.Length > 0)
            {
                // Call the ViewModel's import method
                if (DataContext is MainViewModel viewModel)
                {
                    await viewModel.ImportPhotosAsync(files.ToList());
                }
            }
        }
        e.Handled = true;
    }
}
