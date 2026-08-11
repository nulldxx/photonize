using System.Windows;

namespace Photonize.Views;

public partial class TagFolderDialog : Window
{
    // Public so WPF data binding can reflect over it
    public class TagRow
    {
        public int Tag { get; set; }
        public string FolderName { get; set; } = string.Empty;
        public int Count { get; set; }
        public string CountLabel => Count == 1 ? "1 photo" : $"{Count} photos";
    }

    private readonly List<TagRow> _rows;

    /// <summary>
    /// Sub-folder name chosen for each tag, keyed by tag digit.
    /// </summary>
    public Dictionary<int, string> FolderNames { get; private set; } = new();

    public TagFolderDialog(IDictionary<int, int> photoCountsByTag)
    {
        InitializeComponent();

        _rows = photoCountsByTag
            .OrderBy(entry => entry.Key)
            .Select(entry => new TagRow
            {
                Tag = entry.Key,
                FolderName = entry.Key.ToString(),
                Count = entry.Value
            })
            .ToList();

        TagRows.ItemsSource = _rows;
    }

    private void MoveButton_Click(object sender, RoutedEventArgs e)
    {
        FolderNames = _rows.ToDictionary(r => r.Tag, r => r.FolderName?.Trim() ?? string.Empty);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
