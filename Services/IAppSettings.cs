using CocoroDock.Communication;
using System.Collections.Generic;

namespace CocoroDock.Services
{
    /// <summary>
    /// アプリケーション設定のインターフェース
    /// </summary>
    public interface IAppSettings
    {
        /// <summary>
        /// CocoroDockポート
        /// </summary>
        int CocoroDockPort { get; set; }

        /// <summary>
        /// CocoroCoreポート
        /// </summary>
        int CocoroCorePort { get; set; }

        /// <summary>
        /// CocoroMemoryポート
        /// </summary>
        int CocoroMemoryPort { get; set; }

        /// <summary>
        /// CocoroMemoryDBポート
        /// </summary>
        int CocoroMemoryDBPort { get; set; }

        /// <summary>
        /// CocoroMemoryWebポート
        /// </summary>
        int CocoroMemoryWebPort { get; set; }

        /// <summary>
        /// CocoroShellポート
        /// </summary>
        int CocoroShellPort { get; set; }

        /// <summary>
        /// 通知API有効/無効
        /// </summary>
        bool IsEnableNotificationApi { get; set; }

        /// <summary>
        /// リマインダー有効/無効
        /// </summary>
        bool IsEnableReminder { get; set; }

        /// <summary>
        /// MCP有効/無効
        /// </summary>
        bool IsEnableMcp { get; set; }
        /// <summary>
        /// WebSocketサーバーポート
        /// </summary>
        int CocoroWebPort { get; set; }

        /// <summary>
        /// Web機能有効/無効
        /// </summary>
        bool IsEnableWebService { get; set; }

        /// <summary>
        /// 通知APIポート
        /// </summary>
        int NotificationApiPort { get; set; }

        /// <summary>
        /// キャラクター位置復元
        /// </summary>
        bool IsRestoreWindowPosition { get; set; }

        /// <summary>
        /// 最前面表示
        /// </summary>
        bool IsTopmost { get; set; }

        /// <summary>
        /// カーソル回避
        /// </summary>
        bool IsEscapeCursor { get; set; }

        /// <summary>
        /// 逃げ先座標リスト
        /// </summary>
        List<EscapePosition> EscapePositions { get; set; }

        /// <summary>
        /// 仮想キー入力
        /// </summary>
        bool IsInputVirtualKey { get; set; }

        /// <summary>
        /// 仮想キー文字列
        /// </summary>
        string VirtualKeyString { get; set; }

        /// <summary>
        /// 自動移動
        /// </summary>
        bool IsAutoMove { get; set; }

        /// <summary>
        /// 発話時メッセージウィンドウ表示
        /// </summary>
        bool ShowMessageWindow { get; set; }

        /// <summary>
        /// アンビエントオクルージョン有効
        /// </summary>
        bool IsEnableAmbientOcclusion { get; set; }

        /// <summary>
        /// MSAAレベル
        /// </summary>
        int MsaaLevel { get; set; }

        /// <summary>
        /// キャラクターシャドウ
        /// </summary>
        int CharacterShadow { get; set; }

        /// <summary>
        /// キャラクターシャドウ解像度
        /// </summary>
        int CharacterShadowResolution { get; set; }

        /// <summary>
        /// 背景シャドウ
        /// </summary>
        int BackgroundShadow { get; set; }

        /// <summary>
        /// 背景シャドウ解像度
        /// </summary>
        int BackgroundShadowResolution { get; set; }

        /// <summary>
        /// ウィンドウサイズ
        /// </summary>
        int WindowSize { get; set; }

        /// <summary>
        /// ウィンドウX座標
        /// </summary>
        float WindowPositionX { get; set; }

        /// <summary>
        /// ウィンドウY座標
        /// </summary>
        float WindowPositionY { get; set; }

        /// <summary>
        /// 現在のキャラクターインデックス
        /// </summary>
        int CurrentCharacterIndex { get; set; }

        /// <summary>
        /// スクリーンショット設定
        /// </summary>
        ScreenshotSettings ScreenshotSettings { get; set; }

