using CocoroDock.Communication;
using CocoroDock.Utilities;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace CocoroDock.Services
{
    /// <summary>
    /// CocoroAIとの通信を管理するサービスクラス
    /// </summary>
    public class CommunicationService : ICommunicationService
    {
        private readonly WebSocketServer _webSocketServer;
        private string _sessionId;

        public event EventHandler<string>? ChatMessageReceived;
        public event EventHandler<ConfigResponsePayload>? ConfigResponseReceived;
        public event EventHandler<StatusMessagePayload>? StatusUpdateReceived;
        public event EventHandler<SystemMessagePayload>? SystemMessageReceived;
        public event EventHandler<ControlMessagePayload>? ControlMessageReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;

        public bool IsServerRunning => _webSocketServer.IsRunning;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="port">サーバーポート（例: 55600）</param>
        public CommunicationService(int port)
        {
            _webSocketServer = new WebSocketServer("127.0.0.1", port);
            _sessionId = GenerateSessionId();

            // WebSocketサーバーのイベントハンドラを設定
            _webSocketServer.MessageReceived += OnMessageReceived;
            _webSocketServer.ConnectionError += (sender, error) => ErrorOccurred?.Invoke(this, error);
            _webSocketServer.ClientConnected += (sender, clientId) => Connected?.Invoke(this, EventArgs.Empty);
            _webSocketServer.ClientDisconnected += (sender, clientId) => Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 新しいセッションIDを生成
        /// </summary>
        private string GenerateSessionId()
        {
            return $"session_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        /// <summary>
        /// サーバーを開始
        /// </summary>
        public async Task StartServerAsync()
        {
            try
            {
                await _webSocketServer.StartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CommunicationService: サーバー起動エラー: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"サーバー起動に失敗しました: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// サーバーを停止
        /// </summary>
        public async Task StopServerAsync()
        {
            await _webSocketServer.StopAsync();
        }

        /// <summary>
        /// 設定情報を要求
        /// </summary>
        public async Task RequestConfigAsync()
        {
            await _webSocketServer.SendRequestConfigAsync();
        }

        /// <summary>
        /// 設定情報を更新
        /// </summary>
        /// <param name="settings">更新する設定情報</param>
        public async Task UpdateConfigAsync(ConfigSettings settings)
        {
            await _webSocketServer.SendUpdateConfigAsync(settings);
        }

        /// <summary>
        /// チャットメッセージを送信
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        public async Task SendChatMessageAsync(string message)
        {
            var payload = new ChatMessagePayload
            {
                sessionId = _sessionId,
                message = message
            };

            await _webSocketServer.SendMessageAsync(MessageType.chat, payload);
        }

        /// <summary>
        /// 設定を変更
        /// </summary>
        /// <param name="settingKey">設定キー</param>
        /// <param name="value">設定値</param>
        public async Task ChangeConfigAsync(string settingKey, string value)
        {
            var payload = new ConfigMessagePayload
            {
                settingKey = settingKey,
                value = value
            };

            await _webSocketServer.SendMessageAsync(MessageType.config, payload);
        }

        /// <summary>
        /// 制御コマンドを送信
        /// </summary>
        /// <param name="command">コマンド名</param>
        /// <param name="reason">理由</param>
        public async Task SendControlCommandAsync(string command, string reason)
        {
            var payload = new ControlMessagePayload
            {
                command = command,
                reason = reason
            };

            await _webSocketServer.SendMessageAsync(MessageType.control, payload);
        }

        /// <summary>
        /// 新しいチャットセッションを開始
        /// </summary>
        public void StartNewSession()
        {
            _sessionId = GenerateSessionId();
        }

        /// <summary>
        /// 受信したWebSocketメッセージを処理
        /// </summary>
        private void OnMessageReceived(object? sender, (string ClientId, string Json) args)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(args.Json);
                var message = document.RootElement;

                if (message.TryGetProperty("type", out var typeElement) &&
                    message.TryGetProperty("payload", out var payloadElement))
                {
                    string type = typeElement.GetString() ?? string.Empty;
                    ProcessMessageByType(type, payloadElement);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"メッセージ処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// メッセージタイプに応じて処理を行う
        /// </summary>
        /// <param name="type">メッセージタイプ</param>
        /// <param name="payloadElement">ペイロード要素</param>
        private void ProcessMessageByType(string type, JsonElement payloadElement)
        {
            try
            {
                string payloadJson = payloadElement.GetRawText();

                switch (type)
                {
                    case "chat":
                    case "notification":
                        ProcessChatMessage(payloadJson);
                        break;

                    case "config":
                        ProcessConfigMessage(payloadJson);
                        break;

                    case "status":
                        ProcessStatusMessage(payloadJson);
                        break;

                    case "system":
                        ProcessSystemMessage(payloadJson);
                        break;

                    case "control":
                        ProcessControlMessage(payloadJson);
                        break;

                    default:
                        Debug.WriteLine($"未知のメッセージタイプ: {type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"メッセージタイプ処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// チャットメッセージを処理
        /// </summary>
        private void ProcessChatMessage(string payloadJson)
        {
            var chatResponse = MessageHelper.DeserializeFromJson<ChatResponsePayload>(payloadJson);
            if (chatResponse != null)
            {
                ChatMessageReceived?.Invoke(this, chatResponse.response);
            }
        }

        /// <summary>
        /// 設定メッセージを処理
        /// </summary>
        private void ProcessConfigMessage(string payloadJson)
        {
            var configResponse = MessageHelper.DeserializeFromJson<ConfigResponsePayload>(payloadJson);
            if (configResponse != null)
            {
                ConfigResponseReceived?.Invoke(this, configResponse);
            }
        }

        /// <summary>
        /// ステータスメッセージを処理
        /// </summary>
        private void ProcessStatusMessage(string payloadJson)
        {
            var statusUpdate = MessageHelper.DeserializeFromJson<StatusMessagePayload>(payloadJson);
            if (statusUpdate != null)
            {
                StatusUpdateReceived?.Invoke(this, statusUpdate);
            }
        }

        /// <summary>
        /// システムメッセージを処理
        /// </summary>
        private void ProcessSystemMessage(string payloadJson)
        {
            var systemMessage = MessageHelper.DeserializeFromJson<SystemMessagePayload>(payloadJson);
            if (systemMessage != null)
            {
                SystemMessageReceived?.Invoke(this, systemMessage);
            }
        }

        /// <summary>
        /// 制御メッセージを処理
        /// </summary>
        private void ProcessControlMessage(string payloadJson)
        {
            var controlMessage = MessageHelper.DeserializeFromJson<ControlMessagePayload>(payloadJson);
            if (controlMessage != null)
            {
                ControlMessageReceived?.Invoke(this, controlMessage);
            }
        }

        /// <summary>
        /// 指定されたタイプとペイロードのメッセージを送信
        /// </summary>
        /// <param name="type">メッセージタイプ</param>
        /// <param name="payload">ペイロードデータ</param>
        public async Task SendMessageAsync(MessageType type, object payload)
        {
            await _webSocketServer.SendMessageAsync(type, payload);
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            _webSocketServer.Dispose();
        }
    }
}