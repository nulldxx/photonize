using System.Windows;
using GongSolutions.Wpf.DragDrop;
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

        // Let the default handler do the reordering
        DefaultDropHandler.Drop(dropInfo);

        // Update display order after drop
        if (Application.Current.MainWindow?.DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateDisplayOrder();
        }
    }
}

public static class DefaultDropHandler
{
    public static void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not System.Collections.IList sourceList)
            return;

        if (dropInfo.TargetCollection is not System.Collections.IList targetList)
            return;

        var insertIndex = dropInfo.InsertIndex;
        var itemsToMove = new List<object>();

        foreach (var item in sourceList)
        {
            if (item != null)
                itemsToMove.Add(item);
        }

        foreach (var item in itemsToMove)
        {
            var oldIndex = targetList.IndexOf(item);
            if (oldIndex >= 0)
            {
                targetList.RemoveAt(oldIndex);
                if (oldIndex < insertIndex)
                    insertIndex--;
            }

            if (insertIndex >= targetList.Count)
                targetList.Add(item);
            else
                targetList.Insert(insertIndex, item);

            insertIndex++;
        }
    }
}
