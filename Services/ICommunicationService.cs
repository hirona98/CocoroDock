using CocoroDock.Communication;
using System;
using System.Collections.Generic;
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
        event Action<ChatMessagePayload, List<System.Windows.Media.Imaging.BitmapSource>?>? NotificationMessageReceived;

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
        /// CocoroCore2ステータス変更イベント
        /// </summary>
        event EventHandler<CocoroCore2Status>? StatusChanged;

        /// <summary>
        /// APIサーバーが起動しているかどうか
        /// </summary>
        bool IsServerRunning { get; }

        /// <summary>
        /// 現在のCocoroCore2ステータス
        /// </summary>
        CocoroCore2Status CurrentStatus { get; }

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
        /// CocoroCoreにAPIでチャットメッセージを送信
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        /// <param name="characterName">キャラクター名（オプション）</param>
        /// <param name="imageDataUrl">画像データURL（オプション）</param>
        Task SendChatToCoreUnifiedAsync(string message, string? characterName = null, string? imageDataUrl = null);

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
        /// 通知メッセージを処理（Notification API用）
        /// </summary>
        /// <param name="notification">通知メッセージ</param>
        /// <param name="imageDataUrls">画像データURL配列（オプション）</param>
        Task ProcessNotificationAsync(ChatMessagePayload notification, string[]? imageDataUrls = null);

        /// <summary>
        /// デスクトップモニタリング画像をCocoroCoreに送信
        /// </summary>
        /// <param name="imageBase64">Base64エンコードされた画像データ</param>
        Task SendDesktopMonitoringToCoreAsync(string imageBase64);

        /// <summary>
        /// CocoroShellにTTS状態を送信
        /// </summary>
        /// <param name="isUseTTS">TTS使用状態</param>
        Task SendTTSStateToShellAsync(bool isUseTTS);



        /// <summary>
        /// ログビューアーウィンドウを開く
        /// </summary>
        void OpenLogViewer();

        /// <summary>
        /// CocoroShellから現在のキャラクター位置を取得
        /// </summary>
        Task<PositionResponse> GetShellPositionAsync();

        /// <summary>
        /// CocoroShellに設定の部分更新を送信
        /// </summary>
        /// <param name="updates">更新する設定のキーと値のペア</param>
        Task SendConfigPatchToShellAsync(Dictionary<string, object> updates);

        /// <summary>
        /// 設定キャッシュを更新
        /// </summary>
        void RefreshSettingsCache();
    }
}