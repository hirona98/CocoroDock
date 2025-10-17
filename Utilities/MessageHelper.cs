using System.Text.Json;

namespace CocoroDock.Utilities
{
    /// <summary>
    /// メッセージ処理に関するユーティリティメソッドを提供するヘルパークラス
    /// </summary>
    public static class MessageHelper
    {
        /// <summary>
        /// オブジェクトをJSON形式にシリアライズします
        /// </summary>
        /// <param name="obj">シリアライズするオブジェクト</param>
        /// <returns>JSONテキスト</returns>
        public static string SerializeToJson(object obj)
        {
            return JsonSerializer.Serialize(obj);
        }

        /// <summary>
        /// JSONテキストを指定した型にデシリアライズします
        /// </summary>
        /// <typeparam name="T">デシリアライズする型</typeparam>
        /// <param name="json">JSONテキスト</param>
        /// <returns>デシリアライズされたオブジェクト</returns>
        public static T? DeserializeFromJson<T>(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<T>(json, options);
        }

    }
}