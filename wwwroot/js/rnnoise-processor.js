/**
 * RNNoise WASM Audio Processor
 * 慎重に実装された高品質音声処理クラス
 *
 * 機能:
 * - リアルタイムノイズ除去（RNNoise）
 * - 音声活動検出（VAD）
 * - 音声セグメンテーション
 * - エラーハンドリング
 */
class RNNoiseProcessor {
    constructor() {
        // 初期化状態
        this.wasmModule = null;
        this.denoiseState = null;
        this.isInitialized = false;
        this.isDestroyed = false;
        this.mockMode = false; // WASM使用不可時の模擬モード

        // RNNoise設定（48kHzが推奨）
        this.frameSize = 480; // 10ms @ 48kHz
        this.sampleRate = 48000;

        // VAD設定（循環バッファ対応・高精度VAD用）
        this.vadThreshold = 0.05; // RNNoise VAD闾値を大幅に下げて敏感に
        this.audioLevelThreshold = 0.0005; // 音声レベル闾値も敏感に
        this.silenceFrames = 0;
        this.maxSilenceFrames = 20; // 200ms無音で音声終了（循環バッファで短縮）
        this.speechFrames = 0;
        this.minSpeechFrames = 3; // 30ms以上で音声開始（維持）

        // RNNoise VAD履歴ベースの平滑化（循環バッファ対応）
        this.vadHistory = [];
        this.vadHistorySize = 8; // 80msの履歴（より長い履歴で安定性向上）
        this.vadSmoothThreshold = 0.03; // VAD平滑化闾値

        // 音声レベル履歴ベースの平滑化VAD
        this.audioLevelHistory = [];
        this.audioLevelHistorySize = 5; // 50msの履歴
        this.smoothAudioThreshold = 0.0003; // 平滑化後の音声レベル闾値も敏感に

        // 音声バッファリング
        this.audioBuffer = [];
        this.isRecording = false;
        this.preBuffer = []; // 音声開始前のバッファ
        this.preBufferSize = 10; // 100ms分のプリバッファ

        // 統計情報
        this.stats = {
            framesProcessed: 0,
            voiceSegments: 0,
            totalVoiceTime: 0,
            averageVadProbability: 0,
            lastError: null,
            denoisedFrames: 0,
            vadAccuracy: 0,
            falsePositives: 0,
            missedDetections: 0
        };

        // イベントハンドラー
        this.onVoiceDetected = null;
        this.onVoiceEnded = null;
        this.onAudioLevel = null;
        this.onError = null;
        this.onInitialized = null;

        // デバッグ用
        this.debugMode = false;
        this.debugLog = [];
    }

    /**
     * RNNoise WASM初期化
     * エラーハンドリングを含む慎重な初期化
     */
    async initialize() {
        if (this.isInitialized) {
            return true;
        }

        if (this.isDestroyed) {
            this.logError('破棄されたオブジェクトは初期化できません');
            return false;
        }

        try {

            // WASM使用不可の場合は模擬モードで動作
            if (typeof createRNNWasmModule === 'undefined') {
                this.mockMode = true;
                this.isInitialized = true;

                // 初期化完了イベント
                if (this.onInitialized) {
                    this.onInitialized();
                }
                return true;
            }

            // WASMモジュール初期化
            this.wasmModule = await createRNNWasmModule({
                locateFile: (path) => {
                    if (path.endsWith('.wasm')) {
                        return '/js/rnnoise.wasm';
                    }
                    return path;
                }
            });
            await this.wasmModule.ready;

            // RNNoise関数の存在確認
            const requiredFunctions = ['_rnnoise_create', '_rnnoise_destroy', '_rnnoise_process_frame'];
            for (const funcName of requiredFunctions) {
                if (!this.wasmModule[funcName]) {
                    throw new Error(`必要な関数が見つかりません: ${funcName}`);
                }
            }

            // RNNoise状態初期化
            this.denoiseState = this.wasmModule._rnnoise_create(null);
            if (!this.denoiseState) {
                throw new Error('RNNoise状態の初期化に失敗');
            }

            this.mockMode = false;
            this.isInitialized = true;
            // 初期化完了イベント
            if (this.onInitialized) {
                this.onInitialized();
            }
            return true;

        } catch (error) {
            console.error('[RNNOISE-DEBUG] RNNoiseProcessor初期化エラー:', error);
            console.error('[RNNOISE-DEBUG] エラースタック:', error.stack);
            this.logError('初期化エラー:', error);
            this.stats.lastError = error.message;

            if (this.onError) {
                console.log('[RNNOISE-DEBUG] onErrorコールバック実行中...');
                this.onError(error);
            }

            return false;
        }
    }

