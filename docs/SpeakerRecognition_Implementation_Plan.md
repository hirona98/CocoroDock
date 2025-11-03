# CocoroDock 話者識別機能 実装計画書

## 📋 プロジェクト概要

### 目的
CocoroDockに話者識別（Speaker Recognition）機能を実装し、複数話者の音声を区別して認識できるようにする。

### 目標
- リアルタイム音声入力から話者を自動識別
- 事前登録された話者の厳格な判定（未知話者や閾値未満は異常として停止）
- 既存の音声認識フロー（VAD + STT）との統合
- 軽量・高速な推論パフォーマンス

---

## 🎯 技術選定: WeSpeaker

### WeSpeakerの特徴
| 項目 | 詳細 |
|------|------|
| **モデルサイズ** | 8-15MB（ResNet34ベース） |
| **精度** | VoxCelebデータセットでEER < 2% |
| **推論速度** | 音声3秒あたり約50-100ms |
| **ONNX対応** | 公式サポート、エクスポートツール提供 |
| **埋め込み次元** | 256次元（標準） |
| **入力形式** | 16kHz モノラル音声 |

### 選定理由
1. **ONNX公式対応**: PyTorchからのエクスポートが容易
2. **軽量**: CocoroDockの既存ONNX実装（SileroVAD）と同等サイズ
3. **高精度**: 最新のResNetアーキテクチャ採用
4. **日本語対応**: 多言語データセットで学習済み
5. **ライセンス**: CC BY 4.0（商用利用可、クレジット表記必須）

---

## 🏗️ システムアーキテクチャ

### 全体フロー

```
【登録フェーズ】
ユーザーがUI上で「話者登録」ボタンを押す
  ↓
5-10秒間マイク録音
  ↓
WeSpeaker ONNX推論 → 埋め込みベクトル(256次元)
  ↓
SQLiteに保存 (speaker_id, name, embedding, created_at)

【識別フェーズ】
マイク入力(16kHz/16bit/mono) ← 既存
  ↓
マイクゲイン適用 ← 既存
  ↓
プリバッファ(500ms) ← 既存
  ↓
Silero VAD（音声区間検出） ← 既存
  ↓
音声区間バッファリング ← 既存
  ↓
【新規】WeSpeaker推論 → 埋め込みベクトル
  ↓
【新規】コサイン類似度計算 vs 登録済みベクトル
  ↓
【新規】話者識別（閾値未満または登録話者ゼロの場合は例外スロー → 停止）
  ↓
AmiVoice STT（失敗時は例外スロー → 停止） ← 既存
  ↓
"[話者名] 認識テキスト" を CocoroAI に送信 ← 修正
```

---

## 🛠️ 実装詳細

### 1. SpeakerRecognitionService.cs（新規作成）

**ファイルパス**: `CocoroDock/Services/SpeakerRecognitionService.cs`

#### データベーススキーマ

```sql
-- SQLite: UserDataM/speaker_recognition.db
CREATE TABLE speakers (
    speaker_id TEXT PRIMARY KEY,      -- UUID
    speaker_name TEXT NOT NULL,       -- 表示名（例: "田中さん", "佐藤さん"）
    embedding BLOB NOT NULL,          -- 256次元float配列（1024バイト）
    created_at TEXT NOT NULL,         -- ISO8601形式
    updated_at TEXT NOT NULL
);

CREATE INDEX idx_speaker_name ON speakers(speaker_name);
```

---

### 3. MicrophoneSettings拡張

**ファイルパス**: `CocoroDock/Communication/CommunicationModels.cs`

**変更箇所**: 行280-283

```csharp
public class MicrophoneSettings
{
    public int inputThreshold { get; set; } = -45;

    // ====== 話者識別設定（新規追加） ======
    // 注: 話者識別は常に有効（後方互換禁止方針により無効化オプションは提供しない）
    public float speakerRecognitionThreshold { get; set; } = 0.6f; // 0.5-0.8推奨
    // =====================================
}
```

---

### 4. UI実装: SpeakerManagementControl.xaml（新規）

**ファイルパス**: `CocoroDock/Controls/SpeakerManagementControl.xaml`

