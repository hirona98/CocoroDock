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
        /// CocoroShellポート
        /// </summary>
        int CocoroShellPort { get; set; }

        /// <summary>
        /// 通知API有効/無効
        /// </summary>
        bool IsEnableNotificationApi { get; set; }

        /// <summary>
        /// 通知APIポート
        /// </summary>
        int NotificationApiPort { get; set; }

        /// <summary>
        /// 最前面表示
        /// </summary>
        bool IsTopmost { get; set; }

        /// <summary>
        /// カーソル回避
        /// </summary>
        bool IsEscapeCursor { get; set; }

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
        /// 現在のキャラクターインデックス
        /// </summary>
        int CurrentCharacterIndex { get; set; }

        /// <summary>
        /// スクリーンショット設定
        /// </summary>
        ScreenshotSettings ScreenshotSettings { get; set; }

        /// <summary>
        /// キャラクターリスト
        /// </summary>
        List<CharacterSettings> CharacterList { get; set; }

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
    }
}