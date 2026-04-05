using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ImageCaptionSearch.UI.Converters;

public class BitmapValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    return new Bitmap(path);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading bitmap {path}: {ex.Message}");
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
