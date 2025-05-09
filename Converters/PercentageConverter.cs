using System;
using System.Globalization;
using System.Windows.Data;

namespace CocoroDock.Converters
{
    /// <summary>
    /// 数値をパーセンテージ表示に変換するコンバーター
    /// </summary>
    public class PercentageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                return doubleValue * 100;
            }
            else if (value is float floatValue)
            {
                return floatValue * 100;
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                return doubleValue / 100;
            }
            else if (value is string stringValue && double.TryParse(stringValue, out double parsedValue))
            {
                return parsedValue / 100;
            }

            return value;
        }
    }
}