/**
 * CocoroAI Mobile アプリケーション (RNNoise専用版)
 */
class CocoroAIApp {
    constructor() {
        this.elements = {};
        this.isLoading = false;

        // RNNoise音声システム
        this.voiceSystem = null;
        this.isVoiceEnabled = false;

        // 初期化
        this.initializeElements();
        this.setupWebSocket();
        this.setupEventListeners();
        this.initialize();
    }

    /**
     * DOM要素の初期化
     */
    initializeElements() {
        this.elements = {
            connectionStatus: document.getElementById('connection-status'),
            messages: document.getElementById('messages'),
            messageInput: document.getElementById('message-input'),
            sendButton: document.getElementById('send-button'),
            errorOverlay: document.getElementById('error-overlay'),
            errorMessage: document.getElementById('error-message'),
            errorClose: document.getElementById('error-close'),
            loading: document.getElementById('loading'),
            voiceButton: document.getElementById('voice-button'),
            micIcon: document.getElementById('mic-icon'),
            muteLine: document.getElementById('mute-line')
        };
    }

    /**
     * アプリケーション初期化
     */
    async initialize() {
        this.updateSendButton();
        this.connectToServer();
        this.setupViewportHandler();

        // RNNoise音声システム初期化
        await this.initializeRNNoiseSystem();

        // 音声ボタンの初期状態をOFFに設定
        this.updateVoiceButton('inactive');
    }

    /**
     * RNNoise音声システム初期化
     */
    async initializeRNNoiseSystem() {
        this.log('RNNoise音声システム初期化開始...');

        try {
            // VoiceRecorderWorkletを初期化
            this.voiceSystem = new VoiceRecorderWorklet();

            // 音声検出イベント設定
            this.voiceSystem.onVoiceDetected = (wavData) => {
                this.handleRNNoiseVoice(wavData);
            };

            this.voiceSystem.onError = (error) => {
                this.logError('RNNoise音声システムエラー:', error);
                this.showError('音声システムエラー: ' + error.message);
            };

            this.log('RNNoise音声システム初期化完了');
            return true;

        } catch (error) {
            this.logError('RNNoise初期化失敗:', error);
            this.showError('音声システムの初期化に失敗しました');

            // 音声ボタンを無効化
            if (this.elements.voiceButton) {
                this.elements.voiceButton.style.display = 'none';
            }
            return false;
        }
    }

    /**
     * RNNoise音声システム初期化
     */
    async initializeRNNoiseSystem() {
        // RNNoiseの必要なファイルが存在するかチェック
        if (typeof RNNoiseProcessor === 'undefined' ||
            typeof VoiceRecorderWorklet === 'undefined') {
            throw new Error('RNNoise関連クラスが読み込まれていません');
        }

        // AudioWorkletサポート確認
        if (!window.AudioContext || !window.AudioWorkletNode) {
            throw new Error('AudioWorklet未サポート');
        }

        try {
            this.voiceSystem = new VoiceRecorderWorklet();

            // イベントハンドラー設定
            this.voiceSystem.onVoiceData = (wavData) => {
                this.handleVoiceData(wavData);
            };

            this.voiceSystem.onAudioLevel = (level, isSpeech, vadProb) => {
                this.updateVoiceVisualization(level, isSpeech, vadProb);
            };

            this.voiceSystem.onVoiceDetected = () => {
                this.log('🎤 音声開始');
                this.updateVoiceButton('listening');
            };

            this.voiceSystem.onVoiceEnded = (audioFrames) => {
                this.log(`🔊 音声終了: ${audioFrames.length}フレーム`);
                this.updateVoiceButton('active');
            };

            this.voiceSystem.onError = (error) => {
                this.logError('RNNoise エラー:', error);
                this.showError(`音声処理エラー: ${error.message}`);
            };

            this.voiceSystem.onInitialized = () => {
                this.log('✅ RNNoise音声システム準備完了');
            };

            // 初期化実行
            const success = await this.voiceSystem.initialize();
            if (!success) {
                throw new Error('VoiceRecorderWorklet初期化失敗');
            }

            this.log('RNNoise音声システム初期化完了');
            return true;

        } catch (error) {
            this.logError('RNNoise初期化失敗:', error);
            this.showError('音声システムの初期化に失敗しました');

            if (this.voiceSystem) {
                await this.voiceSystem.destroy();
                this.voiceSystem = null;
            }

            // 音声ボタンを無効化
            if (this.elements.voiceButton) {
                this.elements.voiceButton.style.display = 'none';
            }
            return false;
        }
    }


