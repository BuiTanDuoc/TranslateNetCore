using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace NetTranslate.Infrastructure.Translation.Marian;

/// <summary>
/// Một model MarianMT (một cặp ngôn ngữ) chạy bằng ONNX Runtime.
/// Cần các file do optimum-cli export: encoder_model.onnx, decoder_model.onnx
/// (+ tuỳ chọn decoder_with_past_model.onnx để dùng KV-cache, nhanh hơn nhiều với câu dài).
/// Giải mã greedy theo batch.
/// </summary>
public sealed class MarianOnnxModel : IDisposable
{
    private const string PastPrefix = "past_key_values.";
    private const string PresentPrefix = "present.";

    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly InferenceSession? _decoderWithPast;
    private readonly MarianTokenizer _tokenizer;
    private readonly long _decoderStartId;

    public MarianConfig Config { get; }
    public bool UsesKvCache => _decoderWithPast is not null;

    private MarianOnnxModel(
        MarianConfig config,
        MarianTokenizer tokenizer,
        InferenceSession encoder,
        InferenceSession decoder,
        InferenceSession? decoderWithPast)
    {
        Config = config;
        _tokenizer = tokenizer;
        _encoder = encoder;
        _decoder = decoder;
        _decoderWithPast = decoderWithPast;
        _decoderStartId = config.DecoderStartTokenId ?? tokenizer.PadId;
    }

    public static MarianOnnxModel Load(string directory, MarianConfig config, SessionOptions sessionOptions, bool preferQuantized)
    {
        var tokenizer = new MarianTokenizer(directory);

        var encoderPath = FindOnnx(directory, "encoder_model", preferQuantized)
            ?? throw new FileNotFoundException($"encoder_model.onnx not found in {directory}");
        var decoderPath = FindOnnx(directory, "decoder_model", preferQuantized)
            ?? throw new FileNotFoundException(
                $"decoder_model.onnx not found in {directory}. " +
                "Export with: optimum-cli export onnx --task text2text-generation-with-past --no-post-process ...");
        var withPastPath = FindOnnx(directory, "decoder_with_past_model", preferQuantized);

        var encoder = new InferenceSession(encoderPath, sessionOptions);
        var decoder = new InferenceSession(decoderPath, sessionOptions);
        if (decoder.InputMetadata.ContainsKey("use_cache_branch"))
        {
            encoder.Dispose();
            decoder.Dispose();
            throw new NotSupportedException(
                "decoder_model_merged.onnx is not supported. Re-export with --no-post-process to get separate decoder files.");
        }
        var withPast = withPastPath is null ? null : new InferenceSession(withPastPath, sessionOptions);

        return new MarianOnnxModel(config, tokenizer, encoder, decoder, withPast);
    }

    /// <summary>Tìm file .onnx ở thư mục gốc hoặc thư mục con onnx/ (bố cục của các repo ONNX trên Hugging Face).</summary>
    private static string? FindOnnx(string directory, string baseName, bool preferQuantized)
    {
        var names = preferQuantized
            ? new[] { $"{baseName}_quantized.onnx", $"{baseName}.onnx" }
            : new[] { $"{baseName}.onnx", $"{baseName}_quantized.onnx" };

        foreach (var dir in new[] { directory, Path.Combine(directory, "onnx") })
            foreach (var name in names)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    public IReadOnlyList<string> Translate(IReadOnlyList<string> sentences, CancellationToken cancellationToken)
    {
        if (sentences.Count == 0) return [];

        // ---------------------------------------------------------------- Encoder
        var batch = sentences.Count;
        var encoded = sentences.Select(s => _tokenizer.Encode(s, Config.MaxLength, Config.TargetPrefix)).ToArray();
        var srcLen = encoded.Max(e => e.Length);

        var inputIds = new DenseTensor<long>(new[] { batch, srcLen });
        var attentionMask = new DenseTensor<long>(new[] { batch, srcLen });
        for (var i = 0; i < batch; i++)
            for (var j = 0; j < srcLen; j++)
            {
                var inRange = j < encoded[i].Length;
                inputIds[i, j] = inRange ? encoded[i][j] : _tokenizer.PadId;
                attentionMask[i, j] = inRange ? 1 : 0;
            }

        var encoderInputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_ids", inputIds) };
        if (_encoder.InputMetadata.ContainsKey("attention_mask"))
            encoderInputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask));

