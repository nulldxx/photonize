using System.Windows;
using Photonize.Models;
using Photonize.Services;

namespace Photonize.Views;

public partial class CompareWindow : Window
{
    private readonly ThumbnailGenerator _thumbnailGenerator;

    public CompareWindow(PhotoItem photoA, PhotoItem photoB)
    {
        InitializeComponent();
        _thumbnailGenerator = new ThumbnailGenerator();

        Loaded += async (s, e) => await LoadImagesAsync(photoA, photoB);
    }

    private async Task LoadImagesAsync(PhotoItem photoA, PhotoItem photoB)
    {
        try
        {
            StatusText.Text = "Loading images...";

            // Load both images at full resolution
            var imageATask = _thumbnailGenerator.GeneratePreviewAsync(photoA.FilePath);
            var imageBTask = _thumbnailGenerator.GeneratePreviewAsync(photoB.FilePath);

            await Task.WhenAll(imageATask, imageBTask);

            var imageABitmap = await imageATask;
            var imageBBitmap = await imageBTask;

            if (imageABitmap == null || imageBBitmap == null)
            {
                StatusText.Text = "Error: Failed to load one or both images";
                MessageBox.Show("Failed to load one or both images.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            // Set images in the compare control
            CompareControl.SetImages(
                imageABitmap,
                imageBBitmap,
                photoA.FileName,
                photoB.FileName);

            StatusText.Text = $"Comparing: {photoA.FileName} (left) vs {photoB.FileName} (right)";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            MessageBox.Show($"Error loading images: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
