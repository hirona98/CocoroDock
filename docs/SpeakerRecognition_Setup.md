# 話者識別機能 セットアップ手順

## 概要

CocoroDockの話者識別機能を使用するには、WeSpeaker ONNXモデルのダウンロードが必要です。

## 必要なモデルファイル

- **ファイル名**: `wespeaker_resnet34.onnx`
- **配置先**: `CocoroDock/Resource/wespeaker_resnet34.onnx`
- **モデルサイズ**: 約10-15MB
- **精度**: VoxCelebデータセットでEER < 2%

## ダウンロード手順

### オプション1: WeSpeaker公式リポジトリから直接ダウンロード（推奨）

1. 以下のURLから事前学習済みモデルをダウンロード:
   ```
   https://wespeaker-1256283475.cos.ap-shanghai.myqcloud.com/models/voxceleb/voxceleb_resnet34.onnx
   ```

2. ダウンロードしたファイルを `voxceleb_resnet34.onnx` から `wespeaker_resnet34.onnx` にリネーム

3. ファイルを `CocoroDock/Resource/` ディレクトリに配置

### オプション2: WeSpeaker公式リポジトリからビルド

より詳細な制御が必要な場合は、WeSpeaker公式リポジトリからモデルをビルドすることができます。

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

# 4. モデルをCocoroDockに配置
cp voxceleb_resnet34.onnx /path/to/CocoroDock/Resource/wespeaker_resnet34.onnx
```

## モデル配置後の確認

モデルファイルが正しく配置されているか確認します:

```bash
ls -lh CocoroDock/Resource/wespeaker_resnet34.onnx
```

ファイルサイズが約10-15MBであることを確認してください。

## ビルド実行

モデルファイルの配置が完了したら、プロジェクトをビルドします:

```bash
cd CocoroDock
dotnet build
```

ビルドが成功すれば、話者識別機能が使用可能になります。

## 使用方法

### 話者の登録

1. CocoroDockを起動
2. 設定画面を開く
3. 「話者識別設定」セクションに移動
4. 話者名を入力し、「5秒録音して登録」ボタンをクリック
5. マイクに向かって5秒間話す
6. 登録完了

### 識別の動作

- 音声入力が検出されると、自動的に話者識別が実行されます
- 識別結果は `[話者名] 認識テキスト` の形式でCocoroAIに送信されます
- 識別失敗時は例外が発生し、処理が停止します（異常系停止の原則に従う）

### 識別感度の調整

- 設定画面の「識別感度」スライダーで調整できます
- 推奨値: 0.6-0.7（バランス）
- 低い値（0.5-0.6）: 寛容（偽陽性のリスク）
- 高い値（0.7-0.9）: 厳格（偽陰性のリスク）

## トラブルシューティング

### ビルドエラー: リソース 'wespeaker_resnet34.onnx' を読み込み中にエラー

**原因**: モデルファイルが配置されていない

**解決策**:
1. 上記の手順に従ってモデルファイルをダウンロード
2. `CocoroDock/Resource/wespeaker_resnet34.onnx` に配置
3. 再度ビルド実行

### 実行時エラー: Embedded resource 'wespeaker_resnet34.onnx' not found

**原因**: ビルド時にモデルファイルが埋め込まれていない

**解決策**:
1. モデルファイルが正しい場所に配置されているか確認
2. `dotnet clean` を実行
3. `dotnet build` を再実行

### 話者識別失敗: 話者が一人も登録されていません

**原因**: 話者が登録されていない

**解決策**:
1. 設定画面で最低1人の話者を登録
2. 音声認識を再試行

### 話者を識別できませんでした（最高類似度 < 閾値）

**原因**: 登録済み話者との類似度が閾値未満

**解決策**:
1. 識別感度を下げる（0.5-0.6）
2. 追加の話者サンプルを登録
3. より明瞭に話す

## 技術仕様

### モデル詳細

| 項目 | 詳細 |
|------|------|
| モデルアーキテクチャ | ResNet34 |
| 埋め込み次元 | 256次元 |
| 入力形式 | 16kHz モノラル音声 |
| 推論速度 | 音声3秒あたり約50-100ms |
| 精度 | VoxCelebデータセットでEER < 2% |

### データベース

- **パス**: `UserDataM/speaker_recognition.db`
- **形式**: SQLite
- **スキーマ**: speaker_id, speaker_name, embedding (BLOB), created_at, updated_at

### セキュリティ

- すべての音声処理はローカルで実行
- 音声データは外部サーバーに送信されません
- 埋め込みベクトルはSQLiteに暗号化されずに保存されます（ローカル環境のため）

## 参考資料

- [WeSpeaker公式リポジトリ](https://github.com/wenet-e2e/wespeaker)
- [WeSpeaker事前学習済みモデル](https://github.com/wenet-e2e/wespeaker/blob/main/docs/pretrained.md)
- [VoxCelebデータセット](https://www.robots.ox.ac.uk/~vgg/data/voxceleb/)
- [ONNX Runtime](https://onnxruntime.ai/)

## ライセンス

WeSpeakerモデルは Apache 2.0 ライセンスの下で提供されています。
