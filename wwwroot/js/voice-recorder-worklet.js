/**
 * Advanced Voice Recorder with RNNoise and AudioWorklet
 * 高性能なリアルタイム音声処理とノイズ除去
 */
class VoiceRecorderWorklet {
    constructor() {
        // オーディオコンテキスト
        this.audioContext = null;
        this.mediaStream = null;
        this.sourceNode = null;
        this.workletNode = null;

        // RNNoise処理（メインスレッド）
        this.rnnoiseProcessor = null;
        this.isRecording = false;
        this.isInitialized = false;

        // フレーム処理キュー
        this.frameQueue = [];
        this.processingFrames = false;

        // 統計情報
        this.stats = {
            totalFrames: 0,
            processedFrames: 0,
            droppedFrames: 0,
            averageProcessingTime: 0
        };

        // イベントハンドラー
        this.onVoiceData = null;
        this.onAudioLevel = null;
        this.onVoiceDetected = null;
        this.onVoiceEnded = null;
        this.onError = null;
        this.onInitialized = null;

        // 状態管理
        this.isVoiceDetected = false;

        // 設定
        this.debugMode = false;
    }

    /**
     * 初期化
     */
    async initialize() {
        if (this.isInitialized) {
            this.log('既に初期化済み');
            return true;
        }

        try {
            this.log('VoiceRecorderWorklet初期化開始...');

            // AudioContextの作成
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: 48000,
                latencyHint: 'interactive'
            });

            // AudioWorkletモジュール登録
            await this.audioContext.audioWorklet.addModule('/js/rnnoise-worklet-processor.js');
            this.log('AudioWorkletモジュール登録完了');

            // RNNoiseProcessor初期化（メインスレッド）
            this.rnnoiseProcessor = new RNNoiseProcessor();

            this.rnnoiseProcessor.onError = (error) => {
                this.logError('RNNoise エラー:', error);
                if (this.onError) {
                    this.onError(error);
                }
            };

            this.rnnoiseProcessor.onInitialized = () => {
                this.log('RNNoise初期化完了');
            };

            this.rnnoiseProcessor.setDebugMode(this.debugMode);

            // RNNoise初期化
            const success = await this.rnnoiseProcessor.initialize();
            if (!success) {
                throw new Error('RNNoise初期化失敗');
            }

            // フレーム処理開始
            this.startFrameProcessing();

            this.isInitialized = true;
            this.log('VoiceRecorderWorklet初期化完了');

            if (this.onInitialized) {
                this.onInitialized();
            }

