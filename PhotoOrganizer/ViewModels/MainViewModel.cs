using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using PhotoOrganizer.Models;
using PhotoOrganizer.Services;

namespace PhotoOrganizer.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ThumbnailGenerator _thumbnailGenerator;
    private readonly FileRenamer _fileRenamer;
    private readonly SettingsService _settingsService;

    private string _directoryPath = string.Empty;
    private string _renamePrefix = string.Empty;
    private double _thumbnailSize = 200;
    private string _statusMessage = "Ready";
    private bool _isLoading;

    public MainViewModel()
    {
        _thumbnailGenerator = new ThumbnailGenerator();
        _fileRenamer = new FileRenamer();
        _settingsService = new SettingsService();

        Photos = new ObservableCollection<PhotoItem>();

        BrowseDirectoryCommand = new RelayCommand(BrowseDirectory);
        LoadPhotosCommand = new RelayCommand(async () => await LoadPhotosAsync(), () => !string.IsNullOrEmpty(DirectoryPath));
        ApplyRenameCommand = new RelayCommand(async () => await ApplyRenameAsync(), () => Photos.Count > 0 && !string.IsNullOrEmpty(RenamePrefix));
        RefreshCommand = new RelayCommand(async () => await LoadPhotosAsync(), () => !string.IsNullOrEmpty(DirectoryPath));

        LoadSavedSettings();
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

    public ICommand BrowseDirectoryCommand { get; }
    public ICommand LoadPhotosCommand { get; }
    public ICommand ApplyRenameCommand { get; }
    public ICommand RefreshCommand { get; }

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
    }

    private void SaveSettings()
    {
        var settings = new AppSettings
        {
            LastDirectoryPath = DirectoryPath,
            LastThumbnailSize = ThumbnailSize,
            LastPrefix = RenamePrefix
        };
        _settingsService.SaveSettings(settings);
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

        // Show confirmation dialog
        var result = MessageBox.Show(
            $"This will rename {Photos.Count} file(s) using the pattern:\n{RenamePrefix}-00001, {RenamePrefix}-00002, etc.\n\nDo you want to continue?",
            "Confirm Rename",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        IsLoading = true;
        StatusMessage = "Renaming files...";

        var (success, message) = await _fileRenamer.RenameFilesAsync(Photos.ToList(), RenamePrefix);

        StatusMessage = message;
        IsLoading = false;

        if (success)
        {
            MessageBox.Show(message, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
