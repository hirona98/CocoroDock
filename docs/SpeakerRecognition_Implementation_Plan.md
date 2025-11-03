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

## 📜 ライセンスとクレジット表記

### WeSpeakerモデルのライセンス

**事前学習済みモデル** `voxceleb_resnet34.onnx` は **Creative Commons Attribution 4.0 International License (CC BY 4.0)** の下で提供されています。

| 項目 | 詳細 |
|------|------|
| **コードのライセンス** | Apache 2.0（WeSpeakerリポジトリ） |
| **モデルのライセンス** | CC BY 4.0（VoxCelebデータセットに準拠） |
| **商用利用** | ✅ 可能 |
| **改変・再配布** | ✅ 可能 |
| **クレジット表記** | ⚠️ **必須** |

### 必要な対応

#### 1. License.txtへの追記

**ファイルパス**: `CocoroDock/Resource/License.txt`

```
================================================================================
WeSpeaker Speaker Recognition Model
================================================================================

Model: voxceleb_resnet34.onnx
Trained on VoxCeleb dataset
License: Creative Commons Attribution 4.0 International (CC BY 4.0)

Source: https://github.com/wenet-e2e/wespeaker
VoxCeleb Dataset: http://www.robots.ox.ac.uk/~vgg/data/voxceleb/

Copyright (c) VoxCeleb Contributors
Licensed under CC BY 4.0: https://creativecommons.org/licenses/by/4.0/

--------------------------------------------------------------------------------
```

#### 2. csprojでのLicense.txt組み込み

**ファイルパス**: `CocoroDock/CocoroDock.csproj`

既存の設定（67-68行目）で `Resource\License.txt` は既にEmbeddedResourceとして組み込まれています：
```xml
<EmbeddedResource Include="Resource\License.txt" />
```

### CC BY 4.0 ライセンス要件

1. **著作権表示**: モデルの出所とライセンスを明記
2. **変更の明示**: モデルを改変した場合はその旨を記載
3. **ライセンスへのリンク**: CC BY 4.0へのリンクまたは全文を含める
4. **免責事項**: 保証がないことを明示

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

### コンポーネント構成

```
CocoroDock/
├── Services/
│   ├── SileroVadService.cs          (既存)
│   ├── RealtimeVoiceRecognitionService.cs  (修正)
│   └── SpeakerRecognitionService.cs (新規) ★
├── Communication/
│   └── CommunicationModels.cs       (修正)
├── Controls/
│   └── SpeakerManagementControl.xaml (新規) ★
└── Resource/
    └── wespeaker_resnet34.onnx      (新規) ★
```

---

## 🛠️ 実装詳細

### 1. SpeakerRecognitionService.cs（新規作成）

**ファイルパス**: `CocoroDock/Services/SpeakerRecognitionService.cs`

#### クラス設計

```csharp
namespace CocoroDock.Services
{
    /// <summary>
    /// WeSpeaker話者識別サービス
    /// SileroVadServiceのパターンを踏襲（共有モデル + スレッドセーフ）
    /// </summary>
    public class SpeakerRecognitionService : IDisposable
    {
        // 共有リソース
        private static InferenceSession? _sharedModel;
        private static readonly object _modelLock = new object();

        // インスタンス設定
        private readonly string _dbPath;
        private readonly float _threshold;

        // 定数
        private const int EMBEDDING_DIM = 256; // WeSpeaker ResNet34
        private const int SAMPLE_RATE = 16000;

        // 主要メソッド
        public SpeakerRecognitionService(string dbPath, float threshold = 0.6f);
        private static void EnsureModelLoaded();
        private void InitializeDatabase();

        public float[] ExtractEmbedding(byte[] wavAudio);
        public void RegisterSpeaker(string speakerId, string speakerName, byte[] audioSample);
        public (string speakerId, string speakerName, float confidence) IdentifySpeaker(byte[] wavAudio);
        public List<(string speakerId, string speakerName)> GetRegisteredSpeakers();
        public void DeleteSpeaker(string speakerId);

        public void Dispose();
        public static void DisposeSharedResources();
    }
}
```

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

