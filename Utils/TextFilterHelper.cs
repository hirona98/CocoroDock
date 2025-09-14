using System.Text.RegularExpressions;

namespace CocoroDock.Utils
{
    /// <summary>
    /// テキストフィルタリング用のヘルパークラス
    /// </summary>
    public static class TextFilterHelper
    {
        /// <summary>
        /// [face:～] パターンを除去する正規表現
        /// </summary>
        private static readonly Regex FacePatternRegex = new Regex(@"\[face:[^\]]*\]", RegexOptions.Compiled);

        /// <summary>
        /// テキストから[face:～]パターンを除去します
        /// </summary>
        /// <param name="text">フィルタリング対象のテキスト</param>
        /// <returns>フィルタリング後のテキスト</returns>
        public static string RemoveFacePatterns(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return FacePatternRegex.Replace(text, "");
        }
    }
}