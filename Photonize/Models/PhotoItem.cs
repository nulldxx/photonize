using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Photonize.Services;

namespace Photonize.Models;

public class PhotoItem : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    private string _fileName = string.Empty;
    private BitmapSource? _thumbnail;
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

    public BitmapSource? Thumbnail
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

    public bool IsVideo => !IsFolder && ThumbnailGenerator.IsVideoFile(FilePath);

    /// <summary>
    /// A raw file (DNG). Shown from its embedded JPEG preview, and kept out of the
    /// export, PhotoAI and auto-caption paths, none of which can read raw.
    /// </summary>
    public bool IsRaw => !IsFolder && ThumbnailGenerator.IsRawFile(FilePath);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
