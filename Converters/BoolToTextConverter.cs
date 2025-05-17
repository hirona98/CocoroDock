using System;
using System.Globalization;
using System.Windows.Data;

namespace CocoroDock.Converters
{
    public class BoolToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = (bool)value;
            string parameterString = parameter as string ?? "False|True";
            string[] texts = parameterString.Split('|');

            if (texts.Length != 2)
                return boolValue.ToString();

            return boolValue ? texts[1] : texts[0];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string stringValue = value as string ?? "";
            string parameterString = parameter as string ?? "False|True";
            string[] texts = parameterString.Split('|');

            if (texts.Length != 2)
                return false;

            return stringValue == texts[1];
        }
    }
}
