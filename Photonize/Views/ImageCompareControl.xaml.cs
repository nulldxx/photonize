using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photonize.Views;

public partial class ImageCompareControl : UserControl
{
    public ImageCompareControl()
    {
        InitializeComponent();
        SizeChanged += ImageCompareControl_SizeChanged;
    }

    public void SetImages(BitmapImage imageA, BitmapImage imageB, string labelA, string labelB)
    {
        ImageA.Source = imageA;
        ImageB.Source = imageB;
        LabelA.Text = labelA;
        LabelB.Text = labelB;

        // Initialize clip at center
        UpdateClipGeometry(ActualWidth / 2);
    }

    private void ImageCompareControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Update clip geometry when control size changes
        if (DividerLine.Visibility == Visibility.Visible)
        {
            double x = DividerLine.X1;
            UpdateClipGeometry(x);
        }
        else
        {
            // Initialize at center if not yet shown
            UpdateClipGeometry(ActualWidth / 2);
        }
    }

    private void CompareGrid_MouseMove(object sender, MouseEventArgs e)
    {
        Point position = e.GetPosition(CompareGrid);
        double x = position.X;

        // Clamp to control bounds
        x = Math.Max(0, Math.Min(x, ActualWidth));

        UpdateClipGeometry(x);
        UpdateDividerLine(x);
    }

    private void CompareGrid_MouseEnter(object sender, MouseEventArgs e)
    {
        DividerLine.Visibility = Visibility.Visible;
    }

    private void CompareGrid_MouseLeave(object sender, MouseEventArgs e)
    {
        DividerLine.Visibility = Visibility.Collapsed;
    }

    private void UpdateClipGeometry(double x)
    {
        // Clip the top image (Image A) to only show the left portion up to x
        ClipGeometry.Rect = new Rect(0, 0, x, ActualHeight);
    }

    private void UpdateDividerLine(double x)
    {
        // Position the vertical divider line at the mouse position
        DividerLine.X1 = x;
        DividerLine.X2 = x;
        DividerLine.Y1 = 0;
        DividerLine.Y2 = ActualHeight;
    }
}
