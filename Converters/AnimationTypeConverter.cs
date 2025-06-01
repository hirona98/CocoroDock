using System;
using System.Globalization;
using System.Windows.Data;

namespace CocoroDock.Converters
{
    /// <summary>
    /// アニメーションタイプの数値を文字列に変換するコンバーター
    /// </summary>
    public class AnimationTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int animationType)
            {
                return animationType switch
                {
                    0 => "立ち",
                    1 => "座り",
                    2 => "寝る",
                    _ => "不明"
                };
            }
            return "不明";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}