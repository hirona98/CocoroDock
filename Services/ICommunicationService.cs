using CocoroDock.Communication;
using System;
using System.Threading.Tasks;

namespace CocoroDock.Services
{
    /// <summary>
    /// ステータス更新用のイベント引数
    /// </summary>
    public class StatusUpdateEventArgs : EventArgs
    {
        public bool IsConnected { get; }
        public string? Message { get; }

        public StatusUpdateEventArgs(bool isConnected, string? message = null)
        {
            IsConnected = isConnected;
            Message = message;
        }
    }

    /// <summary>
    /// 通信サービスのインターフェース
    /// </summary>
    public interface ICommunicationService : IDisposable
    {
        /// <summary>
        /// チャットメッセージ受信イベント（CocoroDock APIから）
        /// </summary>
        event EventHandler<ChatRequest>? ChatMessageReceived;

        /// <summary>
        /// 通知メッセージ受信イベント（Notification APIから）
        /// </summary>
        event EventHandler<ChatMessagePayload>? NotificationMessageReceived;

        /// <summary>
        /// 制御コマンド受信イベント（CocoroDock APIから）
        /// </summary>
        event EventHandler<ControlRequest>? ControlCommandReceived;

        /// <summary>
        /// エラー発生イベント
        /// </summary>
        event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// ステータス更新要求イベント
        /// </summary>
        event EventHandler<StatusUpdateEventArgs>? StatusUpdateRequested;

        /// <summary>
        /// APIサーバーが起動しているかどうか
        /// </summary>
        bool IsServerRunning { get; }

        /// <summary>
        /// APIサーバーを開始
        /// </summary>
        Task StartServerAsync();

        /// <summary>
        /// APIサーバーを停止
        /// </summary>
        Task StopServerAsync();

        /// <summary>
        /// 現在の設定を取得
        /// </summary>
        ConfigSettings GetCurrentConfig();

        /// <summary>
        /// 設定を更新して保存
        /// </summary>
        /// <param name="settings">更新する設定情報</param>
        void UpdateAndSaveConfig(ConfigSettings settings);

        /// <summary>
        /// CocoroCoreにチャットメッセージを送信
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        /// <param name="characterName">キャラクター名（オプション）</param>
        Task SendChatToCoreAsync(string message, string? characterName = null);

        /// <summary>
        /// 新しい会話セッションを開始
        /// </summary>
        void StartNewConversation();

        /// <summary>
        /// CocoroShellにアニメーションコマンドを送信
        /// </summary>
        /// <param name="animationName">アニメーション名</param>
        Task SendAnimationToShellAsync(string animationName);

        /// <summary>
        /// CocoroShellに制御コマンドを送信
        /// </summary>
        /// <param name="command">コマンド名</param>
        Task SendControlToShellAsync(string command);

        /// <summary>
        /// 通知メッセージを処理（Notification API用）
        /// </summary>
        /// <param name="notification">通知メッセージ</param>
        Task ProcessNotificationAsync(ChatMessagePayload notification);
    }
}