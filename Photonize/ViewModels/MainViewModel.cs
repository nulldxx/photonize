using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Photonize.Models;
using Photonize.Services;

namespace Photonize.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ThumbnailGenerator _thumbnailGenerator;
    private readonly FileRenamer _fileRenamer;
    private readonly SettingsService _settingsService;
    private readonly PhotoImporter _photoImporter;

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

    public MainViewModel(string? initialDirectory = null)
    {
        _thumbnailGenerator = new ThumbnailGenerator();
        _fileRenamer = new FileRenamer();
        _settingsService = new SettingsService();
        _photoImporter = new PhotoImporter();

        Photos = new ObservableCollection<PhotoItem>();

        BrowseDirectoryCommand = new RelayCommand(BrowseDirectory);
        LoadPhotosCommand = new RelayCommand(async () => await LoadPhotosAsync(), () => !string.IsNullOrEmpty(DirectoryPath));
        ApplyRenameCommand = new RelayCommand(async () => await ApplyRenameAsync(), () => Photos.Count > 0 && !string.IsNullOrEmpty(RenamePrefix));
        RefreshCommand = new RelayCommand(async () => await LoadPhotosAsync(), () => !string.IsNullOrEmpty(DirectoryPath));
        OpenPhotoCommand = new RelayCommand<PhotoItem>(OpenPhoto);
        DeletePhotoCommand = new RelayCommand<PhotoItem?>(DeletePhoto, CanDeletePhoto);
        ShowInExplorerCommand = new RelayCommand<PhotoItem>(ShowInExplorer);
        TogglePreviewCommand = new RelayCommand(TogglePreview);

        LoadSavedSettings();

        // Override with command line directory if provided
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            DirectoryPath = initialDirectory;
        }
    }

    public ObservableCollection<PhotoItem> Photos { get; }

    public string DirectoryPath
    {
        get => _directoryPath;
        set
        {
            _directoryPath = value;
            OnPropertyChanged();
            ((RelayCommand)LoadPhotosCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
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
        _selectedPhotos = selectedItems?.ToList() ?? new List<PhotoItem>();
        ((RelayCommand<PhotoItem?>)DeletePhotoCommand).RaiseCanExecuteChanged();

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

    private void LoadSavedSettings()
    {
        var settings = _settingsService.LoadSettings();
        if (!string.IsNullOrEmpty(settings.LastDirectoryPath) && Directory.Exists(settings.LastDirectoryPath))
        {
            DirectoryPath = settings.LastDirectoryPath;
        }
        ThumbnailSize = settings.LastThumbnailSize;
        if (!string.IsNullOrEmpty(settings.LastPrefix))
        {
            RenamePrefix = settings.LastPrefix;
        }
        _isPreviewVisible = settings.IsPreviewVisible;
        OnPropertyChanged(nameof(IsPreviewVisible));
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
                _ = LoadPhotosAsync();
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

        IsLoading = true;
        StatusMessage = "Loading photos...";
        Photos.Clear();
        _selectedPhotos.Clear();

        try
        {
            var imageFiles = await _thumbnailGenerator.GetImageFilesAsync(DirectoryPath);

            for (int i = 0; i < imageFiles.Count; i++)
            {
                var filePath = imageFiles[i];
                var photoItem = new PhotoItem
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    DisplayOrder = i
                };

                // Load thumbnail asynchronously
                var thumbnail = await _thumbnailGenerator.GenerateThumbnailAsync(filePath, (int)ThumbnailSize);
                photoItem.Thumbnail = thumbnail;

                Photos.Add(photoItem);
            }

            // Detect and set common prefix
            DetectCommonPrefix();

            StatusMessage = $"Loaded {Photos.Count} photo(s)";
            ((RelayCommand)ApplyRenameCommand).RaiseCanExecuteChanged();
            SaveSettings();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading photos: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            ((RelayCommand)ApplyRenameCommand).RaiseCanExecuteChanged();
        }
    }

    private async Task ApplyRenameAsync()
    {
        if (Photos.Count == 0)
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
                $"This will rename {Photos.Count} file(s) using the pattern:\n{RenamePrefix}-00001, {RenamePrefix}-00002, etc.\n\nDo you want to continue?",
                "Confirm Rename",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        IsLoading = true;
        StatusMessage = "Renaming files...";

        var (success, message) = await _fileRenamer.RenameFilesAsync(Photos.ToList(), RenamePrefix);

        StatusMessage = message;
        IsLoading = false;

        if (success)
        {
            SaveSettings();
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
    }

    private void DetectCommonPrefix()
    {
        if (Photos.Count == 0)
        {
            RenamePrefix = string.Empty;
            return;
        }

        // Get filenames without extensions
        var names = Photos.Select(p => Path.GetFileNameWithoutExtension(p.FileName)).ToList();

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
        if (Photos.Count == 0 || string.IsNullOrWhiteSpace(RenamePrefix))
            return false;

        // Check if all files follow the pattern: {RenamePrefix}-{number}{extension}
        foreach (var photo in Photos)
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
        if (photo == null || string.IsNullOrEmpty(photo.FilePath) || !File.Exists(photo.FilePath))
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
        if (photo == null || string.IsNullOrEmpty(photo.FilePath) || !File.Exists(photo.FilePath))
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

        // If there are selected photos in the list, delete all of them
        if (_selectedPhotos.Count > 0)
        {
            photosToDelete.AddRange(_selectedPhotos);
        }
        // Otherwise delete the single photo from context menu (fallback for right-click without selection)
        else if (photo != null)
        {
            photosToDelete.Add(photo);
        }
        // Final fallback to SelectedPhoto property (legacy support)
        else if (SelectedPhoto != null)
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
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete photos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = $"Failed to delete photos";
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
