using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Photonize.Models;

public class PhotoItem : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    private string _fileName = string.Empty;
    private BitmapImage? _thumbnail;
    private int _displayOrder;
    private bool _isFolder;
    private int? _tag;

    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            OnPropertyChanged();
        }
    }

    public string FileName
    {
        get => _fileName;
        set
        {
            _fileName = value;
            OnPropertyChanged();
        }
    }

    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    public int DisplayOrder
    {
        get => _displayOrder;
        set
        {
            _displayOrder = value;
            OnPropertyChanged();
        }
    }

    public bool IsFolder
    {
        get => _isFolder;
        set
        {
            _isFolder = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Quick-tag digit (0-9) applied by the user, or null when untagged.
    /// </summary>
    public int? Tag
    {
        get => _tag;
        set
        {
            _tag = value;
            OnPropertyChanged();
            // Computed properties don't notify on their own, so raise this explicitly
            // or the thumbnail badge won't appear/disappear.
            OnPropertyChanged(nameof(HasTag));
        }
    }

    public bool HasTag => _tag.HasValue;

    public string Extension => IsFolder ? string.Empty : Path.GetExtension(FilePath);

    public bool IsVideo => !IsFolder &&
        (Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
         Extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
         Extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase));

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
