using System;
using System.Diagnostics;
using System.IO;

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
        /// <summary>
        /// 指定した名前のプロセスに対して操作を行います
        /// </summary>
        /// <param name="processName">プロセス名（拡張子なし）</param>
        /// <param name="operation">実行する操作</param>
        /// <returns>プロセスが存在する場合はtrue、存在しない場合はfalse</returns>
        public static bool ProcessUtility(string processName, ProcessOperation operation)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                bool exists = processes.Length > 0;

                // 操作に応じたプロセス処理
                if (operation == ProcessOperation.Terminate || operation == ProcessOperation.RestartIfRunning)
                {
                    foreach (Process process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                // より丁寧な停止方法
                                try
                                {
                                    // taskkillでSIGTERMを送信（/Fなし）
                                    Process taskkillProcess = Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "taskkill",
                                        Arguments = $"/PID {process.Id}",
                                        CreateNoWindow = true,
                                        UseShellExecute = false
                                    });
                                    taskkillProcess?.WaitForExit();

                                    if (!process.WaitForExit(2000))
                                    {
                                        // それでも終了しない場合は強制終了
                                        process.Kill();
                                        process.WaitForExit(2000);
                                    }
                                }
                                catch
                                {
                                    // エラーが発生した場合は従来の方法
                                    process.CloseMainWindow();
                                    process.WaitForExit(5000);
                                    if (!process.HasExited)
                                    {
                                        process.Kill();
                                        process.WaitForExit(2000);
                                    }
                                }
                                Debug.WriteLine($"{processName} プロセスを終了しました。");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"{processName} プロセス終了エラー: {ex.Message}");
                            // プロセス終了のエラーはログに記録するだけで続行
                        }
                    }
                }

                return exists;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{processName} プロセス操作エラー: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 外部アプリケーションを起動します
        /// </summary>
        /// <param name="appName">アプリケーション名</param>
        /// <param name="exePath">実行ファイルのパス（絶対パスまたは相対パス）</param>
        /// <param name="relativeDir">相対ディレクトリ（nullの場合は直接exePathを使用）</param>
        /// <param name="operation">プロセス操作の種類（終了のみか再起動か）</param>
        public static void LaunchExternalApplication(string appName, string exePath, string? relativeDir = null, ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
            try
            {
                // 実行ファイルパスの構築
                string fullPath;
                if (relativeDir != null)
                {
                    fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeDir, exePath);
                }
                else
                {
                    fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exePath);
                }

                // ファイルの存在確認
                if (!File.Exists(fullPath))
                {
                    UIHelper.ShowError("起動エラー", $"{appName}が見つかりません。パス: {fullPath}");
                    return;
                }

                // 同名の実行中プロセスをチェックして終了または再起動
                string processName = Path.GetFileNameWithoutExtension(exePath);
                bool wasRunning = ProcessUtility(processName, operation);

                // 終了のみの場合は起動しない
                if (operation == ProcessOperation.Terminate)
                {
                    return;
                }

                // プロセス起動のためのパラメータを設定
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true
                };

                // CocoroMemoryの場合はウィンドウを非表示にする
                if (processName.Equals("CocoroMemory", StringComparison.OrdinalIgnoreCase))
                {
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    startInfo.CreateNoWindow = true;
                }

                // プロセスを起動
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                UIHelper.ShowError($"{appName}起動エラー", ex.Message);
            }
        }
    }
}