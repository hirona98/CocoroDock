using CocoroDock.Communication;
using CocoroDock.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace CocoroDock.Services
{
    /// <summary>
    /// CocoroAIとの通信を管理するサービスクラス
    /// </summary>
    public class CommunicationService : ICommunicationService
    {
        // リアルタイムストリーミング設定（即座表示方式）

        private readonly CocoroDockApiServer _apiServer;
        private readonly CocoroShellClient _shellClient;
        private readonly CocoroCoreClient _coreClient;
        private readonly WebSocketChatClient _webSocketClient;
        private readonly IAppSettings _appSettings;
        private readonly NotificationApiServer? _notificationApiServer;
        private readonly StatusPollingService _statusPollingService;

        // セッション管理用
        private string? _currentSessionId;

        // WebSocket即座表示用


        // 設定キャッシュ用
        private ConfigSettings? _cachedConfigSettings;
        private readonly Dictionary<string, string> _cachedSystemPrompts = new Dictionary<string, string>();

        public event EventHandler<ChatRequest>? ChatMessageReceived;
        public event EventHandler<StreamingChatEventArgs>? StreamingChatReceived;
        public event Action<ChatMessagePayload, List<System.Windows.Media.Imaging.BitmapSource>?>? NotificationMessageReceived;
        public event EventHandler<ControlRequest>? ControlCommandReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<StatusUpdateEventArgs>? StatusUpdateRequested;
        public event EventHandler<CocoroCoreMStatus>? StatusChanged;

        public bool IsServerRunning => _apiServer.IsRunning;

        /// <summary>
        /// 現在のCocoroCoreMステータス
        /// </summary>
        public CocoroCoreMStatus CurrentStatus => _statusPollingService.CurrentStatus;

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
            _apiServer.StatusUpdateReceived += (sender, request) =>
            {
                // ステータス更新イベントを発火
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, request.message));
            };

            // CocoroShellクライアントの初期化
            _shellClient = new CocoroShellClient(_appSettings.CocoroShellPort);

            // CocoroCoreクライアントの初期化
            _coreClient = new CocoroCoreClient(_appSettings.CocoroCorePort);

            // WebSocketクライアントの初期化
            _webSocketClient = new WebSocketChatClient(_appSettings.CocoroCorePort, $"dock_{DateTime.Now:yyyyMMddHHmmssfff}");
            _webSocketClient.MessageReceived += OnWebSocketMessageReceived;
            _webSocketClient.ConnectionStateChanged += OnWebSocketConnectionStateChanged;
            _webSocketClient.ErrorOccurred += OnWebSocketErrorOccurred;

            // 通知APIサーバーの初期化（有効な場合のみ）
            if (_appSettings.IsEnableNotificationApi)
            {
                _notificationApiServer = new NotificationApiServer(_appSettings.NotificationApiPort, this);
            }

            // 設定キャッシュを初期化
            RefreshSettingsCache();

            // ステータスポーリングサービスの初期化
            _statusPollingService = new StatusPollingService($"http://127.0.0.1:{_appSettings.CocoroCorePort}");
            _statusPollingService.StatusChanged += (sender, status) => StatusChanged?.Invoke(this, status);

            // AppSettingsの変更イベントを購読
            AppSettings.SettingsSaved += OnSettingsSaved;
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
        /// 設定キャッシュを更新
        /// </summary>
        public void RefreshSettingsCache()
        {
            _cachedConfigSettings = _appSettings.GetConfigSettings();
            // SystemPromptキャッシュもクリア（次回アクセス時に再読み込み）
            _cachedSystemPrompts.Clear();
        }

        /// <summary>
        /// AppSettings保存イベントハンドラー
        /// </summary>
        private void OnSettingsSaved(object? sender, EventArgs e)
        {
            RefreshSettingsCache();
        }

        /// <summary>
        /// キャッシュされたSystemPromptを取得
        /// </summary>
        /// <param name="promptFilePath">プロンプトファイルのパス</param>
        /// <returns>プロンプトテキスト</returns>
        private string? GetCachedSystemPrompt(string? promptFilePath)
        {
            if (string.IsNullOrEmpty(promptFilePath))
                return null;

            // キャッシュから取得
            if (_cachedSystemPrompts.TryGetValue(promptFilePath, out var cachedPrompt))
            {
                return cachedPrompt;
            }

            // キャッシュにない場合はファイルから読み込んでキャッシュに保存
            var prompt = AppSettings.Instance.LoadSystemPrompt(promptFilePath);
            _cachedSystemPrompts[promptFilePath] = prompt;
            return prompt;
        }

        /// <summary>
        /// 新しい会話セッションを開始
        /// </summary>
        public void StartNewConversation()
        {
            _currentSessionId = null;
            Debug.WriteLine("新しい会話セッションを開始しました");
        }

        /// <summary>
        /// CocoroCoreMにWebSocketチャットメッセージを送信
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        /// <param name="characterName">キャラクター名（オプション）</param>
        /// <param name="imageDataUrl">画像データURL（オプション）</param>
        public async Task SendChatToCoreUnifiedAsync(string message, string? characterName = null, string? imageDataUrl = null)
        {
            // 単一画像を配列に変換して複数画像対応版を呼び出し
            var imageDataUrls = imageDataUrl != null ? new List<string> { imageDataUrl } : null;
            await SendChatToCoreUnifiedAsync(message, characterName, imageDataUrls);
        }

        /// <summary>
        /// CocoroCoreへメッセージを送信（複数画像対応）
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        /// <param name="characterName">キャラクター名（オプション）</param>
        /// <param name="imageDataUrls">画像データURLリスト（オプション）</param>
        public async Task SendChatToCoreUnifiedAsync(string message, string? characterName = null, List<string>? imageDataUrls = null)
        {
            try
            {
                // 現在のキャラクター設定を取得
                var currentCharacter = GetStoredCharacterSetting();

                // LLMが無効の場合は処理しない
                if (currentCharacter == null || !currentCharacter.isUseLLM)
                {
                    Debug.WriteLine("チャット送信: LLMが無効のためスキップ");
                    return;
                }

                // セッションIDを生成または既存のものを使用
                if (string.IsNullOrEmpty(_currentSessionId))
                {
                    _currentSessionId = $"dock_{DateTime.Now:yyyyMMddHHmmssfff}";
                }

                // WebSocket接続確認
                if (!_webSocketClient.IsConnected)
                {
                    Debug.WriteLine("[WebSocket] 接続を開始します...");
                    var connected = await _webSocketClient.ConnectAsync();
                    if (!connected)
                    {
                        throw new Exception("WebSocket接続に失敗しました");
                    }
                }

                // 画像データを変換
                List<ImageData>? images = null;
                if (imageDataUrls != null && imageDataUrls.Count > 0)
                {
                    images = imageDataUrls.Select(url => new ImageData { data = url }).ToList();
                }

                // チャットタイプを決定
                var chatType = (images != null && images.Count > 0) ? "text_image" : "text";

                // WebSocketチャットリクエストを作成
                var request = new WebSocketChatRequest
                {
                    query = message,
                    chat_type = chatType,
                    images = images,
                    internet_search = false // WebSocketバージョンでは無効化
                };

                // 画像がある場合は画像処理中、そうでなければメッセージ処理中に設定
                var processingStatus = (images != null && images.Count > 0)
                    ? CocoroCoreMStatus.ProcessingImage
                    : CocoroCoreMStatus.ProcessingMessage;
                _statusPollingService.SetProcessingStatus(processingStatus);

                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "WebSocketチャット開始"));

                Debug.WriteLine($"[WebSocket] チャット送信: session_id={_currentSessionId}, query={message.Substring(0, Math.Min(50, message.Length))}...");

                // WebSocketでチャットを送信
                var success = await _webSocketClient.SendChatAsync(_currentSessionId, request);
                if (!success)
                {
                    throw new Exception("WebSocketメッセージ送信に失敗しました");
                }

                Debug.WriteLine("[WebSocket] チャット送信完了、レスポンス待機中...");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebSocketチャット送信エラー: {ex.Message}");
                // エラー時は正常状態に戻す
                _statusPollingService.SetNormalStatus();
                // ステータスバーにエラー表示
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"WebSocket通信エラー: {ex.Message}"));
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
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, $"アニメーション '{animationName}' 実行"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アニメーションコマンド送信エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"アニメーション制御エラー: {ex.Message}"));
            }
        }

        /// <summary>
        /// 通知メッセージを処理（Notification API用）
        /// </summary>
        /// <param name="notification">通知メッセージ</param>
        /// <param name="imageDataUrls">画像データURL配列（オプション）</param>
        public async Task ProcessNotificationAsync(ChatMessagePayload notification, string[]? imageDataUrls = null)
        {
            try
            {
                // 現在のキャラクター設定を取得
                var currentCharacter = GetStoredCharacterSetting();

                // LLMが有効でない場合は処理しない
                if (currentCharacter == null || !currentCharacter.isUseLLM)
                {
                    Debug.WriteLine("通知処理: LLMが無効のためスキップ");
                    return;
                }

                // 画像データを変換
                List<ImageData>? images = null;
                if (imageDataUrls != null && imageDataUrls.Length > 0)
                {
                    images = imageDataUrls
                        .Where(url => !string.IsNullOrEmpty(url))
                        .Select(url => new ImageData { data = url })
                        .ToList();
                }
                await SendNotificationToCoreAsync(notification.from, notification.message, images);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"通知処理エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"通知処理エラー: {ex.Message}"));
            }
        }

        /// <summary>
        /// CocoroCoreMに通知メッセージを送信
        /// </summary>
        /// <param name="originalSource">通知送信元</param>
        /// <param name="originalMessage">元の通知メッセージ</param>
        /// <param name="images">画像データリスト（オプション）</param>
        public async Task SendNotificationToCoreAsync(string originalSource, string originalMessage, List<ImageData>? images = null)
        {
            try
            {
                // セッションIDを生成または既存のものを使用
                if (string.IsNullOrEmpty(_currentSessionId))
                {
                    _currentSessionId = $"dock_{DateTime.Now:yyyyMMddHHmmssfff}";
                }

                // WebSocket接続確認
                if (!_webSocketClient.IsConnected)
                {
                    Debug.WriteLine("[WebSocket] 通知送信のため接続を開始します...");
                    var connected = await _webSocketClient.ConnectAsync();
                    if (!connected)
                    {
                        throw new Exception("WebSocket接続に失敗しました");
                    }
                }

                // 通知専用のWebSocketチャットリクエストを作成
                var request = new WebSocketChatRequest
                {
                    query = originalMessage, // 元のメッセージをそのまま送信
                    chat_type = "notification", // 通知タイプ
                    images = images,
                    notification = new NotificationData
                    {
                        original_source = originalSource,
                        original_message = originalMessage
                    },
                    internet_search = false // 通知では無効化
                };

                // 処理中ステータスを設定
                _statusPollingService.SetProcessingStatus(CocoroCoreMStatus.ProcessingMessage);
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "通知処理開始"));

                // WebSocketメッセージを送信
                await _webSocketClient.SendChatAsync(_currentSessionId, request);

                Debug.WriteLine($"[WebSocket] 通知送信完了: source={originalSource}, message={originalMessage}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"通知送信エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"通知送信エラー: {ex.Message}"));
                throw;
            }
        }

        /// <summary>
        /// 保存済みの現在のキャラクター設定を取得（キャッシュ使用）
        /// </summary>
        private CharacterSettings? GetStoredCharacterSetting()
        {
            // キャッシュされた設定を使用
            var config = _cachedConfigSettings;
            if (config?.characterList != null &&
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
        /// <param name="imageSources">画像データリスト（オプション）</param>
        public void RaiseNotificationMessageReceived(ChatMessagePayload notification, List<System.Windows.Media.Imaging.BitmapSource>? imageSources = null)
        {
            NotificationMessageReceived?.Invoke(notification, imageSources);
        }


        /// <summary>
        /// CocoroShellにTTS状態を送信
        /// </summary>
        /// <param name="isUseTTS">TTS使用状態</param>
        public async Task SendTTSStateToShellAsync(bool isUseTTS)
        {
            try
            {
                var request = new ShellControlRequest
                {
                    command = "ttsControl",
                    @params = new Dictionary<string, object>
                    {
                        { "enabled", isUseTTS }
                    }
                };

                await _shellClient.SendControlCommandAsync(request);

                // 成功時のステータス更新
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, isUseTTS ? "音声合成有効" : "音声合成無効"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TTS状態送信エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"TTS設定通知エラー"));
            }
        }


        /// <summary>
        /// ログビューアーウィンドウを開く
        /// </summary>
        public void OpenLogViewer()
        {
            // MainWindowのOpenLogViewerメソッドに委譲
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.OpenLogViewer();
            }
        }


        /// <summary>
        /// CocoroShellから現在のキャラクター位置を取得
        /// </summary>
        public async Task<PositionResponse> GetShellPositionAsync()
        {
            try
            {
                return await _shellClient.GetPositionAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"位置取得エラー: {ex.Message}");

                // エラー時のステータス更新
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"位置取得に失敗しました: {ex.Message}"));

                throw;
            }
        }

        /// <summary>
        /// CocoroShellに設定の部分更新を送信
        /// </summary>
        /// <param name="updates">更新する設定のキーと値のペア</param>
        public async Task SendConfigPatchToShellAsync(Dictionary<string, object> updates)
        {
            try
            {
                var changedFields = new string[updates.Count];
                updates.Keys.CopyTo(changedFields, 0);

                var patch = new ConfigPatchRequest
                {
                    updates = updates,
                    changedFields = changedFields
                };

                await _shellClient.UpdateConfigPatchAsync(patch);
                Debug.WriteLine($"設定部分更新をCocoroShellに送信しました: {string.Join(", ", changedFields)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroShell設定部分更新エラー: {ex.Message}");
                throw new InvalidOperationException($"Failed to send config patch to shell: {ex.Message}", ex);
            }
        }

        #region WebSocket イベントハンドラー

        /// <summary>
        /// WebSocketメッセージ受信ハンドラー
        /// </summary>
        private void OnWebSocketMessageReceived(object? sender, WebSocketResponseMessage message)
        {
            try
            {
                switch (message.type)
                {
                    case "status":
                        HandleWebSocketStatusMessage(message);
                        break;

                    case "text":
                        HandleWebSocketTextMessage(message);
                        break;

                    case "reference":
                        HandleWebSocketReferenceMessage(message);
                        break;

                    case "time":
                        HandleWebSocketTimeMessage(message);
                        break;

                    case "end":
                        HandleWebSocketEndMessage(message);
                        break;

                    case "error":
                        HandleWebSocketErrorMessage(message);
                        break;

                    default:
                        Debug.WriteLine($"[WebSocket] 未知のメッセージタイプ: {message.type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] メッセージ処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// WebSocket接続状態変更ハンドラー
        /// </summary>
        private void OnWebSocketConnectionStateChanged(object? sender, bool isConnected)
        {
            Debug.WriteLine($"[WebSocket] 接続状態変更: {(isConnected ? "接続" : "切断")}");
            StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(isConnected,
                isConnected ? "WebSocket接続確立" : "WebSocket接続切断"));
        }

        /// <summary>
        /// WebSocketエラーハンドラー
        /// </summary>
        private void OnWebSocketErrorOccurred(object? sender, string errorMessage)
        {
            Debug.WriteLine($"[WebSocket] エラー: {errorMessage}");
            ErrorOccurred?.Invoke(this, errorMessage);
        }

        /// <summary>
        /// WebSocketステータスメッセージ処理
        /// </summary>
        private void HandleWebSocketStatusMessage(WebSocketResponseMessage message)
        {
            // ステータス情報の処理（必要に応じて実装）
            Debug.WriteLine($"[WebSocket] ステータス: {message.data}");
        }

        /// <summary>
        /// WebSocketテキストメッセージ処理
        /// </summary>
        private void HandleWebSocketTextMessage(WebSocketResponseMessage message)
        {
            if (message.data is System.Text.Json.JsonElement jsonElement)
            {
                if (jsonElement.TryGetProperty("content", out var contentElement))
                {
                    var content = contentElement.GetString() ?? "";
                    var sessionId = message.session_id;

                    var currentCharacter = GetStoredCharacterSetting();
                    var memoryId = !string.IsNullOrEmpty(currentCharacter?.memoryId) ? currentCharacter.memoryId : "memory";

                    var chatRequest = new ChatRequest
                    {
                        memoryId = memoryId,
                        sessionId = sessionId,
                        message = content,
                        role = "assistant",
                        content = content
                    };

                    // サーバー側でチャンク処理されたメッセージをそのまま表示
                    ChatMessageReceived?.Invoke(this, chatRequest);

                    Debug.WriteLine($"[WebSocket DIRECT] Length: {content.Length}, Content: {content.Substring(0, Math.Min(50, content.Length))}...");

                    // CocoroShellにメッセージを転送（ノンブロッキング）
                    ForwardMessageToShellAsync(content, currentCharacter);
                }
            }
        }

        /// <summary>
        /// WebSocket参照メッセージ処理
        /// </summary>
        private void HandleWebSocketReferenceMessage(WebSocketResponseMessage message)
        {
            // 参照情報の処理（必要に応じて実装）
            Debug.WriteLine($"[WebSocket] 参照情報受信");
        }

        /// <summary>
        /// WebSocket時間メッセージ処理
        /// </summary>
        private void HandleWebSocketTimeMessage(WebSocketResponseMessage message)
        {
            // 時間情報の処理（必要に応じて実装）
            Debug.WriteLine($"[WebSocket] 処理時間情報受信");
        }

        /// <summary>
        /// WebSocket完了メッセージ処理
        /// </summary>
        private void HandleWebSocketEndMessage(WebSocketResponseMessage message)
        {
            var sessionId = message.session_id;
            Debug.WriteLine($"[WebSocket] ストリーミング完了: session_id={sessionId}");

            // 完了イベントを発火
            var finishedEvent = new StreamingChatEventArgs
            {
                Content = "",
                IsFinished = true,
                IsError = false
            };

            StreamingChatReceived?.Invoke(this, finishedEvent);

            // 正常状態に戻す
            _statusPollingService.SetNormalStatus();
            StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "WebSocketチャット完了"));
        }

        /// <summary>
        /// WebSocketエラーメッセージ処理
        /// </summary>
        private void HandleWebSocketErrorMessage(WebSocketResponseMessage message)
        {
            string errorMessage = "WebSocketエラーが発生しました";

            if (message.data is System.Text.Json.JsonElement jsonElement)
            {
                if (jsonElement.TryGetProperty("message", out var messageElement))
                {
                    errorMessage = messageElement.GetString() ?? errorMessage;
                }
            }

            Debug.WriteLine($"[WebSocket] エラーメッセージ: {errorMessage}");

            // エラーイベントを発火
            var errorEvent = new StreamingChatEventArgs
            {
                Content = "",
                IsFinished = true,
                IsError = true,
                ErrorMessage = errorMessage
            };

            StreamingChatReceived?.Invoke(this, errorEvent);

            // 正常状態に戻す
            _statusPollingService.SetNormalStatus();
            StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"チャットエラー: {errorMessage}"));
        }

        /// <summary>
        /// CocoroShellにメッセージを転送（ノンブロッキング）
        /// </summary>
        /// <param name="content">転送するメッセージ内容</param>
        /// <param name="currentCharacter">現在のキャラクター設定</param>
        private async void ForwardMessageToShellAsync(string content, CharacterSettings? currentCharacter)
        {
            try
            {
                if (string.IsNullOrEmpty(content))
                {
                    return;
                }

                Debug.WriteLine($"[Shell Forward] CocoroShellにメッセージを転送中: {content.Substring(0, Math.Min(50, content.Length))}...");

                // ShellChatRequestを作成
                var shellRequest = new ShellChatRequest
                {
                    content = content,
                    animation = "talk", // 話すアニメーション
                    characterName = currentCharacter?.modelName
                };

                // CocoroShellにメッセージを送信（既に非同期なのでそのまま呼び出し）
                await _shellClient.SendChatMessageAsync(shellRequest);

                Debug.WriteLine($"[Shell Forward] 転送完了: Length={content.Length}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Shell Forward] CocoroShellへの転送エラー: {ex.Message}");
                // エラーログは出力するが、メインの処理は継続させる
            }
        }

        #endregion

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            // イベント購読解除
            AppSettings.SettingsSaved -= OnSettingsSaved;

            _statusPollingService?.Dispose();
            _notificationApiServer?.Dispose();
            _apiServer?.Dispose();
            _shellClient?.Dispose();
            _coreClient?.Dispose();
            _webSocketClient?.Dispose();
        }
    }
}
