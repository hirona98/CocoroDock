using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace CocoroDock.Services
{
    /// <summary>
    /// CocoroCore2のステータス状態を表す列挙型
    /// </summary>
    public enum CocoroCore2Status
    {
        /// <summary>CocoroCore2接続待機中</summary>
        Disconnected,
        /// <summary>正常動作中（CocoroCore2とのポーリングが正常なとき）</summary>
        Normal,
        /// <summary>LLMメッセージ処理中</summary>
        ProcessingMessage,
        /// <summary>LLM画像処理中</summary>
        ProcessingImage
    }


    /// <summary>
    /// CocoroCore2のステータスポーリングサービス
    /// </summary>
    public class StatusPollingService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _healthEndpoint;
        private readonly Timer _pollingTimer;
        private CocoroCore2Status _currentStatus = CocoroCore2Status.Disconnected;
        private volatile bool _disposed = false;

        /// <summary>
        /// ステータス変更時のイベント
        /// </summary>
        public event EventHandler<CocoroCore2Status>? StatusChanged;

        /// <summary>
        /// 現在のステータス
        /// </summary>
        public CocoroCore2Status CurrentStatus => _currentStatus;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="baseUrl">CocoroCore2のベースURL（デフォルト: http://localhost:55601）</param>
        public StatusPollingService(string baseUrl = "http://localhost:55601")
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(800) // ポーリング間隔より短いタイムアウト
            };
            _healthEndpoint = $"{baseUrl.TrimEnd('/')}/api/health";

            // 1秒間隔でポーリングを開始（シンプルなブロッキング実装）
            _pollingTimer = new Timer(_ => PollHealthStatus(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

            Debug.WriteLine($"[StatusPollingService] ポーリング開始: {_healthEndpoint}");
        }

        /// <summary>
        /// ヘルスチェックを実行してステータスを更新（同期・ブロッキング）
        /// </summary>
        private void PollHealthStatus()
        {
            if (_disposed) return;

            try
            {
                var response = _httpClient.GetAsync(_healthEndpoint).Result;
                if (response.IsSuccessStatusCode)
                {
                    var content = response.Content.ReadAsStringAsync().Result;
                    var healthCheck = JsonSerializer.Deserialize<Communication.HealthCheckResponse>(content);

                    if (healthCheck != null && healthCheck.status == "healthy")
                    {
                        // 接続成功時は現在の処理状態を維持（Disconnected以外）
                        if (_currentStatus == CocoroCore2Status.Disconnected)
                        {
                            UpdateStatus(CocoroCore2Status.Normal);
                        }
                    }
                    else
                    {
                        UpdateStatus(CocoroCore2Status.Disconnected);
                    }
                }
                else
                {
                    UpdateStatus(CocoroCore2Status.Disconnected);
                }
            }
            catch (Exception)
            {
                // 接続エラー時はDisconnected状態に
                UpdateStatus(CocoroCore2Status.Disconnected);
            }
        }

        /// <summary>
        /// ステータスを更新してイベントを発火
        /// </summary>
        /// <param name="newStatus">新しいステータス</param>
        private void UpdateStatus(CocoroCore2Status newStatus)
        {
            if (_currentStatus != newStatus)
            {
                _currentStatus = newStatus;
                StatusChanged?.Invoke(this, newStatus);
                Debug.WriteLine($"[StatusPollingService] ステータス変更: {newStatus}");
            }
        }

        /// <summary>
        /// 処理状態を手動で設定（通信開始時に呼び出し）
        /// </summary>
        /// <param name="processingStatus">処理状態</param>
        public void SetProcessingStatus(CocoroCore2Status processingStatus)
        {
            if (processingStatus == CocoroCore2Status.ProcessingMessage ||
                processingStatus == CocoroCore2Status.ProcessingImage)
            {
                UpdateStatus(processingStatus);
            }
        }

        /// <summary>
        /// 処理完了時に正常状態に戻す
        /// </summary>
        public void SetNormalStatus()
        {
            UpdateStatus(CocoroCore2Status.Normal);
        }

        /// <summary>
        /// リソースを解放
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            _pollingTimer?.Dispose();
            _httpClient?.Dispose();

            Debug.WriteLine("[StatusPollingService] ポーリング停止");
        }
    }
}