#### 主要アルゴリズム

**埋め込みベクトル抽出**:
```csharp
public float[] ExtractEmbedding(byte[] wavAudio)
{
    // 1. WAVヘッダー(44バイト)除去
    var samples = ConvertWavToFloat(wavAudio);

    // 2. 音声長調整（3秒未満はパディング、10秒以上はクロップ）
    samples = AdjustAudioLength(samples, targetSeconds: 3);

    // 3. ONNX推論
    lock (_modelLock)
    {
        var inputTensor = new DenseTensor<float>(samples, new[] { 1, samples.Length });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("audio", inputTensor)
        };

        using var results = _sharedModel!.Run(inputs);
        var embedding = results.First(r => r.Name == "embedding")
            .AsEnumerable<float>()
            .ToArray();

        // 4. L2正規化（コサイン類似度計算用）
        return NormalizeEmbedding(embedding);
    }
}
```

**話者識別**:
```csharp
public (string speakerId, string speakerName, float confidence) IdentifySpeaker(byte[] wavAudio)
{
    // 1. クエリ音声から埋め込み抽出
    var queryEmbedding = ExtractEmbedding(wavAudio);

    // 2. DBから全登録話者を取得
    var registeredSpeakers = LoadAllEmbeddings();

    // 登録話者がゼロの場合は異常として停止
    if (registeredSpeakers.Count == 0)
        throw new InvalidOperationException("話者が一人も登録されていません。先に話者を登録してください。");

    // 3. コサイン類似度計算（並列処理）
    var (bestId, bestName, maxSimilarity) = registeredSpeakers
        .AsParallel()
        .Select(s => (s.id, s.name, sim: CosineSimilarity(queryEmbedding, s.embedding)))
        .OrderByDescending(x => x.sim)
        .First();

    // 4. 閾値判定（識別失敗は異常として停止）
    if (maxSimilarity < _threshold)
        throw new InvalidOperationException($"話者を識別できませんでした（最高類似度: {maxSimilarity:F2} < 閾値: {_threshold:F2}）。話者登録を追加するか閾値を調整してください。");

    return (bestId, bestName, maxSimilarity);
}

private float CosineSimilarity(float[] a, float[] b)
{
    return a.Zip(b, (x, y) => x * y).Sum(); // L2正規化済みのため内積のみ
}
```

---

### 2. RealtimeVoiceRecognitionService.cs（修正）

**ファイルパス**: `CocoroDock/Services/RealtimeVoiceRecognitionService.cs`

#### 変更点

**フィールド追加**:
```csharp
private readonly SpeakerRecognitionService _speakerRecognition;

// イベント追加
public event Action<string, string, float>? OnSpeakerIdentified; // (speakerId, name, confidence)
```

**コンストラクタ修正**:
```csharp
public RealtimeVoiceRecognitionService(
    ISpeechToTextService sttService,
    string wakeWords,
    SpeakerRecognitionService speakerRecognition, // 必須パラメータ
    float vadThreshold = 0.5f,
    int silenceTimeoutMs = 300,
    int activeTimeoutMs = 60000,
    bool startActive = false)
{
    _sttService = sttService ?? throw new ArgumentNullException(nameof(sttService));
    _speakerRecognition = speakerRecognition ?? throw new ArgumentNullException(nameof(speakerRecognition));
    _stateMachine = new VoiceRecognitionStateMachine(wakeWords, activeTimeoutMs, startActive);
    _sileroVad = new SileroVadService(vadThreshold, silenceTimeoutMs);

    // ... 既存処理 ...
}
```