    /**
     * 音声フレームを処理（デノイズ + VAD）
     * 慎重なエラーハンドリングとパフォーマンス最適化
     */
    processAudioFrame(inputFrame) {
        if (!this.isInitialized) {
            this.logError('初期化されていません');
            return null;
        }

        if (this.isDestroyed) {
            this.logError('破棄されたオブジェクトは使用できません');
            return null;
        }

        if (!inputFrame || inputFrame.length !== this.frameSize) {
            this.logError(`無効なフレームサイズ: ${inputFrame?.length} (期待値: ${this.frameSize})`);
            return null;
        }

        try {
            let vadProbability, denoisedFrame;

            if (this.mockMode) {
                // 模擬モード: 音声レベルベースのVAD実装
                const audioLevel = this.calculateAudioLevel(inputFrame);
                vadProbability = Math.min(audioLevel * 2.0, 1.0); // 音声レベルを2倍してVAD確率に変換
                denoisedFrame = new Float32Array(inputFrame); // ノイズ除去なしでそのまま使用

                if (this.stats.framesProcessed % 10 === 0) { // より頻繁にログ出力
                    this.log(`模擬モード - AudioLevel: ${(audioLevel * 100).toFixed(1)}%, 推定VAD: ${(vadProbability * 100).toFixed(1)}%`);
                }
            } else {
                // 通常のRNNoise処理
                const inputPtr = this.wasmModule._malloc(this.frameSize * 4);
                const outputPtr = this.wasmModule._malloc(this.frameSize * 4);

                if (!inputPtr || !outputPtr) {
                    throw new Error('メモリ確保に失敗');
                }

                try {
                    // 入力データをWASMメモリにコピー（音声レベル正規化）
                    const inputHeap = new Float32Array(
                        this.wasmModule.HEAPF32.buffer,
                        inputPtr,
                        this.frameSize
                    );

                    // 音声レベルを適切にスケール（RNNoise用）
                    // 現在の最大値: 0.003074 → 目標: 0.1-0.5程度
                    let maxSample = 0;
                    for (let i = 0; i < inputFrame.length; i++) {
                        maxSample = Math.max(maxSample, Math.abs(inputFrame[i]));
                    }

                    if (maxSample > 0.0001) {
                        // 最大値を1.0にスケール（RNNoise VAD用）
                        const scaleFactor = 1.0 / maxSample;
                        for (let i = 0; i < inputFrame.length; i++) {
                            inputHeap[i] = inputFrame[i] * scaleFactor;
                        }

                    } else {
                        inputHeap.set(inputFrame);
                    }

                    // RNNoiseでノイズ除去実行
                    this.wasmModule._rnnoise_process_frame(
                        this.denoiseState,
                        outputPtr,
                        inputPtr
                    );

                    // デノイズ済み音声データを取得
                    const outputHeap = new Float32Array(
                        this.wasmModule.HEAPF32.buffer,
                        outputPtr,
                        this.frameSize
                    );

                    // 音量ベースVAD: デノイズ済み音声の音量で判定
                    const denoisedLevel = this.calculateAudioLevel(outputHeap);
                    vadProbability = Math.min(denoisedLevel * 10.0, 1.0);

                    denoisedFrame = new Float32Array(outputHeap);

                } finally {
                    // メモリ解放（必ず実行）
                    if (inputPtr) this.wasmModule._free(inputPtr);
                    if (outputPtr) this.wasmModule._free(outputPtr);
                }
            }

            // 統計更新
            this.stats.framesProcessed++;
            this.stats.averageVadProbability =
                (this.stats.averageVadProbability * (this.stats.framesProcessed - 1) + vadProbability)
                / this.stats.framesProcessed;
            this.stats.denoisedFrames++;

            // VAD判定と音声レベル計算
            const audioLevel = this.calculateAudioLevel(denoisedFrame);
            const isSpeech = this.enhancedVADJudgment(vadProbability, audioLevel);

            // 音声レベル通知
            if (this.onAudioLevel) {
                this.onAudioLevel(audioLevel, isSpeech, vadProbability);
            }

            // VAD状態管理
            const voiceResult = this.updateEnhancedVADState(isSpeech, denoisedFrame, vadProbability, audioLevel);

            return voiceResult;

        } catch (error) {
            this.logError('フレーム処理エラー:', error);
            this.stats.lastError = error.message;

            if (this.onError) {
                this.onError(error);
            }

            return null;
        }
    }

