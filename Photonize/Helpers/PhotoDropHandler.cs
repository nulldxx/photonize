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

        // Handle internal photo reordering
        if (dropInfo.Data != null && dropInfo.TargetCollection != null)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = DragDropEffects.Move;
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
                if (Application.Current.MainWindow?.DataContext is MainViewModel viewModel)
                {
                    await viewModel.ImportPhotosAsync(files.ToList());
                }
            }
            return;
        }

        // Handle internal photo reordering (existing functionality)
        var targetCollection = dropInfo.TargetCollection as IList;
        if (targetCollection == null)
            return;

        var insertIndex = dropInfo.InsertIndex;
        var itemToMove = dropInfo.Data as PhotoItem;

        if (itemToMove == null)
            return;

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

        // Update display order after drop
        if (Application.Current.MainWindow?.DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateDisplayOrder();
        }
    }
}