        DenseTensor<float> encoderHidden;
        using (var encoderOut = _encoder.Run(encoderInputs))
        {
            encoderHidden = encoderOut.First().AsTensor<float>().ToDenseTensor();
        }

        // ---------------------------------------------------------------- Decoder (greedy)
        var tokens = new List<long>[batch];
        for (var i = 0; i < batch; i++) tokens[i] = [_decoderStartId];
        var finished = new bool[batch];
        var maxNewTokens = Math.Min(Config.MaxLength, srcLen * 3 + 16);

        Dictionary<string, DenseTensor<float>>? cache = null;

        for (var step = 0; step < maxNewTokens; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var useCache = _decoderWithPast is not null && cache is not null;
            var session = useCache ? _decoderWithPast! : _decoder;
            var curLen = tokens[0].Count;
            var feedLen = useCache ? 1 : curLen;

            var decoderIds = new DenseTensor<long>(new[] { batch, feedLen });
            for (var i = 0; i < batch; i++)
                for (var j = 0; j < feedLen; j++)
                    decoderIds[i, j] = tokens[i][curLen - feedLen + j];

            var inputs = new List<NamedOnnxValue>();
            foreach (var name in session.InputMetadata.Keys)
            {
                switch (name)
                {
                    case "input_ids":
                        inputs.Add(NamedOnnxValue.CreateFromTensor(name, decoderIds));
                        break;
                    case "encoder_hidden_states":
                        inputs.Add(NamedOnnxValue.CreateFromTensor(name, encoderHidden));
                        break;
                    case "encoder_attention_mask":
                        inputs.Add(NamedOnnxValue.CreateFromTensor(name, attentionMask));
                        break;
                    default:
                        if (name.StartsWith(PastPrefix, StringComparison.Ordinal) && cache is not null
                            && cache.TryGetValue(PresentPrefix + name[PastPrefix.Length..], out var past))
                        {
                            inputs.Add(NamedOnnxValue.CreateFromTensor(name, past));
                        }
                        else
                        {
                            throw new NotSupportedException($"Unsupported decoder input '{name}'");
                        }
                        break;
                }
            }

            // Không cần KV-cache → chỉ lấy logits cho nhẹ
            var wantCache = _decoderWithPast is not null;
            using var outputs = wantCache ? session.Run(inputs) : session.Run(inputs, new[] { "logits" });

            Tensor<float>? logitsTensor = null;
            foreach (var output in outputs)
            {
                if (output.Name == "logits")
                {
                    logitsTensor = output.AsTensor<float>();
                }
                else if (wantCache && output.Name.StartsWith(PresentPrefix, StringComparison.Ordinal))
                {
                    cache ??= new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
                    // Bản with_past chỉ trả về present.*.decoder.*, phần encoder giữ nguyên từ bước đầu
                    cache[output.Name] = output.AsTensor<float>().ToDenseTensor();
                }
            }
            if (logitsTensor is null) throw new InvalidOperationException("Decoder did not return logits");

            var logits = logitsTensor as DenseTensor<float> ?? logitsTensor.ToDenseTensor();
            var vocab = logits.Dimensions[2];
            var outLen = logits.Dimensions[1];
            var span = logits.Buffer.Span;

            var allDone = true;
            for (var i = 0; i < batch; i++)
            {
                if (finished[i])
                {
                    tokens[i].Add(_tokenizer.PadId);
                    continue;
                }

                var offset = (i * outLen + (outLen - 1)) * vocab;
                var next = ArgMax(span.Slice(offset, vocab), _tokenizer.PadId);
                tokens[i].Add(next);
                if (next == _tokenizer.EosId) finished[i] = true;
                else allDone = false;
            }

            if (allDone) break;
        }

        return tokens.Select(t => _tokenizer.Decode(t.Skip(1))).ToList();
    }

    /// <summary>Chọn token có xác suất cao nhất, bỏ qua pad (Marian cấm sinh pad).</summary>
    private static long ArgMax(ReadOnlySpan<float> row, int excludeId)
    {
        var best = -1;
        var bestValue = float.NegativeInfinity;
        for (var k = 0; k < row.Length; k++)
        {
            if (k == excludeId) continue;
            if (row[k] > bestValue)
            {
                bestValue = row[k];
                best = k;
            }
        }
        return best;
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
        _decoderWithPast?.Dispose();
    }
}