    /**
     * 音声レベルベースVAD判定（RNNoise VADの代替システム）
     * RNNoiseのVADは使用せず、音声レベルのみで高精度判定
     */
    enhancedVADJudgment(vadProbability, audioLevel) {
        // 音声レベル履歴を更新
        this.audioLevelHistory.push(audioLevel);
        if (this.audioLevelHistory.length > this.audioLevelHistorySize) {
            this.audioLevelHistory.shift();
        }

        // 基本判定: 現在の音声レベル
        const basicVAD = audioLevel > this.audioLevelThreshold;

        // 平滑化判定: 最近の音声レベルの平均
        const avgLevel = this.audioLevelHistory.reduce((sum, val) => sum + val, 0) / this.audioLevelHistory.length;
        const smoothVAD = avgLevel > this.smoothAudioThreshold;

        // スパイク検出: 急激なレベル変化で音声開始を検出
        const prevLevel = this.audioLevelHistory.length > 1 ? this.audioLevelHistory[this.audioLevelHistory.length - 2] : 0;
        const spikeDetection = audioLevel > prevLevel * 2 && audioLevel > 0.01;

        // 総合判定: 音声レベルベースの高精度VAD
        const enhancedResult = basicVAD || smoothVAD || spikeDetection;

        // デバッグ用に簡潔ログ（本格運用時はコメントアウト可）
        if (this.debugMode && this.stats.framesProcessed % 20 === 0) {
            console.log(`[AudioLevel-VAD] Level: ${(audioLevel*100).toFixed(1)}%, Avg: ${(avgLevel*100).toFixed(1)}%, Spike: ${spikeDetection}, Result: ${enhancedResult}`);
        }

        return enhancedResult;
    }

