using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetTranslate.Infrastructure.Translation.Marian;

/// <summary>
/// Thông tin đọc từ config.json / generation_config.json (Hugging Face) và model.json (tuỳ chọn, của NetTranslate).
/// </summary>
public sealed class MarianConfig
{
    public string Source { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;

    /// <summary>Token tiền tố cho model đa ngôn ngữ (vd. "&gt;&gt;vie&lt;&lt;" của opus-mt-en-mul).</summary>
    public string? TargetPrefix { get; init; }

    /// <summary>null → dùng pad id của tokenizer (mặc định của Marian).</summary>
    public long? DecoderStartTokenId { get; init; }
    public int MaxLength { get; init; } = 512;

    private sealed class HfConfig
    {
        [JsonPropertyName("decoder_start_token_id")] public long? DecoderStartTokenId { get; set; }
        [JsonPropertyName("pad_token_id")] public long? PadTokenId { get; set; }
        [JsonPropertyName("max_length")] public int? MaxLength { get; set; }
        [JsonPropertyName("max_position_embeddings")] public int? MaxPositionEmbeddings { get; set; }
    }

    private sealed class ManifestFile
    {
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("target")] public string? Target { get; set; }
        [JsonPropertyName("targetPrefix")] public string? TargetPrefix { get; set; }
    }

    /// <summary>
    /// Đọc cấu hình model. Cặp ngôn ngữ lấy từ model.json, nếu không có thì suy ra từ tên thư mục
    /// ("en-vi" hoặc "opus-mt-en-vi").
    /// </summary>
    public static MarianConfig? TryLoad(string directory)
    {
        string? source = null, target = null, prefix = null;

        var manifestPath = Path.Combine(directory, "model.json");
        if (File.Exists(manifestPath))
        {
            var manifest = JsonSerializer.Deserialize<ManifestFile>(File.ReadAllText(manifestPath));
            source = manifest?.Source;
            target = manifest?.Target;
            prefix = manifest?.TargetPrefix;
        }

        if (source is null || target is null)
        {
            var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name.StartsWith("opus-mt-", StringComparison.OrdinalIgnoreCase)) name = name["opus-mt-".Length..];
            var parts = name.Split('-', 2);
            if (parts.Length != 2) return null;
            source ??= parts[0];
            target ??= parts[1];
        }

        long? decoderStart = null;
        int? maxLength = null;
        foreach (var file in new[] { "config.json", "generation_config.json" })
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path)) continue;
            var cfg = JsonSerializer.Deserialize<HfConfig>(File.ReadAllText(path));
            if (cfg is null) continue;
            decoderStart ??= cfg.DecoderStartTokenId ?? cfg.PadTokenId;
            maxLength = cfg.MaxLength ?? maxLength ?? cfg.MaxPositionEmbeddings;
        }

        return new MarianConfig
        {
            Source = source.ToLowerInvariant(),
            Target = target.ToLowerInvariant(),
            TargetPrefix = prefix,
            DecoderStartTokenId = decoderStart,
            MaxLength = Math.Clamp(maxLength ?? 512, 16, 1024),
        };
    }
}
