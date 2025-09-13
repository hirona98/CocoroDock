/**
 * RNNoise AudioWorklet Processor
 * メインスレッドをブロックしない高性能音声処理
 *
 * AudioWorkletGlobalScopeで動作するため、制限された環境での実装
 */

class RNNoiseWorkletProcessor extends AudioWorkletProcessor {
    constructor(options) {
        super();

        console.log('[RNNoiseWorkletProcessor] 初期化開始');

        // RNNoise設定
        this.frameSize = 480; // 10ms @ 48kHz
        this.sampleRate = 48000;

        // バッファリング
        this.inputBuffer = new Float32Array(0);
        this.outputBuffer = new Float32Array(0);

        // VAD状態
        this.vadThreshold = 0.6;
        this.silenceFrames = 0;
        this.maxSilenceFrames = 50; // 500ms
        this.speechFrames = 0;
        this.minSpeechFrames = 3; // 30ms
        this.isRecording = false;

        // 音声バッファ
        this.audioBuffer = [];
        this.preBuffer = [];
        this.preBufferSize = 10; // 100ms

        // 統計
        this.stats = {
            framesProcessed: 0,
            voiceSegments: 0,
            totalVoiceTime: 0
        };

        // WASM関連（後で初期化）
        this.wasmModule = null;
        this.denoiseState = null;
        this.isWasmReady = false;

        // メインスレッドからのメッセージ処理
        this.port.onmessage = (event) => {
            this.handleMessage(event.data);
        };

        this.port.postMessage({
            type: 'log',
            message: 'AudioWorkletProcessor作成完了'
        });
    }

    /**
     * メインスレッドからのメッセージ処理
     */
    handleMessage(message) {
        switch (message.type) {
            case 'initWasm':
                this.initializeWasm(message.wasmModule);
                break;

            case 'setVadThreshold':
                this.vadThreshold = Math.max(0, Math.min(1, message.threshold));
                this.port.postMessage({
                    type: 'log',
                    message: `VAD閾値変更: ${this.vadThreshold}`
                });
                break;

            case 'setSilenceTimeout':
                this.maxSilenceFrames = Math.floor(message.timeoutMs / 10);
                this.port.postMessage({
                    type: 'log',
                    message: `無音タイムアウト変更: ${message.timeoutMs}ms`
                });
                break;

            case 'getStats':
                this.port.postMessage({
                    type: 'stats',
                    stats: { ...this.stats }
                });
                break;
        }
    }

    /**
     * WASM初期化
     * AudioWorkletでは制限があるため、メインスレッドで初期化されたモジュールを受け取る
     */
    async initializeWasm(wasmModuleData) {
        try {
            this.port.postMessage({
                type: 'log',
                message: 'WASM初期化開始...'
            });

            // この実装は簡略化版です
            // 実際のAudioWorkletでのWASM使用は複雑なため、
            // メインスレッドで処理を行い、結果を受け取る方式に変更

            this.isWasmReady = true;

            this.port.postMessage({
                type: 'wasmReady',
                success: true
            });

            this.port.postMessage({
                type: 'log',
                message: 'WASM初期化完了'
            });

        } catch (error) {
            this.port.postMessage({
                type: 'wasmReady',
                success: false,
                error: error.message
            });

            this.port.postMessage({
                type: 'log',
                message: `WASM初期化エラー: ${error.message}`
            });
        }
    }

    /**
     * 音声処理メイン関数
     * リアルタイムで音声データを処理
     */
    process(inputs, outputs, parameters) {
        const input = inputs[0];
        if (!input || !input[0] || !this.isWasmReady) {
            return true;
        }

        const inputData = input[0];

        // 入力バッファに蓄積
        const newBuffer = new Float32Array(this.inputBuffer.length + inputData.length);
        newBuffer.set(this.inputBuffer);
        newBuffer.set(inputData, this.inputBuffer.length);
        this.inputBuffer = newBuffer;

        // フレームサイズ（480サンプル）ごとに処理
        while (this.inputBuffer.length >= this.frameSize) {
            const frame = this.inputBuffer.slice(0, this.frameSize);

            // フレーム処理（簡略化版）
            this.processFrame(frame);

            // バッファから処理済み分を削除
            this.inputBuffer = this.inputBuffer.slice(this.frameSize);
        }

        return true;
    }

