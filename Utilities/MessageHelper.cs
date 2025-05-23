using CocoroDock.Communication;
using System;
using System.Text;
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
            return JsonSerializer.Deserialize<T>(json);
        }

        /// <summary>
        /// テキストをBase64エンコードします
        /// </summary>
        /// <param name="text">エンコードするテキスト</param>
        /// <returns>Base64エンコードされたテキスト</returns>
        public static string EncodeToBase64(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Base64テキストをデコードします
        /// </summary>
        /// <param name="base64Text">デコードするBase64テキスト</param>
        /// <returns>デコードされたテキスト</returns>
        public static string DecodeFromBase64(string base64Text)
        {
            try
            {
                var bytes = Convert.FromBase64String(base64Text);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                // Base64フォーマットでない場合は元のテキストを返す
                return base64Text;
            }
        }

        /// <summary>
        /// 指定されたタイプとペイロードでWebSocketMessageを作成します
        /// </summary>
        /// <param name="type">メッセージタイプ</param>
        /// <param name="payload">ペイロード</param>
        /// <returns>WebSocketMessageオブジェクト</returns>
        public static WebSocketMessage CreateMessage(MessageType type, object payload)
        {
            return new WebSocketMessage(type, payload);
        }
    }
}