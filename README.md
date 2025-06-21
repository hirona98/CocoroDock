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

## 主な機能

### デスクトップウォッチ機能
定期的にデスクトップのスクリーンショットを取得し、AIに送信して作業内容に応じた会話を行います。

- **定期スクリーンショット取得**: 設定した間隔でデスクトップ画像を自動キャプチャ
- **OCRフィルタリング**: Tesseract OCRで画像内のテキストを認識し、正規表現でセキュリティ対策
- **自動設定**: 初回起動時に必要な言語データを自動ダウンロード

## 使用方法

### 起動
アプリケーションは起動時にシステムトレイに格納された状態で開始されます。システムトレイのアイコンをダブルクリックするか、右クリックして「表示」を選択することでウィンドウを表示できます。

### コマンドライン引数
- `/show` または `-show`: アプリケーション起動時にウィンドウを表示します。

## 開発者向け情報

### ドキュメント
- [CLAUDE.md](CLAUDE.md) - Claude Code (claude.ai/code) 向けのガイダンス
