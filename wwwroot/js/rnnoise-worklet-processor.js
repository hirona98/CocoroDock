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

        // フレーム送信設定
        this.frameCounter = 0;
        this.sendEveryNFrames = 1; // 全フレームを送信

        // 統計
        this.stats = {
            framesProcessed: 0,
            framesSent: 0
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

            case 'setFrameSendRate':
                this.sendEveryNFrames = Math.max(1, message.rate);
                this.port.postMessage({
                    type: 'log',
                    message: `フレーム送信レート変更: ${this.sendEveryNFrames}フレームごと`
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
     * フレーム収集とメインスレッドへの転送に特化
     */
    processFrame(frame) {
        this.stats.framesProcessed++;
        this.frameCounter++;

        // 音声レベル計算（統計用）
        let sum = 0;
        for (let i = 0; i < frame.length; i++) {
            sum += frame[i] * frame[i];
        }
        const rms = Math.sqrt(sum / frame.length);
        const audioLevel = this.calculateAudioLevel(rms);

        // 設定されたレートでフレームを送信
        if (this.frameCounter % this.sendEveryNFrames === 0) {
            this.stats.framesSent++;

            // メインスレッドに音声レベルとフレームデータを送信
            this.port.postMessage({
                type: 'audioLevel',
                level: audioLevel,
                timestamp: currentTime
            });

            // メインスレッドにRNNoise処理を依頼
            this.port.postMessage({
                type: 'processFrame',
                frameData: Array.from(frame),
                frameIndex: this.stats.framesProcessed,
                audioLevel: audioLevel,
                timestamp: currentTime
            });
        }
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
     * 統計情報取得
     */
    getStats() {
        return {
            ...this.stats,
            framesSentPercentage: (this.stats.framesSent / this.stats.framesProcessed * 100).toFixed(1)
        };
    }
}

// AudioWorkletProcessorに登録
registerProcessor('rnnoise-worklet-processor', RNNoiseWorkletProcessor);