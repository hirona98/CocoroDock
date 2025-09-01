using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using CocoroDock.Communication;

namespace CocoroDock.Utilities
{
    /// <summary>
    /// オブジェクトのディープコピーを提供する拡張メソッドクラス
    /// </summary>
    public static class DeepCopyExtensions
    {
        /// <summary>
        /// JSONシリアライゼーションを使用してオブジェクトのディープコピーを作成
        /// </summary>
        /// <typeparam name="T">コピー対象の型</typeparam>
        /// <param name="source">コピー元オブジェクト</param>
        /// <returns>ディープコピーされたオブジェクト</returns>
        /// <exception cref="ArgumentNullException">sourceがnullの場合</exception>
        /// <exception cref="InvalidOperationException">シリアライゼーションに失敗した場合</exception>
        public static T DeepCopy<T>(this T source) where T : class
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            try
            {
                // JSONシリアライゼーション設定
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null, // プロパティ名をそのまま使用
                    WriteIndented = false,       // コンパクトなJSON
                    IncludeFields = false,       // フィールドは含めない
                    MaxDepth = 64               // 循環参照対策
                };

                var json = JsonSerializer.Serialize(source, options);
                var copy = JsonSerializer.Deserialize<T>(json, options);

                if (copy == null)
                    throw new InvalidOperationException($"Failed to deserialize object of type {typeof(T).Name}");

                return copy;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to deep copy object of type {typeof(T).Name}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// リストのディープコピーを作成
        /// </summary>
        /// <typeparam name="T">リスト要素の型</typeparam>
        /// <param name="source">コピー元リスト</param>
        /// <returns>ディープコピーされたリスト</returns>
        public static List<T>? DeepCopyList<T>(this List<T>? source) where T : class
        {
            if (source == null)
                return null;

            return source.Select(item => item.DeepCopy()).ToList();
        }

        /// <summary>
        /// Dictionaryのディープコピーを作成
        /// </summary>
        /// <typeparam name="TKey">キーの型</typeparam>
        /// <typeparam name="TValue">値の型</typeparam>
        /// <param name="source">コピー元Dictionary</param>
        /// <returns>ディープコピーされたDictionary</returns>
        public static Dictionary<TKey, TValue>? DeepCopyDictionary<TKey, TValue>(this Dictionary<TKey, TValue>? source)
            where TValue : class
            where TKey : notnull
        {
            if (source == null)
                return null;

            var copy = new Dictionary<TKey, TValue>();
            foreach (var kvp in source)
            {
                copy[kvp.Key] = kvp.Value.DeepCopy();
            }
            return copy;
        }

        /// <summary>
        /// 値型のDictionaryの浅いコピーを作成（値型はディープコピー不要）
        /// </summary>
        /// <typeparam name="TKey">キーの型</typeparam>
        /// <typeparam name="TValue">値の型</typeparam>
        /// <param name="source">コピー元Dictionary</param>
        /// <returns>コピーされたDictionary</returns>
        public static Dictionary<TKey, TValue>? ShallowCopyDictionary<TKey, TValue>(this Dictionary<TKey, TValue>? source)
            where TKey : notnull
        {
            if (source == null)
                return null;

            return new Dictionary<TKey, TValue>(source);
        }

        /// <summary>
        /// ディープコピー機能の動作テスト（デバッグ用）
        /// </summary>
        /// <remarks>
        /// 開発時にディープコピーが正しく動作しているかを確認するためのテストメソッド
        /// リリース時には削除または無効化することを推奨
        /// </remarks>
        [Conditional("DEBUG")]
        public static void TestDeepCopyFunctionality()
        {
            try
            {
                Debug.WriteLine("=== ディープコピー機能テスト開始 ===");

                // CharacterSettingsのテスト
                var originalCharacter = new CharacterSettings
                {
                    modelName = "テストキャラクター",
                    isUseLLM = true,
                    apiKey = "test-key",
                    styleBertVits2Config = new StyleBertVits2Config
                    {
                        modelName = "test-model",
                        speakerName = "test-speaker"
                    }
                };

                var copiedCharacter = originalCharacter.DeepCopy();

                // オブジェクト参照が異なることを確認
                bool isDeepCopy = !ReferenceEquals(originalCharacter, copiedCharacter) &&
                                  !ReferenceEquals(originalCharacter.styleBertVits2Config, copiedCharacter.styleBertVits2Config);

                if (isDeepCopy)
                {
                    Debug.WriteLine("✓ CharacterSettings ディープコピー成功");
                }
                else
                {
                    Debug.WriteLine("✗ CharacterSettings ディープコピー失敗");
                }

                // 値の変更テスト
                copiedCharacter.modelName = "変更されたキャラクター";
                copiedCharacter.styleBertVits2Config.modelName = "changed-model";

                bool valuesIndependent = originalCharacter.modelName != copiedCharacter.modelName &&
                                        originalCharacter.styleBertVits2Config.modelName != copiedCharacter.styleBertVits2Config.modelName;

                if (valuesIndependent)
                {
                    Debug.WriteLine("✓ 値の独立性確認成功");
                }
                else
                {
                    Debug.WriteLine("✗ 値の独立性確認失敗");
                }

                // ConfigSettingsのテスト
                var originalConfig = new ConfigSettings
                {
                    isEnableNotificationApi = true,
                    characterList = new List<CharacterSettings> { originalCharacter },
                    screenshotSettings = new ScreenshotSettings { enabled = true, intervalMinutes = 10 }
                };

                var copiedConfig = originalConfig.DeepCopy();

                bool isConfigDeepCopy = !ReferenceEquals(originalConfig, copiedConfig) &&
                                       !ReferenceEquals(originalConfig.characterList, copiedConfig.characterList) &&
                                       !ReferenceEquals(originalConfig.screenshotSettings, copiedConfig.screenshotSettings);

                if (isConfigDeepCopy)
                {
                    Debug.WriteLine("✓ ConfigSettings ディープコピー成功");
                }
                else
                {
                    Debug.WriteLine("✗ ConfigSettings ディープコピー失敗");
                }

                Debug.WriteLine("=== ディープコピー機能テスト完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"✗ ディープコピー機能テスト中にエラー発生: {ex.Message}");
            }
        }
    }
}