using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTranslate.Application.Abstractions;
using NetTranslate.Application.Options;

namespace NetTranslate.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Thư mục lưu file đã dịch.</summary>
    public string FilesPath { get; set; } = "data/files";
}

/// <summary>
/// Lưu file đã dịch trên đĩa: data/files/{id}/{tên file}.
/// id là chuỗi ngẫu nhiên 32 ký tự hex nên không đoán được.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IOptions<StorageOptions> options, IHostEnvironment env, ILogger<LocalFileStorage> logger)
    {
        _logger = logger;
        var path = options.Value.FilesPath;
        _root = Path.IsPathRooted(path) ? path : Path.Combine(env.ContentRootPath, path);
        Directory.CreateDirectory(_root);
    }

    public (StoredFile File, Stream Stream) Create(string fileName)
    {
        var id = Guid.NewGuid().ToString("N");
        var safeName = SanitizeFileName(fileName);
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, safeName);
        var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        return (new StoredFile(id, safeName, fullPath), stream);
    }

    public StoredFile? Find(string id)
    {
        // Chống path traversal: id phải đúng 32 ký tự hex
        if (id.Length != 32 || !id.All(Uri.IsHexDigit)) return null;

        var dir = Path.Combine(_root, id);
        if (!Directory.Exists(dir)) return null;
        var file = Directory.GetFiles(dir).FirstOrDefault();
        return file is null ? null : new StoredFile(id, Path.GetFileName(file), file);
    }

    public int Cleanup(TimeSpan maxAge)
    {
        var removed = 0;
        foreach (var dir in Directory.GetDirectories(_root))
        {
            try
            {
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) <= maxAge) continue;
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete {Dir}", dir);
            }
        }
        return removed;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(Path.GetFileName(name).Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "file" : clean;
    }
}

/// <summary>Định kỳ xoá file đã dịch quá hạn (Translate:FileRetentionMinutes).</summary>
public sealed class FileCleanupService(
    IFileStorage storage,
    IOptions<TranslateOptions> options,
    ILogger<FileCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retention = TimeSpan.FromMinutes(Math.Max(1, options.Value.FileRetentionMinutes));
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            var removed = storage.Cleanup(retention);
            if (removed > 0) logger.LogInformation("Removed {Count} expired translated files", removed);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
