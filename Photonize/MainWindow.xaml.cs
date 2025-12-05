using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        PhotoListBox.PreviewMouseWheel += PhotoListBox_PreviewMouseWheel;
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

    private void PhotoListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Forward mouse wheel events to the parent ScrollViewer
        // This allows scrolling the window with the mouse wheel even when over the ListBox
        if (sender is UIElement element)
        {
            // Find the parent ScrollViewer
            var scrollViewer = FindParentScrollViewer(element);
            if (scrollViewer != null)
            {
                // Calculate scroll amount (negative delta = scroll down)
                double offset = scrollViewer.VerticalOffset - (e.Delta / 3.0);
                scrollViewer.ScrollToVerticalOffset(offset);

                // Mark the event as handled to prevent double-scrolling
                e.Handled = true;
            }
        }
    }

    private ScrollViewer? FindParentScrollViewer(DependencyObject child)
    {
        var parent = VisualTreeHelper.GetParent(child);

        if (parent == null)
            return null;

        if (parent is ScrollViewer scrollViewer)
            return scrollViewer;

        return FindParentScrollViewer(parent);
    }
}
