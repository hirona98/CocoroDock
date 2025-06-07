using CocoroDock.Communication;
using System;
using System.Threading.Tasks;

namespace CocoroDock.Services
{
    /// <summary>
    /// 通信サービスのインターフェース
    /// </summary>
    public interface ICommunicationService : IDisposable
    {
        /// <summary>
        /// チャットメッセージ受信イベント
        /// </summary>
        event EventHandler<string>? ChatMessageReceived;

        /// <summary>
        /// 通知メッセージ受信イベント
        /// </summary>
        event EventHandler<ChatMessagePayload>? NotificationMessageReceived;

        /// <summary>
        /// 設定レスポンス受信イベント
        /// </summary>
        event EventHandler<ConfigResponsePayload>? ConfigResponseReceived;

        /// <summary>
        /// ステータス更新受信イベント
        /// </summary>
        event EventHandler<StatusMessagePayload>? StatusUpdateReceived;

        /// <summary>
        /// システムメッセージ受信イベント
        /// </summary>
        event EventHandler<SystemMessagePayload>? SystemMessageReceived;

        /// <summary>
        /// 制御メッセージ受信イベント
        /// </summary>
        event EventHandler<ControlMessagePayload>? ControlMessageReceived;

        /// <summary>
        /// エラー発生イベント
        /// </summary>
        event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// 接続イベント
        /// </summary>
        event EventHandler? Connected;

        /// <summary>
        /// 切断イベント
        /// </summary>
        event EventHandler? Disconnected;

        /// <summary>
        /// サーバーが起動しているかどうか
        /// </summary>
        bool IsServerRunning { get; }

        /// <summary>
        /// サーバーを開始
        /// </summary>
        Task StartServerAsync();

        /// <summary>
        /// サーバーを停止
        /// </summary>
        Task StopServerAsync();

        /// <summary>
        /// 設定情報を要求
        /// </summary>
        Task RequestConfigAsync();

        /// <summary>
        /// 設定情報を更新
        /// </summary>
        /// <param name="settings">更新する設定情報</param>
        Task UpdateConfigAsync(ConfigSettings settings);

        /// <summary>
        /// チャットメッセージを送信
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        Task SendChatMessageAsync(string message);

        /// <summary>
        /// 設定を変更
        /// </summary>
        /// <param name="settingKey">設定キー</param>
        /// <param name="value">設定値</param>
        Task ChangeConfigAsync(string settingKey, string value);

        /// <summary>
        /// 制御コマンドを送信
        /// </summary>
        /// <param name="command">コマンド名</param>
        /// <param name="reason">理由</param>
        Task SendControlCommandAsync(string command, string reason);

        /// <summary>
        /// 新しいチャットセッションを開始
        /// </summary>
        void StartNewSession();

        /// <summary>
        /// 指定されたタイプとペイロードのメッセージを送信
        /// </summary>
        /// <param name="type">メッセージタイプ</param>
        /// <param name="payload">ペイロードデータ</param>
        Task SendMessageAsync(MessageType type, object payload);
    }
}