```xml
<UserControl x:Class="CocoroDock.Controls.SpeakerManagementControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <GroupBox Header="話者識別設定" Margin="10">
        <StackPanel>
            <!-- 登録済み話者リスト -->
            <Label Content="登録済み話者:" FontWeight="Bold"/>
            <ListBox ItemsSource="{Binding RegisteredSpeakers}"
                     Height="150"
                     Margin="0,5,0,10">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="{Binding speakerName}"
                                       VerticalAlignment="Center"
                                       Margin="5,0"/>
                            <Button Grid.Column="1"
                                    Content="削除"
                                    Command="{Binding DataContext.DeleteSpeakerCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                    CommandParameter="{Binding speakerId}"
                                    Padding="10,2"/>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- 新規登録 -->
            <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
                <TextBox x:Name="NewSpeakerNameBox"
                         Width="150"
                         Margin="0,0,10,0"
                         VerticalAlignment="Center"/>
                <Button Content="5秒録音して登録"
                        Click="RecordAndRegisterSpeaker_Click"
                        Padding="10,5"/>
            </StackPanel>

            <!-- 識別感度スライダー -->
            <Label Content="識別感度:" FontWeight="Bold" Margin="0,10,0,0"/>
            <Slider Value="{Binding SpeakerRecognitionThreshold}"
                    Minimum="0.5"
                    Maximum="0.9"
                    TickFrequency="0.05"
                    IsSnapToTickEnabled="True"
                    TickPlacement="BottomRight"/>
            <TextBlock Text="{Binding SpeakerRecognitionThreshold, StringFormat='現在値: {0:F2} (低いほど寛容、高いほど厳格)'}"
                       Foreground="Gray"
                       FontSize="11"/>

            <!-- 録音中表示 -->
            <TextBlock x:Name="RecordingStatusText"
                       Text=""
                       Foreground="Red"
                       FontWeight="Bold"
                       Margin="0,10,0,0"
                       Visibility="Collapsed"/>
        </StackPanel>
    </GroupBox>
</UserControl>
```

---

### 5. 設定画面への統合

**ファイルパス**: `CocoroDock/Controls/SystemSettingsControl.xaml`

**追加箇所**: 既存のマイク設定セクションの下に追加

```xml
<!-- 既存のマイク設定の後 -->

<!-- 話者識別設定 -->
<local:SpeakerManagementControl x:Name="SpeakerManagementControl" Margin="0,10,0,0"/>
```

**コードビハインド修正**: `SystemSettingsControl.xaml.cs`

```csharp
public partial class SystemSettingsControl : UserControl
{
    private SpeakerRecognitionService _speakerService;

    public void Initialize()
    {
        // 既存の初期化処理...

        // 話者識別サービス初期化（常に有効）
        var dbPath = Path.Combine(AppSettings.Instance.UserDataDirectory, "speaker_recognition.db");
        _speakerService = new SpeakerRecognitionService(
            dbPath,
            threshold: AppSettings.Instance.MicrophoneSettings.speakerRecognitionThreshold
        );

        SpeakerManagementControl.Initialize(_speakerService);
    }
}
```

---

## 📦 必要なリソース

### 3. データベースファイル

- **パス**: `UserDataM/speaker_recognition.db`
- **自動作成**: SpeakerRecognitionServiceの初期化時に作成
- **バックアップ**: 設定ファイルと同様に管理

---

## ⚙️ 設定仕様

### DefaultSetting.json 追加項目

```json
{
  "microphoneSettings": {
    "inputThreshold": -30,
    "speakerRecognitionThreshold": 0.6
  }
}
```

### 設定パラメータ詳細

| パラメータ | 型 | デフォルト | 範囲 | 説明 |
|-----------|-----|-----------|------|------|
| `speakerRecognitionThreshold` | float | 0.6 | 0.5-0.9 | 識別閾値（高いほど厳格） |

**注意**: 話者識別は常に有効です。後方互換禁止方針により、無効化オプションは提供しません。

**閾値の目安**:
- **0.5-0.6**: 寛容（偽陽性が増える可能性）
- **0.6-0.7**: バランス（推奨）
- **0.7-0.9**: 厳格（偽陰性が増える可能性）

---

## 🎯 期待される動作

### 正常系フロー

1. **話者登録**
   - ユーザーが設定画面で「5秒録音して登録」ボタンをクリック
   - マイクから5秒間録音
   - WeSpeakerで埋め込みベクトルを抽出
   - SQLiteに保存
   - リストに表示

2. **リアルタイム識別**
   - マイクで音声入力
   - Silero VADが音声区間を検出
   - 音声区間をWeSpeakerで解析
   - 登録済み話者との類似度計算
   - 最高類似度が閾値以上なら話者名を付加
   - AmiVoiceで音声認識
   - "[話者名] 認識テキスト" をCocoroAIに送信