**ProcessAudioBuffer修正**:
```csharp
private async Task ProcessAudioBuffer()
{
    if (_audioBuffer.Count == 0 || _isDisposed)
        return;

    var audioData = _audioBuffer.ToArray();
    _audioBuffer.Clear();

    var originalState = _stateMachine.CurrentState;
    _stateMachine.TransitionTo(VoiceRecognitionState.PROCESSING);
    UpdateWavHeader(audioData);

    // ====== 話者識別（必須処理） ======
    // 例外が発生した場合は上位に伝播して停止
    var (speakerId, speakerName, confidence) = _speakerRecognition.IdentifySpeaker(audioData);

    OnSpeakerIdentified?.Invoke(speakerId, speakerName, confidence);

    string speakerPrefix = $"[{speakerName}] ";
    System.Diagnostics.Debug.WriteLine($"[Speaker] {speakerName} (信頼度: {confidence:F2})");
    // =================================

    // STT処理（既存、例外発生時は上位へ伝播）
    var recognitionTask = _sttService.RecognizeAsync(audioData);
    string recognizedText = await recognitionTask.ConfigureAwait(false);

    _stateMachine.TransitionTo(originalState);

    if (!string.IsNullOrEmpty(recognizedText))
    {
        // 話者情報を付加
        var textWithSpeaker = speakerPrefix + recognizedText;
        _stateMachine.ProcessRecognitionResult(textWithSpeaker);
    }
    else
    {
        // STTで認識できなかった場合も異常として停止
        throw new InvalidOperationException("音声認識に失敗しました。");
    }
}
```

**Dispose修正**:
```csharp
public void Dispose()
{
    if (_isDisposed)
        return;

    _isDisposed = true;

    StopListening();
    _stateMachine?.Dispose();
    _sttService?.Dispose();
    _sileroVad?.Dispose();
    _speakerRecognition.Dispose(); // 必須リソース

    System.Diagnostics.Debug.WriteLine("[VoiceService] Disposed");
}
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

**コードビハインド**: `SpeakerManagementControl.xaml.cs`

```csharp
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;

namespace CocoroDock.Controls
{
    public partial class SpeakerManagementControl : UserControl
    {
        private SpeakerRecognitionService? _speakerService;
        private WaveInEvent? _recordingDevice;
        private List<byte> _recordingBuffer = new();

        public SpeakerManagementControl()
        {
            InitializeComponent();
        }

        public void Initialize(SpeakerRecognitionService speakerService)
        {
            _speakerService = speakerService;
            RefreshSpeakerList();
        }

