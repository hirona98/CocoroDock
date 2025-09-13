/**
 * WebSocket通信管理クラス
 */
class WebSocketManager {
    constructor() {
        this.ws = null;
        this.isConnected = false;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 3;
        this.reconnectDelay = 2000;
        this.messageHandlers = new Map();
        
        // イベントリスナー
        this.onOpen = null;
        this.onClose = null;
        this.onError = null;
        this.onMessage = null;
    }

    /**
     * WebSocket接続を開始
     */
    connect() {
        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const host = window.location.hostname;
        const port = window.location.port || (window.location.protocol === 'https:' ? '443' : '80');
        
        const wsPort = window.location.port || '80';
        const wsUrl = `${protocol}//${host}:${wsPort}/mobile`;
        
        console.log(`[WebSocket] 接続試行: ${wsUrl}`);
        
        try {
            this.ws = new WebSocket(wsUrl);
            this.setupEventHandlers();
        } catch (error) {
            console.error('[WebSocket] 接続エラー:', error);
            this.handleConnectionError();
        }
    }

    /**
     * WebSocketイベントハンドラー設定
     */
    setupEventHandlers() {
        this.ws.onopen = (event) => {
            console.log('[WebSocket] 接続成功');
            this.isConnected = true;
            this.reconnectAttempts = 0;
            
            if (this.onOpen) {
                this.onOpen(event);
            }
        };

        this.ws.onclose = (event) => {
            console.log('[WebSocket] 接続切断:', event.code, event.reason);
            this.isConnected = false;
            
            if (this.onClose) {
                this.onClose(event);
            }
            
            // 再接続試行
            this.handleReconnect();
        };

        this.ws.onerror = (event) => {
            console.error('[WebSocket] エラー:', event);
            
            if (this.onError) {
                this.onError(event);
            }
        };

        this.ws.onmessage = (event) => {
            try {
                const message = JSON.parse(event.data);
                console.log('[WebSocket] メッセージ受信:', message);
                
                this.handleMessage(message);
                
                if (this.onMessage) {
                    this.onMessage(message);
                }
            } catch (error) {
                console.error('[WebSocket] メッセージ解析エラー:', error);
            }
        };
    }

    /**
     * メッセージ処理
     */
    handleMessage(message) {
        const { type } = message;
        
        if (this.messageHandlers.has(type)) {
            const handler = this.messageHandlers.get(type);
            handler(message);
        }
    }

    /**
     * メッセージタイプ別ハンドラー登録
     */
    addMessageHandler(type, handler) {
        this.messageHandlers.set(type, handler);
    }

    /**
     * チャットメッセージ送信
     */
    sendChatMessage(message) {
        if (!this.isConnected || !this.ws) {
            console.error('[WebSocket] 未接続のため送信できません');
            throw new Error('WebSocket未接続');
        }

        const chatMessage = {
            type: 'chat',
            timestamp: new Date().toISOString(),
            data: {
                message: message,
                chat_type: 'text'
            }
        };

        console.log('[WebSocket] メッセージ送信:', chatMessage);
        
        try {
            this.ws.send(JSON.stringify(chatMessage));
        } catch (error) {
            console.error('[WebSocket] 送信エラー:', error);
            throw error;
        }
    }

    /**
     * 接続エラー処理
     */
    handleConnectionError() {
        this.isConnected = false;
        
        if (this.onError) {
            this.onError(new Error('接続に失敗しました'));
        }
        
        this.handleReconnect();
    }

    /**
     * 再接続処理
     */
    handleReconnect() {
        if (this.reconnectAttempts >= this.maxReconnectAttempts) {
            console.log('[WebSocket] 再接続試行回数が上限に達しました');
            return;
        }

        this.reconnectAttempts++;
        console.log(`[WebSocket] ${this.reconnectDelay}ms後に再接続試行 (${this.reconnectAttempts}/${this.maxReconnectAttempts})`);
        
        setTimeout(() => {
            if (!this.isConnected) {
                this.connect();
            }
        }, this.reconnectDelay);
        
        // 再接続間隔を少しずつ延ばす
        this.reconnectDelay = Math.min(this.reconnectDelay * 1.5, 10000);
    }

    /**
     * 接続を閉じる
     */
    disconnect() {
        if (this.ws) {
            this.ws.close();
            this.ws = null;
        }
        this.isConnected = false;
    }

    /**
     * 接続状態確認
     */
    getConnectionState() {
        if (!this.ws) return 'disconnected';
        
        switch (this.ws.readyState) {
            case WebSocket.CONNECTING:
                return 'connecting';
            case WebSocket.OPEN:
                return 'connected';
            case WebSocket.CLOSING:
                return 'closing';
            case WebSocket.CLOSED:
                return 'disconnected';
            default:
                return 'unknown';
        }
    }
}

// グローバルインスタンス
window.wsManager = new WebSocketManager();