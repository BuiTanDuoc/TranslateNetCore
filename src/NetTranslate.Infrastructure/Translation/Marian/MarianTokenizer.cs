using System.Text.Json;

namespace NetTranslate.Infrastructure.Translation.Marian;

/// <summary>
/// Tokenizer của MarianMT: SentencePiece (source.spm) để cắt piece, rồi tra vocab.json để ra id.
/// Giải mã: id → piece qua vocab.json → nối lại và thay ▁ bằng dấu cách.
/// </summary>
public sealed class MarianTokenizer
{
    private readonly SentencePieceModel _sourceSpm;
    private readonly Dictionary<string, int> _vocab;
    private readonly Dictionary<int, string> _reverseVocab;
    private readonly HashSet<int> _specialIds;

    public int PadId { get; }
    public int EosId { get; }
    public int UnkId { get; }
    public int VocabSize => _vocab.Count;

    public MarianTokenizer(string modelDirectory)
    {
        var spmPath = Path.Combine(modelDirectory, "source.spm");
        if (!File.Exists(spmPath))
            throw new FileNotFoundException("Missing source.spm in model directory", spmPath);
        _sourceSpm = SentencePieceModel.Load(spmPath);

        var vocabPath = Path.Combine(modelDirectory, "vocab.json");
        if (!File.Exists(vocabPath))
            throw new FileNotFoundException("Missing vocab.json in model directory", vocabPath);
        _vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(vocabPath))
                 ?? throw new InvalidDataException("vocab.json is empty");

        _reverseVocab = new Dictionary<int, string>(_vocab.Count);
        foreach (var (piece, id) in _vocab) _reverseVocab.TryAdd(id, piece);

        PadId = _vocab.GetValueOrDefault("<pad>", 0);
        EosId = _vocab.GetValueOrDefault("</s>", 0);
        UnkId = _vocab.GetValueOrDefault(SentencePieceModel.UnknownPiece, 1);
        _specialIds = [PadId, EosId];
    }

    /// <summary>Mã hoá một câu thành id (đã thêm &lt;/s&gt;), cắt bớt nếu dài hơn maxLength.</summary>
    public long[] Encode(string text, int maxLength, string? prefixToken = null)
    {
        var ids = new List<long>();
        if (!string.IsNullOrEmpty(prefixToken) && _vocab.TryGetValue(prefixToken, out var prefixId))
            ids.Add(prefixId);

        foreach (var piece in _sourceSpm.Encode(text))
            ids.Add(_vocab.TryGetValue(piece, out var id) ? id : UnkId);

        if (ids.Count > maxLength - 1) ids.RemoveRange(maxLength - 1, ids.Count - (maxLength - 1));
        ids.Add(EosId);
        return ids.ToArray();
    }

    public string Decode(IEnumerable<long> ids)
    {
        var pieces = new List<string>();
        foreach (var id in ids)
        {
            var i = (int)id;
            if (_specialIds.Contains(i)) continue;
            if (_reverseVocab.TryGetValue(i, out var piece))
            {
                // Bỏ token điều khiển kiểu >>vie<< hoặc <unk>
                if (piece.StartsWith(">>", StringComparison.Ordinal) && piece.EndsWith("<<", StringComparison.Ordinal)) continue;
                if (i == UnkId) continue;
                pieces.Add(piece);
            }
        }
        return SentencePieceModel.Decode(pieces);
    }
}
