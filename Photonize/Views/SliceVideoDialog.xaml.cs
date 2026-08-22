using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Photonize.Helpers;

namespace Photonize.Views;

/// <summary>
/// Collects the part length for a slice. The first numeric-input dialog in the
/// app, so it stays a plain TextBox with digit filtering rather than dragging in
/// a third-party numeric control (which would also mean an installer edit).
/// </summary>
public partial class SliceVideoDialog : Window
{
    private static readonly Regex NonDigit = new(@"[^0-9]", RegexOptions.Compiled);

    private readonly TimeSpan? _duration;

    /// <summary>The chosen part length. Only meaningful when DialogResult is true.</summary>
    public int SecondsPerPart { get; private set; }

    /// <param name="subject">"clip.mp4" or "3 videos".</param>
    /// <param name="duration">
    /// Clip length when a single clip was selected, so the dialog can rule out a
    /// length that would produce one part. Null for a multi-clip slice, where
    /// there is no single duration to validate against.
    /// </param>
    public SliceVideoDialog(string subject, TimeSpan? duration)
    {
        InitializeComponent();

        _duration = duration;

        SubjectText.Text = duration is { } clipLength
            ? $"{subject} — {VideoOutputNaming.FormatDuration(clipLength)}"
            : subject;

        SecondsBox.Text = DefaultSeconds(duration).ToString();
        SecondsBox.SelectAll();
        SecondsBox.Focus();
    }

    /// <summary>
    /// A minute is the useful default for anything long; for a short clip, half
    /// its length at least guarantees two parts.
    /// </summary>
    private static int DefaultSeconds(TimeSpan? duration)
    {
        if (duration is not { } clipLength || clipLength.TotalSeconds > 120)
            return 60;

        return Math.Max(1, (int)(clipLength.TotalSeconds / 2));
    }

    // Digits only, so a non-numeric value can never be typed rather than merely
    // being rejected once the user has committed to it.
    private void SecondsBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = NonDigit.IsMatch(e.Text);

    private void SecondsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // TextChanged fires during InitializeComponent, before SliceButton exists.
        if (SliceButton == null || PreviewText == null)
            return;

        if (!int.TryParse(SecondsBox.Text, out var seconds))
        {
            SliceButton.IsEnabled = false;
            PreviewText.Text = "Enter a part length of at least 1 second.";
            return;
        }

        if (!VideoOutputNaming.IsValidPartLength(seconds, _duration, out var error))
        {
            SliceButton.IsEnabled = false;
            PreviewText.Text = error;
            return;
        }

        SliceButton.IsEnabled = true;
        PreviewText.Text = _duration is { } clipLength
            ? $"→ about {VideoOutputNaming.ExpectedPartCount(clipLength, seconds)} parts of ~{seconds}s each"
            : $"→ parts of ~{seconds}s each";
    }

    private void SliceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SecondsBox.Text, out var seconds) ||
            !VideoOutputNaming.IsValidPartLength(seconds, _duration, out _))
        {
            return;
        }

        SecondsPerPart = seconds;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