    /**
     * RNNoise音声認識トグル
     */
    async toggleVoiceRecognition() {
        if (!this.voiceSystem) {
            this.showError('音声システムが利用できません');
            return;
        }

        this.isVoiceEnabled = !this.isVoiceEnabled;

        if (this.isVoiceEnabled) {
            await this.startVoiceRecognition();
        } else {
            await this.stopVoiceRecognition();
        }
    }

    /**
     * RNNoise音声認識開始
     */
    async startVoiceRecognition() {
        try {
            await this.voiceSystem.startRecording();
            this.updateVoiceButton('active');
            this.log('🎤 RNNoise録音開始');

        } catch (error) {
            this.logError('音声認識開始エラー:', error);
            this.showError(`音声認識開始失敗: ${error.message}`);
            this.isVoiceEnabled = false;
            this.updateVoiceButton('inactive');
        }
    }

    /**
     * RNNoise音声認識停止
     */
    async stopVoiceRecognition() {
        try {
            await this.voiceSystem.stopRecording();
            this.updateVoiceButton('inactive');
            this.log('🛑 RNNoise録音停止');

        } catch (error) {
            this.logError('音声認識停止エラー:', error);
        }
    }

    /**
     * RNNoise 音声データ処理
     */
    async handleVoiceData(wavData) {
        try {
            this.log(`音声データ受信: ${wavData.length}bytes`);

            // WebSocketで音声データ送信
            const voiceMessage = {
                type: 'voice',
                timestamp: new Date().toISOString(),
                data: {
                    audio_data: Array.from(wavData),
                    sample_rate: 16000, // RNNoiseプロセッサーが16kHzに変換済み
                    channels: 1,
                    format: 'wav',
                    processing: 'rnnoise'
                }
            };

            if (window.wsManager && window.wsManager.isConnected) {
                window.wsManager.sendVoiceMessage(voiceMessage);
                this.log('音声データ送信完了');
            } else {
                this.logError('WebSocket未接続のため音声データ送信失敗');
            }

        } catch (error) {
            this.logError('音声データ処理エラー:', error);
        }
    }

    /**
     * 音声可視化更新（RNNoise用）
     */
    updateVoiceVisualization(level, isSpeech, vadProbability) {
        // 音声レベル表示が必要な場合の処理
        // 現在のUIには音声レベル表示要素がないため、必要に応じて追加
        this.log(`音声レベル: ${(level * 100).toFixed(1)}%, VAD: ${(vadProbability * 100).toFixed(1)}%, Speech: ${isSpeech}`);
    }

    /**
     * マイク権限要求
     */
    async requestMicrophonePermission() {
        try {
            if (navigator.mediaDevices && navigator.mediaDevices.getUserMedia) {
                const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
                stream.getTracks().forEach(track => track.stop());
                return true;
            }
            return false;
        } catch (error) {
            this.log('マイク権限エラー:', error);
            return false;
        }
    }

    /**
     * ログ出力
     */
    log(message) {
        console.log(`[CocoroAI] ${new Date().toISOString()}: ${message}`);
    }

    logError(message, error = null) {
        const errorMessage = error ? `${message} ${error.message}` : message;
        console.error(`[CocoroAI ERROR] ${new Date().toISOString()}: ${errorMessage}`);
    }

    // ==== 以下、既存のメソッドを継承 ====

