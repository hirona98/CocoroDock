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
        this.isPlaybackMode = false; // 音声再生中フラグ

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
            console.log('[VOICE-DEBUG] AudioWorkletモジュール登録中...');
            console.log('[VOICE-DEBUG] audioWorklet存在確認:', typeof this.audioContext.audioWorklet);

            try {
                await this.audioContext.audioWorklet.addModule('/js/rnnoise-worklet-processor.js');
                console.log('[VOICE-DEBUG] AudioWorkletモジュール登録完了');
                this.log('AudioWorkletモジュール登録完了');
            } catch (moduleError) {
                console.error('[VOICE-DEBUG] AudioWorkletモジュール登録エラー:', moduleError);
                throw new Error(`AudioWorkletモジュール登録失敗: ${moduleError.message}`);
            }

            // RNNoiseProcessor初期化（メインスレッド）
            console.log('[VOICE-DEBUG] RNNoiseProcessor作成中...');
            console.log('[VOICE-DEBUG] RNNoiseProcessorクラス確認:', typeof RNNoiseProcessor);
            this.rnnoiseProcessor = new RNNoiseProcessor();
            console.log('[VOICE-DEBUG] RNNoiseProcessor作成完了');

            this.rnnoiseProcessor.onError = (error) => {
                this.logError('RNNoise エラー:', error);
                if (this.onError) {
                    this.onError(error);
                }
            };

            this.rnnoiseProcessor.onInitialized = () => {
                this.log('RNNoise初期化完了');
            };

            // RNNoiseProcessorのイベントハンドラーを設定
            this.rnnoiseProcessor.onVoiceDetected = () => {
                this.log('音声開始検出');
                if (this.onVoiceDetected) {
                    this.onVoiceDetected();
                }
            };

            // onVoiceEndedは重複を避けるためコメントアウト（processFrameQueueで処理）
            // this.rnnoiseProcessor.onVoiceEnded = (recordedAudio) => {
            //     this.log('音声終了検出');
            //     this.handleVoiceSegmentCompleted(recordedAudio);
            // };

            this.rnnoiseProcessor.onAudioLevel = (level, isSpeech, vadProbability) => {
                if (this.onAudioLevel) {
                    this.onAudioLevel(level, isSpeech, vadProbability);
                }
            };

            this.rnnoiseProcessor.setDebugMode(this.debugMode);

            // RNNoise初期化
            console.log('[VOICE-DEBUG] RNNoiseProcessor.initialize()実行中...');
            const success = await this.rnnoiseProcessor.initialize();
            console.log('[VOICE-DEBUG] RNNoiseProcessor.initialize()結果:', success);

            if (!success) {
                console.error('[VOICE-DEBUG] RNNoise初期化失敗');
                throw new Error('RNNoise初期化失敗');
            }

            // フレーム処理開始
            console.log('[VOICE-DEBUG] フレーム処理開始中...');
            this.startFrameProcessing();
            console.log('[VOICE-DEBUG] フレーム処理開始完了');

            this.isInitialized = true;
            console.log('[VOICE-DEBUG] VoiceRecorderWorklet初期化完了');
            this.log('VoiceRecorderWorklet初期化完了');

            if (this.onInitialized) {
                console.log('[VOICE-DEBUG] onInitializedコールバック実行中...');
                this.onInitialized();
            }

            console.log('[VOICE-DEBUG] === VoiceRecorderWorklet初期化完了 ===');
            return true;

        } catch (error) {
            console.error('[VOICE-DEBUG] VoiceRecorderWorklet初期化エラー:', error);
            console.error('[VOICE-DEBUG] エラースタック:', error.stack);
            this.logError('初期化エラー:', error);
            if (this.onError) {
                console.log('[VOICE-DEBUG] onErrorコールバック実行中...');
                this.onError(error);
            }
            return false;
        }
    }

    /**
     * 録音開始
     */
    async startRecording() {
        console.log('[VOICE-DEBUG] === 録音開始処理 ===');
        console.log('[VOICE-DEBUG] 初期化状態:', this.isInitialized);
        console.log('[VOICE-DEBUG] 録音状態:', this.isRecording);

        if (!this.isInitialized) {
            console.error('[VOICE-DEBUG] 初期化されていません');
            throw new Error('初期化されていません');
        }

        if (this.isRecording) {
            console.log('[VOICE-DEBUG] 既に録音中');
            this.log('既に録音中');
            return;
        }

        try {
            console.log('[VOICE-DEBUG] 録音開始処理...');
            this.log('録音開始処理...');

            // マイクアクセス
            console.log('[VOICE-DEBUG] マイクアクセス要求中...');
            console.log('[VOICE-DEBUG] navigator.mediaDevices:', typeof navigator.mediaDevices);
            console.log('[VOICE-DEBUG] getUserMedia:', typeof navigator.mediaDevices?.getUserMedia);

            const constraints = {
                audio: {
                    sampleRate: 48000,
                    channelCount: 1,
                    echoCancellation: true,
                    noiseSuppression: false, // RNNoiseで処理
                    autoGainControl: false
                }
            };
            console.log('[VOICE-DEBUG] マイク制約:', constraints);

            this.mediaStream = await navigator.mediaDevices.getUserMedia(constraints);
            console.log('[VOICE-DEBUG] マイクアクセス成功, tracks:', this.mediaStream.getTracks().length);
            console.log('[VOICE-DEBUG] オーディオトラック詳細:', this.mediaStream.getAudioTracks()[0]?.getSettings());

            console.log('[VOICE-DEBUG] AudioContext状態確認:', this.audioContext.state);
            await this.audioContext.resume();
            console.log('[VOICE-DEBUG] AudioContext resume完了, 新状態:', this.audioContext.state);

            // AudioWorkletNode作成
            console.log('[VOICE-DEBUG] AudioWorkletNode作成中...');
            try {
                this.workletNode = new AudioWorkletNode(
                    this.audioContext,
                    'rnnoise-worklet-processor',
                    {
                        numberOfInputs: 1,
                        numberOfOutputs: 0,
                        channelCount: 1
                    }
                );
                console.log('[VOICE-DEBUG] AudioWorkletNode作成完了');
            } catch (workletError) {
                console.error('[VOICE-DEBUG] AudioWorkletNode作成エラー:', workletError);
                throw new Error(`AudioWorkletNode作成失敗: ${workletError.message}`);
            }

            // Workletからのメッセージ処理
            console.log('[VOICE-DEBUG] Workletメッセージハンドラー設定中...');
            this.workletNode.port.onmessage = (event) => {
                this.handleWorkletMessage(event.data);
            };

            // オーディオソース接続
            console.log('[VOICE-DEBUG] オーディオソース作成中...');
            this.sourceNode = this.audioContext.createMediaStreamSource(this.mediaStream);
            console.log('[VOICE-DEBUG] オーディオソース作成完了');

            console.log('[VOICE-DEBUG] オーディオノード接続中...');
            this.sourceNode.connect(this.workletNode);
            console.log('[VOICE-DEBUG] オーディオノード接続完了');

            // Workletに初期化メッセージ送信
            console.log('[VOICE-DEBUG] Worklet初期化メッセージ送信中...');
            this.workletNode.port.postMessage({
                type: 'initWasm',
                wasmModule: null // 実際の実装では適切なデータを送信
            });
            console.log('[VOICE-DEBUG] Worklet初期化メッセージ送信完了');

            this.isRecording = true;
            console.log('[VOICE-DEBUG] 録音フラグをtrueに設定');
            this.log('録音開始完了');
            console.log('[VOICE-DEBUG] === 録音開始処理完了 ===');

        } catch (error) {
            console.error('[VOICE-DEBUG] 録音開始エラー:', error);
            console.error('[VOICE-DEBUG] エラースタック:', error.stack);
            this.logError('録音開始エメー:', error);
            if (this.onError) {
                console.log('[VOICE-DEBUG] onErrorコールバック実行中...');
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
     * Workletからのメッセージ処理（循環バッファ対応版）
     */
    handleWorkletMessage(message) {
        switch (message.type) {
            case 'processFrame':
                // 循環バッファからの連続フレームデータをキューに追加
                this.queueFrameForProcessing({
                    frameData: message.frameData,
                    frameIndex: message.frameIndex,
                    audioLevel: message.audioLevel,
                    timestamp: message.timestamp,
                    isCircularBuffer: true // 循環バッファ由来であることを明示
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
                // 循環バッファ統計情報もログ出力
                if (message.stats.circularBuffer) {
                    if (this.stats.processedFrames % 100 === 0) {
                        console.log('[VOICE-DEBUG] 循環バッファ統計:', message.stats.circularBuffer);
                    }
                }
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
     * フレーム処理キューに追加（循環バッファ対応版）
     */
    queueFrameForProcessing(frameInfo) {
        // 循環バッファからの連続フレームであることを記録
        const frameData = {
            data: new Float32Array(frameInfo.frameData),
            index: frameInfo.frameIndex,
            audioLevel: frameInfo.audioLevel || 0,
            timestamp: frameInfo.timestamp || performance.now(),
            isCircularBuffer: frameInfo.isCircularBuffer || false
        };

        this.frameQueue.push(frameData);
        this.stats.totalFrames++;

        // 循環バッファ由来フレームの統計
        if (frameInfo.isCircularBuffer) {
            this.stats.circularBufferFrames = (this.stats.circularBufferFrames || 0) + 1;
        }

        // キューサイズ制限（循環バッファの安定性を考慮して緩やかに）
        if (this.frameQueue.length > 50) {
            const dropped = this.frameQueue.shift();
            this.stats.droppedFrames++;

            // ドロップ率が高い場合の警告
            if (this.stats.droppedFrames % 10 === 0) {
                console.warn('[VOICE-DEBUG] フレームドロップ発生:', this.stats.droppedFrames, '個');
            }
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
     * フレーム処理ループ（循環バッファ対応版）
     */
    async processFrameQueue() {
        console.log('[VOICE-DEBUG] 循環バッファ対応フレーム処理ループ開始');

        while (this.processingFrames) {
            if (this.frameQueue.length > 0) {
                const frame = this.frameQueue.shift();
                const startTime = performance.now();

                try {
                    // 音声再生中はRNNoise処理をスキップ
                    if (this.isPlaybackMode) {
                        // フレームは破棄してキューを消化
                        continue;
                    }

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
                    this.logError('循環バッファフレーム処理エラー:', error);
                }
            }

            // CPU負荷を軽減するための短い待機
            await new Promise(resolve => setTimeout(resolve, 0));
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
     * 音声再生モード制御
     */
    setPlaybackMode(isPlaying) {
        this.isPlaybackMode = isPlaying;
        this.log(`音声再生モード: ${isPlaying ? 'ON' : 'OFF'} - RNNoise処理${isPlaying ? '停止' : '再開'}`);
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
        // 一時的にログを有効化してデバッグ
        const logEntry = `[VoiceRecorderWorklet] ${new Date().toISOString()}: ${message}`;
        console.log(logEntry);
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