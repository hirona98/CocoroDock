# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Development Commands

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

1. **WebSocket Communication**: The application runs a WebSocket server on port 55600 (configurable) that handles real-time communication between components. Messages are JSON-formatted with types like "Chat", "SystemMessage", etc.

2. **Single Instance Enforcement**: Uses named pipes ("CocoroAIPipe") to ensure only one instance runs. New instances send their startup arguments to the existing instance.

3. **System Tray Integration**: Runs primarily in the system tray with a notification icon. The main window can be shown/hidden via tray interactions.

4. **Character Management**: Supports multiple virtual characters with settings stored in JSON format. Each character has properties like voice settings, personality, and visual appearance.

5. **Inter-Process Communication**: Communicates with CocoroCore (port 55601) and potentially other components in the CocoroAI ecosystem.

## Key Architectural Patterns

- **MVVM Pattern**: Views (XAML) bind to ViewModels through WPF data binding
- **Service Layer**: ICommunicationService abstracts WebSocket operations
- **Settings Management**: IAppSettings interface with JSON persistence
- **Event-Driven**: Uses C# events for WebSocket message handling and UI updates

## Important Implementation Details

- The main entry point checks for existing instances before starting
- WebSocket server automatically restarts on connection failures
- UI updates must be dispatched to the main thread when handling WebSocket messages
- Character settings are loaded from defaultSetting.json and can be customized per character
- The application supports both Japanese and potentially other languages through resource management

## ユーザーとのコミュニケーション

ユーザーとは日本語でコミュニケーションを取ること
コメントの言語は日本語にすること
