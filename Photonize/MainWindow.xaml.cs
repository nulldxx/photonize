using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Photonize.Models;
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

        // Subscribe to events
        Loaded += MainWindow_Loaded;
        PhotoListBox.SelectionChanged += PhotoListBox_SelectionChanged;
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

    private void PhotoListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Update the ViewModel with the current selection
        if (DataContext is MainViewModel viewModel)
        {
            var selectedPhotos = PhotoListBox.SelectedItems.Cast<PhotoItem>();
            viewModel.UpdateSelectedPhotos(selectedPhotos);
        }
    }
}
