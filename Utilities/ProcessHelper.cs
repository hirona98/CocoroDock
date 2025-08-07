using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CocoroDock.Services;

namespace CocoroDock.Utilities
{
    /// <summary>
    /// プロセス操作の種類を定義する列挙型
    /// </summary>
    public enum ProcessOperation
    {
        /// <summary>既存のプロセスを終了して新しいプロセスを起動</summary>
        RestartIfRunning,
        /// <summary>プロセスを強制終了</summary>
        Terminate,
        /// <summary>プロセスの存在チェックのみ</summary>
        CheckOnly
    }

    /// <summary>
    /// プロセス管理に関するユーティリティメソッドを提供するヘルパークラス
    /// </summary>
    public static class ProcessHelper
    {
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        /// <summary>
        /// 指定した名前のプロセスに対して操作を行います
        /// </summary>
        /// <param name="processName">プロセス名（拡張子なし）</param>
        /// <param name="operation">実行する操作</param>
        /// <returns>プロセスが存在する場合はtrue、存在しない場合はfalse</returns>
        public static bool ExitProcess(string processName, ProcessOperation operation)
        {
            // まずはREST APIによる通常終了を試みる
            // Task.Run を使用してデッドロックを回避
            bool gracefullyTerminated = Task.Run(async () => await TryGracefulTerminationAsync(processName)).GetAwaiter().GetResult();
            if (gracefullyTerminated)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// REST APIを使用してプロセスに協調的終了を要求します
        /// </summary>
        /// <param name="processName">プロセス名</param>
        /// <returns>終了シグナルの送信に成功した場合はtrue</returns>
        private static async Task<bool> TryGracefulTerminationAsync(string processName)
        {
            try
            {
                // 設定から各プロセスのポート番号を取得
                var settings = AppSettings.Instance;
                int? port = processName.ToLower() switch
                {
                    "cocorocore2" => settings.CocoroCorePort,
                    "cocoroshell" => settings.CocoroShellPort,
                    _ => null
                };

                if (port == null)
                {
                    Debug.WriteLine($"{processName} はREST API経由の終了をサポートしていません。");
                    return false;
                }

                // shutdownコマンドをJSONで作成
                var json = "{\"command\":\"shutdown\",\"params\":{}}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // /api/control エンドポイントにPOSTリクエストを送信
                // ConfigureAwait(false)を使用してデッドロックを回避
                var response = await httpClient.PostAsync($"http://127.0.0.1:{port}/api/control", content).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"{processName} にREST API経由で終了シグナルを送信しました。");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"{processName} への終了シグナル送信に失敗しました。ステータス: {response.StatusCode}");
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"{processName} への接続に失敗しました: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"{processName} への終了シグナル送信がタイムアウトしました。");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"REST API経由の協調的終了中にエラーが発生しました: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 外部アプリケーションを起動します
        /// </summary>
        /// <param name="exeName">アプリケーションexe名</param>
        /// <param name="relativeDir">相対ディレクトリ</param>
        /// <param name="operation">プロセス操作の種類（終了のみか再起動か）</param>
        /// <param name="createWindow">ウィンドウを作成するかどうか（デフォルトはfalse：コンソールを非表示）</param>
        public static void LaunchExternalApplication(string exeName, string? relativeDir = null, ProcessOperation operation = ProcessOperation.RestartIfRunning, bool createWindow = false)
        {
            try
            {
                // 実行ファイルパスの構築
                string fullPath;
                if (relativeDir != null)
                {
                    fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeDir, exeName);
                }
                else
                {
                    fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
                }

                // 同名の実行中プロセスをチェックして終了または再起動
                string processName = Path.GetFileNameWithoutExtension(exeName);
                bool wasRunning = ExitProcess(processName, operation);

                // 終了のみの場合は起動しない
                if (operation == ProcessOperation.Terminate)
                {
                    return;
                }

                // ファイルの存在確認
                if (!File.Exists(fullPath))
                {
                    UIHelper.ShowError("起動エラー", $"{exeName}が見つかないため正常動作しません。パス: {fullPath}");
                    return;
                }

                // プロセス起動のためのパラメータを設定
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = false, // 環境により起動しなくなる問題の対策（この操作はユーザーによって取り消されました）
                    WorkingDirectory = Path.GetDirectoryName(fullPath), // 環境により起動しなくなる問題の対策（この操作はユーザーによって取り消されました）
                    CreateNoWindow = !createWindow, // ウィンドウの作成制御
                    WindowStyle = createWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden // ウィンドウスタイルの設定
                };


                // プロセスを起動
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                UIHelper.ShowError($"{exeName}起動エラー", ex.Message);
            }
        }

        /// <summary>
        /// 指定したプロセスの起動完了をHTTPヘルスチェックで確認します
        /// </summary>
        /// <param name="processName">プロセス名</param>
        /// <param name="timeout">タイムアウト（秒）</param>
        /// <returns>起動完了した場合はtrue</returns>
        public static async Task<bool> WaitForProcessStartupAsync(string processName, int timeout = 30)
        {
            try
            {
                var settings = AppSettings.Instance;
                int? port = processName.ToLower() switch
                {
                    "cocorocore2" => settings.CocoroCorePort,
                    "cocoroshell" => settings.CocoroShellPort,
                    _ => null
                };

                if (port == null)
                {
                    Debug.WriteLine($"{processName} はヘルスチェックをサポートしていません。");
                    return false;
                }

                var healthEndpoint = $"http://127.0.0.1:{port}/health";
                var startTime = DateTime.Now;

                while ((DateTime.Now - startTime).TotalSeconds < timeout)
                {
                    try
                    {
                        var response = await httpClient.GetAsync(healthEndpoint);
                        if (response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine($"{processName} の起動が完了しました。");
                            return true;
                        }
                    }
                    catch (HttpRequestException)
                    {
                        // 接続エラーは予想される（まだ起動していない）
                    }
                    catch (TaskCanceledException)
                    {
                        // タイムアウトも予想される
                    }

                    // 500ms待機してから再試行
                    await Task.Delay(500);
                }

                Debug.WriteLine($"{processName} の起動タイムアウトしました（{timeout}秒）。");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{processName} の起動監視中にエラーが発生しました: {ex.Message}");
                return false;
            }
        }
    }
}