    /**
     * イベントリスナーの設定
     */
    setupEventListeners() {
        // 送信ボタン
        this.elements.sendButton.addEventListener('click', () => {
            this.sendMessage();
        });

        // 音声ボタン
        if (this.elements.voiceButton) {
            this.elements.voiceButton.addEventListener('click', () => {
                this.toggleVoiceRecognition();
            });
        }

        // Enter キーで送信
        this.elements.messageInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                this.sendMessage();
            }
        });

        // 入力欄の変更で送信ボタンの状態更新
        this.elements.messageInput.addEventListener('input', () => {
            this.updateSendButton();
        });

        // エラー閉じる
        this.elements.errorClose.addEventListener('click', () => {
            this.hideError();
        });

        // エラーオーバーレイクリックで閉じる
        this.elements.errorOverlay.addEventListener('click', (e) => {
            if (e.target === this.elements.errorOverlay) {
                this.hideError();
            }
        });
    }

    /**
     * WebSocket設定
     */
    setupWebSocket() {
        // 接続イベント
        window.wsManager.onOpen = () => {
            this.updateConnectionStatus('connected');
            this.clearSystemMessage();
            this.addSystemMessage('CocoroAIに接続しました');
        };

        // 切断イベント
        window.wsManager.onClose = () => {
            this.updateConnectionStatus('disconnected');
            this.addSystemMessage('接続が切断されました');
        };

        // エラーイベント
        window.wsManager.onError = (error) => {
            this.updateConnectionStatus('disconnected');
            this.showError('接続エラーが発生しました');
        };

        // メッセージハンドラー
        window.wsManager.addMessageHandler('response', (message) => {
            this.handleResponse(message);
        });

        window.wsManager.addMessageHandler('error', (message) => {
            this.handleError(message);
        });
    }

    /**
     * モバイルビューポート調整
     */
    setupViewportHandler() {
        // VirtualKeyboard API 設定（Chrome Android対応）
        if ('virtualKeyboard' in navigator) {
            try {
                navigator.virtualKeyboard.overlaysContent = true;
                console.log('VirtualKeyboard API enabled');
            } catch (error) {
                console.log('VirtualKeyboard API setup failed:', error);
            }
        }

        // 基本的なビューポート高さ設定
        const setViewportHeight = () => {
            const vh = window.innerHeight * 0.01;
            document.documentElement.style.setProperty('--vh', `${vh}px`);
        };

        // 初期設定
        setViewportHeight();

        // リサイズ時の調整（デバウンス処理）
        let resizeTimeout;
        window.addEventListener('resize', () => {
            clearTimeout(resizeTimeout);
            resizeTimeout = setTimeout(setViewportHeight, 100);
        });

        // オリエンテーション変更時の調整
        window.addEventListener('orientationchange', () => {
            setTimeout(setViewportHeight, 500);
        });

        // 入力フォーカス時のスクロール調整
        this.elements.messageInput.addEventListener('focus', () => {
            setTimeout(() => {
                this.scrollToBottom();
            }, 300);
        });
    }

    /**
     * サーバーに接続
     */
    connectToServer() {
        this.updateConnectionStatus('connecting');
        window.wsManager.connect();
    }

    /**
     * メッセージ送信
     */
    sendMessage() {
        const message = this.elements.messageInput.value.trim();

        if (!message || this.isLoading) {
            return;
        }

        try {
            // ユーザーメッセージを表示
            this.addUserMessage(message);

            // 入力欄をクリア
            this.elements.messageInput.value = '';
            this.updateSendButton();

            // ローディング開始
            this.showLoading();

            // WebSocketで送信
            window.wsManager.sendChatMessage(message);

        } catch (error) {
            console.error('メッセージ送信エラー:', error);
            this.hideLoading();
            this.showError('メッセージの送信に失敗しました');
        }
    }

    /**
     * レスポンス処理
     */
    handleResponse(message) {
        this.hideLoading();

        if (message.data && message.data.text) {
            this.addAIMessage(message.data.text);

            // 音声再生機能（VOICEVOX統合）
            const audioUrl = message.data.audio_url || message.data.audioUrl || message.data.AudioUrl;
            if (audioUrl) {
                try {
                    console.log('音声再生開始:', audioUrl);
                    const audio = new Audio(audioUrl);
                    audio.play().catch(error => {
                        console.warn('音声再生エラー:', error);
                        // 音声再生に失敗してもアプリケーションは継続
                    });
                } catch (error) {
                    console.warn('音声オブジェクト作成エラー:', error);
                    // エラーが発生してもアプリケーションは継続
                }
            }
        }
    }

    /**
     * エラー処理
     */
    handleError(message) {
        this.hideLoading();

        const errorText = message.data?.message || 'エラーが発生しました';
        this.showError(errorText);
    }

    /**
     * ユーザーメッセージ追加
     */
    addUserMessage(text) {
        const messageDiv = this.createMessageElement('user', text);
        this.elements.messages.appendChild(messageDiv);
        this.scrollToBottom();
    }

    /**
     * AIメッセージ追加
     */
    addAIMessage(text) {
        const messageDiv = this.createMessageElement('ai', text);
        this.elements.messages.appendChild(messageDiv);
        this.scrollToBottom();
    }

    /**
     * システムメッセージ追加
     */
    addSystemMessage(text) {
        const messageDiv = this.createMessageElement('system', text);
        this.elements.messages.appendChild(messageDiv);
        this.scrollToBottom();
    }

    /**
     * システムメッセージクリア
     */
    clearSystemMessage() {
        const systemMessages = this.elements.messages.querySelectorAll('.message.system');
        systemMessages.forEach(msg => msg.remove());
    }

    /**
     * メッセージ要素作成
     */
    createMessageElement(type, text) {
        const messageDiv = document.createElement('div');
        messageDiv.className = `message ${type}`;

        const contentDiv = document.createElement('div');
        contentDiv.className = 'message-content';
        contentDiv.textContent = text;

        const timeDiv = document.createElement('div');
        timeDiv.className = 'message-time';
        timeDiv.textContent = this.formatTime(new Date());

        messageDiv.appendChild(contentDiv);
        if (type !== 'system') {
            messageDiv.appendChild(timeDiv);
        }

        return messageDiv;
    }

    /**
     * 時刻フォーマット
     */
    formatTime(date) {
        return date.toLocaleTimeString('ja-JP', {
            hour: '2-digit',
            minute: '2-digit'
        });
    }

    /**
     * 最下部にスクロール
     */
    scrollToBottom() {
        this.elements.messages.scrollTop = this.elements.messages.scrollHeight;
    }

    /**
     * 接続状態更新
     */
    updateConnectionStatus(status) {
        const statusElement = this.elements.connectionStatus;
        statusElement.className = `status ${status}`;

        switch (status) {
            case 'connecting':
                statusElement.textContent = '接続中...';
                break;
            case 'connected':
                statusElement.textContent = '接続済み';
                break;
            case 'disconnected':
                statusElement.textContent = '切断';
                break;
            default:
                statusElement.textContent = '不明';
        }

        this.updateSendButton();
    }

    /**
     * 送信ボタン状態更新
     */
    updateSendButton() {
        const hasMessage = this.elements.messageInput.value.trim().length > 0;
        const isConnected = window.wsManager.isConnected;

        this.elements.sendButton.disabled = !hasMessage || !isConnected || this.isLoading;
    }

    /**
     * ローディング表示
     */
    showLoading() {
        this.isLoading = true;
        this.addSystemMessage('処理中...');
        this.updateSendButton();
    }

    /**
     * ローディング非表示
     */
    hideLoading() {
        this.isLoading = false;
        this.clearSystemMessage();
        this.updateSendButton();
    }

    /**
     * エラー表示
     */
    showError(message) {
        this.elements.errorMessage.textContent = message;
        this.elements.errorOverlay.classList.remove('hidden');
    }

    /**
     * エラー非表示
     */
    hideError() {
        this.elements.errorOverlay.classList.add('hidden');
    }

    /**
     * 音声ボタン状態更新
     */
    updateVoiceButton(state) {
        if (!this.elements.voiceButton) return;

        const button = this.elements.voiceButton;
        const muteLine = this.elements.muteLine;

        // すべてのクラスをリセット
        button.classList.remove('active', 'listening', 'disabled');

        switch (state) {
            case 'active':
                button.classList.add('active');
                if (muteLine) muteLine.style.display = 'none';
                break;
            case 'listening':
                button.classList.add('listening');
                if (muteLine) muteLine.style.display = 'none';
                break;
            case 'inactive':
                if (muteLine) muteLine.style.display = 'block';
                break;
            case 'disabled':
                button.classList.add('disabled');
                if (muteLine) muteLine.style.display = 'block';
                break;
        }
    }

    /**
     * リソース解放
     */
    async destroy() {
        this.log('アプリケーション終了処理...');

        // 音声認識停止
        await this.stopVoiceRecognition();

        // RNNoise システム解放
        if (this.voiceSystem) {
            await this.voiceSystem.destroy();
            this.voiceSystem = null;
        }

        this.log('アプリケーション終了完了');
    }
}

// DOM読み込み完了後にアプリケーション開始
document.addEventListener('DOMContentLoaded', () => {
    window.app = new CocoroAIApp();
});

// ページ離脱時のクリーンアップ
window.addEventListener('beforeunload', async () => {
    if (window.app) {
        await window.app.destroy();
    }
});

// グローバルエクスポート
if (typeof window !== 'undefined') {
    window.CocoroAIApp = CocoroAIApp;
}