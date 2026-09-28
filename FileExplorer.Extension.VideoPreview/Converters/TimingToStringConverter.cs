using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace FileExplorer.Extension.VideoPreview.Converters
{
    public class TimingToStringConverter : MarkupExtension, IMultiValueConverter
    {
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is double value1 && values[1] is double value2)
            {
                TimeSpan current = TimeSpan.FromSeconds(value1);
                TimeSpan maximum = TimeSpan.FromSeconds(value2);

                if (value2 > TimeSpan.TicksPerSecond)
                {
                    current = TimeSpan.FromTicks(System.Convert.ToInt64(value1));
                    maximum = TimeSpan.FromTicks(System.Convert.ToInt64(value2));
                }

                return maximum.Hours > 0 ? $"{current:hh\\:mm\\:ss}" : $"{current:mm\\:ss}";
            }

            return "00:00";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
