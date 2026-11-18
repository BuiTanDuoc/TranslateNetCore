using Microsoft.Extensions.Options;
using NetTranslate.Application.Abstractions;
using NetTranslate.Application.Options;
using NetTranslate.Domain.Exceptions;

namespace NetTranslate.Application.Services;

/// <summary>Dịch file (/translate_file): chọn bộ dịch theo phần mở rộng, lưu kết quả để tải về.</summary>
public sealed class FileTranslationService(
    TranslationService translation,
    IEnumerable<IDocumentTranslator> translators,
    IFileStorage storage,
    IOptions<TranslateOptions> options)
{
    private readonly TranslateOptions _options = options.Value;
    private readonly IReadOnlyList<IDocumentTranslator> _translators = translators.ToList();

    public IReadOnlyList<string> SupportedFormats =>
        _translators.SelectMany(t => t.Extensions).Distinct().Order().ToList();

    public bool Enabled => !_options.DisableFilesTranslation;

    public async Task<StoredFile> TranslateAsync(
        Stream input,
        string fileName,
        long length,
        string source,
        string target,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled) throw new ForbiddenException("Files translation are disabled on this server.");

        var maxBytes = (long)_options.FileSizeLimitMb * 1024 * 1024;
        if (length > maxBytes)
            throw new ValidationException($"File too large: limit is {_options.FileSizeLimitMb} MB");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var translator = _translators.FirstOrDefault(t => t.Extensions.Contains(extension))
            ?? throw new ValidationException($"File format not supported ({extension}). Supported: {string.Join(", ", SupportedFormats)}");

        translation.ValidateLanguages(source, target);
        target = target.ToLowerInvariant();

        // Với source=auto: nhận diện ngôn ngữ từ chính nội dung file ở lần gọi dịch đầu tiên
        string? resolvedSource = null;
        SegmentTranslator segmentTranslator = async (segments, ct) =>
        {
            if (resolvedSource is null)
            {
                var sample = string.Join('\n', segments);
                if (sample.Length > 4000) sample = sample[..4000];
                resolvedSource = string.IsNullOrWhiteSpace(sample)
                    ? target
                    : translation.ResolveSource(source, sample).Source;
            }
            return await translation.TranslatePlainAsync(resolvedSource, target, segments, ct);
        };

        var outputName = $"{Path.GetFileNameWithoutExtension(fileName)}.{target}{extension}";
        var (stored, output) = storage.Create(outputName);
        await using (output)
        {
            await translator.TranslateAsync(input, output, segmentTranslator, cancellationToken);
        }
        return stored;
    }
}
