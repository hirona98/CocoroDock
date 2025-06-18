using CocoroDock.Communication;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CocoroDock.Services
{
    /// <summary>
    /// CocoroAIとの通信を管理するサービスクラス
    /// </summary>
    public class CommunicationService : ICommunicationService
    {
        private readonly CocoroDockApiServer _apiServer;
        private readonly CocoroShellClient _shellClient;
        private readonly CocoroCoreClient _coreClient;
        private readonly IAppSettings _appSettings;
        private readonly NotificationApiServer? _notificationApiServer;

        // セッション管理用
        private string? _currentSessionId;
        private string? _currentContextId;

        public event EventHandler<ChatRequest>? ChatMessageReceived;
        public event EventHandler<ChatMessagePayload>? NotificationMessageReceived;
        public event EventHandler<ControlRequest>? ControlCommandReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<StatusUpdateEventArgs>? StatusUpdateRequested;

        public bool IsServerRunning => _apiServer.IsRunning;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="appSettings">アプリケーション設定</param>
        public CommunicationService(IAppSettings appSettings)
        {
            _appSettings = appSettings;

            // APIサーバーの初期化
            _apiServer = new CocoroDockApiServer(_appSettings.CocoroDockPort, _appSettings);
            _apiServer.ChatMessageReceived += (sender, request) => ChatMessageReceived?.Invoke(this, request);
            _apiServer.ControlCommandReceived += (sender, request) => ControlCommandReceived?.Invoke(this, request);

            // CocoroShellクライアントの初期化
            _shellClient = new CocoroShellClient(_appSettings.CocoroShellPort);

            // CocoroCoreクライアントの初期化
            _coreClient = new CocoroCoreClient(_appSettings.CocoroCorePort);

            // 通知APIサーバーの初期化（有効な場合のみ）
            if (_appSettings.IsEnableNotificationApi)
            {
                _notificationApiServer = new NotificationApiServer(_appSettings.NotificationApiPort, this);
            }
        }


        /// <summary>
        /// APIサーバーを開始
        /// </summary>
        public async Task StartServerAsync()
        {
            try
            {
                // CocoroDock APIサーバーを起動
                await _apiServer.StartAsync();

                // 通知APIサーバーを起動（有効な場合）
                if (_notificationApiServer != null)
                {
                    await _notificationApiServer.StartAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CommunicationService: サーバー起動エラー: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"サーバー起動に失敗しました: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// APIサーバーを停止
        /// </summary>
        public async Task StopServerAsync()
        {
            try
            {
                // 通知APIサーバーを停止
                if (_notificationApiServer != null)
                {
                    await _notificationApiServer.StopAsync();
                }

                // CocoroDock APIサーバーを停止
                await _apiServer.StopAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CommunicationService: サーバー停止エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在の設定を取得
        /// </summary>
        public ConfigSettings GetCurrentConfig()
        {
            return _appSettings.GetConfigSettings();
        }

        /// <summary>
        /// 設定を更新して保存
        /// </summary>
        /// <param name="settings">更新する設定情報</param>
        public void UpdateAndSaveConfig(ConfigSettings settings)
        {
            _appSettings.UpdateSettings(settings);
            _appSettings.SaveSettings();
        }

        /// <summary>
        /// 新しい会話セッションを開始
        /// </summary>
        public void StartNewConversation()
        {
            _currentSessionId = null;
            _currentContextId = null;
            Debug.WriteLine("新しい会話セッションを開始しました");
        }

        /// <summary>
        /// CocoroCoreにチャットメッセージを送信
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        /// <param name="characterName">キャラクター名（オプション）</param>
        /// <param name="imageDataUrl">画像データURL（オプション）</param>
        public async Task SendChatToCoreAsync(string message, string? characterName = null, string? imageDataUrl = null)
        {
            try
            {
                // 現在のキャラクター設定を取得
                var currentCharacter = GetCurrentCharacterSettings();

                // セッションIDを生成または既存のものを使用
                if (string.IsNullOrEmpty(_currentSessionId))
                {
                    _currentSessionId = $"dock_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                }

                // 画像データがある場合はfilesリストを作成
                List<object>? files = null;
                if (!string.IsNullOrEmpty(imageDataUrl))
                {
                    files = new List<object>
                    {
                        new Dictionary<string, string>
                        {
                            { "type", "image" },
                            { "url", imageDataUrl }
                        }
                    };
                }

                // AIAvatarKit仕様のリクエストを作成
                var request = new CoreChatRequest
                {
                    type = "invoke",
                    session_id = _currentSessionId,
                    user_id = "user",
                    context_id = _currentContextId, // 前回の会話からのコンテキストを使用
                    text = message,
                    audio_data = null, // テキストのみ
                    files = files,
                    system_prompt_params = null,
                    metadata = new Dictionary<string, object>
                    {
                        { "source", "CocoroDock" },
                        { "character_name", characterName ?? currentCharacter?.modelName ?? "default" }
                    }
                };

                var response = await _coreClient.SendChatMessageAsync(request);

                // SSEレスポンスから新しいcontext_idを保存
                if (!string.IsNullOrEmpty(response.context_id))
                {
                    _currentContextId = response.context_id;
                    Debug.WriteLine($"新しいcontext_idを取得: {_currentContextId}");
                }

                // 成功時のステータス更新
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "メッセージを送信しました"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroCoreへのチャット送信エラー: {ex.Message}");
                // ステータスバーにエラー表示
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"CocoroCoreへの通信エラー: {ex.Message}"));
            }
        }

        /// <summary>
        /// CocoroShellにアニメーションコマンドを送信
        /// </summary>
        /// <param name="animationName">アニメーション名</param>
        public async Task SendAnimationToShellAsync(string animationName)
        {
            try
            {
                var request = new AnimationRequest
                {
                    animationName = animationName
                };

                await _shellClient.SendAnimationCommandAsync(request);

                // 成功時のステータス更新
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, $"アニメーション '{animationName}' を実行しました"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アニメーションコマンド送信エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"アニメーション制御エラー: {ex.Message}"));
            }
        }

        /// <summary>
        /// CocoroShellに制御コマンドを送信
        /// </summary>
        /// <param name="command">コマンド名</param>
        public async Task SendControlToShellAsync(string command)
        {
            try
            {
                var request = new ShellControlRequest
                {
                    command = command,
                    @params = new System.Collections.Generic.Dictionary<string, object>()
                };

                await _shellClient.SendControlCommandAsync(request);

                // 成功時のステータス更新
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, $"コマンド '{command}' を実行しました"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"制御コマンド送信エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"制御コマンドエラー: {ex.Message}"));
            }
        }

        /// <summary>
        /// 通知メッセージを処理（Notification API用）
        /// </summary>
        /// <param name="notification">通知メッセージ</param>
        public async Task ProcessNotificationAsync(ChatMessagePayload notification)
        {
            try
            {
                // 通知イベントを発火
                NotificationMessageReceived?.Invoke(this, notification);

                // AIAvatarKit仕様のリクエストを作成してCocoroCoreに転送
                var request = new CoreNotificationRequest
                {
                    type = "invoke",
                    session_id = $"notif_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}",
                    user_id = notification.userId,
                    context_id = null,
                    text = $"[{notification.userId}] {notification.message}",
                    metadata = new Dictionary<string, object>
                    {
                        { "source", "notification" },
                        { "original_session_id", notification.sessionId },
                        { "notification_from", notification.userId }
                    }
                };

                await _coreClient.SendNotificationAsync(request);

                // 成功時のステータス更新
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "通知を処理しました"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"通知処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のキャラクター設定を取得
        /// </summary>
        private CharacterSettings? GetCurrentCharacterSettings()
        {
            var config = _appSettings.GetConfigSettings();
            if (config.characterList != null &&
                config.currentCharacterIndex >= 0 &&
                config.currentCharacterIndex < config.characterList.Count)
            {
                return config.characterList[config.currentCharacterIndex];
            }
            return null;
        }

        /// <summary>
        /// 通知メッセージ受信イベントを発火（内部使用）
        /// </summary>
        /// <param name="notification">通知メッセージペイロード</param>
        public void RaiseNotificationMessageReceived(ChatMessagePayload notification)
        {
            NotificationMessageReceived?.Invoke(this, notification);
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            _notificationApiServer?.Dispose();
            _apiServer?.Dispose();
            _shellClient?.Dispose();
            _coreClient?.Dispose();
        }
    }
}