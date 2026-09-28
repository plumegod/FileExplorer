using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace FileExplorer.Extension.VideoPreview.Converters
{
    public class LongToDoubleConverter : MarkupExtension, IValueConverter
    {
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return System.Convert.ToInt64(value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return System.Convert.ToDouble(value);
        }
    }
}
