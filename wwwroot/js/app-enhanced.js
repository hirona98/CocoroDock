/**
 * カメラ管理クラス
 */
class CameraManager {
    constructor() {
        this.stream = null;
        this.currentFacingMode = 'environment'; // デフォルトは背面カメラ
        this.elements = {};
        this.isInitialized = false;
        this.onImageCaptured = null; // コールバック関数
    }

    /**
     * DOM要素を初期化
     */
    initializeElements() {
        this.elements = {
            modal: document.getElementById('camera-modal'),
            preview: document.getElementById('camera-preview'),
            canvas: document.getElementById('camera-canvas'),
            captureButton: document.getElementById('camera-capture'),
            switchButton: document.getElementById('camera-switch'),
            closeButton: document.getElementById('camera-close')
        };
    }

    /**
     * カメラを初期化
     */
    async initialize() {
        this.initializeElements();
        this.setupEventListeners();
        this.isInitialized = true;
    }

    /**
     * イベントリスナーを設定
     */
    setupEventListeners() {
        if (this.elements.captureButton) {
            this.elements.captureButton.addEventListener('click', () => this.captureImage());
        }
        if (this.elements.switchButton) {
            this.elements.switchButton.addEventListener('click', () => this.switchCamera());
        }
        if (this.elements.closeButton) {
            this.elements.closeButton.addEventListener('click', () => this.closeCamera());
        }
        if (this.elements.modal) {
            this.elements.modal.addEventListener('click', (e) => {
                if (e.target === this.elements.modal) {
                    this.closeCamera();
                }
            });
        }
    }

    /**
     * カメラを開く
     */
    async openCamera() {
        try {
            // 既存のストリームがあれば停止
            if (this.stream) {
                this.stopCamera();
            }

            // カメラアクセス権限を要求
            this.stream = await navigator.mediaDevices.getUserMedia({
                video: {
                    facingMode: this.currentFacingMode,
                    width: { ideal: 640 },
                    height: { ideal: 480 }
                }
            });

            // プレビューに表示
            if (this.elements.preview) {
                this.elements.preview.srcObject = this.stream;
            }

            // モーダルを表示
            if (this.elements.modal) {
                this.elements.modal.classList.remove('hidden');
            }

            return true;
        } catch (error) {
            console.error('カメラアクセスエラー:', error);
            throw new Error(`カメラアクセスに失敗しました: ${error.message}`);
        }
    }

    /**
     * カメラを閉じる
     */
    closeCamera() {
        this.stopCamera();
        if (this.elements.modal) {
            this.elements.modal.classList.add('hidden');
        }
    }

    /**
     * カメラストリームを停止
     */
    stopCamera() {
        if (this.stream) {
            this.stream.getTracks().forEach(track => track.stop());
            this.stream = null;
        }
        if (this.elements.preview) {
            this.elements.preview.srcObject = null;
        }
    }

    /**
     * カメラを切り替え（フロント/バック）
     */
    async switchCamera() {
        try {
            // フェイシングモードを切り替え
            this.currentFacingMode = this.currentFacingMode === 'user' ? 'environment' : 'user';

            // カメラを再初期化
            if (this.stream) {
                await this.openCamera();
            }
        } catch (error) {
            console.error('カメラ切り替えエラー:', error);
            // 切り替えに失敗した場合は元に戻す
            this.currentFacingMode = this.currentFacingMode === 'user' ? 'environment' : 'user';
            throw error;
        }
    }