    /**
     * 強化されたVAD状態管理と音声データバッファリング
     * RNNoise処理後の高精度VADに最適化された状態管理
     */
    updateEnhancedVADState(isSpeech, audioFrame, vadProbability, audioLevel) {
        // プリバッファに常に追加（メモリリーク防止）
        this.preBuffer.push({
            frame: new Float32Array(audioFrame),
            vad: vadProbability,
            audioLevel: audioLevel,
            enhancedVAD: isSpeech,
            timestamp: Date.now()
        });

        if (this.preBuffer.length > this.preBufferSize) {
            this.preBuffer.shift();
        }

        if (isSpeech) {
            this.speechFrames++;
            this.silenceFrames = 0;

            // 音声開始判定（強化されたVADに基づく）
            if (!this.isRecording && this.speechFrames >= this.minSpeechFrames) {
                this.isRecording = true;

                // プリバッファを含めて録音開始（強化されたデータ構造）
                this.audioBuffer = this.preBuffer.map(item => ({
                    frame: new Float32Array(item.frame),
                    vad: item.vad,
                    audioLevel: item.audioLevel || 0,
                    enhancedVAD: item.enhancedVAD || false,
                    timestamp: item.timestamp
                }));

                this.stats.voiceSegments++;
                this.log(`音声開始検出 (セグメント #${this.stats.voiceSegments})`);

                if (this.onVoiceDetected) {
                    this.onVoiceDetected();
                }
            }

            // 録音中は音声データを蓄積（強化されたデータ構造）
            if (this.isRecording) {
                this.audioBuffer.push({
                    frame: new Float32Array(audioFrame),
                    vad: vadProbability,
                    audioLevel: audioLevel,
                    enhancedVAD: isSpeech,
                    timestamp: Date.now()
                });
            }

        } else {
            this.silenceFrames++;
            this.speechFrames = Math.max(0, this.speechFrames - 1);

            // 音声終了判定
            if (this.isRecording && this.silenceFrames >= this.maxSilenceFrames) {
                const recordedAudio = this.finalizeRecording();

                this.log(`音声終了検出: ${recordedAudio.length}フレーム, ${recordedAudio.length * 10}ms`);

                // onVoiceEndedはprocessAudioFrameの戻り値で処理するため呼び出さない
                // if (this.onVoiceEnded) {
                //     this.onVoiceEnded(recordedAudio);
                // }

                return recordedAudio;
            }

            // 録音中は無音も含める（自然な話し方に対応、強化されたデータ構造）
            if (this.isRecording) {
                this.audioBuffer.push({
                    frame: new Float32Array(audioFrame),
                    vad: vadProbability,
                    audioLevel: audioLevel,
                    enhancedVAD: isSpeech,
                    timestamp: Date.now()
                });
            }
        }

        return null;
    }

    /**
     * 録音終了処理（強化されたバージョン）
     */
    finalizeRecording() {
        const recordedAudio = this.audioBuffer;
        const voiceTime = recordedAudio.length * 10; // ms

        this.stats.totalVoiceTime += voiceTime;

        // 録音品質統計の計算
        const avgVAD = recordedAudio.reduce((sum, frame) => sum + (frame.vad || 0), 0) / recordedAudio.length;
        const avgAudioLevel = recordedAudio.reduce((sum, frame) => sum + (frame.audioLevel || 0), 0) / recordedAudio.length;
        const speechFrames = recordedAudio.filter(frame => frame.enhancedVAD).length;
        const speechRatio = speechFrames / recordedAudio.length;

        this.audioBuffer = [];
        this.isRecording = false;
        this.speechFrames = 0;
        this.silenceFrames = 0;

        this.log(`録音終了: ${voiceTime}ms, VAD平均: ${(avgVAD * 100).toFixed(1)}%, 音声比率: ${(speechRatio * 100).toFixed(1)}%, レベル平均: ${(avgAudioLevel * 100).toFixed(1)}%`);

        return recordedAudio;
    }

    /**
     * 音声レベル計算（RMS + dB変換） - デバッグ強化版
     */
    calculateAudioLevel(audioFrame) {
        if (!audioFrame || audioFrame.length === 0) {
            this.log('音声フレームが空です');
            return 0;
        }

        let sum = 0;
        let maxValue = 0;
        let nonZeroSamples = 0;

        for (let i = 0; i < audioFrame.length; i++) {
            const sample = audioFrame[i];
            sum += sample * sample;
            maxValue = Math.max(maxValue, Math.abs(sample));
            if (Math.abs(sample) > 0.001) { // 実質的に非ゼロ
                nonZeroSamples++;
            }
        }

        const rms = Math.sqrt(sum / audioFrame.length);

        // より感度の良いレベル計算
        if (rms > 1e-10) { // より小さい値まで検出
            // 線形スケールでも計算（比較用）
            const linearLevel = Math.min(rms * 100, 1.0);

            // dB変換（-80dB～0dBを0～1にマッピング、範囲を拡大）
            const db = 20 * Math.log10(rms);
            const dbLevel = Math.max(0, Math.min(1, (db + 80) / 80));

            // より高い感度を持つ方を採用
            const finalLevel = Math.max(linearLevel, dbLevel);

            return finalLevel;
        }

        return 0;
    }

