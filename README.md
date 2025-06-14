# CocoroDock

CocoroDock は デスクトップマスコット CocoroAI のチャットおよび設定用UIです

CocoroAI
https://alice-encoder.booth.pm/items/6821221

----

CocoroCore に合わせてそのうち作り直すと思うので、かな～り雑に作ってます

プルリクしたい場合はお気軽にどうぞ！

CocoroAI全体構成は CocoroCoreリポジトリの CocoroAI全体構成.drawio を参照願います
----

## 開発環境

- Windows
- C# .NET8
- WPF

## 通信仕様

CocoroDockはREST APIベースの通信を採用しています：

- **CocoroDock API Server** (ポート: 55600) - チャット表示、設定管理、制御コマンド
- **Notification API Server** (ポート: 55604) - 外部アプリケーションからの通知受信
- **CocoroShell Client** - CocoroShell (ポート: 55605) へのメッセージ送信

詳細なAPI仕様は [統一API仕様書](../API_SPECIFICATION_UNIFIED.md) を参照してください。

## 使用方法

### 起動
アプリケーションは起動時にシステムトレイに格納された状態で開始されます。システムトレイのアイコンをダブルクリックするか、右クリックして「表示」を選択することでウィンドウを表示できます。

### コマンドライン引数
- `/show` または `-show`: アプリケーション起動時にウィンドウを表示します。

## 開発者向け情報

### ドキュメント
- [CLAUDE.md](CLAUDE.md) - Claude Code (claude.ai/code) 向けのガイダンス
- [REST_API_IMPLEMENTATION_PLAN.md](REST_API_IMPLEMENTATION_PLAN.md) - REST API実装計画書
