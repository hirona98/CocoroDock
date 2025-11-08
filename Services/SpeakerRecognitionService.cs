using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CocoroDock.Services
{
    /// <summary>
    /// WeSpeaker話者識別サービス
    /// SileroVadServiceのパターンを踏襲（共有モデル + スレッドセーフ）
    /// </summary>
    public class SpeakerRecognitionService : IDisposable
    {
        // 共有リソース
        private static InferenceSession? _sharedModel;
        private static readonly object _modelLock = new object();

        // インスタンス設定
        private readonly string _dbPath;
        private readonly float _threshold;

        // 定数
        private const int EMBEDDING_DIM = 256; // WeSpeaker ResNet34
        private const int SAMPLE_RATE = 16000;
        private const int TARGET_SAMPLES = SAMPLE_RATE * 3; // 3秒

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="dbPath">SQLiteデータベースのパス</param>
        /// <param name="threshold">識別閾値（0.5-0.9推奨、デフォルト0.6）</param>
        public SpeakerRecognitionService(string dbPath, float threshold = 0.6f)
        {
            _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
            _threshold = threshold;

            EnsureModelLoaded();
            InitializeDatabase();

            System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] Initialized (threshold: {threshold:F2}, db: {dbPath})");
        }

        /// <summary>
        /// ONNXモデルの初期化（共有リソース）
        /// </summary>
        private static void EnsureModelLoaded()
        {
            if (_sharedModel != null) return;

            lock (_modelLock)
            {
                if (_sharedModel != null) return;

                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var resourceName = "CocoroDock.Resource.wespeaker_resnet34.onnx";

                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                        throw new FileNotFoundException($"Embedded resource '{resourceName}' not found. WeSpeaker ONNXモデルをCocoroDock/Resource/に配置してください。");

                    var modelData = new byte[stream.Length];
                    stream.Read(modelData, 0, modelData.Length);

                    var options = new SessionOptions
                    {
                        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                        InterOpNumThreads = 1,
                        IntraOpNumThreads = 1
                    };

                    _sharedModel = new InferenceSession(modelData, options);
                    System.Diagnostics.Debug.WriteLine("[SpeakerRecognition] Shared model loaded");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] Model load error: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// SQLiteデータベースの初期化
        /// </summary>
        private void InitializeDatabase()
        {
            try
            {
                var directory = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                var createTableSql = @"
                    CREATE TABLE IF NOT EXISTS speakers (
                        speaker_id TEXT PRIMARY KEY,
                        speaker_name TEXT NOT NULL,
                        embedding BLOB NOT NULL,
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_speaker_name ON speakers(speaker_name);
                ";

                using var command = new SqliteCommand(createTableSql, connection);
                command.ExecuteNonQuery();

                System.Diagnostics.Debug.WriteLine("[SpeakerRecognition] Database initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] Database init error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 音声から埋め込みベクトルを抽出
        /// </summary>
        /// <param name="wavAudio">WAV形式の音声データ（ヘッダー含む）</param>
        /// <returns>256次元の正規化済み埋め込みベクトル</returns>
        public float[] ExtractEmbedding(byte[] wavAudio)
        {
            if (wavAudio == null || wavAudio.Length == 0)
                throw new ArgumentException("音声データが空です", nameof(wavAudio));

            // 1. WAVヘッダー(44バイト)除去してfloat配列に変換
            var samples = ConvertWavToFloat(wavAudio);

            // 2. Fbank特徴量を抽出
            var fbankExtractor = new FbankExtractor();
            var features = fbankExtractor.ExtractFeatures(samples); // [num_frames, 80]

            // 3. ONNX推論
            lock (_modelLock)
            {
                if (_sharedModel == null)
                    throw new InvalidOperationException("ONNXモデルがロードされていません");

                // 特徴量を1次元配列に変換してテンソル作成
                int numFrames = features.GetLength(0);
                int numBins = features.GetLength(1); // 80

                var featureArray = new float[numFrames * numBins];
                for (int i = 0; i < numFrames; i++)
                {
                    for (int j = 0; j < numBins; j++)
                    {
                        featureArray[i * numBins + j] = features[i, j];
                    }
                }

                var inputTensor = new DenseTensor<float>(featureArray, new[] { 1, numFrames, numBins });
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("feats", inputTensor)
                };

                using var results = _sharedModel.Run(inputs);
                var embedding = results.First(r => r.Name == "embs")
                    .AsEnumerable<float>()
                    .ToArray();

                if (embedding.Length != EMBEDDING_DIM)
                    throw new InvalidOperationException($"埋め込み次元が不正です: {embedding.Length} (期待値: {EMBEDDING_DIM})");

                // 4. L2正規化（コサイン類似度計算用）
                return NormalizeEmbedding(embedding);
            }
        }

        /// <summary>
        /// WAV音声データをfloat配列に変換
        /// </summary>
        private float[] ConvertWavToFloat(byte[] wavAudio)
        {
            // WAVヘッダー(44バイト)をスキップ
            const int headerSize = 44;
            if (wavAudio.Length < headerSize)
                throw new ArgumentException("WAVデータが短すぎます", nameof(wavAudio));

            var audioData = wavAudio.Skip(headerSize).ToArray();
            var sampleCount = audioData.Length / 2; // 16bit = 2bytes
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                var sample = BitConverter.ToInt16(audioData, i * 2);
                samples[i] = sample / 32768f; // -1.0 ~ 1.0に正規化
            }

            return samples;
        }

        /// <summary>
        /// 音声長を調整（パディングまたはクロップ）
        /// </summary>
        private float[] AdjustAudioLength(float[] samples, int targetLength)
        {
            if (samples.Length == targetLength)
                return samples;

            if (samples.Length < targetLength)
            {
                // パディング（ゼロ埋め）
                var padded = new float[targetLength];
                Array.Copy(samples, padded, samples.Length);
                return padded;
            }
            else
            {
                // クロップ（前方から切り出し）
                var cropped = new float[targetLength];
                Array.Copy(samples, cropped, targetLength);
                return cropped;
            }
        }

        /// <summary>
        /// L2正規化
        /// </summary>
        private float[] NormalizeEmbedding(float[] embedding)
        {
            var norm = (float)Math.Sqrt(embedding.Sum(x => x * x));
            if (norm < 1e-6f)
                throw new InvalidOperationException("埋め込みベクトルのノルムがゼロです");

            var normalized = new float[embedding.Length];
            for (int i = 0; i < embedding.Length; i++)
            {
                normalized[i] = embedding[i] / norm;
            }
            return normalized;
        }

        /// <summary>
        /// 話者を登録
        /// </summary>
        /// <param name="speakerId">話者ID（UUID推奨）</param>
        /// <param name="speakerName">話者名（表示用）</param>
        /// <param name="audioSample">登録用音声サンプル（WAV形式、3秒以上推奨）</param>
        public void RegisterSpeaker(string speakerId, string speakerName, byte[] audioSample)
        {
            if (string.IsNullOrEmpty(speakerId))
                throw new ArgumentException("話者IDが空です", nameof(speakerId));
            if (string.IsNullOrEmpty(speakerName))
                throw new ArgumentException("話者名が空です", nameof(speakerName));
            if (audioSample == null || audioSample.Length == 0)
                throw new ArgumentException("音声サンプルが空です", nameof(audioSample));

            try
            {
                // 埋め込みベクトル抽出
                var embedding = ExtractEmbedding(audioSample);

                // float配列をbyte配列に変換
                var embeddingBytes = new byte[embedding.Length * sizeof(float)];
                Buffer.BlockCopy(embedding, 0, embeddingBytes, 0, embeddingBytes.Length);

                // SQLiteに保存
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                var now = DateTime.UtcNow.ToString("o"); // ISO8601形式
                var insertSql = @"
                    INSERT OR REPLACE INTO speakers (speaker_id, speaker_name, embedding, created_at, updated_at)
                    VALUES (@speakerId, @speakerName, @embedding, @createdAt, @updatedAt)
                ";

                using var command = new SqliteCommand(insertSql, connection);
                command.Parameters.AddWithValue("@speakerId", speakerId);
                command.Parameters.AddWithValue("@speakerName", speakerName);
                command.Parameters.AddWithValue("@embedding", embeddingBytes);
                command.Parameters.AddWithValue("@createdAt", now);
                command.Parameters.AddWithValue("@updatedAt", now);
                command.ExecuteNonQuery();

                System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] Speaker registered: {speakerName} ({speakerId})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] RegisterSpeaker error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 登録済み話者が存在するかを確認
        /// </summary>
        public bool HasRegisteredSpeakers()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                var countSql = "SELECT 1 FROM speakers LIMIT 1";
                using var command = new SqliteCommand(countSql, connection);
                using var reader = command.ExecuteReader();

                return reader.Read();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] HasRegisteredSpeakers error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 音声から話者を識別
        /// </summary>
        /// <param name="wavAudio">識別対象の音声データ（WAV形式）</param>
        /// <returns>(話者ID, 話者名, 信頼度)</returns>
        public (string speakerId, string speakerName, float confidence) IdentifySpeaker(byte[] wavAudio)
        {
            try
            {
                // 1. クエリ音声から埋め込み抽出
                var queryEmbedding = ExtractEmbedding(wavAudio);

                // 2. DBから全登録話者を取得
                var registeredSpeakers = LoadAllEmbeddings();

                // 登録話者がゼロの場合は異常として停止
                if (registeredSpeakers.Count == 0)
                    throw new InvalidOperationException("話者が一人も登録されていません。先に話者を登録してください。");

                // 3. コサイン類似度計算（並列処理）
                var (bestId, bestName, maxSimilarity) = registeredSpeakers
                    .AsParallel()
                    .Select(s => (s.id, s.name, sim: CosineSimilarity(queryEmbedding, s.embedding)))
                    .OrderByDescending(x => x.sim)
                    .First();

                // 4. 閾値判定（識別失敗は異常として停止）
                if (maxSimilarity < _threshold)
                    throw new InvalidOperationException($"話者を識別できませんでした（最高類似度: {maxSimilarity:F2} < 閾値: {_threshold:F2}）。話者登録を追加するか閾値を調整してください。");

                System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] Identified: {bestName} ({maxSimilarity:F2})");
                return (bestId, bestName, maxSimilarity);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] IdentifySpeaker error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// DBから全登録話者の埋め込みを読み込み
        /// </summary>
        private List<(string id, string name, float[] embedding)> LoadAllEmbeddings()
        {
            var result = new List<(string, string, float[])>();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var selectSql = "SELECT speaker_id, speaker_name, embedding FROM speakers";
            using var command = new SqliteCommand(selectSql, connection);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                var embeddingBytes = (byte[])reader.GetValue(2);

                var embedding = new float[EMBEDDING_DIM];
                Buffer.BlockCopy(embeddingBytes, 0, embedding, 0, embeddingBytes.Length);

                result.Add((id, name, embedding));
            }

            return result;
        }

        /// <summary>
        /// コサイン類似度計算（L2正規化済みベクトル用）
        /// </summary>
        private float CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                throw new ArgumentException("ベクトルの次元が一致しません");

            return a.Zip(b, (x, y) => x * y).Sum(); // L2正規化済みのため内積のみ
        }

        /// <summary>
        /// 登録済み話者一覧を取得
        /// </summary>
        public List<(string speakerId, string speakerName)> GetRegisteredSpeakers()
        {
            try
            {
                var result = new List<(string, string)>();

                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                var selectSql = "SELECT speaker_id, speaker_name FROM speakers ORDER BY speaker_name";
                using var command = new SqliteCommand(selectSql, connection);
                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    result.Add((reader.GetString(0), reader.GetString(1)));
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] GetRegisteredSpeakers error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 話者を削除
        /// </summary>
        public void DeleteSpeaker(string speakerId)
        {
            if (string.IsNullOrEmpty(speakerId))
                throw new ArgumentException("話者IDが空です", nameof(speakerId));

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                var deleteSql = "DELETE FROM speakers WHERE speaker_id = @speakerId";
                using var command = new SqliteCommand(deleteSql, connection);
                command.Parameters.AddWithValue("@speakerId", speakerId);
                var rowsAffected = command.ExecuteNonQuery();

                if (rowsAffected > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] Speaker deleted: {speakerId}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] Speaker not found: {speakerId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SpeakerRecognition] DeleteSpeaker error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            // インスタンス固有のリソースは特になし
            System.Diagnostics.Debug.WriteLine("[SpeakerRecognition] Disposed");
        }

        /// <summary>
        /// 共有リソースの解放（アプリケーション終了時）
        /// </summary>
        public static void DisposeSharedResources()
        {
            lock (_modelLock)
            {
                _sharedModel?.Dispose();
                _sharedModel = null;
                System.Diagnostics.Debug.WriteLine("[SpeakerRecognition] Shared resources disposed");
            }
        }
    }
}