            return true;

        } catch (error) {
            this.logError('初期化エラー:', error);
            if (this.onError) {
                this.onError(error);
            }
            return false;
        }
    }

    /**
     * 録音開始
     */
    async startRecording() {
        if (!this.isInitialized) {
            throw new Error('初期化されていません');
        }

        if (this.isRecording) {
            this.log('既に録音中');
            return;
        }

        try {
            this.log('録音開始処理...');

            // マイクアクセス
            this.mediaStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    sampleRate: 48000,
                    channelCount: 1,
                    echoCancellation: true,
                    noiseSuppression: false, // RNNoiseで処理
                    autoGainControl: false
                }
            });

            await this.audioContext.resume();

            // AudioWorkletNode作成
            this.workletNode = new AudioWorkletNode(
                this.audioContext,
                'rnnoise-worklet-processor',
                {
                    numberOfInputs: 1,
                    numberOfOutputs: 0,
                    channelCount: 1
                }
            );

            // Workletからのメッセージ処理
            this.workletNode.port.onmessage = (event) => {
                this.handleWorkletMessage(event.data);
            };

            // オーディオソース接続
            this.sourceNode = this.audioContext.createMediaStreamSource(this.mediaStream);
            this.sourceNode.connect(this.workletNode);

            // Workletに初期化メッセージ送信
            this.workletNode.port.postMessage({
                type: 'initWasm',
                wasmModule: null // 実際の実装では適切なデータを送信
            });

            this.isRecording = true;
            this.log('録音開始完了');

        } catch (error) {
            this.logError('録音開始エラー:', error);
            if (this.onError) {
                this.onError(error);
            }
            throw error;
        }
    }

    /**
     * 録音停止
     */
    async stopRecording() {
        if (!this.isRecording) {
            return;
        }

        try {
            this.log('録音停止処理...');

            // ノード切断
            if (this.sourceNode) {
                this.sourceNode.disconnect();
                this.sourceNode = null;
            }

            if (this.workletNode) {
                this.workletNode.disconnect();
                this.workletNode = null;
            }

            // メディアストリーム停止
            if (this.mediaStream) {
                this.mediaStream.getTracks().forEach(track => track.stop());
                this.mediaStream = null;
            }

            // 残りのフレームを処理
            await this.flushFrameQueue();

            this.isRecording = false;
            this.log('録音停止完了');

        } catch (error) {
            this.logError('録音停止エラー:', error);
            if (this.onError) {
                this.onError(error);
            }
        }
    }

    /**
     * Workletからのメッセージ処理（最適化版）
     */
    handleWorkletMessage(message) {
        switch (message.type) {
            case 'processFrame':
                // フレームデータと音声レベル情報を含めてキューに追加
                this.queueFrameForProcessing({
                    frameData: message.frameData,
                    frameIndex: message.frameIndex,
                    audioLevel: message.audioLevel,
                    timestamp: message.timestamp
                });
                break;

            case 'audioLevel':
                // 音声レベルのみを通知（VAD判定はRNNoise処理後に実施）
                if (this.onAudioLevel) {
                    this.onAudioLevel(message.level, false, 0); // VAD情報は仮値
                }
                break;

            case 'stats':
                this.updateStats(message.stats);
                break;

            case 'log':
                this.log(`[Worklet] ${message.message}`);
                break;

            case 'wasmReady':
                if (message.success) {
                    this.log('Worklet WASM初期化完了');
                } else {
                    this.logError('Worklet WASM初期化失敗:', message.error);
                }
                break;
        }
    }

    /**
     * フレーム処理キューに追加（最適化版）
     */
    queueFrameForProcessing(frameInfo) {
        this.frameQueue.push({
            data: new Float32Array(frameInfo.frameData),
            index: frameInfo.frameIndex,
            audioLevel: frameInfo.audioLevel || 0,
            timestamp: frameInfo.timestamp || performance.now()
        });

        this.stats.totalFrames++;

        // キューサイズ制限
        if (this.frameQueue.length > 100) {
            this.frameQueue.shift();
            this.stats.droppedFrames++;
        }
    }

    /**
     * フレーム処理ループ開始
     */
    startFrameProcessing() {
        if (this.processingFrames) {
            return;
        }

        this.processingFrames = true;
        this.processFrameQueue();
    }

    /**
     * フレーム処理ループ（最適化版）
     */
    async processFrameQueue() {
        while (this.processingFrames) {
            if (this.frameQueue.length > 0) {
                const frame = this.frameQueue.shift();
                const startTime = performance.now();

                try {
                    // RNNoiseで処理し、VAD結果を取得
                    if (this.rnnoiseProcessor && this.rnnoiseProcessor.isInitialized) {
                        const voiceResult = this.rnnoiseProcessor.processAudioFrame(frame.data);

                        // 処理時間計算
                        const processingTime = performance.now() - startTime;
                        this.updateProcessingStats(processingTime);

                        this.stats.processedFrames++;

                        // 音声セグメントが完了した場合の処理
                        if (voiceResult && Array.isArray(voiceResult)) {
                            this.handleVoiceSegmentCompleted(voiceResult);
                        }
                    }

                } catch (error) {
                    this.logError('フレーム処理エラー:', error);
                }
            }

            // 次の処理まで少し待機
            await new Promise(resolve => setTimeout(resolve, 1));
        }
    }

    /**
     * フレームキューをフラッシュ
     */
    async flushFrameQueue() {
        this.log(`フレームキューフラッシュ: ${this.frameQueue.length}フレーム`);

        while (this.frameQueue.length > 0) {
            const frame = this.frameQueue.shift();

            try {
                if (this.rnnoiseProcessor && this.rnnoiseProcessor.isInitialized) {
                    this.rnnoiseProcessor.processAudioFrame(frame.data);
                    this.stats.processedFrames++;
                }
            } catch (error) {
                this.logError('フラッシュ中エラー:', error);
            }
        }
    }

    /**
     * 音声セグメント完了時の処理（最適化版）
     */
    async handleVoiceSegmentCompleted(voiceSegment) {
        try {
            this.log(`音声セグメント完了: ${voiceSegment.length}フレーム`);

            // 音声開始通知（初回のみ）
            if (!this.isVoiceDetected) {
                this.isVoiceDetected = true;
                if (this.onVoiceDetected) {
                    this.onVoiceDetected();
                }
            }

            // 音声フレームから音声データを抽出
            const audioFrames = voiceSegment.map(item => item.frame || item);

            // WAV変換
            const wavData = this.rnnoiseProcessor.convertToWav(audioFrames);
            if (wavData && this.onVoiceData) {
                this.onVoiceData(wavData);
            }

            // 音声終了通知
            if (this.onVoiceEnded) {
                this.onVoiceEnded(audioFrames);
            }

            // 状態リセット
            this.isVoiceDetected = false;

        } catch (error) {
            this.logError('音声セグメント処理エラー:', error);
        }
    }

    /**
     * 処理統計更新
     */
    updateProcessingStats(processingTime) {
        this.stats.averageProcessingTime =
            (this.stats.averageProcessingTime * (this.stats.processedFrames - 1) + processingTime)
            / this.stats.processedFrames;
    }

    /**
     * 統計情報更新
     */
    updateStats(workletStats) {
        // WorkletとメインスレッドのStats統合
        Object.assign(this.stats, workletStats);
    }

    /**
     * 設定変更（最適化版）
     */
    setVADThreshold(threshold) {
        if (this.rnnoiseProcessor) {
            this.rnnoiseProcessor.setVADThreshold(threshold);
        }
    }

    setVADSmoothThreshold(threshold) {
        if (this.rnnoiseProcessor) {
            this.rnnoiseProcessor.setVADSmoothThreshold(threshold);
        }
    }

    setSilenceTimeout(timeoutMs) {
        if (this.rnnoiseProcessor) {
            this.rnnoiseProcessor.setSilenceTimeout(timeoutMs);
        }
    }

    setVADHistorySize(size) {
        if (this.rnnoiseProcessor) {
            this.rnnoiseProcessor.setVADHistorySize(size);
        }
    }

    setFrameSendRate(rate) {
        if (this.workletNode) {
            this.workletNode.port.postMessage({
                type: 'setFrameSendRate',
                rate: rate
            });
        }
    }

    setDebugMode(enabled) {
        this.debugMode = enabled;
        if (this.rnnoiseProcessor) {
            this.rnnoiseProcessor.setDebugMode(enabled);
        }
    }

    /**
     * 統計情報取得
     */
    getStats() {
        const rnnoiseStats = this.rnnoiseProcessor ? this.rnnoiseProcessor.getStats() : {};

        return {
            ...this.stats,
            ...rnnoiseStats,
            isInitialized: this.isInitialized,
            isRecording: this.isRecording,
            queueLength: this.frameQueue.length,
            processingFrames: this.processingFrames
        };
    }

    /**
     * ログ出力
     */
    log(message) {
        // デバッグログを停止 - エラーのみ出力
        // const logEntry = `[VoiceRecorderWorklet] ${new Date().toISOString()}: ${message}`;
        // console.log(logEntry);
    }

    logError(message, error = null) {
        const errorMessage = error ? `${message} ${error.message}` : message;
        const logEntry = `[VoiceRecorderWorklet ERROR] ${new Date().toISOString()}: ${errorMessage}`;
        console.error(logEntry);
    }

    /**
     * リソース解放
     */
    async destroy() {
        this.log('リソース解放開始...');

        // フレーム処理停止
        this.processingFrames = false;

        // 録音停止
        await this.stopRecording();

        // RNNoiseProcessor解放
        if (this.rnnoiseProcessor) {
            this.rnnoiseProcessor.destroy();
            this.rnnoiseProcessor = null;
        }

        // AudioContext解放
        if (this.audioContext && this.audioContext.state !== 'closed') {
            await this.audioContext.close();
            this.audioContext = null;
        }

        // バッファクリア
        this.frameQueue = [];

        this.isInitialized = false;
        this.log('リソース解放完了');
    }
}

// グローバルエクスポート
if (typeof window !== 'undefined') {
    window.VoiceRecorderWorklet = VoiceRecorderWorklet;
}

// Node.js環境対応
if (typeof module !== 'undefined' && module.exports) {
    module.exports = VoiceRecorderWorklet;
}