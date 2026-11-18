using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTranslate.Application.Abstractions;
using NetTranslate.Application.Options;
using NetTranslate.Domain.Exceptions;
using NetTranslate.Domain.Languages;

namespace NetTranslate.Application.Services;

public sealed record LanguageInfo(string Code, string Name, IReadOnlyList<string> Targets);

public sealed record TranslateResult(string TranslatedText, DetectionResult? DetectedLanguage);

/// <summary>
/// Điều phối việc dịch: auto-detect, chọn đường dịch (trực tiếp hoặc pivot qua tiếng Anh),
/// tách câu, gom câu trùng, xử lý HTML. Engine bên dưới chỉ việc dịch từng câu.
/// </summary>
public sealed class TranslationService(
    ITranslationEngine engine,
    ILanguageDetector detector,
    IHtmlTranslator htmlTranslator,
    IOptions<TranslateOptions> options,
    ILogger<TranslationService> logger)
{
    public const string AutoSource = "auto";

    private readonly TranslateOptions _options = options.Value;

    // ----------------------------------------------------------------------------- Languages

    public IReadOnlyList<LanguageInfo> GetLanguages()
    {
        var pairs = engine.GetAvailablePairs();
        var sources = pairs.Select(p => p.Source)
            .Concat(pairs.Select(p => p.Target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => LanguageCatalog.GetName(c), StringComparer.OrdinalIgnoreCase);

        var result = new List<LanguageInfo>();
        foreach (var code in sources)
        {
            var targets = pairs.Select(p => p.Target)
                .Concat(pairs.Select(p => p.Source))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(t => !t.Equals(code, StringComparison.OrdinalIgnoreCase) && GetRoute(code, t) is not null)
                .OrderBy(t => LanguageCatalog.GetName(t), StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Chỉ liệt kê ngôn ngữ có thể làm nguồn (dịch được sang ít nhất 1 ngôn ngữ)
            if (targets.Count > 0)
                result.Add(new LanguageInfo(code, LanguageCatalog.GetName(code), targets));
        }
        return result;
    }

    private HashSet<string> SourceLanguages() =>
        GetLanguages().Select(l => l.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // ----------------------------------------------------------------------------- Detect

    public IReadOnlyList<DetectionResult> Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("Invalid request: missing q parameter");

        var candidates = SourceLanguages();
        if (candidates.Count == 0)
            throw new ValidationException("No translation models installed");

        return detector.Detect(text, candidates);
    }

    // ----------------------------------------------------------------------------- Validation

    public void ValidateLanguages(string source, string target)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ValidationException("Invalid request: missing source parameter");
        if (string.IsNullOrWhiteSpace(target))
            throw new ValidationException("Invalid request: missing target parameter");

        var sources = SourceLanguages();
        if (!source.Equals(AutoSource, StringComparison.OrdinalIgnoreCase) && !sources.Contains(source))
            throw new ValidationException($"{source} is not supported");

        var allTargets = GetLanguages().SelectMany(l => l.Targets).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!allTargets.Contains(target))
            throw new ValidationException($"{target} is not supported");
    }

    /// <summary>Trả về mã nguồn thực tế, tự nhận diện nếu source = auto.</summary>
    public (string Source, DetectionResult? Detected) ResolveSource(string source, string sample)
    {
        if (!source.Equals(AutoSource, StringComparison.OrdinalIgnoreCase))
            return (source.ToLowerInvariant(), null);

        var detected = Detect(sample).FirstOrDefault()
            ?? throw new ValidationException("Could not detect source language");
        return (detected.Language, detected);
    }

    // ----------------------------------------------------------------------------- Translate

    public async Task<IReadOnlyList<TranslateResult>> TranslateAsync(
        IReadOnlyList<string> texts,
        string source,
        string target,
        string format,
        CancellationToken cancellationToken = default)
    {
        ValidateLanguages(source, target);
        target = target.ToLowerInvariant();
        var isHtml = format.Equals("html", StringComparison.OrdinalIgnoreCase);

        var results = new TranslateResult[texts.Count];

        // Xác định ngôn ngữ nguồn cho từng phần tử (LibreTranslate detect riêng từng q)
        var resolved = new (string Source, DetectionResult? Detected)[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            resolved[i] = string.IsNullOrWhiteSpace(texts[i])
                ? (source.Equals(AutoSource, StringComparison.OrdinalIgnoreCase) ? target : source.ToLowerInvariant(), null)
                : ResolveSource(source, isHtml ? StripTags(texts[i]) : texts[i]);
        }

        // Gom theo ngôn ngữ nguồn để batch hiệu quả
        foreach (var group in Enumerable.Range(0, texts.Count).GroupBy(i => resolved[i].Source))
        {
            var src = group.Key;
            var indices = group.ToList();
            IReadOnlyList<string> translated;

            if (isHtml)
            {
                var translator = CreateSegmentTranslator(src, target);
                var list = new List<string>(indices.Count);
                foreach (var i in indices)
                    list.Add(await htmlTranslator.TranslateAsync(texts[i], translator, cancellationToken));
                translated = list;
            }
            else
            {
                translated = await TranslatePlainAsync(src, target, indices.Select(i => texts[i]).ToList(), cancellationToken);
            }

            for (var k = 0; k < indices.Count; k++)
                results[indices[k]] = new TranslateResult(translated[k], resolved[indices[k]].Detected);
        }

        return results;
    }

    /// <summary>Tạo hàm dịch đoạn văn cho các bộ dịch HTML/file.</summary>
    public SegmentTranslator CreateSegmentTranslator(string source, string target) =>
        (segments, ct) => TranslatePlainAsync(source, target, segments, ct);

    /// <summary>Dịch nhiều đoạn văn bản thuần, giữ nguyên bố cục dòng.</summary>
    public async Task<IReadOnlyList<string>> TranslatePlainAsync(
        string source,
        string target,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
            return texts;

        var split = texts.Select(TextSegmenter.Split).ToList();
        var sentences = split
            .SelectMany(p => p)
            .Where(p => p.Kind == TextSegmenter.PieceKind.Sentence)
            .Select(p => p.Text)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var translations = await TranslateSentencesAsync(source, target, sentences, cancellationToken);
        return split.Select(pieces => TextSegmenter.Join(pieces, translations, target)).ToList();
    }

    private async Task<Dictionary<string, string>> TranslateSentencesAsync(
        string source,
        string target,
        IReadOnlyList<string> sentences,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (sentences.Count == 0) return map;

        var route = GetRoute(source, target)
            ?? throw new ValidationException($"No translation model available for {source} → {target}");

        IReadOnlyList<string> current = sentences;
        foreach (var step in route)
        {
            logger.LogDebug("Translating {Count} sentences {Source}->{Target}", current.Count, step.Source, step.Target);
            current = await engine.TranslateAsync(step.Source, step.Target, current, cancellationToken);
        }

        for (var i = 0; i < sentences.Count; i++)
            map[sentences[i]] = current[i];
        return map;
    }

    /// <summary>Đường dịch: trực tiếp, hoặc qua ngôn ngữ trung gian (mặc định en) như Argos Translate.</summary>
    private IReadOnlyList<LanguagePair>? GetRoute(string source, string target)
    {
        if (engine.CanTranslate(source, target))
            return [new LanguagePair(source, target)];

        var pivot = _options.PivotLanguage;
        if (!string.IsNullOrEmpty(pivot)
            && !pivot.Equals(source, StringComparison.OrdinalIgnoreCase)
            && !pivot.Equals(target, StringComparison.OrdinalIgnoreCase)
            && engine.CanTranslate(source, pivot)
            && engine.CanTranslate(pivot, target))
        {
            return [new LanguagePair(source, pivot), new LanguagePair(pivot, target)];
        }

        return null;
    }

    private static string StripTags(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
}