    /**
     * 48kHzから16kHzにダウンサンプリング
     */
    downsampleTo16kHz(audioFrames) {
        try {
            // 48kHz → 16kHz = 3:1 の間引き処理
            const ratio = 3;
            const downsampledFrames = [];

            for (const frameData of audioFrames) {
                const frame = frameData.frame || frameData;
                const downsampledFrame = new Float32Array(Math.floor(frame.length / ratio));

                for (let i = 0; i < downsampledFrame.length; i++) {
                    downsampledFrame[i] = frame[i * ratio];
                }

                downsampledFrames.push({
                    ...frameData,
                    frame: downsampledFrame
                });
            }

            this.log(`ダウンサンプリング完了: 48kHz → 16kHz`);
            return downsampledFrames;
        } catch (error) {
            this.logError('ダウンサンプリングエラー:', error);
            return audioFrames; // フォールバック
        }
    }

    /**
     * Float32Array配列をWAVバイナリに変換（16kHzで出力）
     * 高品質な音声変換 + サンプルレート変換
     */
    convertToWav(audioFrames) {
        if (!audioFrames || audioFrames.length === 0) {
            this.logError('音声フレームが空です');
            return null;
        }

        try {
            // 48kHzから16kHzにダウンサンプリング
            const downsampledFrames = this.downsampleTo16kHz(audioFrames);
            const targetSampleRate = 16000; // AmiVoice推奨サンプルレート

            // ダウンサンプリング後のサンプル数計算
            const totalSamples = downsampledFrames.reduce((total, frameData) => {
                const frame = frameData.frame || frameData;
                return total + frame.length;
            }, 0);

            const wavBuffer = new ArrayBuffer(44 + totalSamples * 2);
            const view = new DataView(wavBuffer);

            // WAVヘッダー作成（16kHz設定）
            const writeString = (offset, string) => {
                for (let i = 0; i < string.length; i++) {
                    view.setUint8(offset + i, string.charCodeAt(i));
                }
            };

            // RIFFヘッダー
            writeString(0, 'RIFF');
            view.setUint32(4, 36 + totalSamples * 2, true);
            writeString(8, 'WAVE');
            writeString(12, 'fmt ');
            view.setUint32(16, 16, true);
            view.setUint16(20, 1, true); // PCM
            view.setUint16(22, 1, true); // モノラル
            view.setUint32(24, targetSampleRate, true);
            view.setUint32(28, targetSampleRate * 2, true);
            view.setUint16(32, 2, true);
            view.setUint16(34, 16, true);
            writeString(36, 'data');
            view.setUint32(40, totalSamples * 2, true);

            // 音声データ変換（Float32 → Int16, 16kHz）
            let offset = 44;
            for (const frameData of downsampledFrames) {
                const frame = frameData.frame || frameData;
                for (let i = 0; i < frame.length; i++) {
                    const sample = Math.max(-1, Math.min(1, frame[i]));
                    const intSample = Math.floor(sample * 32767);
                    view.setInt16(offset, intSample, true);
                    offset += 2;
                }
            }

            this.log(`WAV変換完了: ${totalSamples}サンプル @ 16kHz, ${wavBuffer.byteLength}バイト`);
            return new Uint8Array(wavBuffer);

        } catch (error) {
            this.logError('WAV変換エラー:', error);
            return null;
        }
    }

    /**
     * 設定変更メソッド
     */
    setVADThreshold(threshold) {
        this.vadThreshold = Math.max(0, Math.min(1, threshold));
        this.log(`VAD閾値変更: ${this.vadThreshold}`);
    }