        private async void RecordAndRegisterSpeaker_Click(object sender, RoutedEventArgs e)
        {
            var speakerName = NewSpeakerNameBox.Text.Trim();
            if (string.IsNullOrEmpty(speakerName))
            {
                MessageBox.Show("話者名を入力してください", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 録音開始
                RecordingStatusText.Text = "録音中... (5秒間)";
                RecordingStatusText.Visibility = Visibility.Visible;

                var audioSample = await RecordAudioAsync(5000); // 5秒

                RecordingStatusText.Text = "処理中...";

                // 話者登録
                var speakerId = Guid.NewGuid().ToString();
                _speakerService?.RegisterSpeaker(speakerId, speakerName, audioSample);

                MessageBox.Show($"話者「{speakerName}」を登録しました", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                NewSpeakerNameBox.Clear();
                RefreshSpeakerList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"登録エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RecordingStatusText.Visibility = Visibility.Collapsed;
            }
        }

        private Task<byte[]> RecordAudioAsync(int durationMs)
        {
            var tcs = new TaskCompletionSource<byte[]>();

            _recordingBuffer.Clear();
            _recordingDevice = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50
            };

            _recordingDevice.DataAvailable += (s, e) =>
            {
                _recordingBuffer.AddRange(e.Buffer.Take(e.BytesRecorded));
            };

            _recordingDevice.StartRecording();

            Task.Delay(durationMs).ContinueWith(_ =>
            {
                _recordingDevice?.StopRecording();
                _recordingDevice?.Dispose();

                // WAVヘッダー追加
                var wavData = AddWavHeader(_recordingBuffer.ToArray());
                tcs.SetResult(wavData);
            });

            return tcs.Task;
        }

        private byte[] AddWavHeader(byte[] audioData)
        {
            // WAVヘッダー生成ロジック（RealtimeVoiceRecognitionServiceと同様）
            // ... 省略 ...
        }

        private void RefreshSpeakerList()
        {
            // DataContextのコレクションを更新
        }
    }
}
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

### 6. MainWindow.xaml.cs での統合

**ファイルパス**: `CocoroDock/MainWindow.xaml.cs`

**修正箇所**: RealtimeVoiceRecognitionServiceの初期化

```csharp
private void InitializeVoiceRecognition()
{
    // ... 既存のSTTサービス初期化 ...

    // 話者識別サービス初期化（常に有効）
    var dbPath = Path.Combine(AppSettings.Instance.UserDataDirectory, "speaker_recognition.db");
    var speakerService = new SpeakerRecognitionService(
        dbPath,
        threshold: AppSettings.Instance.MicrophoneSettings.speakerRecognitionThreshold
    );

    // 音声認識サービス初期化（speakerServiceは必須パラメータ）
    _voiceRecognitionService = new RealtimeVoiceRecognitionService(
        sttService,
        wakeWords: "...",
        speakerRecognition: speakerService
    );

    // イベントハンドラ追加
    _voiceRecognitionService.OnSpeakerIdentified += (speakerId, name, confidence) =>
    {
        Dispatcher.Invoke(() =>
        {
            // UIに表示（例: ステータスバー）
            StatusText.Text = $"話者: {name} ({confidence:P0})";
        });
    };
}
```

---

## 📦 必要なリソース

### 1. NuGetパッケージ（追加）

```xml
<!-- CocoroDock.csproj -->
<!-- 既存パッケージ -->
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.19.2" />
<PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.0" />
<PackageReference Include="NAudio" Version="2.2.1" />

<!-- 追加不要（既存で対応可能） -->
```

### 2. ONNXモデルファイル

**取得方法**:

#### オプション1: WeSpeaker公式リポジトリから変換（推奨）

```bash
# 1. WeSpeakerリポジトリをクローン
git clone https://github.com/wenet-e2e/wespeaker.git
cd wespeaker

# 2. 環境セットアップ
conda create -n wespeaker python=3.9
conda activate wespeaker
pip install -r requirements.txt

# 3. 事前学習済みモデルダウンロード
wget https://wespeaker-1256283475.cos.ap-shanghai.myqcloud.com/models/voxceleb/voxceleb_resnet34.onnx

# 4. モデル検証
python examples/onnx/inference.py --model voxceleb_resnet34.onnx --audio test.wav
```

#### オプション2: PyTorchモデルからONNX変換

```python
# export_wespeaker_to_onnx.py
import torch
import onnx
from wespeaker.models import ResNet34

model = ResNet34(feat_dim=80, embed_dim=256)
model.load_state_dict(torch.load('voxceleb_resnet34.pt'))
model.eval()

dummy_input = torch.randn(1, 48000)  # 3秒 @ 16kHz
torch.onnx.export(
    model,
    dummy_input,
    "wespeaker_resnet34.onnx",
    input_names=["audio"],
    output_names=["embedding"],
    dynamic_axes={"audio": {0: "batch", 1: "length"}}
)

# モデル検証
onnx_model = onnx.load("wespeaker_resnet34.onnx")
onnx.checker.check_model(onnx_model)
```

**配置場所**:
```
CocoroDock/Resource/wespeaker_resnet34.onnx
```

**csproj設定追加**:
```xml
<!-- CocoroDock.csproj -->
<ItemGroup>
    <EmbeddedResource Include="Resource\silero_vad.onnx" />
    <EmbeddedResource Include="Resource\wespeaker_resnet34.onnx" /> <!-- 追加 -->
</ItemGroup>
```

**Git LFS設定**:

`.gitattributes` に以下を追加済み（モデルファイルを効率的に管理）:
```
*.onnx filter=lfs diff=lfs merge=lfs -text
```

これにより、ONNXモデルファイル（8-15MB）はGit LFSで管理され、リポジトリの肥大化を防ぎます。

### 3. データベースファイル

- **パス**: `UserDataM/speaker_recognition.db`
- **自動作成**: SpeakerRecognitionServiceの初期化時に作成
- **バックアップ**: 設定ファイルと同様に管理

---

## 🚀 開発ステップ

### Phase 1: 環境準備（1日）

- [ ] WeSpeaker ONNXモデル取得・検証
- [ ] モデルを `CocoroDock/Resource/` に配置
- [ ] `Resource/License.txt` にWeSpeakerモデルのクレジット表記を追加（完了）
- [ ] `.gitattributes` にONNX用LFS設定を追加（完了）
- [ ] csprojにEmbeddedResource追加
- [ ] ビルド確認

### Phase 2: コア実装（2-3日）

- [ ] `SpeakerRecognitionService.cs` 作成
  - [ ] ONNXモデルロード処理
  - [ ] SQLite初期化
  - [ ] ExtractEmbedding実装
  - [ ] RegisterSpeaker実装
  - [ ] IdentifySpeaker実装
- [ ] 単体テスト作成
  - [ ] サンプル音声で埋め込み抽出テスト
  - [ ] 類似度計算の精度検証

### Phase 3: 統合（2日）

- [ ] `RealtimeVoiceRecognitionService.cs` 修正
  - [ ] コンストラクタ修正
  - [ ] ProcessAudioBuffer修正
  - [ ] イベントハンドラ追加
- [ ] `MicrophoneSettings` 拡張
- [ ] `AppSettings` への設定保存・読込実装

### Phase 4: UI実装（2-3日）

- [ ] `SpeakerManagementControl.xaml` 作成
- [ ] ViewModelバインディング実装
- [ ] 録音機能実装
- [ ] 話者リスト表示実装
- [ ] 削除機能実装
- [ ] `SystemSettingsControl` への統合

### Phase 5: テスト・最適化（2-3日）

- [ ] 実音声での動作確認
  - [ ] 複数話者登録テスト
  - [ ] 識別精度検証
  - [ ] 誤識別パターン分析
- [ ] パフォーマンス計測
  - [ ] 推論時間測定
  - [ ] メモリ使用量確認
- [ ] エラーハンドリング強化
- [ ] ログ出力整備

### Phase 6: ドキュメント・リリース（1日）

- [ ] ユーザーマニュアル作成
- [ ] コメント整備
- [ ] バージョン番号更新
- [ ] リリースノート作成

**総開発期間見積もり**: 約10-12日

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

3. **結果表示**
   - チャット画面: `[田中さん] おはようございます`
   - ステータスバー: `話者: 田中さん (85%)`

### エラーハンドリング

| エラーケース | 動作 |
|-------------|------|
| 話者未登録 | `InvalidOperationException` をスローして停止。ユーザーに登録を促すメッセージを表示 |
| 識別失敗（閾値未満） | `InvalidOperationException` をスローして停止。閾値調整または追加登録を促すメッセージを表示 |
| ONNXモデル読込失敗 | 起動時に例外スロー、アプリケーション起動を停止 |
| DB接続失敗 | 例外スロー、音声認識処理を停止 |
| STT失敗 | 例外スロー、処理を停止 |

**方針**: フォールバック禁止・異常系停止の原則に従い、全ての異常は例外として上位に伝播させます。

---

## 📊 パフォーマンス目標

| 指標 | 目標値 |
|------|--------|
| モデルサイズ | < 20MB |
| メモリ使用量（推論時） | < 100MB |
| 推論時間（3秒音声） | < 100ms |
| 識別精度（EER） | < 5% |
| 登録可能話者数 | 100名以上 |

---

## 🔒 セキュリティ・プライバシー考慮

1. **ローカル処理**: 音声埋め込みは全てローカルで処理、外部送信なし
2. **データ保護**: SQLiteファイルはUserDataM配下に保存
3. **削除機能**: ユーザーが任意に話者データを削除可能
4. **透明性**: 識別結果は必ずログ出力

---

## 🧪 テスト計画

### 単体テスト

```csharp
[Test]
public void ExtractEmbedding_ValidAudio_Returns256DimVector()
{
    var service = new SpeakerRecognitionService("test.db");
    var audioData = LoadTestAudio("sample_3sec.wav");

    var embedding = service.ExtractEmbedding(audioData);

    Assert.AreEqual(256, embedding.Length);
    Assert.IsTrue(Math.Abs(embedding.Sum(x => x * x) - 1.0f) < 0.01f); // L2正規化確認
}

[Test]
public void IdentifySpeaker_SameSpeaker_HighConfidence()
{
    var service = new SpeakerRecognitionService("test.db");
    var audioSample1 = LoadTestAudio("speaker1_sample1.wav");
    var audioSample2 = LoadTestAudio("speaker1_sample2.wav");

    service.RegisterSpeaker("sp1", "テスト話者", audioSample1);
    var (id, name, conf) = service.IdentifySpeaker(audioSample2);

    Assert.AreEqual("sp1", id);
    Assert.IsTrue(conf > 0.8f);
}
```

### 統合テスト

- 実際のマイク入力での動作確認
- 複数話者の同時録音での識別精度
- 長時間動作の安定性確認

---

## 📝 今後の拡張案

1. **話者適応**: 継続的な音声入力で埋め込みを更新
2. **グループ管理**: 家族、同僚などグループ分け
3. **統計情報**: 話者別の発話時間・頻度表示
4. **音声サンプル管理**: 複数サンプルの平均埋め込み使用
5. **クラウド同期**: 複数デバイス間での話者データ共有

---

## 📚 参考資料

### WeSpeaker関連
- 公式リポジトリ: https://github.com/wenet-e2e/wespeaker
- 論文: "WeSpeaker: A Research and Production oriented Speaker Embedding Learning Toolkit"
- モデルダウンロード: https://github.com/wenet-e2e/wespeaker/blob/main/docs/pretrained.md
- コードライセンス: Apache 2.0 (https://github.com/wenet-e2e/wespeaker/blob/main/LICENSE)
- モデルライセンス: CC BY 4.0 (VoxCelebデータセットに準拠)

### ONNX Runtime
- ドキュメント: https://onnxruntime.ai/docs/
- C# API: https://onnxruntime.ai/docs/api/csharp/api/

### 話者認識技術
- VoxCelebデータセット: https://www.robots.ox.ac.uk/~vgg/data/voxceleb/
- VoxCelebライセンス: CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/)
- ECAPA-TDNN論文: "ECAPA-TDNN: Emphasized Channel Attention, Propagation and Aggregation in TDNN Based Speaker Verification"

---

## ✅ チェックリスト

### 実装前確認
- [ ] WeSpeaker ONNXモデルの入手方法確認
- [ ] ライセンス確認（CC BY 4.0、クレジット表記必須）
- [ ] License.txtへのクレジット表記追加
- [ ] 開発環境準備（Visual Studio, .NET 8.0）
- [ ] 既存コードのバックアップ

### 実装中確認
- [ ] SileroVadServiceのパターンを踏襲
- [ ] エラーハンドリング実装
- [ ] ログ出力整備
- [ ] コメント記述

### 実装後確認
- [ ] ビルド成功
- [ ] 実音声での動作確認
- [ ] パフォーマンス測定
- [ ] ドキュメント更新

---

## 💬 問い合わせ・サポート

実装中に不明点があれば、以下を確認:
1. WeSpeaker公式ドキュメント
2. ONNX Runtime C# サンプルコード
3. 既存のSileroVadService実装

---

**作成日**: 2025-11-03
**最終更新**: 2025-11-03
**バージョン**: 1.2（Git LFS対応追加版）
**対象プロジェクト**: CocoroDock v4.5.2

## 📝 変更履歴

### v1.2 (2025-11-03)
- `.gitattributes` にONNX用Git LFS設定を追加
- `Resource/License.txt` にWeSpeakerクレジット表記を追加（完了）
- 実装計画書にGit LFS設定セクションを追加
- 開発ステップPhase 1にLFS設定項目を追加

### v1.1 (2025-11-03)
- ライセンス情報を修正: Apache 2.0 → CC BY 4.0
- ライセンスとクレジット表記セクションを追加
- License.txtへのクレジット表記方法を明記
- 開発ステップにライセンス対応を追加
- チェックリストにクレジット表記項目を追加

### v1.0 (2025-11-03)
- 初版作成
