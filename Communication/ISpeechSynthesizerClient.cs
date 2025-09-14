using System;
using System.IO;
using System.Threading.Tasks;

namespace CocoroDock.Communication
{
    /// <summary>
    /// 音声合成クライアントの統一インターフェース
    /// </summary>
    public interface ISpeechSynthesizerClient : IDisposable
    {
        /// <summary>
        /// 音声合成を実行し、音声ファイルのURLを返す
        /// </summary>
        /// <param name="text">合成するテキスト</param>
        /// <param name="characterSettings">キャラクター設定</param>
        /// <returns>音声ファイルのURL（失敗時はnull）</returns>
        Task<string?> SynthesizeAsync(string text, CharacterSettings characterSettings);

        /// <summary>
        /// 音声ファイルの静的配信用ストリームを取得
        /// </summary>
        /// <param name="fileName">ファイル名</param>
        /// <returns>FileStream（失敗時はnull）</returns>
        FileStream? GetAudioFileStream(string fileName);

        /// <summary>
        /// 接続テスト
        /// </summary>
        /// <returns>接続成功時true</returns>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// プロバイダー名
        /// </summary>
        string ProviderName { get; }
    }
}