    setVADSmoothThreshold(threshold) {
        this.vadSmoothThreshold = Math.max(0, Math.min(1, threshold));
        this.log(`VAD平滑化閾値変更: ${this.vadSmoothThreshold}`);
    }

    setSilenceTimeout(timeoutMs) {
        this.maxSilenceFrames = Math.floor(timeoutMs / 10);
        this.log(`無音タイムアウト変更: ${timeoutMs}ms (${this.maxSilenceFrames}フレーム)`);
    }

    setVADHistorySize(size) {
        this.vadHistorySize = Math.max(1, Math.min(20, size));
        if (this.vadHistory) {
            this.vadHistory = this.vadHistory.slice(-this.vadHistorySize);
        }
        this.log(`VAD履歴サイズ変更: ${this.vadHistorySize}フレーム`);
    }

    setDebugMode(enabled) {
        this.debugMode = enabled;
        if (!enabled) {
            this.debugLog = [];
        }
        this.log(`デバッグモード: ${enabled}`);
    }

    /**
     * 統計情報取得（強化されたバージョン）
     */
    getStats() {
        const currentVADAvg = this.vadHistory && this.vadHistory.length > 0
            ? this.vadHistory.reduce((sum, val) => sum + val, 0) / this.vadHistory.length
            : 0;

        return {
            ...this.stats,
            isInitialized: this.isInitialized,
            isRecording: this.isRecording,
            vadThreshold: this.vadThreshold,
            vadSmoothThreshold: this.vadSmoothThreshold || this.vadThreshold * 0.8,
            currentVADAverage: currentVADAvg,
            vadHistoryLength: this.vadHistory ? this.vadHistory.length : 0,
            silenceFrames: this.silenceFrames,
            speechFrames: this.speechFrames,
            preBufferLength: this.preBuffer ? this.preBuffer.length : 0,
            audioBufferLength: this.audioBuffer ? this.audioBuffer.length : 0,
            debugLogEntries: this.debugLog.length
        };
    }

    /**
     * デバッグ情報取得
     */
    getDebugLog() {
        return [...this.debugLog];
    }

    clearDebugLog() {
        this.debugLog = [];
    }

    /**
     * ログ出力
     */
    log(message) {
        // 一時的にログを常に有効化してデバッグ
        const logEntry = `[RNNoiseProcessor] ${new Date().toISOString()}: ${message}`;
        console.log(logEntry);
        this.debugLog.push(logEntry);
        if (this.debugLog.length > 1000) {
            this.debugLog.shift();
        }
    }

    logError(message, error = null) {
        const errorMessage = error ? `${message} ${error.message}` : message;
        const logEntry = `[RNNoiseProcessor ERROR] ${new Date().toISOString()}: ${errorMessage}`;
        console.error(logEntry);

        if (this.debugMode) {
            this.debugLog.push(logEntry);
            if (this.debugLog.length > 1000) {
                this.debugLog.shift();
            }
        }
    }

    /**
     * リソース解放
     * 必ずメモリリークを防止
     */
    destroy() {
        if (this.isDestroyed) {
            return;
        }

        this.log('リソース解放開始...');

        try {
            // RNNoise状態解放
            if (this.denoiseState && this.wasmModule && this.wasmModule._rnnoise_destroy) {
                this.wasmModule._rnnoise_destroy(this.denoiseState);
                this.denoiseState = null;
            }

            // バッファクリア
            this.audioBuffer = [];
            this.preBuffer = [];
            this.debugLog = [];

            // 状態リセット
            this.isInitialized = false;
            this.isDestroyed = true;
            this.wasmModule = null;

            this.log('リソース解放完了');

        } catch (error) {
            this.logError('リソース解放エラー:', error);
        }
    }
}

// グローバルエクスポート
if (typeof window !== 'undefined') {
    window.RNNoiseProcessor = RNNoiseProcessor;
}

// Node.js環境対応
if (typeof module !== 'undefined' && module.exports) {
    module.exports = RNNoiseProcessor;
}