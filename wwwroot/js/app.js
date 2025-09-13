/**
 * CocoroAI Mobile アプリケーション
 */
class CocoroAIApp {
    constructor() {
        this.elements = {};
        this.isLoading = false;
        
        this.initializeElements();
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
            loading: document.getElementById('loading')
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
        this.elements.loading.classList.remove('hidden');
        this.updateSendButton();
    }

    /**
     * ローディング非表示
     */
    hideLoading() {
        this.isLoading = false;
        this.elements.loading.classList.add('hidden');
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
}

// DOM読み込み完了後にアプリケーション開始
document.addEventListener('DOMContentLoaded', () => {
    window.app = new CocoroAIApp();
});