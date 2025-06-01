# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Development Commands
本プロジェクトは、Windowsを対象にしているが、コードの編集はWSL経由で君に手伝ってもらっている。
参考までに、Windows上でのセットアップ方法を記載する。
君が powershel 経由で起動してもいいよ。

### WSL環境でのコマンド
```bash
# Build the project (WSL上から実行)
powershell.exe -Command "dotnet build"

# Run the application (WSL上から実行)
powershell.exe -Command "dotnet run"

# Clean build artifacts (WSL上から実行)
powershell.exe -Command "dotnet clean"

# Publish for deployment (WSL上から実行)
powershell.exe -Command "dotnet publish -c Release"

# Restore NuGet packages
powershell.exe -Command "dotnet restore"

# Build specific configuration
powershell.exe -Command "dotnet build -c Debug"  # Debug build
powershell.exe -Command "dotnet build -c Release"  # Release build
```

### Windows環境でのコマンド
```bash
# Build the project
dotnet build

# Run the application
dotnet run

# Clean build artifacts
dotnet clean

# Publish for deployment
dotnet publish
```

## Architecture Overview

CocoroDock is a WPF application serving as the control panel and chat interface for the CocoroAI desktop mascot system. It implements:

1. **WebSocket Communication**: The application runs a WebSocket server on port 55600 (configurable) that handles real-time communication between components. Messages are JSON-formatted with types like "Chat", "SystemMessage", "Config", "Status", and "Control". All messages are Base64 encoded for transport.

2. **Single Instance Enforcement**: Uses named pipes ("CocoroAIPipe") to ensure only one instance runs. New instances send their startup arguments to the existing instance.

3. **System Tray Integration**: Runs primarily in the system tray with a notification icon. The main window can be shown/hidden via tray interactions. Window close action minimizes to tray instead of terminating the application.

4. **Character Management**: Supports multiple virtual characters with settings stored in JSON format. Each character has properties like voice settings (TTS), speech recognition (STT), LLM integration, memory features, and visual appearance.

5. **Inter-Process Communication**: 
   - CocoroCore (port 55601) - LLM処理
   - CocoroMemory (port 55602) - 記憶管理
   - CocoroMemoryDB (port 55603) - データベース
   - CocoroShell - UI表示

## External Process Management

CocoroDockは以下の外部プロセスを管理します：

- **CocoroShell.exe**: キャラクター表示用のシェル（常時起動）
- **CocoroCore.exe**: LLM処理用（キャラクターのisUseLLMがtrueの場合のみ起動）
- **CocoroMemory.exe**: 記憶機能用（キャラクターのisEnableMemoryがtrueの場合のみ起動）

これらのプロセスは：
- DEBUGビルドでは起動されません
- アプリケーション起動時に自動的に起動/再起動されます
- アプリケーション終了時に自動的に終了されます
- 条件に応じて動的に起動/終了が制御されます

## Key Architectural Patterns

- **MVVM Pattern**: Views (XAML) bind to ViewModels through WPF data binding
- **Service Layer**: ICommunicationService abstracts WebSocket operations
- **Settings Management**: IAppSettings interface with JSON persistence
- **Event-Driven**: Uses C# events for WebSocket message handling and UI updates

## Important Implementation Details

- The main entry point checks for existing instances before starting
- WebSocket server automatically restarts on connection failures (3-second interval)
- UI updates must be dispatched to the main thread when handling WebSocket messages
- Character settings are loaded from defaultSetting.json and can be customized per character
- The application supports both Japanese and potentially other languages through resource management
- Assembly name is "CocoroAI" (different from project name "CocoroDock")
- Uses both WPF and Windows Forms libraries for UI components
- Message encoding: All WebSocket messages use Base64 encoding for transport
- Process termination uses taskkill for graceful shutdown attempts

## Configuration Structure

The application uses a comprehensive JSON configuration with the following key sections:

- **Port Configuration**: Separate ports for each component (55600-55603)
- **Window Settings**: Topmost display, escape cursor, virtual key bindings, auto-move
- **Graphics Settings**: MSAA level, ambient occlusion, shadow settings
- **Character List**: Array of character configurations, each containing:
  - Model information (name, VRM file path)
  - LLM settings (API key, model, system prompt)
  - TTS settings (endpoint URL, speaker ID)
  - STT settings (wake word, API key)
  - Memory settings (user ID, embedding model)
  - Read-only flag for default characters

## Message Types and Communication

WebSocket messages follow a standard format with Base64 encoding:

- **Chat**: User and AI conversation messages
- **SystemMessage**: System notifications with severity levels (Info, Error)
- **Config**: Configuration requests and updates
- **Status**: Component status updates
- **Control**: System control commands (e.g., shutdownCocoroAI)

## Development Tips

- **デバッグ時の注意**: DEBUGビルドではCocoroShellが起動しないため、UI表示のテストには注意
- **プロセス管理**: 外部プロセスの起動/終了はProcessHelper.csで一元管理
- **設定変更**: キャラクター設定の変更時は、関連するプロセスの再起動が必要な場合がある
- **WebSocket通信**: 接続エラー時は自動的に再接続を試みるが、手動での再接続も可能

## ユーザーとのコミュニケーション

ユーザーとは日本語でコミュニケーションを取ること
コメントの言語は日本語にすること