    /**
     * 音声フレーム処理（AudioWorklet版）
     * RNNoiseは実際にはメインスレッドで処理されるため、
     * ここでは基本的なVADと音声レベル計算のみ
     */
    processFrame(frame) {
        this.stats.framesProcessed++;

        // 簡易音声レベル計算
        let sum = 0;
        for (let i = 0; i < frame.length; i++) {
            sum += frame[i] * frame[i];
        }
        const rms = Math.sqrt(sum / frame.length);

        // 簡易VAD（音声レベルベース）
        const audioLevel = this.calculateAudioLevel(rms);
        const isSpeech = audioLevel > 0.1; // 簡易閾値

        // VAD状態管理
        this.updateVADState(isSpeech, frame, audioLevel);

        // メインスレッドに音声レベル通知
        if (this.stats.framesProcessed % 5 === 0) { // 50msごとに通知
            this.port.postMessage({
                type: 'audioLevel',
                level: audioLevel,
                isSpeech: isSpeech,
                vadProbability: audioLevel // 簡易版
            });
        }

        // メインスレッドに実際のRNNoise処理を依頼
        this.port.postMessage({
            type: 'processFrame',
            frameData: Array.from(frame),
            frameIndex: this.stats.framesProcessed
        });
    }

    /**
     * 音声レベル計算
     */
    calculateAudioLevel(rms) {
        if (rms > 0) {
            const db = 20 * Math.log10(rms);
            return Math.max(0, Math.min(1, (db + 60) / 60));
        }
        return 0;
    }

    /**
     * VAD状態管理
     */
    updateVADState(isSpeech, frame, audioLevel) {
        // プリバッファ管理
        this.preBuffer.push({
            frame: new Float32Array(frame),
            level: audioLevel,
            timestamp: currentTime
        });

        if (this.preBuffer.length > this.preBufferSize) {
            this.preBuffer.shift();
        }

        if (isSpeech) {
            this.speechFrames++;
            this.silenceFrames = 0;

            // 音声開始判定
            if (!this.isRecording && this.speechFrames >= this.minSpeechFrames) {
                this.isRecording = true;

                // プリバッファを含めて音声バッファ開始
                this.audioBuffer = this.preBuffer.map(item => ({
                    frame: new Float32Array(item.frame),
                    level: item.level,
                    timestamp: item.timestamp
                }));

                this.stats.voiceSegments++;

                this.port.postMessage({
                    type: 'voiceDetected',
                    segmentNumber: this.stats.voiceSegments
                });
            }

            // 録音中は音声データを蓄積
            if (this.isRecording) {
                this.audioBuffer.push({
                    frame: new Float32Array(frame),
                    level: audioLevel,
                    timestamp: currentTime
                });
            }

        } else {
            this.silenceFrames++;
            this.speechFrames = Math.max(0, this.speechFrames - 1);

            // 音声終了判定
            if (this.isRecording && this.silenceFrames >= this.maxSilenceFrames) {
                const recordedAudio = this.finalizeRecording();

                this.port.postMessage({
                    type: 'voiceEnded',
                    audioData: recordedAudio.map(item => Array.from(item.frame)),
                    duration: recordedAudio.length * 10 // ms
                });
            }

            // 録音中は無音も含める
            if (this.isRecording) {
                this.audioBuffer.push({
                    frame: new Float32Array(frame),
                    level: audioLevel,
                    timestamp: currentTime
                });
            }
        }
    }

    /**
     * 録音終了処理
     */
    finalizeRecording() {
        const recordedAudio = this.audioBuffer;
        const voiceTime = recordedAudio.length * 10; // ms

        this.stats.totalVoiceTime += voiceTime;

        this.audioBuffer = [];
        this.isRecording = false;
        this.speechFrames = 0;
        this.silenceFrames = 0;

        this.port.postMessage({
            type: 'log',
            message: `録音終了: ${voiceTime}ms, 累計: ${this.stats.totalVoiceTime}ms`
        });

        return recordedAudio;
    }

    /**
     * 統計情報取得
     */
    getStats() {
        return { ...this.stats, isRecording: this.isRecording };
    }
}

// AudioWorkletProcessorに登録
registerProcessor('rnnoise-worklet-processor', RNNoiseWorkletProcessor);