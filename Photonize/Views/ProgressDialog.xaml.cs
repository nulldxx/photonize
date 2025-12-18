using System.Windows;

namespace Photonize.Views;

public partial class ProgressDialog : Window
{
    public bool WasCancelled { get; private set; }

    public ProgressDialog()
    {
        InitializeComponent();
        WasCancelled = false;
    }

    public void UpdateProgress(int current, int total, string statusMessage)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.Maximum = total;
            ProgressBar.Value = current;
            ProgressText.Text = $"{current} of {total}";
            StatusText.Text = statusMessage;
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        CancelButton.IsEnabled = false;
        CancelButton.Content = "Cancelling...";
        StatusText.Text = "Cancelling operation...";
    }
}
