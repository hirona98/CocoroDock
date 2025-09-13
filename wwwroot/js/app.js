/**
 * CocoroAI Mobile アプリケーション
 */
class CocoroAIApp {
    constructor() {
        this.elements = {};
        this.isLoading = false;
        this.voiceRecognition = null;

        this.initializeElements();
        this.setupVoiceRecognition();
        this.setupEventListeners();
        this.setupWebSocket();
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
     * 音声認識設定
     */
    setupVoiceRecognition() {
        // Web Speech APIサポート確認
        if (!('webkitSpeechRecognition' in window) && !('SpeechRecognition' in window)) {
            console.warn('Web Speech API is not supported');
            return;
        }

        const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;

        try {
            const recognition = new SpeechRecognition();

            // 認識設定（根本修正）
            recognition.continuous = true;
            recognition.interimResults = true;
            recognition.lang = 'ja-JP';
            recognition.maxAlternatives = 1;
            recognition.serviceURI = undefined; // デフォルトサービスを使用

            this.voiceRecognition = {
                recognition: recognition,
                isListening: false,
                isEnabled: false,
                silenceTimer: null,
                finalTranscript: ''
            };

        } catch (error) {
            console.error('Web Speech API初期化エラー:', error);
            return;
        }

        // イベントハンドラー
        this.voiceRecognition.recognition.onstart = () => {
            this.updateVoiceButton('listening');
        };

        this.voiceRecognition.recognition.onresult = (event) => {
            this.handleVoiceResult(event);
        };

        this.voiceRecognition.recognition.onend = () => {
            this.voiceRecognition.isListening = false;

            if (this.voiceRecognition.silenceTimer) {
                clearTimeout(this.voiceRecognition.silenceTimer);
                this.voiceRecognition.silenceTimer = null;
            }

            // マイクON状態であれば自動再開
            if (this.voiceRecognition.isEnabled) {
                setTimeout(() => {
                    this.startVoiceRecognition();
                }, 100);
            }
        };

        this.voiceRecognition.recognition.onerror = (event) => {
            // モバイルデバッグ用（一時的）
            console.log('音声認識エラー:', event.error);
            if (event.error === 'not-allowed' || event.error === 'service-not-allowed') {
                this.addSystemMessage('マイク権限が必要です');
            }
            this.stopVoiceRecognition();
        };
    }

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
     * アプリケーション初期化
     */
    initialize() {
        this.updateSendButton();
        this.connectToServer();
        this.setupViewportHandler();
        // 音声ボタンの初期状態をOFFに設定
        if (this.voiceRecognition) {
            this.updateVoiceButton('inactive');
        }
    }

    /**
     * モバイルビューポート調整
     */
    setupViewportHandler() {
        // VirtualKeyboard API 設定（Chrome Android対応）
        if ('virtualKeyboard' in navigator) {
            try {
                // キーボードがコンテンツをオーバーレイするように設定
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
     * 音声認識トグル
     */
    async toggleVoiceRecognition() {
        if (!this.voiceRecognition) {
            return;
        }

        this.voiceRecognition.isEnabled = !this.voiceRecognition.isEnabled;

        if (this.voiceRecognition.isEnabled) {
            // モバイルでは事前にマイク権限を要求
            const hasPermission = await this.requestMicrophonePermission();
            if (hasPermission) {
                this.updateVoiceButton('active');
                this.startVoiceRecognition();
            } else {
                this.voiceRecognition.isEnabled = false;
                this.addSystemMessage('マイク権限を許可してください');
            }
        } else {
            this.updateVoiceButton('inactive');
            this.stopVoiceRecognition();
        }
    }

    /**
     * 音声認識開始
     */
    startVoiceRecognition() {
        if (!this.voiceRecognition || this.voiceRecognition.isListening || !this.voiceRecognition.isEnabled) {
            return;
        }

        try {
            this.voiceRecognition.isListening = true;
            this.voiceRecognition.recognition.start();
        } catch (error) {
            console.error('音声認識開始エラー:', error);
            this.voiceRecognition.isListening = false;
        }
    }

    /**
     * 音声認識停止
     */
    stopVoiceRecognition() {
        if (!this.voiceRecognition || !this.voiceRecognition.isListening) {
            return;
        }

        try {
            this.voiceRecognition.recognition.stop();
        } catch (error) {
            console.error('音声認識停止エラー:', error);
        }

        if (this.voiceRecognition.silenceTimer) {
            clearTimeout(this.voiceRecognition.silenceTimer);
            this.voiceRecognition.silenceTimer = null;
        }
    }

    /**
     * 音声認識結果処理
     */
    handleVoiceResult(event) {
        let interimTranscript = '';
        let finalTranscript = this.voiceRecognition.finalTranscript;
        let hasFinalResult = false;

        for (let i = event.resultIndex; i < event.results.length; i++) {
            const result = event.results[i];
            const isFinal = result.isFinal;

            if (result.length > 0) {
                const transcript = result[0].transcript;

                if (isFinal) {
                    finalTranscript += transcript;
                    hasFinalResult = true;
                } else {
                    interimTranscript += transcript;
                }
            }
        }

        // 入力欄更新
        this.elements.messageInput.value = finalTranscript + interimTranscript;
        this.updateSendButton();

        // 最終結果が更新された場合、無音タイマーをリセット
        if (hasFinalResult) {
            this.voiceRecognition.finalTranscript = finalTranscript;
            this.resetSilenceTimer();
        }
    }

    /**
     * 無音タイマーリセット（1秒後自動送信）
     */
    resetSilenceTimer() {
        if (this.voiceRecognition.silenceTimer) {
            clearTimeout(this.voiceRecognition.silenceTimer);
        }

        this.voiceRecognition.silenceTimer = setTimeout(() => {
            // 音声認識結果をローカル変数に保存
            const voiceMessage = this.voiceRecognition.finalTranscript.trim();

            // メッセージがある場合のみ送信
            if (voiceMessage) {
                this.elements.messageInput.value = voiceMessage;
                this.sendMessage();

                // 送信後は新しい認識を準備
                this.voiceRecognition.finalTranscript = '';
            }
        }, 500); // 0.5秒
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
                muteLine.style.display = 'none';
                break;
            case 'listening':
                button.classList.add('listening');
                muteLine.style.display = 'none';
                break;
            case 'inactive':
                muteLine.style.display = 'block';
                break;
            case 'disabled':
                button.classList.add('disabled');
                muteLine.style.display = 'block';
                break;
        }
    }

    /**
     * マイク権限要求（モバイル対応）
     */
    async requestMicrophonePermission() {
        try {
            // MediaDevices APIでマイク権限を要求
            if (navigator.mediaDevices && navigator.mediaDevices.getUserMedia) {
                const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
                // ストリームを即座停止（権限確認のみ）
                stream.getTracks().forEach(track => track.stop());
                return true;
            }
            return false;
        } catch (error) {
            console.log('マイク権限エラー:', error);
            return false;
        }
    }
}

// DOM読み込み完了後にアプリケーション開始
document.addEventListener('DOMContentLoaded', () => {
    window.app = new CocoroAIApp();
});