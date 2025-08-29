# CocoroDock

CocoroDock は デスクトップマスコット CocoroAI のチャットおよび設定用UIです

CocoroAI
https://alice-encoder.booth.pm/items/6821221

----

CocoroCore に合わせてそのうち作り直すと思うので、かな～り雑に作ってます

プルリクしたい場合はお気軽にどうぞ！

CocoroAI全体構成は CocoroCoreMリポジトリの CocoroAI全体構成.drawio に作成予定です
----

## 開発環境

- Windows
- C# .NET8
- WPF

## REST API 概要

※仕様古いかも

このアプリは 2 種類の REST API を提供します。

- CocoroDock API（アプリ内通信用）
	- ベースURL: http://127.0.0.1:{cocoroDockPort}（既定 55600）
	- バインド: 127.0.0.1 のみ
	- 形式: application/json; charset=utf-8
	- エラーフォーマット: { status: "error", message, errorCode?, timestamp }

- Notification API（外部からのアクセス用）
	- ベースURL: http://{ホスト}:{notificationApiPort}（既定 55604）
	- バインド: すべてのNIC（0.0.0.0）
	- 形式: application/json
	- エラーフォーマット: { error }

### CocoroDock API エンドポイント

- POST /api/addChatUi
	- 目的: チャットメッセージ受信（UI 表示等）
	- リクエスト: { userId, sessionId, message, role: "user"|"assistant", content, timestamp }
	- 必須: role, content（非空）
	- 200: { status: "success", message }

- GET /api/config
	- 目的: 設定取得（`ConfigSettings` 全体）
	- 200: 設定オブジェクト

- PUT /api/config
	- 目的: 設定の全更新＋保存
	- リクエスト: `ConfigSettings` 全体
	- 200: { status: "success", message }

- PUT /api/config/patch
	- 目的: 設定の部分更新（パッチ）
	- リクエスト: { updates: { key: value, ... }, changedFields?: string[] }
	- 備考: プロパティ名は大文字小文字を区別せず、配列/複合型も受理
	- 200: { status: "success", message }

- POST /api/control
	- 目的: アプリ制御コマンド
	- リクエスト: { command: "shutdown"|"restart"|"reloadConfig", params?: object, reason?: string }
	- 200: { status: "success", message }

- POST /api/status
	- 目的: ステータス更新/通知
	- リクエスト: { message, type?, timestamp }
	- 必須: message（非空）
	- 200: { status: "success", message }

- POST /api/logs
	- 目的: ログメッセージ受信
	- リクエスト: { timestamp, level: "DEBUG"|"INFO"|"WARNING"|"ERROR", component: "CocoroCoreM"|"SEPARATOR", message }
	- 必須: level, component, message
	- 200: { status: "success", message }

エラー時（400/500 など）は ErrorResponse を返します（JSON パース失敗: JSON_ERROR、バリデーション: VALIDATION_ERROR、内部エラー: INTERNAL_ERROR ほか）。

### Notification API エンドポイント

- POST /api/v1/notification
	- 目的: 通知を受け取り UI に即時反映、バックグラウンドで Core に転送
	- リクエスト: { from, message, images?: string[] }
		- images は data URL 形式（例: data:image/png;base64,...）、最大 5 枚
	- バリデーション:
		- from と message は必須・非空
		- from+message 合計長 ≤ 10MB（文字列長基準）
		- 画像 1 枚 ≤ 5MB、合計 ≤ 15MB（Base64 サイズ）
	- 成功: 204 No Content（即時応答）
	- 代表的エラー: 400（バリデーション/JSON 形式）, 503（一時的不可）, 500（内部エラー）

### 注意事項

- CocoroDock API はローカルホスト専用、Notification API は LAN から到達可能です。必要に応じてファイアウォールやリバースプロキシで制限してください。
