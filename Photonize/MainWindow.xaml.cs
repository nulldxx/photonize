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
    public MainWindow(string? initialDirectory = null)
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
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.HasUnsavedChanges)
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
