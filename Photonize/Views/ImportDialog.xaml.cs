using System.Windows;

namespace Photonize.Views;

public enum ImportMode
{
    Insert,
    Distribute
}

public partial class ImportDialog : Window
{
    public ImportMode SelectedMode { get; private set; }

    public ImportDialog(int existingCount, int newCount, int insertIndex)
    {
        InitializeComponent();
        FileCountText.Text = insertIndex >= existingCount
            ? $"Adding {newCount} new photo(s) to {existingCount} existing photo(s)"
            : $"Adding {newCount} new photo(s) to {existingCount} existing photo(s), inserting at position {insertIndex + 1}";
    }

    private void InsertButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = ImportMode.Insert;
        DialogResult = true;
        Close();
    }

    private void DistributeButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = ImportMode.Distribute;
        DialogResult = true;
        Close();
    }
}
