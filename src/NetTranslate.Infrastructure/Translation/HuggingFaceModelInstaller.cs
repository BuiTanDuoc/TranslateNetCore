using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTranslate.Application.Abstractions;
using NetTranslate.Domain.Exceptions;
using NetTranslate.Infrastructure.Translation.Marian;

namespace NetTranslate.Infrastructure.Translation;

/// <summary>
/// Tải model ONNX (bố cục repo Xenova/opus-mt-*) từ Hugging Face về models/&lt;pair&gt;.
/// Luôn tải qua /resolve/ (file thật), kiểm tra kích thước + SHA-256 (file LFS) + nội dung không phải HTML,
/// ghi vào models/.staging trước rồi mới đổi tên, sau đó nạp thử và dịch một câu để xác nhận.
/// </summary>
public sealed partial class HuggingFaceModelInstaller : IModelInstaller, IDisposable
{
    public const string HttpClientName = "huggingface";
    private const string StagingFolder = ".staging";
    private const long MinOnnxBytes = 1024 * 1024;
    private const int MaxJobsKept = 10;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(60);

    [GeneratedRegex("^[a-z]{2,3}-[a-z]{2,3}$")]
    private static partial Regex PairRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.\-]{0,95}/[A-Za-z0-9][A-Za-z0-9_.\-]{0,95}$")]
    private static partial Regex RepoRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.\-]{0,127}$")]
    private static partial Regex FolderRegex();

    [GeneratedRegex("^>>[A-Za-z_]{2,16}<<$")]
    private static partial Regex PrefixRegex();

    private sealed class FileJob(string remotePath, long size, string? sha256)
    {
        public string RemotePath { get; } = remotePath;
        public string LocalName { get; } = Path.GetFileName(remotePath);
        public long Size { get; } = size;
        public string? Sha256 { get; } = sha256;
        public long Downloaded;
        public volatile string State = "pending";
    }

    private sealed class Job(ModelDownloadRequest request, string pair, string repo, CancellationToken stopping)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
        public string Pair { get; } = pair;
        public string Repo { get; } = repo;
        public ModelDownloadRequest Request { get; } = request;
        public CancellationTokenSource Cts { get; } = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public volatile string State = "queued";
        public volatile string? Message;
        public volatile string? Error;
        public DateTimeOffset? FinishedAt;
        public volatile IReadOnlyList<FileJob> Files = [];

        public bool IsActive => State is not ("done" or "failed" or "cancelled");

        public ModelDownloadStatus Snapshot()
        {
            var files = Files.Select(f => new ModelDownloadFileStatus(
                f.LocalName, f.Size, Interlocked.Read(ref f.Downloaded), f.State)).ToList();
            return new ModelDownloadStatus(
                Id, Pair, Repo, Request.Quantized, State, Message, Error,
                files.Sum(f => f.Size), files.Sum(f => f.Downloaded),
                StartedAt, FinishedAt, files);
        }
    }

    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ITranslationEngine _engine;
    private readonly ILogger<HuggingFaceModelInstaller> _logger;
    private readonly ModelOptions _options;
    private readonly string _modelsPath;
    private readonly CancellationToken _stopping;
    private readonly Lock _lock = new();
    private readonly List<Job> _jobs = [];

    public HuggingFaceModelInstaller(
        IHttpClientFactory httpFactory,
        ITranslationEngine engine,
        IOptions<ModelOptions> options,
        IHostEnvironment environment,
        IHostApplicationLifetime lifetime,
        IMemoryCache cache,
        ILogger<HuggingFaceModelInstaller> logger)
    {
        _cache = cache;
        _httpFactory = httpFactory;
        _engine = engine;
        _logger = logger;
        _options = options.Value;
        _modelsPath = _options.ResolvePath(environment.ContentRootPath);
        _stopping = lifetime.ApplicationStopping;

        // Dọn file tạm của lần chạy trước (bị tắt giữa chừng)
        TryDeleteDirectory(Path.Combine(_modelsPath, StagingFolder));
    }

    // ------------------------------------------------------------------ API

    public ModelDownloadStatus Start(ModelDownloadRequest request)
    {
        var pair = NormalizePair(request.Pair);

        var repo = string.IsNullOrWhiteSpace(request.Repo) ? $"Xenova/opus-mt-{pair}" : request.Repo.Trim();
        if (repo.StartsWith("https://huggingface.co/", StringComparison.OrdinalIgnoreCase))
            repo = string.Join('/', repo["https://huggingface.co/".Length..].Split('/').Take(2));
        if (!RepoRegex().IsMatch(repo) || repo.Contains(".."))
            throw new ValidationException("Invalid Hugging Face repository. Use the format 'owner/name', e.g. 'Xenova/opus-mt-ja-en'.");

        var prefix = string.IsNullOrWhiteSpace(request.TargetPrefix) ? null : request.TargetPrefix.Trim();
        if (prefix is not null && !PrefixRegex().IsMatch(prefix))
            throw new ValidationException("Invalid target prefix. Expected a token like '>>vie<<'.");

        lock (_lock)
        {
            if (_jobs.Any(j => j.IsActive))
                throw new ValidationException("Another model download is in progress. Wait for it to finish or cancel it.");
            if (Directory.Exists(Path.Combine(_modelsPath, pair)) && !request.Overwrite)
                throw new ValidationException($"Model folder '{pair}' already exists. Enable 'overwrite' to replace it.");

            var job = new Job(request with { TargetPrefix = prefix }, pair, repo, _stopping);
            _jobs.Insert(0, job);
            while (_jobs.Count > MaxJobsKept)
            {
                var old = _jobs[^1];
                _jobs.RemoveAt(_jobs.Count - 1);
                old.Cts.Dispose();
            }

            _ = Task.Run(() => RunAsync(job));
            return job.Snapshot();
        }
    }

    public IReadOnlyList<ModelDownloadStatus> List()
    {
        lock (_lock) return _jobs.Select(j => j.Snapshot()).ToList();
    }

    public ModelDownloadStatus? Get(string id)
    {
        lock (_lock) return _jobs.FirstOrDefault(j => j.Id == id)?.Snapshot();
    }

    public bool Cancel(string id)
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == id);
            if (job is null || !job.IsActive) return false;
            // Bước cài đặt (đổi tên thư mục) rất nhanh, không huỷ giữa chừng
            if (job.State == "installing") return false;
            job.Cts.Cancel();
            return true;
        }
    }

    public void Delete(string folderName)
    {
        if (!FolderRegex().IsMatch(folderName) || folderName.Contains(".."))
            throw new ValidationException("Invalid model folder name.");

        var dir = Path.Combine(_modelsPath, folderName);
        if (!Directory.Exists(dir)) throw new NotFoundException($"Model folder '{folderName}' not found.");

        lock (_lock)
        {
            if (_jobs.Any(j => j.IsActive && string.Equals(j.Pair, folderName, StringComparison.OrdinalIgnoreCase)))
                throw new ValidationException("This model is being downloaded. Cancel the download first.");
        }

        _engine.Unload(folderName);
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ValidationException($"Cannot delete model folder: {ex.Message}");
        }
        finally
        {
            _engine.Reload();
        }
        _logger.LogInformation("Deleted model folder {Dir}", dir);
    }

    // ------------------------------------------------------------------ Search

    private sealed record KnownRepo(string Repo, bool Recommended, string? Note, string? Warning = null, string? TargetPrefix = null);

    /// <summary>Repo đã thử: luôn được đưa vào danh sách và xếp đầu (hoặc gắn cảnh báo).</summary>
    private static readonly Dictionary<string, KnownRepo[]> KnownRepos = new(StringComparer.Ordinal)
    {
        ["en-ja"] = [new("Kadonox/fugumt-en-ja-onnx", true, "FuguMT (staka/fugumt-en-ja)")],
        ["ja-en"] = [new("Xenova/opus-mt-ja-en", true, "Helsinki-NLP OPUS-MT")],
        ["en-vi"] = [new("Xenova/opus-mt-en-vi", true, "Helsinki-NLP OPUS-MT")],
        ["vi-en"] = [new("Xenova/opus-mt-vi-en", true, "Helsinki-NLP OPUS-MT")],
        // opus-mt-en-zh có nhiều biến thể chữ Hán ở đầu ra → cần token chọn tiếng Trung giản thể
        ["en-zh"] = [new("Xenova/opus-mt-en-zh", false, "Helsinki-NLP OPUS-MT, outputs several Chinese variants", TargetPrefix: ">>cmn_Hans<<")],
    };

    private const string JapWarning = "Trained only on Bible text (language code 'jap'), poor quality for general use.";

    private const int MaxCandidatesChecked = 12;

    public async Task<IReadOnlyList<ModelRepoCandidate>> SearchReposAsync(string pair, CancellationToken cancellationToken = default)
    {
        pair = NormalizePair(pair);
        var cacheKey = "hf-repos:" + pair;
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<ModelRepoCandidate>? cached) && cached is not null)
            return cached;

        var (source, target) = SplitPair(pair);
        // Tên repo phải chứa đúng "src-tgt" (hoặc src_tgt) theo đúng chiều, không dính chữ cái hai bên
        // → "en-ja" không khớp "ja-en" hay "en-jap"
        var nameRegex = new Regex($"(?<![a-z]){Regex.Escape(source)}[-_]{Regex.Escape(target)}(?![a-z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var known = KnownRepos.GetValueOrDefault(pair, []);
        var downloads = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();

        void AddName(string repo)
        {
            if (!names.Contains(repo, StringComparer.OrdinalIgnoreCase)) names.Add(repo);
        }

        foreach (var k in known) AddName(k.Repo);
        AddName($"Xenova/opus-mt-{pair}");

        var url = $"{BaseUrl}/api/models?search={Uri.EscapeDataString(pair)}&filter=onnx&sort=downloads&direction=-1&limit=50";
        using (var request = CreateRequest(url))
        using (var response = await Http.SendAsync(request, cancellationToken))
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Hugging Face search failed: HTTP {(int)response.StatusCode}.");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(id) || !RepoRegex().IsMatch(id)) continue;
                if (item.TryGetProperty("downloads", out var d) && d.ValueKind == JsonValueKind.Number)
                    downloads[id] = d.GetInt64();
                if (nameRegex.IsMatch(id.Split('/')[1])) AddName(id);
            }
        }

        // Kiểm tra song song từng repo: đủ file + là Marian
        using var gate = new SemaphoreSlim(4);
        var checks = names.Take(MaxCandidatesChecked).Select(async repo =>
        {
            await gate.WaitAsync(cancellationToken);
            try { return await CheckRepoAsync(repo, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug("Skip repo {Repo}: {Message}", repo, ex.Message);
                return null;
            }
            finally { gate.Release(); }
        });
        var checkedRepos = await Task.WhenAll(checks);

        var result = checkedRepos
            .Where(c => c is not null)
            .Select(c =>
            {
                var info = known.FirstOrDefault(k => k.Repo.Equals(c!.Repo, StringComparison.OrdinalIgnoreCase));
                var warning = info?.Warning ?? (source == "jap" || target == "jap" ? JapWarning : null);
                return new ModelRepoCandidate(
                    c!.Repo, c.FullBytes, c.QuantizedBytes,
                    downloads.TryGetValue(c.Repo, out var n) ? n : null,
                    info?.Recommended ?? false, info?.Note, warning, info?.TargetPrefix);
            })
            .OrderByDescending(c => c.Recommended)
            .ThenBy(c => c.Warning is not null)
            .ThenByDescending(c => c.Downloads ?? -1)
            .ToList();

        _cache.Set(cacheKey, (IReadOnlyList<ModelRepoCandidate>)result, TimeSpan.FromMinutes(10));
        return result;
    }

    private sealed record RepoCheck(string Repo, long? FullBytes, long? QuantizedBytes);

    private async Task<RepoCheck?> CheckRepoAsync(string repo, CancellationToken ct)
    {
        var tree = await ListRepoFilesAsync(repo, ct);
        var full = BuildPlan(tree, quantized: false);
        var quantized = BuildPlan(tree, quantized: true);
        var hasQuantized = quantized.Any(f => f.LocalName.Contains("_quantized", StringComparison.Ordinal));
        var hasFull = full.Any(f => f.LocalName == "encoder_model.onnx");

        // Chỉ nhận model Marian (engine không chạy được NLLB, M2M100, T5...)
        var configUrl = $"{BaseUrl}/{EscapePath(repo)}/resolve/main/config.json";
        using var request = CreateRequest(configUrl);
        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        using var config = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!config.RootElement.TryGetProperty("model_type", out var type)
            || !string.Equals(type.GetString(), "marian", StringComparison.OrdinalIgnoreCase))
            return null;

        return new RepoCheck(repo, hasFull ? full.Sum(f => f.Size) : null, hasQuantized ? quantized.Sum(f => f.Size) : null);
    }

    private static string NormalizePair(string? pair)
    {
        var normalized = (pair ?? string.Empty).Trim().ToLowerInvariant();
        if (!PairRegex().IsMatch(normalized))
            throw new ValidationException("Invalid language pair. Use the format 'en-vi' (2-3 letter codes).");
        return normalized;
    }

    // ------------------------------------------------------------------ Job

    private async Task RunAsync(Job job)
    {
        var ct = job.Cts.Token;
        var staging = Path.Combine(_modelsPath, StagingFolder, job.Id);
        try
        {
            _logger.LogInformation("Model download {Id}: {Pair} <- {Repo}", job.Id, job.Pair, job.Repo);

            job.State = "listing";
            var tree = await ListRepoFilesAsync(job.Repo, ct);
            var plan = BuildPlan(tree, job.Request.Quantized);
            job.Files = plan;

            EnsureDiskSpace(plan.Sum(f => f.Size));
            Directory.CreateDirectory(staging);

            job.State = "downloading";
            foreach (var file in plan)
                await DownloadWithRetryAsync(job.Repo, file, staging, ct);

            job.State = "validating";
            Validate(staging, plan);
            await WriteManifestAsync(job, staging, ct);

            job.State = "installing";
            job.Message = await InstallAsync(job, staging, ct);
            job.State = "done";
            _logger.LogInformation("Model download {Id}: installed {Pair}", job.Id, job.Pair);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            job.State = "cancelled";
            _logger.LogInformation("Model download {Id} cancelled", job.Id);
        }
        catch (Exception ex)
        {
            job.Error = DescribeError(job, ex);
            job.State = "failed";
            _logger.LogWarning(ex, "Model download {Id} ({Repo}) failed", job.Id, job.Repo);
        }
        finally
        {
            job.FinishedAt = DateTimeOffset.UtcNow;
            TryDeleteDirectory(staging);
        }
    }

    private sealed record RepoFile(long Size, string? Sha256);

    private async Task<Dictionary<string, RepoFile>> ListRepoFilesAsync(string repo, CancellationToken ct)
    {
        var url = $"{BaseUrl}/api/models/{EscapePath(repo)}/tree/main?recursive=true";
        using var request = CreateRequest(url);
        using var response = await Http.SendAsync(request, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException(
                $"Repository '{repo}' was not found, or it is private/gated (then set Models:HuggingFaceToken in the server configuration).");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Repository '{repo}' was not found on Hugging Face.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Cannot list files of '{repo}': HTTP {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var files = new Dictionary<string, RepoFile>(StringComparer.Ordinal);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.GetProperty("type").GetString() != "file") continue;
            var path = item.GetProperty("path").GetString();
            if (string.IsNullOrEmpty(path)) continue;

            long size = item.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            string? sha = null;
            if (item.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object)
            {
                if (lfs.TryGetProperty("size", out var ls)) size = ls.GetInt64();
                if (lfs.TryGetProperty("oid", out var oid)) sha = oid.GetString()?.ToLowerInvariant();
            }
            files[path] = new RepoFile(size, sha);
        }
        return files;
    }

    private static List<FileJob> BuildPlan(Dictionary<string, RepoFile> tree, bool quantized)
    {
        var plan = new List<FileJob>();

        void Add(string path)
        {
            var f = tree[path];
            plan.Add(new FileJob(path, f.Size, f.Sha256));
        }

        string? PickOnnx(string baseName)
        {
            var names = quantized
                ? new[] { $"{baseName}_quantized.onnx", $"{baseName}.onnx" }
                : new[] { $"{baseName}.onnx", $"{baseName}_quantized.onnx" };
            foreach (var name in names)
                foreach (var dir in new[] { "onnx/", "" })
                    if (tree.ContainsKey(dir + name)) return dir + name;
            return null;
        }

        var encoder = PickOnnx("encoder_model")
            ?? throw new InvalidOperationException(
                "Repository has no encoder_model.onnx. Use a repository with ONNX exports, e.g. Xenova/opus-mt-<src>-<tgt>.");
        var decoder = PickOnnx("decoder_model")
            ?? throw new InvalidOperationException(tree.Keys.Any(k => k.EndsWith("decoder_model_merged.onnx"))
                ? "Repository only has decoder_model_merged.onnx, which is not supported (separate decoder_model.onnx is required)."
                : "Repository has no decoder_model.onnx.");
        var withPast = PickOnnx("decoder_with_past_model");

        foreach (var onnx in new[] { encoder, decoder, withPast })
        {
            if (onnx is null) continue;
            Add(onnx);
            // Model lớn có thể tách trọng số ra file ngoài (external data)
            if (tree.ContainsKey(onnx + "_data")) Add(onnx + "_data");
        }

        foreach (var required in new[] { "config.json", "source.spm", "vocab.json" })
        {
            if (!tree.ContainsKey(required))
                throw new InvalidOperationException($"Repository is missing required file '{required}'.");
            Add(required);
        }
        foreach (var optional in new[] { "generation_config.json", "target.spm", "tokenizer_config.json" })
            if (tree.ContainsKey(optional)) Add(optional);

        return plan;
    }

    private void EnsureDiskSpace(long needed)
    {
        try
        {
            Directory.CreateDirectory(_modelsPath);
            var root = Path.GetPathRoot(Path.GetFullPath(_modelsPath));
            if (string.IsNullOrEmpty(root)) return;
            var free = new DriveInfo(root).AvailableFreeSpace;
            if (free < needed + 200L * 1024 * 1024)
                throw new InvalidOperationException(
                    $"Not enough disk space: need {needed / 1048576} MB, only {free / 1048576} MB free.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Không xác định được ổ đĩa (vd. mount trong container) → bỏ qua kiểm tra
        }
    }

    private async Task DownloadWithRetryAsync(string repo, FileJob file, string staging, CancellationToken ct)
    {
        const int attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                file.State = "downloading";
                Interlocked.Exchange(ref file.Downloaded, 0);
                await DownloadAsync(repo, file, staging, ct);
                file.State = "done";
                return;
            }
            catch (Exception ex) when (attempt < attempts && !ct.IsCancellationRequested
                                       && ex is HttpRequestException or IOException or TimeoutException)
            {
                _logger.LogWarning("Download of {File} failed (attempt {Attempt}): {Message}", file.RemotePath, attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
            catch
            {
                file.State = "failed";
                throw;
            }
        }
    }

    private async Task DownloadAsync(string repo, FileJob file, string staging, CancellationToken ct)
    {
        // /resolve/ trả về nội dung file thật (qua CDN); /blob/ chỉ là trang HTML xem file
        var url = $"{BaseUrl}/{EscapePath(repo)}/resolve/main/{EscapePath(file.RemotePath)}";
        using var request = CreateRequest(url);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var message = $"Download of '{file.RemotePath}' failed: HTTP {(int)response.StatusCode}.";
            if ((int)response.StatusCode >= 500) throw new HttpRequestException(message);
            throw new InvalidOperationException(message);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"'{file.RemotePath}': the server returned an HTML page instead of the file.");

        var length = response.Content.Headers.ContentLength;
        if (length is { } len && file.Size > 0 && len != file.Size)
            throw new InvalidOperationException($"'{file.RemotePath}': size mismatch (server says {len} bytes, expected {file.Size}).");

        var path = Path.Combine(staging, file.LocalName);
        using var hash = file.Sha256 is null ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;

        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buffer = new byte[1 << 16];
            while (true)
            {
                int read;
                using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    readCts.CancelAfter(ReadTimeout);
                    try
                    {
                        read = await source.ReadAsync(buffer, readCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException($"'{file.RemotePath}': no data received for {ReadTimeout.TotalSeconds:0} s.");
                    }
                }
                if (read == 0) break;

                if (total == 0 && LooksLikeHtml(buffer.AsSpan(0, read)))
                    throw new InvalidOperationException($"'{file.RemotePath}': the server returned an HTML page instead of the file.");

                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                hash?.AppendData(buffer, 0, read);
                total += read;
                Interlocked.Exchange(ref file.Downloaded, total);
            }
        }

        if (file.Size > 0 && total != file.Size)
            throw new IOException($"'{file.RemotePath}': incomplete download ({total} of {file.Size} bytes).");

        if (hash is not null)
        {
            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (actual != file.Sha256)
                throw new IOException($"'{file.RemotePath}': SHA-256 checksum mismatch.");
        }
    }

    private static void Validate(string staging, IReadOnlyList<FileJob> plan)
    {
        foreach (var file in plan)
        {
            var path = Path.Combine(staging, file.LocalName);
            var info = new FileInfo(path);
            if (!info.Exists) throw new InvalidOperationException($"'{file.LocalName}' was not downloaded.");
            file.State = "validating";

            if (file.LocalName.EndsWith(".onnx", StringComparison.Ordinal))
            {
                if (info.Length < MinOnnxBytes)
                    throw new InvalidOperationException($"'{file.LocalName}' is too small ({info.Length} bytes) to be an ONNX model.");
                using var fs = File.OpenRead(path);
                // ModelProto bắt đầu bằng field 1 (ir_version, varint) → byte 0x08
                if (fs.ReadByte() != 0x08)
                    throw new InvalidOperationException($"'{file.LocalName}' is not a valid ONNX file.");
            }
            else if (file.LocalName.EndsWith(".json", StringComparison.Ordinal))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                    if (file.LocalName == "vocab.json"
                        && (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.EnumerateObject().Count() < 100))
                        throw new InvalidOperationException("'vocab.json' does not look like a vocabulary.");
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException($"'{file.LocalName}' is not valid JSON.");
                }
            }
            else if (file.LocalName.EndsWith(".spm", StringComparison.Ordinal))
            {
                int count;
                try { count = SentencePieceModel.Load(path).Count; }
                catch (Exception ex) { throw new InvalidOperationException($"'{file.LocalName}' is not a valid SentencePiece model: {ex.Message}"); }
                if (count < 100) throw new InvalidOperationException($"'{file.LocalName}' has too few pieces ({count}).");
            }
            file.State = "done";
        }
    }

    private static async Task WriteManifestAsync(Job job, string staging, CancellationToken ct)
    {
        var (source, target) = SplitPair(job.Pair);
        var manifest = new Dictionary<string, object?>
        {
            ["source"] = source,
            ["target"] = target,
            ["repo"] = job.Repo,
            ["quantized"] = job.Request.Quantized,
        };
        if (job.Request.TargetPrefix is not null) manifest["targetPrefix"] = job.Request.TargetPrefix;

        await File.WriteAllTextAsync(
            Path.Combine(staging, "model.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
    }

    private async Task<string> InstallAsync(Job job, string staging, CancellationToken ct)
    {
        var target = Path.Combine(_modelsPath, job.Pair);
        string? backup = null;

        if (Directory.Exists(target))
        {
            if (!job.Request.Overwrite)
                throw new InvalidOperationException($"Model folder '{job.Pair}' already exists.");
            _engine.Unload(job.Pair);
            backup = Path.Combine(_modelsPath, StagingFolder, job.Id + "-old");
            Directory.Move(target, backup);
        }
        Directory.Move(staging, target);
        _engine.Reload();

        // Nạp model và dịch thử một câu; lỗi → khôi phục trạng thái cũ
        const string sample = "Hello world.";
        var (source, tgt) = SplitPair(job.Pair);
        try
        {
            using var testCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            testCts.CancelAfter(TimeSpan.FromMinutes(3));
            var result = await _engine.TranslateAsync(source, tgt, [sample], testCts.Token);
            if (result.Count != 1) throw new InvalidOperationException("unexpected output");

            if (backup is not null) TryDeleteDirectory(backup);
            return $"\"{sample}\" → \"{result[0]}\"";
        }
        catch (Exception ex)
        {
            _engine.Unload(job.Pair);
            TryDeleteDirectory(target);
            if (backup is not null && !Directory.Exists(target)) Directory.Move(backup, target);
            _engine.Reload();

            if (ex is OperationCanceledException && ct.IsCancellationRequested) throw;
            throw new InvalidOperationException($"Model was downloaded but failed to load: {ex.Message}", ex);
        }
    }

    // ------------------------------------------------------------------ Helpers

    private HttpClient Http => _httpFactory.CreateClient(HttpClientName);

    private string BaseUrl => _options.HuggingFaceUrl.TrimEnd('/');

    private HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(_options.HuggingFaceToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.HuggingFaceToken);
        return request;
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static (string Source, string Target) SplitPair(string pair)
    {
        var parts = pair.Split('-', 2);
        return (parts[0], parts[1]);
    }

    /// <summary>
    /// Thông báo lỗi kèm inner exception (vd. "The SSL connection could not be established" → lý do thật bên trong)
    /// và bước/file đang xử lý, để admin chẩn đoán được mà không cần đọc log server.
    /// </summary>
    private static string DescribeError(Job job, Exception ex)
    {
        var messages = new List<string>();
        for (var e = ex; e is not null && messages.Count < 5; e = e.InnerException)
            if (!string.IsNullOrWhiteSpace(e.Message) && !messages.Contains(e.Message))
                messages.Add(e.Message.Trim());
        var text = string.Join(" → ", messages);

        if (ex is HttpRequestException or TimeoutException || ex.InnerException is HttpRequestException)
        {
            var file = job.Files.FirstOrDefault(f => f.State is "downloading" or "failed");
            var step = file is null ? "listing repository files" : $"downloading '{file.RemotePath}'";
            text = $"Network error while {step} from Hugging Face " +
                   "(files are served from huggingface.co and CDN hosts such as cas-bridge.xethub.hf.co / cdn-lfs.hf.co): " + text;
        }
        return text;
    }

    private static bool LooksLikeHtml(ReadOnlySpan<byte> data)
    {
        var text = Encoding.ASCII.GetString(data[..Math.Min(data.Length, 256)]).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Cannot delete {Path}: {Message}", path, ex.Message);
        }
    }

    public void Dispose()
    {
        lock (_lock)
            foreach (var job in _jobs)
            {
                job.Cts.Cancel();
                job.Cts.Dispose();
            }
    }
}
