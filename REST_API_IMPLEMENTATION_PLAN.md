# CocoroDock REST API実装計画書

> 本計画書は、WebSocketからREST APIへの移行に関する統合計画書です。  
> API仕様の詳細は [統一API仕様書](../API_SPECIFICATION_UNIFIED.md) を参照してください。

## 概要

CocoroDockの通信設計をWebSocketベースからREST APIベースに変更します。この変更により、システムの簡素化、デバッグの容易化、およびステートレスな通信の実現を目指します。

## 変更の背景と目的

### 背景
- 現在のWebSocket実装は、接続管理やBase64エンコーディングなど複雑な処理を含む
- リアルタイム双方向通信の必要性が低い（リクエスト/レスポンス型で十分）
- 各コンポーネントが独立したサービスとして動作することが望ましい

### 目的
1. **簡素化**: ステートレスなREST APIによる通信管理の簡素化
2. **保守性向上**: HTTPステータスコードによる明確なエラーハンドリング
3. **テスト容易性**: Postman等のツールで直接APIをテスト可能
4. **パフォーマンス向上**: Base64エンコーディング廃止による処理効率化

## 実装計画

### フェーズ1: 新規コンポーネント作成

#### 1.1 CocoroDockApiServer.cs
**場所**: `Communication/CocoroDockApiServer.cs`

**エンドポイント実装**:
- POST /api/chat - チャットメッセージUIへの表示
- GET /api/config - 設定取得
- PUT /api/config - 設定更新
- POST /api/control - 制御コマンド実行

**必要なペイロードクラス**:
```csharp
public class ChatRequest
{
    public string role { get; set; } // "user" | "assistant"
    public string content { get; set; }
    public DateTime timestamp { get; set; }
}

public class ControlRequest
{
    public string command { get; set; } // "shutdown" | "restart" | "reload_config"
    public Dictionary<string, object> @params { get; set; }
    public string reason { get; set; } // optional
}

public class StandardResponse
{
    public string status { get; set; } // "success" | "error"
    public string message { get; set; }
    public DateTime timestamp { get; set; }
}
```

#### 1.2 CocoroShellClient.cs
**場所**: `Communication/CocoroShellClient.cs`

**実装メソッド**:
```csharp
public class CocoroShellClient
{
    Task<StandardResponse> SendChatMessageAsync(ShellChatRequest request);
    Task<StandardResponse> SendAnimationCommandAsync(AnimationRequest request);
    Task<StandardResponse> SendControlCommandAsync(ShellControlRequest request);
}
```

**必要なペイロードクラス**:
```csharp
public class ShellChatRequest
{
    public string content { get; set; }
    public VoiceParams voiceParams { get; set; }
    public string animation { get; set; } // "talk" | "idle" | null
    public string characterName { get; set; } // optional
}

public class VoiceParams
{
    public int speaker_id { get; set; }
    public float speed { get; set; }
    public float pitch { get; set; }
    public float volume { get; set; }
}

public class AnimationRequest
{
    public string animationName { get; set; }
}

public class ShellControlRequest
{
    public string command { get; set; }
    public Dictionary<string, object> @params { get; set; }
}
```

### フェーズ2: 既存コード改修

#### 2.1 CommunicationService.cs
- WebSocketServerをCocoroDockApiServerに置き換え
- CocoroShellClientを統合
- イベントハンドラーは維持（APIレスポンスに基づいて発火）

#### 2.2 ICommunicationService.cs
- インターフェース定義の更新（WebSocket関連を削除）

#### 2.3 CommunicationModels.cs
- WebSocketMessage関連クラスを削除
- 新しいREST APIペイロードクラスを追加
- MessageTypeの削除（不要）

#### 2.4 MainWindow.xaml.cs
- サーバー起動処理の更新
- 通信エラーハンドリングの簡素化

### フェーズ3: クリーンアップ

#### 3.1 削除対象ファイル
- `Communication/WebSocketServer.cs`
- `Utilities/MessageHelper.cs`のBase64関連メソッド

#### 3.2 設定ファイル
- cocoroShellPort（55605）は既に存在（変更不要）

## 実装詳細

### エラーハンドリング統一
すべてのAPIレスポンスで以下の形式を使用:
```json
{
  "status": "error",
  "message": "エラーの詳細説明",
  "errorCode": "ERROR_CODE",  // optional
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### HTTPクライアント設定
```csharp
services.AddHttpClient<CocoroShellClient>(client =>
{
    client.BaseAddress = new Uri($"http://127.0.0.1:{_appSettings.CocoroShellPort}");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});
```

### 後方互換性の考慮
- control commandの"shutdownCocoroAI"を"shutdown"にマッピング
- 既存の設定ファイル構造は維持

## テスト計画

### 単体テスト
1. CocoroDockApiServerの各エンドポイント
2. CocoroShellClientの各メソッド
3. エラーレスポンスの形式確認

### 統合テスト
1. CocoroDock ↔ CocoroShell間の通信
2. 設定更新後の外部プロセス再起動
3. 通知API（55604）の動作確認

### 動作確認チェックリスト
- [ ] チャットメッセージの表示（user/assistant両方）
- [ ] 設定の取得・更新
- [ ] アプリケーション制御（shutdown/restart/reload_config）
- [ ] CocoroShellへのメッセージ送信とTTS
- [ ] アニメーション制御
- [ ] エラー時の適切なレスポンス
- [ ] タイムアウト処理

## リスクと対策

### リスク1: CocoroShell側の準備
**対策**: CocoroShell側のAPI実装を事前に確認し、必要に応じて仕様調整

### リスク2: パフォーマンス
**対策**: HttpClientの再利用、HTTP/2の活用、Keep-Aliveの設定

### リスク3: 移行期間中の互換性
**対策**: フィーチャーフラグで新旧切り替え可能にする

## 実装スケジュール

1. **フェーズ1（新規コンポーネント）**: 1-2日
2. **フェーズ2（既存コード改修）**: 2-3日
3. **フェーズ3（クリーンアップ）**: 0.5日
4. **テスト・デバッグ**: 1-2日

**合計見積もり**: 4-7日

## 付録: 主な変更点まとめ

### 削除されるもの
- WebSocketServer.cs
- Base64エンコーディング処理
- WebSocketメッセージ関連クラス
- 接続状態管理ロジック

### 追加されるもの
- CocoroDockApiServer.cs（REST APIサーバー）
- CocoroShellClient.cs（HTTPクライアント）
- REST API用ペイロードクラス群
- 統一エラーレスポンス処理

### 変更されるもの
- CommunicationService（WebSocket→REST API）
- メッセージ送受信ロジック
- エラーハンドリング方式
- 設定更新時の処理フロー