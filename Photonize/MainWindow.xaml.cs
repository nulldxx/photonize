using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Photonize.Models;
using Photonize.ViewModels;

namespace Photonize;

public partial class MainWindow : Window
{
    public MainWindow(string? initialDirectory = null, List<string>? filesToExport = null)
    {
        InitializeComponent();

        // Create ViewModel with initial directory if provided
        var viewModel = new MainViewModel(initialDirectory);
        DataContext = viewModel;

        // Subscribe to events
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        PhotoListBox.SelectionChanged += PhotoListBox_SelectionChanged;
        PhotoListBox.PreviewMouseWheel += PhotoListBox_PreviewMouseWheel;
        PhotoListBox.PreviewKeyDown += PhotoListBox_PreviewKeyDown;

        // If files were provided for export, trigger export after window loads
        if (filesToExport != null && filesToExport.Count > 0)
        {
            Loaded += async (s, e) => await viewModel.ExportFilesDirectlyAsync(filesToExport);
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            // Check for unsaved changes first
            if (viewModel.HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have loaded photos and set a rename prefix but haven't applied the changes.\n\n" +
                    "Are you sure you want to quit without applying the rename?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true; // Cancel the closing
                    return;
                }
            }

            // If we're actually closing (not cancelled), dispose the ViewModel
            // This will cancel any background thumbnail loading operations
            if (!e.Cancel)
            {
                viewModel.Dispose();
            }
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Photos are now automatically loaded when DirectoryPath is set in the ViewModel
        // No need to manually trigger loading here
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

    private void PhotoListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Intercept Ctrl-A to select only files (not folders)
        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (DataContext is MainViewModel viewModel)
            {
                // Clear existing selection
                PhotoListBox.SelectedItems.Clear();

                // Select only non-folder items
                foreach (var photo in viewModel.Photos.Where(p => !p.IsFolder))
                {
                    PhotoListBox.SelectedItems.Add(photo);
                }

                // Mark the event as handled to prevent default Ctrl-A behavior
                e.Handled = true;
            }
        }
        // Handle arrow key navigation with row wrapping
        else if ((e.Key == Key.Right || e.Key == Key.Left) &&
                 Keyboard.Modifiers == ModifierKeys.None &&
                 PhotoListBox.Items.Count > 0)
        {
            HandleRowWrappingNavigation(e.Key);
            e.Handled = true;
        }
    }

    private void HandleRowWrappingNavigation(Key key)
    {
        int currentIndex = PhotoListBox.SelectedIndex;

        // If nothing selected, select first item
        if (currentIndex < 0)
        {
            PhotoListBox.SelectedIndex = 0;
            return;
        }

        // Calculate items per row based on container positions
        var itemsPerRow = CalculateItemsPerRow();
        if (itemsPerRow <= 0) return;

        int totalItems = PhotoListBox.Items.Count;
        int currentRow = currentIndex / itemsPerRow;
        int currentColumn = currentIndex % itemsPerRow;

        if (key == Key.Right)
        {
            // Check if at the end of a row
            bool isLastInRow = currentColumn == itemsPerRow - 1 || currentIndex == totalItems - 1;

            if (isLastInRow && currentIndex < totalItems - 1)
            {
                // Move to first item of next row
                PhotoListBox.SelectedIndex = (currentRow + 1) * itemsPerRow;
                // Ensure we don't go beyond the last item
                if (PhotoListBox.SelectedIndex >= totalItems)
                {
                    PhotoListBox.SelectedIndex = totalItems - 1;
                }
            }
            else if (currentIndex < totalItems - 1)
            {
                // Normal right navigation
                PhotoListBox.SelectedIndex = currentIndex + 1;
            }
        }
        else if (key == Key.Left)
        {
            // Check if at the beginning of a row
            bool isFirstInRow = currentColumn == 0;

            if (isFirstInRow && currentIndex > 0)
            {
                // Move to last item of previous row
                int previousRowStart = (currentRow - 1) * itemsPerRow;
                int previousRowEnd = Math.Min(currentRow * itemsPerRow - 1, totalItems - 1);
                PhotoListBox.SelectedIndex = previousRowEnd;
            }
            else if (currentIndex > 0)
            {
                // Normal left navigation
                PhotoListBox.SelectedIndex = currentIndex - 1;
            }
        }

        // Ensure the selected item is visible
        PhotoListBox.ScrollIntoView(PhotoListBox.SelectedItem);
    }

    private int CalculateItemsPerRow()
    {
        if (PhotoListBox.Items.Count == 0)
            return 0;

        // Get the first two ListBoxItem containers and check their vertical positions
        var firstContainer = PhotoListBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
        if (firstContainer == null)
            return 1;

        double firstItemTop = GetItemTopPosition(firstContainer);

        // Find the first item that's on a different row
        for (int i = 1; i < PhotoListBox.Items.Count; i++)
        {
            var container = PhotoListBox.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
            if (container != null)
            {
                double itemTop = GetItemTopPosition(container);
                // If this item is lower than the first item, we've reached the next row
                if (Math.Abs(itemTop - firstItemTop) > 5) // 5px tolerance for floating point comparison
                {
                    return i; // i is the number of items in the first row
                }
            }
        }

        // All items are on one row
        return PhotoListBox.Items.Count;
    }

    private double GetItemTopPosition(ListBoxItem item)
    {
        // Get the position of the item relative to the ListBox
        var transform = item.TransformToAncestor(PhotoListBox);
        var position = transform.Transform(new Point(0, 0));
        return position.Y;
    }

    private void PhotoScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Handle mouse wheel scrolling even during drag operations
        if (sender is ScrollViewer scrollViewer)
        {
            // Calculate scroll amount (negative delta = scroll down)
            double offset = scrollViewer.VerticalOffset - (e.Delta / 3.0);
            scrollViewer.ScrollToVerticalOffset(offset);

            // Mark as handled to prevent the event from bubbling
            e.Handled = true;
        }
    }

    private void DirectoryPathTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // When Enter is pressed, commit the binding and move focus away
        if (e.Key == Key.Enter)
        {
            if (sender is TextBox textBox)
            {
                // Force the binding to update
                var bindingExpression = textBox.GetBindingExpression(TextBox.TextProperty);
                bindingExpression?.UpdateSource();

                // Move focus away from the TextBox to prevent further editing
                Keyboard.ClearFocus();
            }

            e.Handled = true;
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
