using CocoroDock.Communication;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace CocoroDock.Services
{
    /// <summary>
    /// CocoroAIとの通信を管理するサービスクラス
    /// </summary>
    public class CommunicationService : IDisposable
    {
        private readonly WebSocketServer _webSocketServer;
        private readonly string _userId;
        private string _sessionId;
        private readonly Dictionary<string, string> _clientSessions = new Dictionary<string, string>();

        public event EventHandler<string>? ChatMessageReceived;
        public event EventHandler<ConfigResponsePayload>? ConfigResponseReceived;
        public event EventHandler<StatusMessagePayload>? StatusUpdateReceived;
        public event EventHandler<SystemMessagePayload>? SystemMessageReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;

        public bool IsConnected => _webSocketServer.IsRunning;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="host">サーバーホスト（例: 127.0.0.1）</param>
        /// <param name="port">サーバーポート（例: 55600）</param>
        /// <param name="userId">ユーザーID</param>
        public CommunicationService(string host, int port, string userId)
        {
            _webSocketServer = new WebSocketServer(host, port);
            _userId = userId;
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
        public async Task ConnectAsync()
        {
            await _webSocketServer.StartAsync();
        }

        /// <summary>
        /// サーバーを停止
        /// </summary>
        public async Task DisconnectAsync()
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
                UserId = _userId,
                SessionId = _sessionId,
                Message = message
            };

            await _webSocketServer.SendMessageAsync(MessageType.Chat, payload);
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
                SettingKey = settingKey,
                Value = value
            };

            await _webSocketServer.SendMessageAsync(MessageType.Config, payload);
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
                Command = command,
                Reason = reason
            };

            await _webSocketServer.SendMessageAsync(MessageType.Control, payload);
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

                if (message.TryGetProperty("Type", out var typeElement) &&
                    message.TryGetProperty("Payload", out var payloadElement))
                {
                    string type = typeElement.GetString() ?? string.Empty;

                    switch (type)
                    {
                        case "Chat":
                            var chatResponse = JsonSerializer.Deserialize<ChatResponsePayload>(
                                payloadElement.GetRawText());

                            if (chatResponse != null)
                            {
                                ChatMessageReceived?.Invoke(this, chatResponse.Response);
                            }
                            break;

                        case "Config":
                            var configResponse = JsonSerializer.Deserialize<ConfigResponsePayload>(
                                payloadElement.GetRawText());

                            if (configResponse != null)
                            {
                                ConfigResponseReceived?.Invoke(this, configResponse);
                            }
                            break;

                        case "Status":
                            var statusUpdate = JsonSerializer.Deserialize<StatusMessagePayload>(
                                payloadElement.GetRawText());

                            if (statusUpdate != null)
                            {
                                StatusUpdateReceived?.Invoke(this, statusUpdate);
                            }
                            break;

                        case "System":
                            var systemMessage = JsonSerializer.Deserialize<SystemMessagePayload>(
                                payloadElement.GetRawText());

                            if (systemMessage != null)
                            {
                                SystemMessageReceived?.Invoke(this, systemMessage);
                            }
                            break;

                        default:
                            // 未知のメッセージタイプ
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"メッセージ処理エラー: {ex.Message}");
            }
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