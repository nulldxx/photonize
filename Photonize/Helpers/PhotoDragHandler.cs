using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using GongSolutions.Wpf.DragDrop;
using Photonize.Models;
using Photonize.ViewModels;

namespace Photonize.Helpers;

/// <summary>
/// Supplies the payload for drags starting in the photo grid.
/// <para>
/// The drag carries two payloads on one data object: gong's own format, so drops back
/// inside Photonize behave exactly as they always have, and CF_HDROP, so Windows
/// Explorer sees real files and can move them.
/// </para>
/// <para>
/// Deliberately, this class never deletes anything. When a drop lands outside the app
/// Explorer performs an "optimised move" - it moves the files itself before we get
/// control back - so finishing the gesture is a matter of noticing what left and
/// refreshing. Deleting source files on the strength of a reported drop effect would
/// put the user's photos one wrong flag away from being destroyed by an ordinary
/// in-app reorder.
/// </para>
/// </summary>
public class PhotoDragHandler : DefaultDragHandler
{
    private const string PreferredDropEffectFormat = "Preferred DropEffect";
    private const string PerformedDropEffectFormat = "Performed DropEffect";
    private const int DropEffectMove = 2; // DROPEFFECT_MOVE

    private List<PhotoItem> _draggedItems = new();
    private bool _droppedInsidePhotonize;

    public override bool CanStartDrag(IDragInfo dragInfo)
    {
        // The ".." tile points at the parent directory; dropping it on Explorer would
        // hand over the whole folder we are sitting inside.
        if (dragInfo.SourceItem is PhotoItem item && DragPayloadBuilder.IsParentNavigationItem(item))
            return false;

        return base.CanStartDrag(dragInfo);
    }

    public override void StartDrag(IDragInfo dragInfo)
    {
        // Let gong work out selection-vs-single-item exactly as it does today.
        base.StartDrag(dragInfo);

        _draggedItems = new List<PhotoItem>();
        _droppedInsidePhotonize = false;

        // Second line of defence: strip ".." out of a mixed selection.
        var items = DragPayloadBuilder.SelectDraggableItems(dragInfo.Data);
        if (items.Count == 0)
        {
            // Effects of None makes gong abandon the drag before DoDragDrop.
            dragInfo.Data = null;
            dragInfo.Effects = DragDropEffects.None;
            return;
        }

        // Keep gong's payload in step with what we actually intend to drag.
        dragInfo.Data = items.Count == 1 ? items[0] : (object)items;
        _draggedItems = items;

        // gong resolves its own format first when building a DropInfo, so in-app drops
        // keep seeing PhotoItems rather than the file list.
        var dataObject = new DataObject(dragInfo.DataFormat.Name, dragInfo.Data);

        var paths = DragPayloadBuilder.BuildFileDropPaths(items);
        if (paths.Count > 0)
        {
            var fileDropList = new StringCollection();
            foreach (var path in paths)
            {
                fileDropList.Add(path);
            }

            dataObject.SetFileDropList(fileDropList);

            // Without this Explorer defaults to copy for a drag offering both effects.
            SetDropEffect(dataObject, PreferredDropEffectFormat, DropEffectMove);
        }

        dragInfo.DataObject = dataObject;
        dragInfo.Effects = DragDropEffects.Move | DragDropEffects.Copy;
    }

    public override void Dropped(IDropInfo dropInfo)
    {
        // gong calls this only when the drop landed on one of its drop targets in this
        // process - i.e. our own grid, where PhotoDropHandler has already done the work.
        // Set the flag first so nothing below can leave it unset.
        _droppedInsidePhotonize = true;

        base.Dropped(dropInfo);
    }

    public override void DragDropOperationFinished(DragDropEffects operationResult, IDragInfo dragInfo)
    {
        base.DragDropOperationFinished(operationResult, dragInfo);

        var items = _draggedItems;
        var handledInternally = _droppedInsidePhotonize;

        // Reset before any early return so a failed drag can't poison the next one.
        _draggedItems = new List<PhotoItem>();
        _droppedInsidePhotonize = false;

        if (handledInternally || items.Count == 0)
            return;

        // DoDragDrop can OR in the Scroll bit. Esc gives None.
        if ((operationResult & ~DragDropEffects.Scroll) != DragDropEffects.Move)
            return;

        // Explorer writes this back into the very data object we handed to DoDragDrop.
        // It is only a hint about whether to refresh - never a licence to delete.
        var targetReportedMove =
            ReadDropEffect(dragInfo?.DataObject as DataObject, PerformedDropEffectFormat) == DropEffectMove;

        // We are still inside gong's mouse handler with the drag machinery unwinding,
        // so defer the reload (and any message box) until the pump is idle.
        Application.Current?.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(async () =>
            {
                if (Application.Current?.MainWindow?.DataContext is MainViewModel viewModel)
                {
                    await viewModel.CompleteExternalMoveAsync(items, targetReportedMove);
                }
            }));
    }

    /// <summary>
    /// Writes a DROPEFFECT as the 4-byte little-endian DWORD the shell expects.
    /// </summary>
    private static void SetDropEffect(DataObject dataObject, string format, int effect)
    {
        // Not disposed on purpose - the data object owns the stream for the drag's life.
        var stream = new MemoryStream(4);
        stream.Write(BitConverter.GetBytes(effect), 0, 4);
        stream.Position = 0;

        dataObject.SetData(format, stream);
    }

    private static int? ReadDropEffect(DataObject? dataObject, string format)
    {
        if (dataObject == null)
            return null;

        try
        {
            if (!dataObject.GetDataPresent(format, autoConvert: false))
                return null;

            var bytes = dataObject.GetData(format, autoConvert: false) switch
            {
                MemoryStream memory => memory.ToArray(),
                byte[] raw => raw,
                Stream stream => ReadAllBytes(stream),
                _ => null
            };

            return bytes is { Length: >= 4 } ? BitConverter.ToInt32(bytes, 0) : null;
        }
        catch
        {
            // Unreadable means we simply don't get the refresh hint.
            return null;
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var buffer = new MemoryStream();

        if (stream.CanSeek)
            stream.Position = 0;

        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
