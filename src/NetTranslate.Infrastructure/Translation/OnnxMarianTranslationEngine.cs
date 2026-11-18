using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using NetTranslate.Application.Abstractions;
using NetTranslate.Infrastructure.Translation.Marian;

namespace NetTranslate.Infrastructure.Translation;

/// <summary>
/// ITranslationEngine dùng các model MarianMT (ONNX) trong thư mục Models:Path.
/// Mỗi thư mục con là một cặp ngôn ngữ; model được nạp lười (lần dùng đầu tiên) và giữ trong RAM.
/// </summary>
public sealed class OnnxMarianTranslationEngine : ITranslationEngine, IDisposable
{
    private sealed class Entry(string directory, MarianConfig config)
    {
        public string Directory { get; } = directory;
        public MarianConfig Config { get; } = config;
        public MarianOnnxModel? Model;
        public string? Error;
        public readonly Lock LoadLock = new();
    }

    private readonly ModelOptions _options;
    private readonly ILogger<OnnxMarianTranslationEngine> _logger;
    private readonly SemaphoreSlim _concurrency;
    private readonly SessionOptions _sessionOptions;
    private readonly string _modelsPath;
    private readonly Lock _reloadLock = new();
    private Dictionary<(string, string), Entry> _entries = new();

    public OnnxMarianTranslationEngine(
        IOptions<ModelOptions> options,
        IHostEnvironment environment,
        ILogger<OnnxMarianTranslationEngine> logger)
    {
        _options = options.Value;
        _logger = logger;

        _modelsPath = _options.ResolvePath(environment.ContentRootPath);

        var concurrency = _options.MaxConcurrency > 0
            ? _options.MaxConcurrency
            : Math.Max(1, Environment.ProcessorCount / 2);
        _concurrency = new SemaphoreSlim(concurrency, concurrency);

        _sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        if (_options.IntraOpThreads > 0) _sessionOptions.IntraOpNumThreads = _options.IntraOpThreads;

        Reload();

        if (_options.Preload)
            foreach (var entry in _entries.Values)
            {
                try { GetModel(entry); }
                catch { /* đã log trong GetModel, model lỗi sẽ bị ẩn khỏi danh sách */ }
            }
    }

    public void Reload()
    {
        lock (_reloadLock)
        {
            var entries = new Dictionary<(string, string), Entry>();
            if (!System.IO.Directory.Exists(_modelsPath))
            {
                _logger.LogWarning("Models directory {Path} does not exist. No language will be available.", _modelsPath);
            }
            else
            {
                foreach (var dir in System.IO.Directory.GetDirectories(_modelsPath))
                {
                    var hasEncoder = File.Exists(System.IO.Path.Combine(dir, "encoder_model.onnx"))
                        || File.Exists(System.IO.Path.Combine(dir, "encoder_model_quantized.onnx"))
                        || File.Exists(System.IO.Path.Combine(dir, "onnx", "encoder_model.onnx"))
                        || File.Exists(System.IO.Path.Combine(dir, "onnx", "encoder_model_quantized.onnx"));
                    if (!hasEncoder) continue;

                    var config = MarianConfig.TryLoad(dir);
                    if (config is null)
                    {
                        _logger.LogWarning("Skip {Dir}: cannot infer language pair (name it 'en-vi' or add model.json)", dir);
                        continue;
                    }

                    var key = (config.Source, config.Target);
                    // Giữ lại model đã nạp nếu thư mục không đổi
                    if (_entries.TryGetValue(key, out var old) && old.Directory == dir)
                        entries[key] = old;
                    else
                        entries[key] = new Entry(dir, config);
                }
            }

            // Giải phóng model không còn dùng
            foreach (var (key, old) in _entries)
                if (!entries.TryGetValue(key, out var kept) || !ReferenceEquals(kept, old))
                    old.Model?.Dispose();

            _entries = entries;
            _logger.LogInformation("Found {Count} translation models in {Path}: {Pairs}",
                entries.Count, _modelsPath, string.Join(", ", entries.Keys.Select(k => $"{k.Item1}-{k.Item2}")));
        }
    }

    public void Unload(string folderName)
    {
        lock (_reloadLock)
        {
            var entries = new Dictionary<(string, string), Entry>(_entries);
            foreach (var (key, entry) in _entries)
            {
                if (!string.Equals(System.IO.Path.GetFileName(entry.Directory), folderName, StringComparison.OrdinalIgnoreCase))
                    continue;
                entries.Remove(key);
                lock (entry.LoadLock)
                {
                    entry.Model?.Dispose();
                    entry.Model = null;
                }
            }
            _entries = entries;
        }
    }

    public IReadOnlyList<LanguagePair> GetAvailablePairs() =>
        _entries.Values
            .Where(e => e.Error is null)
            .Select(e => new LanguagePair(e.Config.Source, e.Config.Target))
            .ToList();

    public IReadOnlyList<ModelInfo> GetModels() =>
        _entries.Values
            .OrderBy(e => e.Config.Source).ThenBy(e => e.Config.Target)
            .Select(e => new ModelInfo(
                e.Config.Source,
                e.Config.Target,
                System.IO.Path.GetFileName(e.Directory),
                e.Model is not null,
                e.Error))
            .ToList();

    public bool CanTranslate(string source, string target) =>
        _entries.TryGetValue((source.ToLowerInvariant(), target.ToLowerInvariant()), out var e) && e.Error is null;

    public async Task<IReadOnlyList<string>> TranslateAsync(
        string source,
        string target,
        IReadOnlyList<string> sentences,
        CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue((source.ToLowerInvariant(), target.ToLowerInvariant()), out var entry))
            throw new InvalidOperationException($"No model for {source}-{target}");

        var model = GetModel(entry);
        var results = new string[sentences.Count];

        // Sắp theo độ dài để mỗi batch ít padding nhất
        var order = Enumerable.Range(0, sentences.Count).OrderBy(i => sentences[i].Length).ToArray();
        var batchSize = Math.Max(1, _options.BatchSize);

        for (var start = 0; start < order.Length; start += batchSize)
        {
            var indices = order.Skip(start).Take(batchSize).ToArray();
            var batch = indices.Select(i => sentences[i]).ToList();

            await _concurrency.WaitAsync(cancellationToken);
            try
            {
                // ONNX Runtime chạy đồng bộ, CPU-bound → đẩy ra thread pool
                var translated = await Task.Run(() => model.Translate(batch, cancellationToken), cancellationToken);
                for (var k = 0; k < indices.Length; k++) results[indices[k]] = translated[k];
            }
            finally
            {
                _concurrency.Release();
            }
        }

        return results;
    }

    private MarianOnnxModel GetModel(Entry entry)
    {
        if (entry.Model is { } loaded) return loaded;

        lock (entry.LoadLock)
        {
            if (entry.Model is not null) return entry.Model;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                entry.Model = MarianOnnxModel.Load(entry.Directory, entry.Config, _sessionOptions, _options.PreferQuantized);
                entry.Error = null;
                _logger.LogInformation("Loaded model {Source}-{Target} in {Ms} ms (kv-cache: {Kv})",
                    entry.Config.Source, entry.Config.Target, sw.ElapsedMilliseconds, entry.Model.UsesKvCache);
                return entry.Model;
            }
            catch (Exception ex)
            {
                entry.Error = ex.Message;
                _logger.LogError(ex, "Failed to load model in {Dir}", entry.Directory);
                throw;
            }
        }
    }

    public void Dispose()
    {
        foreach (var e in _entries.Values) e.Model?.Dispose();
        _sessionOptions.Dispose();
        _concurrency.Dispose();
    }
}