    /**
     * 画像をキャプチャしてBase64で返す
     */
    async captureImage() {
        try {
            if (!this.stream || !this.elements.preview || !this.elements.canvas) {
                throw new Error('カメラが初期化されていません');
            }

            // Canvasに現在のフレームを描画
            const canvas = this.elements.canvas;
            const context = canvas.getContext('2d');
            const video = this.elements.preview;

            // キャンバスサイズを動画サイズに合わせる
            canvas.width = video.videoWidth || 640;
            canvas.height = video.videoHeight || 480;

            // 動画フレームをキャンバスに描画
            context.drawImage(video, 0, 0, canvas.width, canvas.height);

            // Base64形式で画像データを取得
            const base64Image = canvas.toDataURL('image/jpeg', 0.8);

            // Base64プレフィックスを除去
            const base64Data = base64Image.split(',')[1];

            // 画像データを作成
            const imageData = {
                base64: base64Data,
                width: canvas.width,
                height: canvas.height,
                format: 'jpeg',
                facingMode: this.currentFacingMode
            };

            // コールバック関数があれば実行
            if (this.onImageCaptured) {
                this.onImageCaptured(imageData);
            }

            // カメラを閉じる
            this.closeCamera();

            return imageData;

        } catch (error) {
            console.error('画像キャプチャエラー:', error);
            throw error;
        }
    }

