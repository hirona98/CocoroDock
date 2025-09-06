using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using CocoroDock.Communication;
using CocoroDock.Services;
using CocoroDock.Utilities;
using CocoroDock.Windows;

namespace CocoroDock.Controls
{
    public partial class MemorySettingsControl : UserControl
    {
        private CharacterSettings? _currentCharacter;

        public MemorySettingsControl()
        {
            InitializeComponent();
        }

        public void LoadCharacterSettings(CharacterSettings character)
        {
            _currentCharacter = character;
            if (character != null)
            {
                EmbeddedModelTextBox.Text = character.embeddedModel;
                EmbeddedDimensionTextBox.Text = character.embeddedDimension;
                EmbeddedApiKeyPasswordBox.Text = character.embeddedApiKey;
            }
        }

        public void SaveToCharacterSettings(CharacterSettings character)
        {
            if (character != null)
            {
                character.embeddedModel = EmbeddedModelTextBox.Text;
                character.embeddedDimension = EmbeddedDimensionTextBox.Text;
                character.embeddedApiKey = EmbeddedApiKeyPasswordBox.Text;
            }
        }

        private void EmbeddedApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            PasteFromClipboardIntoTextBox(EmbeddedApiKeyPasswordBox);
        }

        private void PasteFromClipboardIntoTextBox(TextBox textBox)
        {
            if (Clipboard.ContainsText())
            {
                textBox.Text = Clipboard.GetText();
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening link: {ex.Message}");
            }
        }

        /// <summary>
        /// UserDataMディレクトリを探索して見つける（AppSettingsのロジックを簡略化して流用）
        /// </summary>
        private string FindUserDataMDirectory()
        {
            var baseDirectory = AppContext.BaseDirectory;

            // 探索するパスの配列
            string[] searchPaths = {
#if !DEBUG
                Path.Combine(baseDirectory, "UserDataM"),
#endif
                Path.Combine(baseDirectory, "..", "UserDataM"),
                Path.Combine(baseDirectory, "..", "..", "UserDataM"),
                Path.Combine(baseDirectory, "..", "..", "..", "UserDataM"),
                Path.Combine(baseDirectory, "..", "..", "..", "..", "UserDataM")
            };

            foreach (var path in searchPaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            // 見つからない場合は、最初のパスを使用
            return Path.GetFullPath(searchPaths[0]);
        }

        private async void DeleteMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            var characterName = _currentCharacter?.modelName ?? "不明";

            // 確認ダイアログを表示
            var result = MessageBox.Show(
                $"全キャラクターのすべての記憶データを削除します。\n" +
                "この操作は元に戻せません。\n\n" +
                "本当に削除しますか？",
                "記憶の削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // 二重確認
            result = MessageBox.Show(
                "本当に削除してもよろしいですか？\n" +
                "すべての記憶とデータベースが失われます。",
                "最終確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // 最低限ダイアログを表示
            var progressDialog = new SimpleProgressDialog
            {
                Owner = Window.GetWindow(this)
            };

            // ボタンを無効化
            DeleteMemoryButton.IsEnabled = false;

            try
            {
                // ダイアログを表示
                progressDialog.Show();
                progressDialog.MessageText.Text = "CocoroCoreMを停止しています...";

                // 削除処理を実行
                await Task.Run(async () =>
                {
                    // CocoroCoreMプロセスを停止
                    ProcessHelper.LaunchExternalApplication("CocoroCoreM.exe", "CocoroCoreM", ProcessOperation.Terminate, false);

                    // プロセスの停止を待つ（ポーリング）
                    int waitCount = 0;
                    while (waitCount < 60) // 最大60秒待機
                    {
                        await Task.Delay(1000);
                        var processes = Process.GetProcessesByName("CocoroCoreM");
                        if (processes.Length == 0)
                        {
                            break;
                        }
                        foreach (var process in processes)
                        {
                            process.Dispose();
                        }
                        waitCount++;
                    }

                    // 少し待つ（ファイルハンドルが解放されるのを確実にするため）
                    await Task.Delay(2000);

                    // UI更新
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "記憶データを削除しています...";
                    });

                    // フォルダを削除（AppSettingsの探索ロジックを簡略化して流用）
                    string userDataPath = FindUserDataMDirectory();
                    string memoryPath = Path.Combine(userDataPath, "Memory");

                    // UserDataMと同一階層のCocoroCoreMディレクトリからneo4jパスを取得
                    string baseDirectory = Path.GetDirectoryName(userDataPath) ?? AppContext.BaseDirectory;
                    string neo4jDataPath = Path.Combine(baseDirectory, "CocoroCoreM", "neo4j", "data");

                    // Memoryフォルダの削除
                    if (Directory.Exists(memoryPath))
                    {
                        try
                        {
                            Directory.Delete(memoryPath, true);
                            Debug.WriteLine($"削除完了: {memoryPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Memory削除エラー: {ex.Message}");
                            throw new Exception($"Memoryフォルダの削除に失敗しました: {ex.Message}");
                        }
                    }

                    // Neo4j dataフォルダの削除
                    if (Directory.Exists(neo4jDataPath))
                    {
                        try
                        {
                            Directory.Delete(neo4jDataPath, true);
                            Debug.WriteLine($"削除完了: {neo4jDataPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Neo4j data削除エラー: {ex.Message}");
                            throw new Exception($"Neo4jデータフォルダの削除に失敗しました: {ex.Message}");
                        }
                    }

                    await Task.Delay(1000);
                });

                // ダイアログを閉じる
                progressDialog.Close();

                // 完了メッセージ
                MessageBox.Show(
                    "記憶データの削除が完了しました。\n\n" +
                    "新しい記憶データベースを作成するには、\n" +
                    "CocoroCoreMを再起動してください。",
                    "削除完了",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                progressDialog.Close();
                MessageBox.Show(
                    $"記憶の削除中にエラーが発生しました:\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // ボタンを有効化
                DeleteMemoryButton.IsEnabled = true;
            }
        }
    }
}