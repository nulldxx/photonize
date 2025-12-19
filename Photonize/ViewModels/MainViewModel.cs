using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Photonize.Models;
using Photonize.Services;
using Photonize.Views;

namespace Photonize.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ThumbnailGenerator _thumbnailGenerator;
    private readonly FileRenamer _fileRenamer;
    private readonly SettingsService _settingsService;
    private readonly PhotoImporter _photoImporter;
    private readonly WebPExporter _webpExporter;
    private readonly ImageExporter _imageExporter;
    private readonly TopazPhotoAIService _topazPhotoAIService;
    private readonly WorkerQueue<PhotoItem> _thumbnailQueue;

    private string _directoryPath = string.Empty;
    private string _renamePrefix = string.Empty;
    private double _thumbnailSize = 200;
    private string _statusMessage = "Ready";
    private bool _isLoading;
    private PhotoItem? _selectedPhoto;
    private List<PhotoItem> _selectedPhotos = new List<PhotoItem>();
    private bool _isPreviewVisible = false;
    private PhotoItem? _previewPhoto;
    private BitmapImage? _previewImage;
    private bool _hasUnsavedChanges = false;
    private bool _suppressUnsavedChanges = false;

    public MainViewModel(string? initialDirectory = null)
    {
        _thumbnailGenerator = new ThumbnailGenerator();
        _fileRenamer = new FileRenamer();
        _settingsService = new SettingsService();
        _photoImporter = new PhotoImporter();
        _webpExporter = new WebPExporter();
        _imageExporter = new ImageExporter();
        _topazPhotoAIService = new TopazPhotoAIService();

        // Initialize worker queue for parallel thumbnail loading
        // Use CPU core count for worker count and 0.2s stagger delay
        var workerCount = Math.Max(1, Environment.ProcessorCount);
        _thumbnailQueue = new WorkerQueue<PhotoItem>(
            workerCount: workerCount,
            staggerDelay: TimeSpan.FromSeconds(0.2));

        Photos = new ObservableCollection<PhotoItem>();

        BrowseDirectoryCommand = new RelayCommand(BrowseDirectory);
        LoadPhotosCommand = new RelayCommand(async () => await LoadPhotosAsync(), () => !string.IsNullOrEmpty(DirectoryPath));
        ApplyRenameCommand = new RelayCommand(async () => await ApplyRenameAsync(), () => Photos.Any(p => !p.IsFolder) && !string.IsNullOrEmpty(RenamePrefix) && HasUnsavedChanges);
        RefreshCommand = new RelayCommand(async () => await LoadPhotosAsync(), () => !string.IsNullOrEmpty(DirectoryPath));
        OpenPhotoCommand = new RelayCommand<PhotoItem>(OpenPhoto);
        DeletePhotoCommand = new RelayCommand<PhotoItem?>(DeletePhoto, CanDeletePhoto);
        ShowInExplorerCommand = new RelayCommand<PhotoItem>(ShowInExplorer);
        TogglePreviewCommand = new RelayCommand(TogglePreview);
        ExportWebPCommand = new RelayCommand<PhotoItem?>(async (photo) => await ExportToWebPAsync(photo), CanExportWebP);
        ExportPNGCommand = new RelayCommand<PhotoItem?>(async (photo) => await ExportToPNGAsync(photo), CanExportImage);
        ExportJPGCommand = new RelayCommand<PhotoItem?>(async (photo) => await ExportToJPGAsync(photo), CanExportImage);
        ExportPhotoAICommand = new RelayCommand<PhotoItem?>(async (photo) => await ExportToPhotoAIAsync(photo), CanExportPhotoAI);
        CompareImagesCommand = new RelayCommand(CompareImages, CanCompareImages);

        // If a command line directory is provided, use it instead of loading saved settings
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            // Load settings but don't set DirectoryPath from settings
            LoadSavedSettings(skipDirectoryPath: true);
            DirectoryPath = initialDirectory;
        }
        else
        {
            // No command line argument, load all saved settings including directory
            LoadSavedSettings(skipDirectoryPath: false);
        }
    }

    public ObservableCollection<PhotoItem> Photos { get; }

    public string DirectoryPath
    {
        get => _directoryPath;
        set
        {
            if (_directoryPath == value)
                return;

            _directoryPath = value;
            OnPropertyChanged();
            ((RelayCommand)LoadPhotosCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();

            // Auto-load photos when directory path changes to a valid directory
            if (!string.IsNullOrEmpty(value) && Directory.Exists(value))
            {
                _ = LoadPhotosAsync();
            }
        }
    }

    public string RenamePrefix
    {
        get => _renamePrefix;
        set
        {
            _renamePrefix = value;
            OnPropertyChanged();
            ((RelayCommand)ApplyRenameCommand).RaiseCanExecuteChanged();
            if (!_suppressUnsavedChanges)
            {
                UpdateUnsavedChanges();
            }
        }
    }

    public double ThumbnailSize
    {
        get => _thumbnailSize;
        set
        {
            _thumbnailSize = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public PhotoItem? SelectedPhoto
    {
        get => _selectedPhoto;
        set
        {
            _selectedPhoto = value;
            OnPropertyChanged();
            ((RelayCommand<PhotoItem?>)DeletePhotoCommand).RaiseCanExecuteChanged();
        }
    }

    public bool IsPreviewVisible
    {
        get => _isPreviewVisible;
        set
        {
            _isPreviewVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewColumnWidth));
            SaveSettings();
        }
    }

    public GridLength PreviewColumnWidth => IsPreviewVisible ? new GridLength(500) : new GridLength(0);

    public bool IsTopazInstalled => _topazPhotoAIService.IsTopazInstalled();

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set
        {
            _hasUnsavedChanges = value;
            OnPropertyChanged();
            ((RelayCommand)ApplyRenameCommand).RaiseCanExecuteChanged();
        }
    }

    public PhotoItem? PreviewPhoto
    {
        get => _previewPhoto;
        set
        {
            _previewPhoto = value;
            OnPropertyChanged();
            _ = LoadPreviewImageAsync();
        }
    }

    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        set
        {
            _previewImage = value;
            OnPropertyChanged();
        }
    }

    private async Task LoadPreviewImageAsync()
    {
        if (_previewPhoto == null)
        {
            PreviewImage = null;
            return;
        }

        try
        {
            PreviewImage = await _thumbnailGenerator.GeneratePreviewAsync(_previewPhoto.FilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading preview image: {ex.Message}");
            PreviewImage = null;
        }
    }

    public void UpdateSelectedPhotos(IEnumerable<PhotoItem> selectedItems)
    {
        // Ignore selection updates while loading to prevent stale PhotoItem references
        if (IsLoading)
        {
            return;
        }

        _selectedPhotos = selectedItems?.ToList() ?? new List<PhotoItem>();
        ((RelayCommand<PhotoItem?>)DeletePhotoCommand).RaiseCanExecuteChanged();
        ((RelayCommand<PhotoItem?>)ExportWebPCommand).RaiseCanExecuteChanged();
        ((RelayCommand<PhotoItem?>)ExportPNGCommand).RaiseCanExecuteChanged();
        ((RelayCommand<PhotoItem?>)ExportJPGCommand).RaiseCanExecuteChanged();
        ((RelayCommand<PhotoItem?>)ExportPhotoAICommand).RaiseCanExecuteChanged();
        ((RelayCommand)CompareImagesCommand).RaiseCanExecuteChanged();

        // Update preview photo to the first selected item
        PreviewPhoto = _selectedPhotos.FirstOrDefault();
    }

    public ICommand BrowseDirectoryCommand { get; }
    public ICommand LoadPhotosCommand { get; }
    public ICommand ApplyRenameCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenPhotoCommand { get; }
    public ICommand DeletePhotoCommand { get; }
    public ICommand ShowInExplorerCommand { get; }
    public ICommand TogglePreviewCommand { get; }
    public ICommand ExportWebPCommand { get; }
    public ICommand ExportPNGCommand { get; }
    public ICommand ExportJPGCommand { get; }
    public ICommand ExportPhotoAICommand { get; }
    public ICommand CompareImagesCommand { get; }

    private void LoadSavedSettings(bool skipDirectoryPath = false)
    {
        var settings = _settingsService.LoadSettings();

        // Only load directory path from settings if not skipped (i.e., no command line arg)
        if (!skipDirectoryPath && !string.IsNullOrEmpty(settings.LastDirectoryPath) && Directory.Exists(settings.LastDirectoryPath))
        {
            DirectoryPath = settings.LastDirectoryPath;
        }

        ThumbnailSize = settings.LastThumbnailSize;
        if (!string.IsNullOrEmpty(settings.LastPrefix))
        {
            RenamePrefix = settings.LastPrefix;
        }

        // Restore preview visibility state
        IsPreviewVisible = settings.IsPreviewVisible;
    }

    private void SaveSettings()
    {
        var settings = new AppSettings
        {
            LastDirectoryPath = DirectoryPath,
            LastThumbnailSize = ThumbnailSize,
            LastPrefix = RenamePrefix,
            IsPreviewVisible = IsPreviewVisible
        };
        _settingsService.SaveSettings(settings);
    }

    private void TogglePreview()
    {
        IsPreviewVisible = !IsPreviewVisible;
    }

    private void BrowseDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Select Folder",
            Filter = "Folders|*.none",
            ValidateNames = false
        };

        // Workaround to select folder using OpenFileDialog
        if (dialog.ShowDialog() == true)
        {
            var path = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrEmpty(path))
            {
                DirectoryPath = path;
                // Photos will be automatically loaded by the DirectoryPath setter
            }
        }
    }

    private async Task LoadPhotosAsync()
    {
        if (string.IsNullOrEmpty(DirectoryPath) || !Directory.Exists(DirectoryPath))
        {
            StatusMessage = "Invalid directory path";
            return;
        }

        // Clear selected photos FIRST, before clearing Photos collection
        // This prevents SelectionChanged events from repopulating with stale PhotoItem references
        _selectedPhotos.Clear();
        SelectedPhoto = null;
        PreviewPhoto = null;

        IsLoading = true;
        StatusMessage = "Loading photos...";
        Photos.Clear();
        _suppressUnsavedChanges = true; // Suppress during load

        try
        {
            int displayOrder = 0;

            // Add parent directory navigation if not at root
            var parentDirectory = Directory.GetParent(DirectoryPath)?.FullName;
            if (!string.IsNullOrEmpty(parentDirectory))
            {
                var parentItem = new PhotoItem
                {
                    FilePath = parentDirectory,
                    FileName = "..",
                    DisplayOrder = displayOrder++,
                    IsFolder = true,
                    Thumbnail = _thumbnailGenerator.GenerateFolderThumbnail((int)ThumbnailSize)
                };

                Photos.Add(parentItem);
            }

            // Load subfolders
            var subfolders = Directory.GetDirectories(DirectoryPath)
                .OrderBy(d => d)
                .ToList();

            foreach (var subfolder in subfolders)
            {
                var folderItem = new PhotoItem
                {
                    FilePath = subfolder,
                    FileName = Path.GetFileName(subfolder),
                    DisplayOrder = displayOrder++,
                    IsFolder = true,
                    Thumbnail = _thumbnailGenerator.GenerateFolderThumbnail((int)ThumbnailSize)
                };

                Photos.Add(folderItem);
            }

            // Load image files
            var imageFiles = await _thumbnailGenerator.GetImageFilesAsync(DirectoryPath);

            // Create all PhotoItem objects first and add to collection
            // Thumbnails will be loaded in parallel using the worker queue
            var photoItems = new List<PhotoItem>();
            for (int i = 0; i < imageFiles.Count; i++)
            {
                var filePath = imageFiles[i];
                var photoItem = new PhotoItem
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    DisplayOrder = displayOrder++,
                    IsFolder = false,
                    Thumbnail = null // Thumbnail will be loaded by worker queue
                };

                photoItems.Add(photoItem);
                Photos.Add(photoItem);
            }

            // Enqueue thumbnail loading for all photos in parallel
            foreach (var photoItem in photoItems)
            {
                _thumbnailQueue.Enqueue(photoItem, async (item, ct) =>
                {
                    try
                    {
                        var thumbnail = await _thumbnailGenerator.GenerateThumbnailAsync(item.FilePath, (int)ThumbnailSize);

                        // Update thumbnail on UI thread
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            item.Thumbnail = thumbnail;
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading thumbnail for {item.FilePath}: {ex.Message}");
                    }
                }, CancellationToken.None);
            }

            // Wait for all thumbnails to complete loading
            await _thumbnailQueue.WaitForCompletionAsync();

            // Detect and set common prefix (only from photos, not folders)
            DetectCommonPrefix();

            StatusMessage = $"Loaded {subfolders.Count} folder(s) and {imageFiles.Count} photo(s)";
            ((RelayCommand)ApplyRenameCommand).RaiseCanExecuteChanged();
            SaveSettings();
            HasUnsavedChanges = false; // Clear unsaved changes after loading
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading photos: {ex.Message}";
        }
        finally
        {
            _suppressUnsavedChanges = false; // Re-enable tracking
            IsLoading = false;
            ((RelayCommand)ApplyRenameCommand).RaiseCanExecuteChanged();
        }
    }

    private async Task ApplyRenameAsync()
    {
        var photoItems = Photos.Where(p => !p.IsFolder).ToList();
        if (photoItems.Count == 0)
        {
            MessageBox.Show("No photos to rename.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(RenamePrefix))
        {
            MessageBox.Show("Please enter a prefix for the files.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Only show confirmation if prefix is changing (not just reordering)
        if (!FilesAlreadyUsePrefix())
        {
            var result = MessageBox.Show(
                $"This will rename {photoItems.Count} file(s) using the pattern:\n{RenamePrefix}-00001, {RenamePrefix}-00002, etc.\n\nDo you want to continue?",
                "Confirm Rename",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        IsLoading = true;
        StatusMessage = "Renaming files...";

        var (success, message) = await _fileRenamer.RenameFilesAsync(photoItems, RenamePrefix);

        StatusMessage = message;
        IsLoading = false;

        if (success)
        {
            SaveSettings();
            HasUnsavedChanges = false; // Clear unsaved changes after successful rename
        }
        else
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void UpdateDisplayOrder()
    {
        for (int i = 0; i < Photos.Count; i++)
        {
            Photos[i].DisplayOrder = i;
        }

        // Notify that the collection state has changed
        ((RelayCommand)ApplyRenameCommand).RaiseCanExecuteChanged();
        UpdateUnsavedChanges();
    }

    private void UpdateUnsavedChanges()
    {
        // Mark as having unsaved changes if we can apply rename (photos loaded + prefix set)
        HasUnsavedChanges = Photos.Any(p => !p.IsFolder) && !string.IsNullOrEmpty(RenamePrefix);
    }

    private void DetectCommonPrefix()
    {
        // Only detect prefix from photos, not folders
        var photoItems = Photos.Where(p => !p.IsFolder).ToList();

        if (photoItems.Count == 0)
        {
            RenamePrefix = string.Empty;
            return;
        }

        // Get filenames without extensions
        var names = photoItems.Select(p => Path.GetFileNameWithoutExtension(p.FileName)).ToList();

        if (names.Count == 1)
        {
            // Single file: use entire name without extension as prefix
            RenamePrefix = TrimTrailingNumbersAndSeparators(names[0]);
            return;
        }

        // Find the longest common prefix
        string commonPrefix = names[0];
        for (int i = 1; i < names.Count; i++)
        {
            commonPrefix = GetCommonPrefix(commonPrefix, names[i]);
            if (string.IsNullOrEmpty(commonPrefix))
                break;
        }

        // Trim the prefix to end at a sensible boundary (separator or before trailing numbers)
        commonPrefix = TrimTrailingNumbersAndSeparators(commonPrefix);

        RenamePrefix = commonPrefix;
    }

    private bool FilesAlreadyUsePrefix()
    {
        var photoItems = Photos.Where(p => !p.IsFolder).ToList();
        if (photoItems.Count == 0 || string.IsNullOrWhiteSpace(RenamePrefix))
            return false;

        // Check if all files follow the pattern: {RenamePrefix}-{number}{extension}
        foreach (var photo in photoItems)
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(photo.FileName);

            // Check if filename starts with the prefix followed by a dash
            if (!nameWithoutExtension.StartsWith(RenamePrefix + "-", StringComparison.OrdinalIgnoreCase))
                return false;

            // Extract the part after the prefix and dash
            var suffix = nameWithoutExtension.Substring(RenamePrefix.Length + 1);

            // Check if the suffix is all digits
            if (string.IsNullOrEmpty(suffix) || !suffix.All(char.IsDigit))
                return false;
        }

        return true;
    }

    private static string GetCommonPrefix(string str1, string str2)
    {
        int minLength = Math.Min(str1.Length, str2.Length);
        int i = 0;
        while (i < minLength && str1[i] == str2[i])
        {
            i++;
        }
        return str1.Substring(0, i);
    }

    private static string TrimTrailingNumbersAndSeparators(string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        // Trim trailing whitespace first
        str = str.TrimEnd();

        // Remove trailing numbers and common separators (-, _, space)
        int endIndex = str.Length - 1;
        while (endIndex >= 0 && (char.IsDigit(str[endIndex]) || str[endIndex] == '-' || str[endIndex] == '_' || str[endIndex] == ' '))
        {
            endIndex--;
        }

        return str.Substring(0, endIndex + 1);
    }

    private void OpenPhoto(PhotoItem? photo)
    {
        if (photo == null || string.IsNullOrEmpty(photo.FilePath))
            return;

        // If it's a folder, navigate into it
        if (photo.IsFolder)
        {
            if (Directory.Exists(photo.FilePath))
            {
                DirectoryPath = photo.FilePath;
                // Photos will be automatically loaded by the DirectoryPath setter
            }
            return;
        }

        // Otherwise, open the photo file
        if (!File.Exists(photo.FilePath))
            return;

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = photo.FilePath,
                UseShellExecute = true
            };
            Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open photo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowInExplorer(PhotoItem? photo)
    {
        if (photo == null || string.IsNullOrEmpty(photo.FilePath))
            return;

        // Check if it's a folder or file and that it exists
        if (photo.IsFolder && !Directory.Exists(photo.FilePath))
            return;
        if (!photo.IsFolder && !File.Exists(photo.FilePath))
            return;

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{photo.FilePath}\"",
                UseShellExecute = false
            };
            Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to show in Explorer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanDeletePhoto(PhotoItem? photo)
    {
        // Allow delete if we have a parameter (from context menu) or if we have selected photos
        return photo != null || SelectedPhoto != null || _selectedPhotos.Count > 0;
    }

    private void DeletePhoto(PhotoItem? photo)
    {
        // Determine which photos to delete
        List<PhotoItem> photosToDelete = new List<PhotoItem>();

        // If there are selected photos in the list, delete all of them (excluding folders)
        if (_selectedPhotos.Count > 0)
        {
            photosToDelete.AddRange(_selectedPhotos.Where(p => !p.IsFolder));
        }
        // Otherwise delete the single photo from context menu (fallback for right-click without selection)
        else if (photo != null && !photo.IsFolder)
        {
            photosToDelete.Add(photo);
        }
        // Final fallback to SelectedPhoto property (legacy support)
        else if (SelectedPhoto != null && !SelectedPhoto.IsFolder)
        {
            photosToDelete.Add(SelectedPhoto);
        }

        if (photosToDelete.Count == 0)
            return;

        // Show confirmation dialog
        string message;
        if (photosToDelete.Count == 1)
        {
            message = $"Are you sure you want to permanently delete this file?\n\n{photosToDelete[0].FileName}\n\nThis action cannot be undone.";
        }
        else
        {
            message = $"Are you sure you want to permanently delete {photosToDelete.Count} files?\n\nThis action cannot be undone.";
        }

        var result = MessageBox.Show(
            message,
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            int deletedCount = 0;
            List<string> failedFiles = new List<string>();

            foreach (var photoToDelete in photosToDelete)
            {
                try
                {
                    // Delete the file from disk
                    if (File.Exists(photoToDelete.FilePath))
                    {
                        File.Delete(photoToDelete.FilePath);
                    }

                    // Remove from collection
                    Photos.Remove(photoToDelete);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    failedFiles.Add($"{photoToDelete.FileName}: {ex.Message}");
                }
            }

            // Update display order for remaining photos
            UpdateDisplayOrder();

            // Update status message
            if (deletedCount == 1)
            {
                StatusMessage = $"Deleted {photosToDelete[0].FileName}";
            }
            else
            {
                StatusMessage = $"Deleted {deletedCount} file(s)";
            }

            // Show errors if any files failed to delete
            if (failedFiles.Count > 0)
            {
                MessageBox.Show(
                    $"Failed to delete {failedFiles.Count} file(s):\n\n{string.Join("\n", failedFiles)}",
                    "Delete Errors",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // Clear selection
            SelectedPhoto = null;
            _selectedPhotos.Clear();

            // Update command states
            ((RelayCommand)ApplyRenameCommand).RaiseCanExecuteChanged();
            ((RelayCommand<PhotoItem?>)DeletePhotoCommand).RaiseCanExecuteChanged();
            ((RelayCommand<PhotoItem?>)ExportWebPCommand).RaiseCanExecuteChanged();
            ((RelayCommand<PhotoItem?>)ExportPNGCommand).RaiseCanExecuteChanged();
            ((RelayCommand<PhotoItem?>)ExportJPGCommand).RaiseCanExecuteChanged();
            ((RelayCommand<PhotoItem?>)ExportPhotoAICommand).RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete photos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = $"Failed to delete photos";
        }
    }

    private bool CanExportWebP(PhotoItem? photo)
    {
        // Allow export if we have a parameter (from context menu) or if we have selected photos
        return photo != null || _selectedPhotos.Count > 0;
    }

    private bool CanExportImage(PhotoItem? photo)
    {
        // Allow export if we have a parameter (from context menu) or if we have selected photos
        return photo != null || _selectedPhotos.Count > 0;
    }

    private bool CanExportPhotoAI(PhotoItem? photo)
    {
        // Allow export if Topaz is installed and we have photos
        return IsTopazInstalled && (photo != null || _selectedPhotos.Count > 0);
    }

    private bool CanCompareImages()
    {
        // Only allow comparison when exactly 2 non-folder photos are selected
        return _selectedPhotos.Count(p => !p.IsFolder) == 2;
    }

    private void CompareImages()
    {
        var photosToCompare = _selectedPhotos.Where(p => !p.IsFolder).Take(2).ToList();

        if (photosToCompare.Count != 2)
            return;

        // Create and show the compare window
        var compareWindow = new Photonize.Views.CompareWindow(photosToCompare[0], photosToCompare[1]);
        compareWindow.Owner = Application.Current.MainWindow;
        compareWindow.Show();
    }

    private async Task ExportToWebPAsync(PhotoItem? photo)
    {
        // Determine which photos to export
        List<PhotoItem> photosToExport = new List<PhotoItem>();

        // If there are selected photos in the list, export all of them (excluding folders)
        if (_selectedPhotos.Count > 0)
        {
            photosToExport.AddRange(_selectedPhotos.Where(p => !p.IsFolder));
        }
        // Otherwise export the single photo from context menu (fallback for right-click without selection)
        else if (photo != null && !photo.IsFolder)
        {
            photosToExport.Add(photo);
        }

        if (photosToExport.Count == 0)
            return;

        IsLoading = true;
        StatusMessage = "Exporting to WebP...";

        try
        {
            // Check for existing files and ask about overwriting
            Func<List<string>, OverwriteOption> overwriteCallback = (existingFiles) =>
            {
                string message;
                if (existingFiles.Count == 1)
                {
                    message = $"The file '{existingFiles[0]}' already exists in the WebP folder.\n\n" +
                              "Choose an option:\n" +
                              "• Yes: Overwrite all existing files\n" +
                              "• No: Skip existing files\n" +
                              "• Cancel: Cancel the entire export";
                }
                else
                {
                    message = $"{existingFiles.Count} files already exist in the WebP folder:\n\n{string.Join("\n", existingFiles.Take(5))}" +
                              (existingFiles.Count > 5 ? $"\n...and {existingFiles.Count - 5} more" : "") +
                              "\n\nChoose an option:\n" +
                              "• Yes: Overwrite all existing files\n" +
                              "• No: Skip existing files\n" +
                              "• Cancel: Cancel the entire export";
                }

                var result = MessageBox.Show(
                    message,
                    "Files Already Exist",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                return result switch
                {
                    MessageBoxResult.Yes => OverwriteOption.OverwriteAll,
                    MessageBoxResult.No => OverwriteOption.SkipExisting,
                    _ => OverwriteOption.Cancel
                };
            };

            var (success, message) = await _webpExporter.ExportToWebPAsync(
                photosToExport,
                DirectoryPath,
                overwriteCallback);

            StatusMessage = message;

            if (success)
            {
                MessageBox.Show(message, "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(message, "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error exporting to WebP: {ex.Message}";
            MessageBox.Show($"Error exporting to WebP: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExportToPNGAsync(PhotoItem? photo)
    {
        await ExportToFormatAsync(photo, ImageFormat.PNG);
    }

    private async Task ExportToJPGAsync(PhotoItem? photo)
    {
        await ExportToFormatAsync(photo, ImageFormat.JPG);
    }

    private async Task ExportToFormatAsync(PhotoItem? photo, ImageFormat format)
    {
        // Determine which photos to export
        List<PhotoItem> photosToExport = new List<PhotoItem>();

        // If there are selected photos in the list, export all of them (excluding folders)
        if (_selectedPhotos.Count > 0)
        {
            photosToExport.AddRange(_selectedPhotos.Where(p => !p.IsFolder));
        }
        // Otherwise export the single photo from context menu (fallback for right-click without selection)
        else if (photo != null && !photo.IsFolder)
        {
            photosToExport.Add(photo);
        }

        if (photosToExport.Count == 0)
            return;

        IsLoading = true;
        StatusMessage = $"Exporting to {format}...";

        try
        {
            // Check for existing files and ask about overwriting
            Func<List<string>, OverwriteOption> overwriteCallback = (existingFiles) =>
            {
                string message;
                if (existingFiles.Count == 1)
                {
                    message = $"The file '{existingFiles[0]}' already exists in the {format} folder.\n\n" +
                              "Choose an option:\n" +
                              "• Yes: Overwrite all existing files\n" +
                              "• No: Skip existing files\n" +
                              "• Cancel: Cancel the entire export";
                }
                else
                {
                    message = $"{existingFiles.Count} files already exist in the {format} folder:\n\n{string.Join("\n", existingFiles.Take(5))}" +
                              (existingFiles.Count > 5 ? $"\n...and {existingFiles.Count - 5} more" : "") +
                              "\n\nChoose an option:\n" +
                              "• Yes: Overwrite all existing files\n" +
                              "• No: Skip existing files\n" +
                              "• Cancel: Cancel the entire export";
                }

                var result = MessageBox.Show(
                    message,
                    "Files Already Exist",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                return result switch
                {
                    MessageBoxResult.Yes => OverwriteOption.OverwriteAll,
                    MessageBoxResult.No => OverwriteOption.SkipExisting,
                    _ => OverwriteOption.Cancel
                };
            };

            var (success, message) = await _imageExporter.ExportToFormatAsync(
                photosToExport,
                DirectoryPath,
                format,
                overwriteCallback);

            StatusMessage = message;

            if (success)
            {
                MessageBox.Show(message, "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(message, "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error exporting to {format}: {ex.Message}";
            MessageBox.Show($"Error exporting to {format}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExportToPhotoAIAsync(PhotoItem? photo)
    {
        // Determine which photos/folders to export
        List<PhotoItem> photosToExport = new List<PhotoItem>();

        // If there are selected photos in the list, export all of them (including folders)
        if (_selectedPhotos.Count > 0)
        {
            photosToExport.AddRange(_selectedPhotos);
        }
        // Otherwise export the single photo/folder from context menu (fallback for right-click without selection)
        else if (photo != null)
        {
            photosToExport.Add(photo);
        }

        if (photosToExport.Count == 0)
            return;

        // Check for existing files and ask about overwriting BEFORE showing progress dialog
        Func<List<string>, OverwriteOption> overwriteCallback = (existingFiles) =>
        {
            string message;
            if (existingFiles.Count == 1)
            {
                message = $"The file '{existingFiles[0]}' already exists in the TopazUpscaled folder.\n\n" +
                          "Choose an option:\n" +
                          "• Yes: Overwrite all existing files\n" +
                          "• No: Skip existing files\n" +
                          "• Cancel: Cancel the entire upscale";
            }
            else
            {
                message = $"{existingFiles.Count} files already exist in the TopazUpscaled folder:\n\n{string.Join("\n", existingFiles.Take(5))}" +
                          (existingFiles.Count > 5 ? $"\n...and {existingFiles.Count - 5} more" : "") +
                          "\n\nChoose an option:\n" +
                          "• Yes: Overwrite all existing files\n" +
                          "• No: Skip existing files\n" +
                          "• Cancel: Cancel the entire upscale";
            }

            var result = MessageBox.Show(
                message,
                "Files Already Exist",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            return result switch
            {
                MessageBoxResult.Yes => OverwriteOption.OverwriteAll,
                MessageBoxResult.No => OverwriteOption.SkipExisting,
                _ => OverwriteOption.Cancel
            };
        };

        // Create progress dialog
        var progressDialog = new ProgressDialog
        {
            Owner = Application.Current.MainWindow
        };

        // Create cancellation token source
        var cts = new CancellationTokenSource();

        // Handle dialog closing (user clicked X or Cancel)
        progressDialog.Closed += (s, e) =>
        {
            if (progressDialog.WasCancelled)
            {
                cts.Cancel();
            }
        };

        // Create progress reporter
        var progress = new Progress<(int current, int total, string status)>(p =>
        {
            progressDialog.UpdateProgress(p.current, p.total, p.status);
        });

        // Show the progress dialog non-modally
        progressDialog.Show();

        try
        {
            // Run the upscale operation
            var (success, message) = await _topazPhotoAIService.UpscalePhotosAsync(
                photosToExport,
                DirectoryPath,
                overwriteCallback,
                progress,
                cts.Token);

            // Close the progress dialog
            progressDialog.Close();

            StatusMessage = message;

            // Show result
            if (success)
            {
                MessageBox.Show(message, "Upscale Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (!cts.Token.IsCancellationRequested)
            {
                MessageBox.Show(message, "Upscale Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            progressDialog.Close();
            StatusMessage = $"Error upscaling with Topaz Photo AI: {ex.Message}";
            MessageBox.Show($"Error upscaling with Topaz Photo AI: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async Task MoveItemsToFolderAsync(List<PhotoItem> itemsToMove, PhotoItem targetFolder)
    {
        if (itemsToMove.Count == 0 || targetFolder == null || !targetFolder.IsFolder)
            return;

        try
        {
            string targetPath = targetFolder.FilePath;

            // Validate target directory exists
            if (!Directory.Exists(targetPath))
            {
                MessageBox.Show($"Target folder does not exist: {targetPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IsLoading = true;
            StatusMessage = $"Moving {itemsToMove.Count} item(s) to {targetFolder.FileName}...";

            int movedCount = 0;
            List<string> failedItems = new List<string>();

            foreach (var item in itemsToMove)
            {
                try
                {
                    string itemName = Path.GetFileName(item.FilePath);
                    string destinationPath = Path.Combine(targetPath, itemName);

                    if (item.IsFolder)
                    {
                        // Moving a folder
                        if (!Directory.Exists(item.FilePath))
                            continue;

                        // Check if folder already exists in destination
                        if (Directory.Exists(destinationPath))
                        {
                            var result = MessageBox.Show(
                                $"Folder '{itemName}' already exists in the target folder.\n\nDo you want to merge the contents?",
                                "Folder Exists",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (result != MessageBoxResult.Yes)
                                continue;

                            // Move contents to existing folder (this would require recursive handling)
                            // For simplicity, we'll just skip for now
                            failedItems.Add($"{itemName}: Merging folders not yet supported");
                            continue;
                        }

                        // Move the folder
                        Directory.Move(item.FilePath, destinationPath);
                        movedCount++;
                    }
                    else
                    {
                        // Moving a file
                        if (!File.Exists(item.FilePath))
                            continue;

                        // Check if file already exists in destination
                        if (File.Exists(destinationPath))
                        {
                            var result = MessageBox.Show(
                                $"File '{itemName}' already exists in the target folder.\n\nDo you want to overwrite it?",
                                "File Exists",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (result != MessageBoxResult.Yes)
                                continue;
                        }

                        // Move the file
                        File.Move(item.FilePath, destinationPath, overwrite: true);
                        movedCount++;
                    }
                }
                catch (Exception ex)
                {
                    failedItems.Add($"{item.FileName}: {ex.Message}");
                }
            }

            // Refresh the current directory view
            await LoadPhotosAsync();

            // Update status message
            if (movedCount == 1)
            {
                StatusMessage = $"Moved {itemsToMove[0].FileName} to {targetFolder.FileName}";
            }
            else
            {
                StatusMessage = $"Moved {movedCount} item(s) to {targetFolder.FileName}";
            }

            // Show errors if any items failed to move
            if (failedItems.Count > 0)
            {
                MessageBox.Show(
                    $"Failed to move {failedItems.Count} item(s):\n\n{string.Join("\n", failedItems)}",
                    "Move Errors",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to move items: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = $"Failed to move items";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task MovePhotosToFolderAsync(List<PhotoItem> photosToMove, PhotoItem targetFolder)
    {
        if (photosToMove.Count == 0 || targetFolder == null || !targetFolder.IsFolder)
            return;

        try
        {
            string targetPath = targetFolder.FilePath;

            // Validate target directory exists
            if (!Directory.Exists(targetPath))
            {
                MessageBox.Show($"Target folder does not exist: {targetPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IsLoading = true;
            StatusMessage = $"Moving {photosToMove.Count} file(s) to {targetFolder.FileName}...";

            int movedCount = 0;
            List<string> failedFiles = new List<string>();

            foreach (var photo in photosToMove)
            {
                try
                {
                    if (!File.Exists(photo.FilePath))
                        continue;

                    string fileName = Path.GetFileName(photo.FilePath);
                    string destinationPath = Path.Combine(targetPath, fileName);

                    // Check if file already exists in destination
                    if (File.Exists(destinationPath))
                    {
                        var result = MessageBox.Show(
                            $"File '{fileName}' already exists in the target folder.\n\nDo you want to overwrite it?",
                            "File Exists",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result != MessageBoxResult.Yes)
                            continue;
                    }

                    // Move the file
                    File.Move(photo.FilePath, destinationPath, overwrite: true);
                    movedCount++;
                }
                catch (Exception ex)
                {
                    failedFiles.Add($"{photo.FileName}: {ex.Message}");
                }
            }

            // Refresh the current directory view
            await LoadPhotosAsync();

            // Update status message
            if (movedCount == 1)
            {
                StatusMessage = $"Moved {photosToMove[0].FileName} to {targetFolder.FileName}";
            }
            else
            {
                StatusMessage = $"Moved {movedCount} file(s) to {targetFolder.FileName}";
            }

            // Show errors if any files failed to move
            if (failedFiles.Count > 0)
            {
                MessageBox.Show(
                    $"Failed to move {failedFiles.Count} file(s):\n\n{string.Join("\n", failedFiles)}",
                    "Move Errors",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to move files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = $"Failed to move files";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ExportFilesDirectlyAsync(List<string> filePaths)
    {
        if (filePaths == null || filePaths.Count == 0)
        {
            return;
        }

        try
        {
            // Filter for supported image files only
            var supportedFiles = filePaths
                .Where(f => ThumbnailGenerator.IsImageFile(f) && File.Exists(f))
                .ToList();

            if (supportedFiles.Count == 0)
            {
                MessageBox.Show("No supported image files found in the selection.", "No Images", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Determine output directory (use the directory of the first file)
            string outputDirectory = Path.GetDirectoryName(supportedFiles[0]) ?? Directory.GetCurrentDirectory();

            // Convert file paths to PhotoItem objects
            var photosToExport = supportedFiles.Select(filePath => new PhotoItem
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                IsFolder = false
            }).ToList();

            IsLoading = true;
            StatusMessage = $"Exporting {photosToExport.Count} file(s) to WebP...";

            // Export to WebP (no overwrite callback - always create new files)
            var (success, message) = await _webpExporter.ExportToWebPAsync(
                photosToExport,
                outputDirectory,
                overwriteCallback: null);

            StatusMessage = message;

            if (success)
            {
                var webpFolder = Path.Combine(outputDirectory, "WebP");

                MessageBox.Show(
                    $"{message}\n\nOutput folder:\n{webpFolder}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Load the output directory AFTER user dismisses the message box
                // This ensures all files are written and avoids race conditions
                DirectoryPath = outputDirectory;
            }
            else
            {
                MessageBox.Show(message, "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error during export: {ex.Message}";
            MessageBox.Show($"Error during export: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ImportPhotosAsync(List<string> filesToImport)
    {
        if (string.IsNullOrEmpty(DirectoryPath) || !Directory.Exists(DirectoryPath))
        {
            MessageBox.Show("Please select a target directory first.", "No Directory", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(RenamePrefix))
        {
            MessageBox.Show("Please enter a rename prefix first.", "No Prefix", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (filesToImport.Count == 0)
        {
            return;
        }

        // Filter for supported image files
        var supportedFiles = filesToImport
            .Where(f => ThumbnailGenerator.IsImageFile(f))
            .ToList();

        if (supportedFiles.Count == 0)
        {
            MessageBox.Show("No supported image files found in the selection.", "No Images", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Show dialog to choose import mode
        var dialog = new Photonize.Views.ImportDialog(Photos.Count, supportedFiles.Count);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() != true)
        {
            return; // User cancelled
        }

        IsLoading = true;
        StatusMessage = "Importing photos...";

        try
        {
            var (success, message, updatedPhotos) = await _photoImporter.ImportPhotosAsync(
                supportedFiles,
                Photos.ToList(),
                DirectoryPath,
                RenamePrefix,
                dialog.SelectedMode,
                (int)ThumbnailSize);

            if (success)
            {
                // Replace the entire Photos collection with the updated list
                Photos.Clear();
                _selectedPhotos.Clear();
                foreach (var photo in updatedPhotos.OrderBy(p => p.DisplayOrder))
                {
                    Photos.Add(photo);
                }

                StatusMessage = message;
                ((RelayCommand)ApplyRenameCommand).RaiseCanExecuteChanged();
            }
            else
            {
                StatusMessage = message;
                MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error importing photos: {ex.Message}";
            MessageBox.Show($"Error importing photos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _thumbnailQueue?.Dispose();
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        if (_canExecute == null)
            return true;

        if (parameter == null && typeof(T).IsValueType)
            return false;

        return _canExecute((T?)parameter);
    }

    public void Execute(object? parameter)
    {
        _execute((T?)parameter);
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
