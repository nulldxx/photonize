using System.Globalization;
using System.Windows.Data;

namespace Photonize.Converters;

public class BoolToPlayPauseIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isPlaying)
        {
            return isPlaying ? "⏸" : "▶";
        }
        return "▶";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
