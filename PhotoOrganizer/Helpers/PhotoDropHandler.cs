using System.Collections;
using System.Windows;
using GongSolutions.Wpf.DragDrop;
using PhotoOrganizer.Models;
using PhotoOrganizer.ViewModels;

namespace PhotoOrganizer.Helpers;

public class PhotoDropHandler : IDropTarget
{
    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data != null && dropInfo.TargetCollection != null)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = DragDropEffects.Move;
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data == null || dropInfo.TargetCollection == null)
            return;

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
