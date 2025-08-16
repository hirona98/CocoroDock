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
        // リアルタイムストリーミング設定
        private const int MIN_PARTIAL_RESPONSE_LENGTH = 20; // 部分送信の最小文字数

        private readonly CocoroDockApiServer _apiServer;
        private readonly CocoroShellClient _shellClient;
        private readonly CocoroCoreClient _coreClient;
        private readonly WebSocketChatClient _webSocketClient;
        private readonly IAppSettings _appSettings;
        private readonly NotificationApiServer? _notificationApiServer;
        private readonly StatusPollingService _statusPollingService;

        // セッション管理用
        private string? _currentSessionId;

        // WebSocket部分レスポンス管理用
        private readonly Dictionary<string, System.Text.StringBuilder> _partialResponses = new Dictionary<string, System.Text.StringBuilder>();
        private readonly Dictionary<string, System.Text.StringBuilder> _fullResponses = new Dictionary<string, System.Text.StringBuilder>();
        private readonly Dictionary<string, bool> _isFirstPartialMessage = new Dictionary<string, bool>();

        // ログビューアー管理用
        private LogViewerWindow? _logViewerWindow;

        // 設定キャッシュ用
        private ConfigSettings? _cachedConfigSettings;
        private readonly Dictionary<string, string> _cachedSystemPrompts = new Dictionary<string, string>();

        public event EventHandler<ChatRequest>? ChatMessageReceived;
        public event EventHandler<StreamingChatEventArgs>? StreamingChatReceived;
        public event Action<ChatMessagePayload, List<System.Windows.Media.Imaging.BitmapSource>?>? NotificationMessageReceived;
        public event EventHandler<ControlRequest>? ControlCommandReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<StatusUpdateEventArgs>? StatusUpdateRequested;
        public event EventHandler<CocoroCore2Status>? StatusChanged;

        public bool IsServerRunning => _apiServer.IsRunning;

        /// <summary>
        /// 現在のCocoroCore2ステータス
        /// </summary>
        public CocoroCore2Status CurrentStatus => _statusPollingService.CurrentStatus;

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
            _apiServer.LogMessageReceived += (sender, logMessage) =>
            {
                // ログビューアーが開いている場合のみログを転送
                _logViewerWindow?.AddLogMessage(logMessage);
            };

            // CocoroShellクライアントの初期化
            _shellClient = new CocoroShellClient(_appSettings.CocoroShellPort);

            // CocoroCoreクライアントの初期化
            _coreClient = new CocoroCoreClient(_appSettings.CocoroCorePort);

            // WebSocketクライアントの初期化
            _webSocketClient = new WebSocketChatClient(_appSettings.CocoroCorePort, $"dock_{Environment.MachineName}_{DateTime.Now:yyyyMMddHHmmss}");
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
            _statusPollingService = new StatusPollingService($"http://localhost:{_appSettings.CocoroCorePort}");
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
        /// CocoroCore2にWebSocketチャットメッセージを送信
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        /// <param name="characterName">キャラクター名（オプション）</param>
        /// <param name="imageDataUrl">画像データURL（オプション）</param>
        public async Task SendChatToCoreUnifiedAsync(string message, string? characterName = null, string? imageDataUrl = null)
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
                if (!string.IsNullOrEmpty(imageDataUrl))
                {
                    images = new List<ImageData>
                    {
                        new ImageData { data = imageDataUrl }
                    };
                }

                // チャットタイプを決定
                var chatType = !string.IsNullOrEmpty(imageDataUrl) ? "text_image" : "text";

                // WebSocketチャットリクエストを作成
                var request = new WebSocketChatRequest
                {
                    query = message,
                    chat_type = chatType,
                    images = images,
                    internet_search = false // WebSocketバージョンでは無効化
                };

                // 画像がある場合は画像処理中、そうでなければメッセージ処理中に設定
                var processingStatus = !string.IsNullOrEmpty(imageDataUrl)
                    ? CocoroCore2Status.ProcessingImage
                    : CocoroCore2Status.ProcessingMessage;
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
        /// 通知メッセージを処理（Notification API用）- WebSocket版
        /// </summary>
        /// <param name="notification">通知メッセージ</param>
        /// <param name="imageDataUrls">画像データURL配列（オプション）</param>
        public async Task ProcessNotificationAsync(ChatMessagePayload notification, string[]? imageDataUrls = null)
        {
            try
            {
                // 現在のキャラクター設定を取得
                var currentCharacter = GetCurrentCharacterSettings();

                // LLMが有効でない場合は処理しない
                if (currentCharacter == null || !currentCharacter.isUseLLM)
                {
                    Debug.WriteLine("通知処理: LLMが無効のためスキップ");
                    return;
                }

                // 通知メッセージを構築
                var notificationMessage = $"【{notification.from}からの通知】{notification.message}";

                // 画像がある場合は最初の画像のみ使用（WebSocket統一API）
                string? imageDataUrl = null;
                if (imageDataUrls != null && imageDataUrls.Length > 0)
                {
                    imageDataUrl = imageDataUrls.FirstOrDefault(url => !string.IsNullOrEmpty(url));
                }

                // WebSocket統一APIを使用して通知処理
                await SendChatToCoreUnifiedAsync(notificationMessage, null, imageDataUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"通知処理エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"通知処理エラー: {ex.Message}"));
            }
        }

        /// <summary>
        /// 現在のキャラクター設定を取得（キャッシュ使用）
        /// </summary>
        private CharacterSettings? GetCurrentCharacterSettings()
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
        /// 部分レスポンスを送信すべきかどうかを判定（20文字以上 + 句読点・文章切れ目）
        /// </summary>
        /// <param name="text">判定対象のテキスト</param>
        /// <returns>送信すべき場合はtrue</returns>
        private bool ShouldSendPartialResponse(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < MIN_PARTIAL_RESPONSE_LENGTH)
            {
                return false;
            }

            // 句読点・文章切れ目の文字一覧
            var punctuationMarks = new char[] { '。', '、', '！', '？', '\n', '\r', '…', '～', '：', '；', '.', ',', '!', '?', ':', ';' };

            // 末尾から句読点・切れ目を検索（最新の追加分をチェック）
            for (int i = text.Length - 1; i >= Math.Max(0, text.Length - 10); i--)
            {
                if (punctuationMarks.Contains(text[i]))
                {
                    // 句読点・切れ目が見つかった場合、最小文字数以上かつその位置が適切かチェック
                    if (i >= MIN_PARTIAL_RESPONSE_LENGTH - 1) // 0ベースなので-1
                    {
                        Debug.WriteLine($"[PARTIAL CHECK] Found punctuation '{text[i]}' at position {i}, text length: {text.Length}");
                        return true;
                    }
                }
            }

            return false;
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
            // 既にウィンドウが開いている場合は前面に表示
            if (_logViewerWindow != null)
            {
                _logViewerWindow.Activate();
                _logViewerWindow.WindowState = System.Windows.WindowState.Normal;
                return;
            }

            // 新しいログビューアーウィンドウを作成
            _logViewerWindow = new LogViewerWindow();

            // ウィンドウクローズ時のイベントハンドラー
            _logViewerWindow.LogForwardingStopped += async (sender, e) =>
            {
                // ログ送信停止を通知
                await SendLogForwardingControlAsync(false);
                _logViewerWindow = null;
            };

            // ログ送信開始を通知（バックグラウンドで実行）
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendLogForwardingControlAsync(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ログ送信開始通知エラー: {ex.Message}");
                }
            });

            // ウィンドウを表示
            _logViewerWindow.Show();
        }

        /// <summary>
        /// ログ送信制御コマンドを送信
        /// </summary>
        /// <param name="enabled">ログ送信を有効にするかどうか</param>
        private async Task SendLogForwardingControlAsync(bool enabled)
        {
            var command = enabled ? "start_log_forwarding" : "stop_log_forwarding";

            // CocoroCoreに送信
            try
            {
                var controlRequest = new CoreControlRequest { action = command };
                await _coreClient.SendControlCommandAsync(controlRequest);
            }
            catch (System.Net.Http.HttpRequestException)
            {
                // CocoroCore未起動の場合は正常（サイレント処理）
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoreCoreへのログ制御コマンド送信エラー: {ex.Message}");
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
                    
                    // セッション初期化
                    if (!_partialResponses.ContainsKey(sessionId))
                    {
                        _partialResponses[sessionId] = new System.Text.StringBuilder();
                        _fullResponses[sessionId] = new System.Text.StringBuilder();
                        _isFirstPartialMessage[sessionId] = true;
                    }
                    
                    // コンテンツを追加
                    _partialResponses[sessionId].Append(content);
                    _fullResponses[sessionId].Append(content);
                    
                    // 20文字以上 + 句読点・文章切れ目での部分送信判定
                    if (ShouldSendPartialResponse(_partialResponses[sessionId].ToString()))
                    {
                        var partialText = _partialResponses[sessionId].ToString();
                        var currentCharacter = GetCurrentCharacterSettings();
                        var memoryId = !string.IsNullOrEmpty(currentCharacter?.memoryId) ? currentCharacter.memoryId : "memory";
                        
                        var partialChatRequest = new ChatRequest
                        {
                            memoryId = memoryId,
                            sessionId = sessionId,
                            message = partialText,
                            role = "assistant",
                            content = partialText
                        };
                        
                        if (_isFirstPartialMessage[sessionId])
                        {
                            // 初回メッセージとして送信
                            ChatMessageReceived?.Invoke(this, partialChatRequest);
                            _isFirstPartialMessage[sessionId] = false;
                        }
                        else
                        {
                            // 追加のメッセージとして送信（UI側で同じメッセージに追記）
                            partialChatRequest.content = "[COCORO_APPEND]" + partialChatRequest.content;
                            ChatMessageReceived?.Invoke(this, partialChatRequest);
                        }
                        
                        // 部分レスポンスをリセット
                        _partialResponses[sessionId].Clear();
                        
                        Debug.WriteLine($"[WebSocket PARTIAL SENT] Length: {partialText.Length}, Content: {partialText.Substring(0, Math.Min(50, partialText.Length))}...");
                    }
                    
                    // StreamingChatEventArgs形式で既存ロジックに統合
                    var streamingEvent = new StreamingChatEventArgs
                    {
                        Content = content,
                        IsFinished = false,
                        IsError = false
                    };
                    
                    StreamingChatReceived?.Invoke(this, streamingEvent);
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
            
            // 最後の部分的レスポンスを送信（残りがある場合）
            if (_partialResponses.ContainsKey(sessionId) && _partialResponses[sessionId].Length > 0)
            {
                var partialText = _partialResponses[sessionId].ToString();
                var currentCharacter = GetCurrentCharacterSettings();
                var memoryId = !string.IsNullOrEmpty(currentCharacter?.memoryId) ? currentCharacter.memoryId : "memory";
                
                var partialChatRequest = new ChatRequest
                {
                    memoryId = memoryId,
                    sessionId = sessionId,
                    message = partialText,
                    role = "assistant",
                    content = partialText
                };
                
                if (_isFirstPartialMessage.ContainsKey(sessionId) && _isFirstPartialMessage[sessionId])
                {
                    // 初回メッセージとして送信
                    ChatMessageReceived?.Invoke(this, partialChatRequest);
                }
                else
                {
                    // 追加のメッセージとして送信（UI側で同じメッセージに追記）
                    partialChatRequest.content = "[COCORO_APPEND]" + partialChatRequest.content;
                    ChatMessageReceived?.Invoke(this, partialChatRequest);
                }
                
                Debug.WriteLine($"[WebSocket FINAL SENT] Length: {partialText.Length}, Content: {partialText.Substring(0, Math.Min(50, partialText.Length))}...");
            }
            
            // セッションデータクリーンアップ
            _partialResponses.Remove(sessionId);
            _fullResponses.Remove(sessionId);
            _isFirstPartialMessage.Remove(sessionId);
            
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

        #endregion

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            // イベント購読解除
            AppSettings.SettingsSaved -= OnSettingsSaved;

            _logViewerWindow?.Close();
            _statusPollingService?.Dispose();
            _notificationApiServer?.Dispose();
            _apiServer?.Dispose();
            _shellClient?.Dispose();
            _coreClient?.Dispose();
            _webSocketClient?.Dispose();
        }
    }
}
