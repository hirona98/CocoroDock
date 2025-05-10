# CocoroAI WebSocketAPI仕様書

## 概要

CocoroShellとのコミュニケーションに使用するWebSocket APIの仕様を解説します。(自動生成メモ)

## 接続情報

- **プロトコル**: WebSocket
- **デフォルトホスト**: 127.0.0.1
- **デフォルトポート**: 55600
- **エンドポイントURL**: `ws://127.0.0.1:55600/`

## メッセージフォーマット

### 基本構造

すべてのメッセージは共通の構造を持ちます：

```json
{
  "Type": "[メッセージタイプ]",
  "Timestamp": "[ISO 8601形式のタイムスタンプ]",
  "Payload": {
    // タイプ別のペイロード内容
  }
}
```

### エンコーディング

特定のWebSocketライブラリにて、UTF-8の特定の文字列を扱うと文章の一部が欠ける事があったため、メッセージはBASE64でエンコーディングします
メッセージは以下の手順でエンコード/デコードされます：

1. JSONオブジェクトをUTF-8でバイト配列に変換
2. バイト配列をBASE64文字列にエンコード
3. WebSocketを通じてテキストメッセージとして送信

受信側は逆の手順でデコードします：

1. BASE64文字列をデコード
2. UTF-8バイト配列からJSONに変換
3. メッセージタイプに応じたオブジェクトにデシリアライズ

## メッセージタイプ

### 1. Chat（チャット）

ユーザーとCocoroAI間のテキストチャットメッセージを処理します。

#### リクエスト（クライアント → サーバー）

```json
{
  "Type": "Chat",
  "Timestamp": "2025-05-10T10:00:00.000Z",
  "Payload": {
    "UserId": "ユーザーID",
    "SessionId": "session_xxxxxxxx",
    "Message": "こんにちは、元気ですか？"
  }
}
```

#### レスポンス（サーバー → クライアント）

```json
{
  "Type": "Chat",
  "Timestamp": "2025-05-10T10:00:05.000Z",
  "Payload": {
    "Response": "こんにちは！元気です。あなたはどうですか？"
  }
}
```

### 2. Config（設定）

アプリケーションの設定を取得・更新します。

#### 設定リクエスト（クライアント → サーバー）

```json
{
  "Type": "Config",
  "Timestamp": "2025-05-10T10:05:00.000Z",
  "Payload": {
    "Action": "Get"
  }
}
```

#### 設定更新（クライアント → サーバー）

```json
{
  "Type": "Config",
  "Timestamp": "2025-05-10T10:10:00.000Z",
  "Payload": {
    "Action": "Update",
    "Settings": {
      "IsTopmost": true,
      "IsEscapeCursor": false,
      "IsInputVirtualKey": true,
      "VirtualKeyString": "Ctrl+Shift+Space",
      "IsAutoMove": true,
      "IsEnableAmbientOcclusion": true,
      "MsaaLevel": 4,
      "CharacterShadow": 1,
      "CharacterShadowResolution": 1024,
      "BackgroundShadow": 1,
      "BackgroundShadowResolution": 512,
      "WindowSize": 1.0,
      "CurrentCharacterIndex": 0,
      "CharacterList": [
        {
          "IsReadOnly": false,
          "ModelName": "キャラクター名",
          "VRMFilePath": "モデルファイルパス",
          "IsUseLLM": true,
          "ApiKey": "APIキー",
          "LLMModel": "モデル名",
          "SystemPrompt": "システムプロンプト",
          "IsUseTTS": true,
          "TTSEndpointURL": "TTSエンドポイントURL",
          "TTSSperkerID": "TTSスピーカーID"
        }
      ]
    }
  }
}
```

#### 個別設定変更（クライアント → サーバー）

```json
{
  "Type": "Config",
  "Timestamp": "2025-05-10T10:15:00.000Z",
  "Payload": {
    "SettingKey": "IsTopmost",
    "Value": "true"
  }
}
```

#### 設定レスポンス（サーバー → クライアント）

```json
{
  "Type": "Config",
  "Timestamp": "2025-05-10T10:16:00.000Z",
  "Payload": {
    "Status": "Success",
    "Message": "設定を更新しました",
    "Settings": {
      // 現在の設定情報（上記のSettings構造と同じ）
    }
  }
}
```

### 3. Control（制御）

アプリケーションの動作を制御します。

```json
{
  "Type": "Control",
  "Timestamp": "2025-05-10T10:20:00.000Z",
  "Payload": {
    "Command": "Restart",
    "Reason": "設定変更による再起動"
  }
}
```

### 4. Status（状態）

システムの状態を通知します。

```json
{
  "Type": "Status",
  "Timestamp": "2025-05-10T10:25:00.000Z",
  "Payload": {
    "CurrentCPU": 15,
    "Status": "Ready"
  }
}
```

### 5. System（システム）

システムメッセージを送信します。

```json
{
  "Type": "System",
  "Timestamp": "2025-05-10T10:30:00.000Z",
  "Payload": {
    "Level": "Info",
    "Message": "アプリケーションが正常に起動しました"
  }
}
```

## イベント一覧

クライアントは以下のイベントを受け取ることができます：

1. **ChatMessageReceived** - チャットメッセージ受信時
2. **ConfigResponseReceived** - 設定レスポンス受信時
3. **StatusUpdateReceived** - 状態更新通知受信時
4. **SystemMessageReceived** - システムメッセージ受信時
5. **ErrorOccurred** - エラー発生時
6. **Connected** - クライアント接続時
7. **Disconnected** - クライアント切断時

## セッション管理

- セッションIDはクライアント側で生成され、チャットメッセージに含められます
- 新しいセッションを開始する際は、新しいセッションIDを生成します
- セッションIDの形式: `session_xxxxxxxx`（xは16進数）

## エラーハンドリング

- 通信エラーが発生した場合、ErrorOccurredイベントが発火されます
- 接続が切断された場合は、自動的に再接続を試みるか、明示的に接続の再確立が必要です

## セキュリティ

- APIキーなどの機密情報は暗号化されていないため、ローカル環境での使用を前提としています
- 本番環境で使用する場合は、SSL/TLS（wss://プロトコル）の使用を検討してください

## 実装例

### クライアント初期化

```csharp
// WebSocketサーバーとの接続を初期化
var communicationService = new CommunicationService("127.0.0.1", 55600, "user123");
await communicationService.ConnectAsync();

// イベントハンドラを設定
communicationService.ChatMessageReceived += (sender, message) => 
{
    Console.WriteLine($"受信メッセージ: {message}");
};
```

### メッセージ送信

```csharp
// チャットメッセージを送信
await communicationService.SendChatMessageAsync("こんにちは！");

// 設定を要求
await communicationService.RequestConfigAsync();

// 設定を更新
await communicationService.ChangeConfigAsync("IsTopmost", "true");
```

---
