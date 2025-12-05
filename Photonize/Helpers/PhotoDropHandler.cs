using System.Collections;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GongSolutions.Wpf.DragDrop;
using Photonize.Models;
using Photonize.ViewModels;

namespace Photonize.Helpers;

public class PhotoDropHandler : IDropTarget
{
    private DispatcherTimer? _autoScrollTimer;
    private ScrollViewer? _scrollViewer;
    private double _autoScrollSpeed;
    private const double ScrollMargin = 50.0; // Distance from edge to trigger scroll
    private const double MaxScrollSpeed = 10.0; // Maximum scroll speed per tick

    public void DragOver(IDropInfo dropInfo)
    {
        // Check if this is an external file drop from Windows Explorer
        if (dropInfo.Data is System.Windows.IDataObject dataObject &&
            dataObject.GetDataPresent(DataFormats.FileDrop))
        {
            dropInfo.Effects = DragDropEffects.Copy;
            return;
        }

        // Handle internal photo reordering (single or multiple items)
        if (dropInfo.Data != null && dropInfo.TargetCollection != null)
        {
            // Check if we're dragging PhotoItem(s)
            bool isPhotoItem = dropInfo.Data is PhotoItem;
            bool isPhotoCollection = dropInfo.Data is IEnumerable enumerable &&
                                      enumerable.Cast<object>().Any() &&
                                      enumerable.Cast<object>().First() is PhotoItem;

            if (isPhotoItem || isPhotoCollection)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                dropInfo.Effects = DragDropEffects.Move;

                // Handle auto-scrolling when dragging near edges
                HandleAutoScroll(dropInfo);
            }
            else
            {
                StopAutoScroll();
            }
        }
        else
        {
            StopAutoScroll();
        }
    }

    public async void Drop(IDropInfo dropInfo)
    {
        // Stop auto-scrolling when drop occurs
        StopAutoScroll();

        if (dropInfo.Data == null || dropInfo.TargetCollection == null)
            return;

        // Check if this is an external file drop from Windows Explorer
        if (dropInfo.Data is System.Windows.IDataObject dataObject &&
            dataObject.GetDataPresent(DataFormats.FileDrop))
        {
            var files = dataObject.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                // Call the ViewModel's import method
                if (Application.Current.MainWindow?.DataContext is MainViewModel vm)
                {
                    await vm.ImportPhotosAsync(files.ToList());
                }
            }
            return;
        }

        // Handle internal photo reordering
        var targetCollection = dropInfo.TargetCollection as IList;
        if (targetCollection == null)
            return;

        var insertIndex = dropInfo.InsertIndex;

        // Handle multiple selected items
        if (dropInfo.Data is IEnumerable enumerable && !(dropInfo.Data is string))
        {
            var itemsToMove = enumerable.Cast<PhotoItem>().ToList();
            if (itemsToMove.Count == 0)
                return;

            // Get the current indices of all items to move
            var itemIndices = itemsToMove
                .Select(item => new { Item = item, Index = targetCollection.IndexOf(item) })
                .Where(x => x.Index >= 0)
                .OrderBy(x => x.Index)
                .ToList();

            if (itemIndices.Count == 0)
                return;

            // Remove all items from their current positions (in reverse order to maintain indices)
            foreach (var itemInfo in itemIndices.OrderByDescending(x => x.Index))
            {
                targetCollection.RemoveAt(itemInfo.Index);

                // Adjust insert index if we removed an item before the insertion point
                if (itemInfo.Index < insertIndex)
                    insertIndex--;
            }

            // Insert all items at the new position
            foreach (var itemInfo in itemIndices)
            {
                if (insertIndex >= targetCollection.Count)
                    targetCollection.Add(itemInfo.Item);
                else
                    targetCollection.Insert(insertIndex, itemInfo.Item);
                insertIndex++;
            }
        }
        // Handle single item
        else if (dropInfo.Data is PhotoItem itemToMove)
        {
            // Find current position of the item
            var oldIndex = targetCollection.IndexOf(itemToMove);
            if (oldIndex < 0)
                return;

            // Remove from old position
            targetCollection.RemoveAt(oldIndex);

            // Adjust insert index if needed
            if (oldIndex < insertIndex)
                insertIndex--;

            // Insert at new position
            if (insertIndex >= targetCollection.Count)
                targetCollection.Add(itemToMove);
            else
                targetCollection.Insert(insertIndex, itemToMove);
        }
        else
        {
            return;
        }

        // Update display order after drop
        if (Application.Current.MainWindow?.DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateDisplayOrder();
        }
    }

    private void HandleAutoScroll(IDropInfo dropInfo)
    {
        // Find the ScrollViewer if we haven't already
        if (_scrollViewer == null && dropInfo.VisualTarget is DependencyObject visualTarget)
        {
            _scrollViewer = FindScrollViewer(visualTarget);
        }

        if (_scrollViewer == null)
            return;

        // Get the actual mouse position relative to the ScrollViewer
        Point mousePosition = Mouse.GetPosition(_scrollViewer);

        // Calculate scroll speed based on proximity to edges
        double newScrollSpeed = 0;

        // Check if near top edge
        if (mousePosition.Y >= 0 && mousePosition.Y < ScrollMargin)
        {
            // Scroll up - speed increases as we get closer to edge
            double ratio = 1.0 - (mousePosition.Y / ScrollMargin);
            newScrollSpeed = -ratio * MaxScrollSpeed;
        }
        // Check if near bottom edge
        else if (mousePosition.Y > (_scrollViewer.ActualHeight - ScrollMargin) &&
                 mousePosition.Y <= _scrollViewer.ActualHeight)
        {
            // Scroll down - speed increases as we get closer to edge
            double distanceFromBottom = _scrollViewer.ActualHeight - mousePosition.Y;
            double ratio = 1.0 - (distanceFromBottom / ScrollMargin);
            newScrollSpeed = ratio * MaxScrollSpeed;
        }
        else
        {
            // Not near any edge, stop scrolling
            StopAutoScroll();
            return;
        }

        _autoScrollSpeed = newScrollSpeed;

        // Start the timer if not already running
        if (_autoScrollTimer == null)
        {
            _autoScrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(20) // ~50 FPS
            };
            _autoScrollTimer.Tick += OnAutoScrollTick;
            _autoScrollTimer.Start();
        }
    }

    private void OnAutoScrollTick(object? sender, EventArgs e)
    {
        if (_scrollViewer == null)
        {
            StopAutoScroll();
            return;
        }

        // Scroll the viewer
        double newOffset = _scrollViewer.VerticalOffset + _autoScrollSpeed;
        _scrollViewer.ScrollToVerticalOffset(newOffset);
    }

    private void StopAutoScroll()
    {
        if (_autoScrollTimer != null)
        {
            _autoScrollTimer.Stop();
            _autoScrollTimer.Tick -= OnAutoScrollTick;
            _autoScrollTimer = null;
        }
        _autoScrollSpeed = 0;
    }

    private ScrollViewer? FindScrollViewer(DependencyObject? element)
    {
        if (element == null)
            return null;

        if (element is ScrollViewer scrollViewer)
            return scrollViewer;

        // Walk up the visual tree
        DependencyObject? parent = VisualTreeHelper.GetParent(element);
        if (parent != null)
            return FindScrollViewer(parent);

        // If not found in visual tree, try logical tree
        if (element is FrameworkElement frameworkElement)
        {
            parent = frameworkElement.Parent;
            if (parent != null)
                return FindScrollViewer(parent);
        }

        return null;
    }
}