        /// <summary>
        /// マイク設定
        /// </summary>
        MicrophoneSettings MicrophoneSettings { get; set; }

        /// <summary>
        /// 定期コマンド実行設定
        /// </summary>
        Models.ScheduledCommandSettings ScheduledCommandSettings { get; set; }

        /// <summary>
        /// キャラクターリスト
        /// </summary>
        List<CharacterSettings> CharacterList { get; set; }

        /// <summary>
        /// 現在のアニメーション設定インデックス
        /// </summary>
        int CurrentAnimationSettingIndex { get; set; }

        /// <summary>
        /// アニメーション設定リスト
        /// </summary>
        List<AnimationSetting> AnimationSettings { get; set; }

        /// <summary>
        /// 設定が読み込まれたかどうか
        /// </summary>
        bool IsLoaded { get; set; }

        /// <summary>
        /// 設定値を更新
        /// </summary>
        /// <param name="config">サーバーから受信した設定値</param>
        void UpdateSettings(ConfigSettings config);

        /// <summary>
        /// 現在の設定からConfigSettingsオブジェクトを作成
        /// </summary>
        /// <returns>ConfigSettings オブジェクト</returns>
        ConfigSettings GetConfigSettings();

        /// <summary>
        /// 設定ファイルから設定を読み込む
        /// </summary>
        void LoadSettings();

        /// <summary>
        /// アプリケーション設定ファイルを読み込む
        /// </summary>
        void LoadAppSettings();

        /// <summary>
        /// アプリケーション設定をファイルに保存
        /// </summary>
        void SaveAppSettings();

        /// <summary>
        /// 全設定をファイルに保存
        /// </summary>
        void SaveSettings();

        /// <summary>
        /// アニメーション設定をファイルから読み込む
        /// </summary>
        void LoadAnimationSettings();

        /// <summary>
        /// アニメーション設定をファイルに保存
        /// </summary>
        void SaveAnimationSettings();

        /// <summary>
        /// キャラクターのsystemPromptをファイルから読み込む
        /// </summary>
        /// <param name="promptFilePath">プロンプトファイルのパス</param>
        /// <returns>プロンプトテキスト</returns>
        string LoadSystemPrompt(string promptFilePath);

        /// <summary>
        /// キャラクターのsystemPromptをファイルに保存
        /// </summary>
        /// <param name="promptFilePath">プロンプトファイルのパス</param>
        /// <param name="promptText">プロンプトテキスト</param>
        void SaveSystemPrompt(string promptFilePath, string promptText);

        /// <summary>
        /// 新しいsystemPromptファイル用のファイルパスを生成
        /// </summary>
        /// <param name="modelName">キャラクターのモデル名</param>
        /// <returns>モデル名_UUIDベースのファイルパス</returns>
        string GenerateSystemPromptFilePath(string modelName);

        /// <summary>
        /// UUID中間一致でsystemPromptファイルを検索
        /// </summary>
        /// <param name="uuid">検索するUUID</param>
        /// <returns>見つかったファイルパス、見つからない場合はnull</returns>
        string? FindSystemPromptFileByUuid(string uuid);

        /// <summary>
        /// systemPromptファイル名からUUIDを抽出
        /// </summary>
        /// <param name="fileName">ファイル名</param>
        /// <returns>抽出されたUUID、抽出できない場合はnull</returns>
        string? ExtractUuidFromFileName(string fileName);

        /// <summary>
        /// modelName変更時にsystemPromptファイル名を更新
        /// </summary>
        /// <param name="oldFileName">古いファイル名</param>
        /// <param name="newModelName">新しいモデル名</param>
        /// <returns>新しいファイル名</returns>
        string UpdateSystemPromptFileName(string oldFileName, string newModelName);

        /// <summary>
        /// ユーザーデータディレクトリを取得
        /// </summary>
        string UserDataDirectory { get; }
        /// <summary>
        /// 現在選択されているキャラクター設定を取得
        /// </summary>
        /// <returns>現在のキャラクター設定、存在しない場合はnull</returns>
        CharacterSettings? GetCurrentCharacter();
    }
}