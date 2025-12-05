using System.Collections;
using System.Linq;
using System.Windows;
using GongSolutions.Wpf.DragDrop;
using Photonize.Models;
using Photonize.ViewModels;

namespace Photonize.Helpers;

public class PhotoDropHandler : IDropTarget
{
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
            }
        }
    }

    public async void Drop(IDropInfo dropInfo)
    {
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
}
