using System.Windows;

namespace Photonize.Views;

public enum ImportMode
{
    Append,
    Distribute
}

public partial class ImportDialog : Window
{
    public ImportMode SelectedMode { get; private set; }

    public ImportDialog(int existingCount, int newCount)
    {
        InitializeComponent();
        FileCountText.Text = $"Adding {newCount} new photo(s) to {existingCount} existing photo(s)";
    }

    private void AppendButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = ImportMode.Append;
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
