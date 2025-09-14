/**
 * RNNoise AudioWorklet Processor
 * メインスレッドをブロックしない高性能音声処理
 *
 * AudioWorkletGlobalScopeで動作するため、制限された環境での実装
 */

class RNNoiseWorkletProcessor extends AudioWorkletProcessor {
    constructor(options) {
        super();

        console.log('[RNNoiseWorkletProcessor] 循環バッファ版初期化開始');

        // RNNoise設定
        this.frameSize = 480; // 10ms @ 48kHz
        this.sampleRate = 48000;

        // 循環バッファ設定（Jitsi方式）
        this.bufferSize = this.frameSize * 4; // 1920サンプル（4フレーム分）
        this.circularBuffer = new Float32Array(this.bufferSize);
        this.writeIndex = 0; // 書き込みポインタ
        this.readIndex = 0;  // 読み込みポインタ
        this.availableSamples = 0; // 利用可能サンプル数

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
     * 音声処理メイン関数（循環バッファ版）
     * 高性能な循環バッファによるリアルタイム音声処理
     */
    process(inputs, outputs, parameters) {
        const input = inputs[0];
        if (!input || !input[0] || !this.isWasmReady) {
            return true;
        }

        const inputData = input[0];

        // 循環バッファに書き込み
        this.writeToCircularBuffer(inputData);

        // 480サンプルフレームが利用可能な間、処理を続行
        while (this.availableSamples >= this.frameSize) {
            const frame = this.readFrameFromCircularBuffer();
            if (frame) {
                this.processFrame(frame);
            }
        }

        return true;
    }

    /**
     * 循環バッファへの書き込み（高性能版）
     */
    writeToCircularBuffer(inputData) {
        const inputLength = inputData.length;

        for (let i = 0; i < inputLength; i++) {
            this.circularBuffer[this.writeIndex] = inputData[i];
            this.writeIndex = (this.writeIndex + 1) % this.bufferSize;
            this.availableSamples = Math.min(this.availableSamples + 1, this.bufferSize);

            // バッファオーバーフロー防止
            if (this.availableSamples === this.bufferSize) {
                this.readIndex = (this.readIndex + 1) % this.bufferSize;
                this.availableSamples--;

                // オーバーフロー警告（デバッグ用）
                if (this.stats.framesProcessed % 1000 === 0) {
                    this.port.postMessage({
                        type: 'log',
                        message: '循環バッファオーバーフロー: データ処理が追いついていません'
                    });
                }
            }
        }
    }

    /**
     * 循環バッファからのフレーム読み込み（連続性保証）
     */
    readFrameFromCircularBuffer() {
        if (this.availableSamples < this.frameSize) {
            return null;
        }

        const frame = new Float32Array(this.frameSize);

        for (let i = 0; i < this.frameSize; i++) {
            frame[i] = this.circularBuffer[this.readIndex];
            this.readIndex = (this.readIndex + 1) % this.bufferSize;
        }

        this.availableSamples -= this.frameSize;
        return frame;
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
     * 統計情報取得（循環バッファ版）
     */
    getStats() {
        return {
            ...this.stats,
            framesSentPercentage: (this.stats.framesSent / this.stats.framesProcessed * 100).toFixed(1),
            circularBuffer: {
                bufferSize: this.bufferSize,
                availableSamples: this.availableSamples,
                writeIndex: this.writeIndex,
                readIndex: this.readIndex,
                bufferUtilization: ((this.availableSamples / this.bufferSize) * 100).toFixed(1) + '%'
            }
        };
    }
}

// AudioWorkletProcessorに登録
registerProcessor('rnnoise-worklet-processor', RNNoiseWorkletProcessor);