using System;
using System.Diagnostics;

namespace CocoroDock.Communication
{
    /// <summary>
    /// 音声合成クライアントのファクトリー
    /// </summary>
    public static class SpeechSynthesizerFactory
    {
        /// <summary>
        /// キャラクター設定に基づいて適切な音声合成クライアントを作成
        /// </summary>
        /// <param name="characterSettings">キャラクター設定</param>
        /// <param name="audioDirectory">音声ファイル保存ディレクトリ</param>
        /// <returns>音声合成クライアント</returns>
        public static ISpeechSynthesizerClient CreateClient(CharacterSettings characterSettings, string audioDirectory = "wwwroot/audio")
        {
            if (characterSettings == null)
            {
                throw new ArgumentNullException(nameof(characterSettings));
            }

            try
            {
                return characterSettings.ttsType?.ToLower() switch
                {
                    "voicevox" => new VoicevoxClient(
                        characterSettings.voicevoxConfig?.endpointUrl ?? "http://127.0.0.1:50021",
                        audioDirectory),

                    "style-bert-vits2" => new StyleBertVits2Client(
                        characterSettings.styleBertVits2Config,
                        audioDirectory),

                    "aivis-cloud" => new AivisCloudClient(
                        characterSettings.aivisCloudConfig,
                        audioDirectory),

                    _ => new VoicevoxClient(
                        characterSettings.voicevoxConfig?.endpointUrl ?? "http://127.0.0.1:50021",
                        audioDirectory)
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpeechSynthesizerFactory] クライアント作成エラー {characterSettings.ttsType}: {ex.Message}");
                // フォールバックとしてVOICEVOXクライアントを返す
                return new VoicevoxClient(
                    characterSettings.voicevoxConfig?.endpointUrl ?? "http://127.0.0.1:50021",
                    audioDirectory);
            }
        }

        /// <summary>
        /// サポートされているTTSタイプかどうかをチェック
        /// </summary>
        /// <param name="ttsType">TTSタイプ</param>
        /// <returns>サポートされている場合true</returns>
        public static bool IsSupportedTtsType(string ttsType)
        {
            return ttsType?.ToLower() switch
            {
                "voicevox" => true,
                "style-bert-vits2" => true,
                "aivis-cloud" => true,
                _ => false
            };
        }

        /// <summary>
        /// 利用可能なTTSタイプの一覧を取得
        /// </summary>
        /// <returns>TTSタイプの配列</returns>
        public static string[] GetAvailableTtsTypes()
        {
            return new[] { "voicevox", "style-bert-vits2", "aivis-cloud" };
        }
    }
}