    /**
     * リソースを解放
     */
    destroy() {
        this.stopCamera();
        this.isInitialized = false;
    }
}

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
        this.isPlayingAudio = false; // 音声再生中フラグ

        // 音声再生キューシステム
        this.audioQueue = [];
        this.isProcessingAudioQueue = false;

        // カメラシステム
        this.cameraManager = null;

        // 撮影済み画像データ
        this.capturedImageData = null;

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
            muteLine: document.getElementById('mute-line'),
            cameraButton: document.getElementById('camera-button'),
            cameraIcon: document.getElementById('camera-icon'),
            imagePreviewArea: document.getElementById('image-preview-area'),
            previewImage: document.getElementById('preview-image'),
            removeImageButton: document.getElementById('remove-image')
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
        const initResult = await this.initializeRNNoiseSystem();

        // 音声ボタンの初期状態をOFFに設定
        this.updateVoiceButton('inactive');

        // カメラシステム初期化
        await this.initializeCameraSystem();

    }

    /**
     * カメラシステム初期化
     */
    async initializeCameraSystem() {
        try {
            this.cameraManager = new CameraManager();
            await this.cameraManager.initialize();

            // カメラ画像キャプチャ時のコールバックを設定
            this.cameraManager.onImageCaptured = (imageData) => {
                this.handleCameraImage(imageData);
            };

            // カメラボタンの初期状態を設定
            this.updateCameraButton('inactive');

            console.log('[DEBUG] カメラシステム初期化完了');
            this.log('カメラシステム初期化完了');
            return true;

        } catch (error) {
            console.error('[DEBUG] カメラ初期化失敗:', error);
            this.logError('カメラ初期化失敗:', error);

            // カメラボタンを無効化
            if (this.elements.cameraButton) {
                console.log('[DEBUG] カメラボタン無効化中...');
                this.elements.cameraButton.style.display = 'none';
            }
            return false;
        }
    }

    /**
     * RNNoise音声システム初期化
     */
    async initializeRNNoiseSystem() {

        // クラス存在確認

        // RNNoiseの必要なファイルが存在するかチェック
        if (typeof RNNoiseProcessor === 'undefined' ||
            typeof VoiceRecorderWorklet === 'undefined') {
            console.error('RNNoise関連クラスが読み込まれていません');
            throw new Error('RNNoise関連クラスが読み込まれていません');
        }

        // AudioWorkletサポート確認

        if (!window.AudioContext && !window.webkitAudioContext) {
            console.error('AudioContext未サポート');
            throw new Error('AudioContext未サポート');
        }
        if (!window.AudioWorkletNode) {
            console.error('AudioWorklet未サポート');
            throw new Error('AudioWorklet未サポート');
        }

        try {
            this.voiceSystem = new VoiceRecorderWorklet();

            // イベントハンドラー設定
            this.voiceSystem.onVoiceData = (wavData) => {
                console.log('[DEBUG] 音声データ受信:', wavData ? wavData.length : 'null');
                this.handleVoiceData(wavData);
            };

            this.voiceSystem.onAudioLevel = (level, isSpeech, vadProb) => {
                // 音声再生中は視覚的フィードバックを停止
                if (this.isPlayingAudio) {
                    return;
                }
                // console.log('[DEBUG] 音声レベル:', level, 'Speech:', isSpeech, 'VAD:', vadProb);
                this.updateVoiceVisualization(level, isSpeech, vadProb);
            };

            this.voiceSystem.onVoiceDetected = () => {
                console.log('[DEBUG] 🎤 音声開始検出');
                // 音声再生中は音声ボタン状態変更を停止
                if (this.isPlayingAudio) {
                    return;
                }
                this.updateVoiceButton('listening');
            };

            this.voiceSystem.onVoiceEnded = (audioFrames) => {
                console.log('[DEBUG] 🔊 音声終了検出:', audioFrames ? audioFrames.length : 0, 'フレーム');
                // 音声再生中は音声ボタン状態変更を停止
                if (this.isPlayingAudio) {
                    return;
                }
                this.updateVoiceButton('active');
            };

            this.voiceSystem.onError = (error) => {
                console.error('[DEBUG] RNNoise エラー:', error);
                this.logError('RNNoise エラー:', error);
                this.showError(`音声処理エラー: ${error.message}`);
            };

            this.voiceSystem.onInitialized = () => {
                console.log('[DEBUG] ✅ RNNoise音声システム準備完了');
                this.log('✅ RNNoise音声システム準備完了');
            };

            console.log('[DEBUG] イベントハンドラー設定完了');

            // 初期化実行
            console.log('[DEBUG] VoiceRecorderWorklet.initialize()実行中...');
            const success = await this.voiceSystem.initialize();
            console.log('[DEBUG] VoiceRecorderWorklet.initialize()結果:', success);

            if (!success) {
                console.error('[DEBUG] VoiceRecorderWorklet初期化失敗');
                throw new Error('VoiceRecorderWorklet初期化失敗');
            }

            console.log('[DEBUG] RNNoise音声システム初期化完了');
            this.log('RNNoise音声システム初期化完了');
            return true;

        } catch (error) {
            console.error('[DEBUG] RNNoise初期化失敗:', error);
            this.logError('RNNoise初期化失敗:', error);
            this.showError('音声システムの初期化に失敗しました');

            if (this.voiceSystem) {
                console.log('[DEBUG] VoiceRecorderWorkletクリーンアップ中...');
                await this.voiceSystem.destroy();
                this.voiceSystem = null;
            }

            // 音声ボタンを無効化
            if (this.elements.voiceButton) {
                console.log('[DEBUG] 音声ボタン無効化中...');
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
        console.log('[DEBUG] === 音声認識開始処理 ===');
        try {
            console.log('[DEBUG] VoiceSystem.startRecording()実行中...');
            await this.voiceSystem.startRecording();
            console.log('[DEBUG] VoiceSystem.startRecording()完了');

            this.updateVoiceButton('active');
            console.log('[DEBUG] 音声ボタン状態をactiveに更新');

            this.log('🎤 RNNoise録音開始');
            console.log('[DEBUG] === 音声認識開始処理完了 ===');

        } catch (error) {
            console.error('[DEBUG] 音声認識開始エラー:', error);
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
            // 音声再生中は認識データを破棄
            if (this.isPlayingAudio) {
                // this.log('🔇 音声再生中のため認識データを破棄');
                return;
            }

            // wavDataをBase64エンコード（より安全な方法）
            let binaryString = '';
            const chunkSize = 1024; // 1KB chunks（スタックオーバーフロー回避）
            for (let i = 0; i < wavData.length; i += chunkSize) {
                const chunk = wavData.slice(i, i + chunkSize);
                // 小さなchunkに分割してString.fromCharCodeを適用
                let chunkString = '';
                for (let j = 0; j < chunk.length; j++) {
                    chunkString += String.fromCharCode(chunk[j]);
                }
                binaryString += chunkString;
            }
            const base64Audio = btoa(binaryString);
            const voiceMessage = {
                type: 'voice',
                timestamp: new Date().toISOString(),
                data: {
                    audio_data_base64: base64Audio,  // Base64データ
                    encoding: 'base64',              // エンコーディング明示
                    sample_rate: 16000,
                    channels: 1,
                    format: 'wav',
                    processing: 'rnnoise'
                }
            };

            if (window.wsManager && window.wsManager.isConnected) {
                window.wsManager.sendVoiceMessage(voiceMessage);
            } else {
                // this.logError('WebSocket未接続のため音声データ送信失敗');
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
        // this.log(`音声レベル: ${(level * 100).toFixed(1)}%, VAD: ${(vadProbability * 100).toFixed(1)}%, Speech: ${isSpeech}`); // デバッグログ停止
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

    // ==== カメラ関連メソッド ====

    /**
     * カメラを開く
     */
    async openCamera() {
        if (!this.cameraManager) {
            this.showError('カメラシステムが利用できません');
            return;
        }

        try {
            this.updateCameraButton('active');
            await this.cameraManager.openCamera();
            this.log('📷 カメラを開きました');

        } catch (error) {
            console.error('カメラオープンエラー:', error);
            this.logError('カメラオープンエラー:', error);
            this.showError(`カメラアクセス失敗: ${error.message}`);
            this.updateCameraButton('inactive');
        }
    }

    /**
     * カメラ画像データを処理（プレビュー表示）
     */
    async handleCameraImage(imageData) {
        try {
            console.log('[DEBUG] カメラ画像データ受信:', imageData);

            // 画像データを保存
            this.capturedImageData = imageData;

            // Base64データをdata URLに変換してプレビュー表示
            const mimeType = imageData.format === 'jpeg' ? 'image/jpeg' : `image/${imageData.format}`;
            const dataUrl = `data:${mimeType};base64,${imageData.base64}`;

            // プレビューエリアを表示
            this.showImagePreview(dataUrl);

            this.log('📷 画像をプレビュー表示しました');

        } catch (error) {
            this.logError('画像データ処理エラー:', error);
            this.showError(`画像処理エラー: ${error.message}`);
        }
    }

    /**
     * 画像プレビューを表示
     */
    showImagePreview(dataUrl) {
        if (this.elements.previewImage && this.elements.imagePreviewArea) {
            this.elements.previewImage.src = dataUrl;
            this.elements.imagePreviewArea.classList.remove('hidden');

            // 送信ボタンの状態を更新（画像だけでも送信可能）
            this.updateSendButton();
        }
    }

    /**
     * プレビュー画像を削除
     */
    removePreviewImage() {
        this.capturedImageData = null;

        if (this.elements.imagePreviewArea) {
            this.elements.imagePreviewArea.classList.add('hidden');
        }

        if (this.elements.previewImage) {
            this.elements.previewImage.src = '';
        }

        // 送信ボタンの状態を更新
        this.updateSendButton();

        this.log('📷 プレビュー画像を削除しました');
    }

    /**
     * カメラボタン状態更新
     */
    updateCameraButton(state) {
        if (!this.elements.cameraButton) return;

        const button = this.elements.cameraButton;

        // すべてのクラスをリセット
        button.classList.remove('active', 'disabled');

        switch (state) {
            case 'active':
                button.classList.add('active');
                break;
            case 'inactive':
                // デフォルト状態
                break;
            case 'disabled':
                button.classList.add('disabled');
                break;
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

        // カメラボタン
        if (this.elements.cameraButton) {
            this.elements.cameraButton.addEventListener('click', () => {
                this.openCamera();
            });
        }

        // 画像削除ボタン
        if (this.elements.removeImageButton) {
            this.elements.removeImageButton.addEventListener('click', () => {
                this.removePreviewImage();
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

        window.wsManager.addMessageHandler('chat', (message) => {
            this.handleChatMessage(message);
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
     * メッセージ送信（画像との組み合わせにも対応）
     */
    sendMessage() {
        const message = this.elements.messageInput.value.trim();
        const hasImage = this.capturedImageData !== null;

        // メッセージまたは画像のいずれかが必要
        if (!message && !hasImage) {
            return;
        }

        if (this.isLoading) {
            return;
        }

        try {
            // 画像がある場合は画像メッセージとして送信
            if (hasImage) {
                this.sendImageWithMessage(message);
            } else {
                // テキストのみの場合は従来通り
                this.sendTextMessage(message);
            }

        } catch (error) {
            console.error('メッセージ送信エラー:', error);
            this.hideLoading();
            this.showError('メッセージの送信に失敗しました');
        }
    }

    /**
     * テキストメッセージのみ送信
     */
    sendTextMessage(message) {
        // ユーザーメッセージを表示
        this.addUserMessage(message);

        // 入力欄をクリア
        this.elements.messageInput.value = '';
        this.updateSendButton();

        // ローディング開始
        this.showLoading();

        // WebSocketで送信
        window.wsManager.sendChatMessage(message);
    }

    /**
     * 画像とメッセージを組み合わせて送信
     */
    sendImageWithMessage(message) {
        // 画像付きメッセージを表示（画像は後でaddUserMessageWithImageで表示）
        const mimeType = this.capturedImageData.format === 'jpeg' ? 'image/jpeg' : `image/${this.capturedImageData.format}`;
        const imageDataUrl = `data:${mimeType};base64,${this.capturedImageData.base64}`;

        this.addUserMessageWithImage(message || '', imageDataUrl);

        // 画像メッセージを作成
        const imageMessage = {
            type: 'image',
            timestamp: new Date().toISOString(),
            data: {
                image_data_base64: this.capturedImageData.base64,
                encoding: 'base64',
                format: this.capturedImageData.format,
                width: this.capturedImageData.width,
                height: this.capturedImageData.height,
                camera_facing: this.capturedImageData.facingMode,
                message: message || '' // テキストメッセージも含める
            }
        };

        // UI をクリア
        this.elements.messageInput.value = '';
        this.removePreviewImage();
        this.updateSendButton();

        // ローディング開始
        this.showLoading();

        // WebSocketで送信
        if (window.wsManager && window.wsManager.isConnected) {
            window.wsManager.sendImageMessage(imageMessage);
            this.log('📷 画像とメッセージを送信しました');
        } else {
            this.hideLoading();
            this.logError('WebSocket未接続のため画像データ送信失敗');
            this.showError('接続が切断されています');
        }
    }

    /**
     * 音声をキューに追加
     */
    addAudioToQueue(audioUrl) {
        this.audioQueue.push(audioUrl);
        // キューが処理中でない場合は処理開始
        if (!this.isProcessingAudioQueue) {
            this.processAudioQueue();
        }
    }

    /**
     * 音声キューを順次処理
     */
    async processAudioQueue() {
        if (this.isProcessingAudioQueue || this.audioQueue.length === 0) {
            return;
        }

        this.isProcessingAudioQueue = true;

        // キュー全体の処理中フラグを設定
        this.isPlayingAudio = true;

        // キュー処理開始時に音声認識を停止
        if (this.voiceSystem && this.isVoiceEnabled) {
            this.voiceSystem.setPlaybackMode(true);
        }

        try {
            while (this.audioQueue.length > 0) {
                const audioUrl = this.audioQueue.shift();
                await this.playAudioSequentially(audioUrl);
            }
        } catch (error) {
            console.error('音声キュー処理エラー:', error);
            // エラーが発生してもキューをクリア
            this.audioQueue = [];
        } finally {
            // エラー発生時も確実に状態をリセット
            this.isProcessingAudioQueue = false;
            this.isPlayingAudio = false;

            // 音声認識を確実に再開
            if (this.voiceSystem && this.isVoiceEnabled) {
                this.voiceSystem.setPlaybackMode(false);
            }
        }
    }

    /**
     * 音声を順次再生
     */
    async playAudioSequentially(audioUrl) {
        return new Promise((resolve, reject) => {
            try {
                // console.log('音声再生開始:', audioUrl);
                const audio = new Audio(audioUrl);

                // フラグは processAudioQueue で一括管理
                // 個別の音声再生では変更しない
                // this.log('🔊 音声再生開始');

                // 音声再生終了時の処理
                audio.addEventListener('ended', () => {
                    // this.log('🔊 音声再生終了');
                    resolve();
                });

                // 音声再生エラー時の処理
                audio.addEventListener('error', (error) => {
                    this.log('🔊 音声再生エラー');
                    console.warn('音声再生エラー:', error);
                    reject(error);
                });

                // 音声再生中断時の処理
                audio.addEventListener('pause', () => {
                    // this.log('🔊 音声再生中断');
                    resolve();
                });

                audio.play().catch(error => {
                    console.warn('音声再生エラー:', error);
                    reject(error);
                });

            } catch (error) {
                console.warn('音声オブジェクト作成エラー:', error);
                reject(error);
            }
        });
    }

    /**
     * レスポンス処理
     */
    handleResponse(message) {
        this.hideLoading();

        if (message.data && message.data.text) {
            this.addAIMessage(message.data.text);

            // 音声再生機能（VOICEVOX統合）- キューシステム使用
            const audioUrl = message.data.audio_url || message.data.audioUrl || message.data.AudioUrl;
            if (audioUrl) {
                this.addAudioToQueue(audioUrl);
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
     * チャットメッセージ処理（音声認識結果用）
     */
    handleChatMessage(message) {
        if (message.data?.chat_type === 'voice_recognition_user') {
            this.addUserMessage(message.data.message);
        }
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
     * 画像付きユーザーメッセージ追加
     */
    addUserMessageWithImage(text, imageDataUrl) {
        const messageDiv = this.createMessageElementWithImage('user', text, imageDataUrl);
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
        // [face:～] パターンを非表示にする
        const filteredText = text.replace(/\[face:[^\]]*\]/g, '');
        contentDiv.textContent = filteredText;

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
     * 画像付きメッセージ要素作成
     */
    createMessageElementWithImage(type, text, imageDataUrl) {
        const messageDiv = document.createElement('div');
        messageDiv.className = `message ${type}`;

        const contentDiv = document.createElement('div');
        contentDiv.className = 'message-content with-image';

        // 画像要素を作成（上部に配置）
        const imageElement = document.createElement('img');
        imageElement.className = 'message-image';
        imageElement.src = imageDataUrl;
        imageElement.alt = '送信した画像';
        contentDiv.appendChild(imageElement);

        // テキストがある場合のみテキスト要素を追加（下部に配置）
        if (text && text.trim()) {
            // [face:～] パターンを非表示にする
            const filteredText = text.replace(/\[face:[^\]]*\]/g, '');
            const textElement = document.createElement('div');
            textElement.textContent = filteredText;
            contentDiv.appendChild(textElement);
        }

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
        const hasImage = this.capturedImageData !== null;
        const isConnected = window.wsManager.isConnected;

        // メッセージまたは画像のいずれかがあれば送信可能
        this.elements.sendButton.disabled = (!hasMessage && !hasImage) || !isConnected || this.isLoading;
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

        // 音声キューをクリア
        this.audioQueue = [];
        this.isProcessingAudioQueue = false;

        // RNNoise システム解放
        if (this.voiceSystem) {
            await this.voiceSystem.destroy();
            this.voiceSystem = null;
        }

        // カメラシステム解放
        if (this.cameraManager) {
            this.cameraManager.destroy();
            this.cameraManager = null;
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