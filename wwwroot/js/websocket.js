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
                // エラーメッセージの場合は詳細を出力
                if (message.type === 'error') {
                    console.error('[WebSocket] エラーメッセージ受信:', {
                        code: message.data?.code,
                        message: message.data?.message,
                        details: message.data
                    });
                }

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
     * 音声メッセージ送信 (RNNoise統合版)
     */
    sendVoiceMessage(voiceMessage) {
        if (!this.isConnected || !this.ws) {
            console.error('[WebSocket] 音声データ送信: 未接続');
            throw new Error('WebSocket未接続');
        }

        // 音声データサイズチェック（Base64とList<int>の両方に対応）
        const audioData = voiceMessage.data.audio_data;
        const audioDataBase64 = voiceMessage.data.audio_data_base64;

        if (!audioData && !audioDataBase64) {
            throw new Error('音声データが空です');
        }

        if (audioData && audioData.length === 0) {
            throw new Error('音声データが空です');
        }

        if (audioDataBase64 && audioDataBase64.length === 0) {
            throw new Error('音声データが空です');
        }

        // サイズ制限チェック (10MB)
        const maxSize = 10 * 1024 * 1024;
        const estimatedSize = JSON.stringify(voiceMessage).length;
        if (estimatedSize > maxSize) {
            throw new Error(`音声データが大きすぎます: ${estimatedSize}bytes (上限: ${maxSize}bytes)`);
        }

        // 音声データのバイト数を計算
        let actualBytes;
        if (audioDataBase64) {
            // Base64の場合は約1.33倍なので元のサイズを推定
            actualBytes = Math.floor(audioDataBase64.length * 0.75);
        } else {
            actualBytes = audioData.length;
        }

        try {
            // 音声データ送信
            const jsonString = JSON.stringify(voiceMessage);
            this.ws.send(jsonString);

            // 統計情報記録（実際のバイト数を使用）
            this.recordVoiceStats(actualBytes, voiceMessage.data.format);

        } catch (error) {
            console.error('[WebSocket] 音声データ送信エラー:', error);

            // より詳細なエラー情報
            if (error.name === 'InvalidStateError') {
                throw new Error('WebSocket接続状態エラー');
            } else if (error.name === 'SyntaxError') {
                throw new Error('音声データ形式エラー');
            } else {
                throw new Error(`音声送信失敗: ${error.message}`);
            }
        }
    }

    /**
     * 画像メッセージ送信
     */
    sendImageMessage(imageMessage) {
        if (!this.isConnected || !this.ws) {
            console.error('[WebSocket] 画像データ送信: 未接続');
            throw new Error('WebSocket未接続');
        }

        // 画像データチェック
        const imageData = imageMessage.data.image_data_base64;
        if (!imageData || imageData.length === 0) {
            throw new Error('画像データが空です');
        }

        // サイズ制限チェック (10MB)
        const maxSize = 10 * 1024 * 1024;
        const estimatedSize = JSON.stringify(imageMessage).length;
        if (estimatedSize > maxSize) {
            throw new Error(`画像データが大きすぎます: ${estimatedSize}bytes (上限: ${maxSize}bytes)`);
        }

        // Base64データサイズを計算（約1.33倍なので元のサイズを推定）
        const actualBytes = Math.floor(imageData.length * 0.75);

        try {
            // 画像データ送信
            const jsonString = JSON.stringify(imageMessage);
            this.ws.send(jsonString);

            console.log('[WebSocket] 画像データ送信成功:', {
                size: actualBytes,
                format: imageMessage.data.format,
                width: imageMessage.data.width,
                height: imageMessage.data.height
            });

            // 統計情報記録
            this.recordImageStats(actualBytes, imageMessage.data.format);

        } catch (error) {
            console.error('[WebSocket] 画像データ送信エラー:', error);

            // より詳細なエラー情報
            if (error.name === 'InvalidStateError') {
                throw new Error('WebSocket接続状態エラー');
            } else if (error.name === 'SyntaxError') {
                throw new Error('画像データ形式エラー');
            } else {
                throw new Error(`画像送信失敗: ${error.message}`);
            }
        }
    }

    /**
     * 音声統計情報記録
     */
    recordVoiceStats(dataSize, format) {
        if (!this.voiceStats) {
            this.voiceStats = {
                totalMessages: 0,
                totalBytes: 0,
                formats: {},
                lastSent: null
            };
        }

        this.voiceStats.totalMessages++;
        this.voiceStats.totalBytes += dataSize;
        this.voiceStats.formats[format] = (this.voiceStats.formats[format] || 0) + 1;
        this.voiceStats.lastSent = new Date().toISOString();
    }

    /**
     * 画像統計情報記録
     */
    recordImageStats(dataSize, format) {
        if (!this.imageStats) {
            this.imageStats = {
                totalMessages: 0,
                totalBytes: 0,
                formats: {},
                lastSent: null
            };
        }

        this.imageStats.totalMessages++;
        this.imageStats.totalBytes += dataSize;
        this.imageStats.formats[format] = (this.imageStats.formats[format] || 0) + 1;
        this.imageStats.lastSent = new Date().toISOString();
    }

    /**
     * 音声統計情報取得
     */
    getVoiceStats() {
        return this.voiceStats || {
            totalMessages: 0,
            totalBytes: 0,
            formats: {},
            lastSent: null
        };
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