using System.Text;

namespace NetTranslate.Infrastructure.Translation.Marian;

/// <summary>
/// Bộ tách từ SentencePiece (Unigram) viết thuần C#, đọc trực tiếp file source.spm của MarianMT.
/// Không phụ thuộc thư viện native: tự parse protobuf ModelProto và chạy Viterbi
/// để chọn cách cắt có tổng log-probability lớn nhất (giống sentencepiece gốc).
/// </summary>
public sealed class SentencePieceModel
{
    public const char SpaceSymbol = '▁'; // ▁
    public const string UnknownPiece = "<unk>";

    private enum PieceType { Normal = 1, Unknown = 2, Control = 3, UserDefined = 4, Unused = 5, Byte = 6 }

    private readonly Dictionary<string, float> _scores = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly int _maxPieceLength;
    private readonly float _unknownScore;

    private SentencePieceModel(IEnumerable<(string Piece, float Score, PieceType Type)> pieces)
    {
        var minScore = 0f;
        foreach (var (piece, score, type) in pieces)
        {
            if (type is not (PieceType.Normal or PieceType.UserDefined)) continue;
            if (string.IsNullOrEmpty(piece)) continue;

            // USER_DEFINED luôn được ưu tiên khớp
            _scores[piece] = type == PieceType.UserDefined ? 0f : score;
            _maxPieceLength = Math.Max(_maxPieceLength, piece.Length);
            minScore = Math.Min(minScore, score);
        }
        _unknownScore = minScore - 10f; // giống kUnkPenalty của sentencepiece
        _lookup = _scores.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public int Count => _scores.Count;

    public static SentencePieceModel Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new SentencePieceModel(ParseModelProto(bytes));
    }

    /// <summary>Tách văn bản thành các piece (chuỗi), piece không có trong từ điển trả về &lt;unk&gt;.</summary>
    public List<string> Encode(string text)
    {
        var normalized = Normalize(text);
        var result = new List<string>();
        if (normalized.Length == 0) return result;

        var n = normalized.Length;
        var bestScore = new float[n + 1];
        var backPos = new int[n + 1];
        var backUnknown = new bool[n + 1];
        Array.Fill(bestScore, float.NegativeInfinity);
        bestScore[0] = 0;

        var span = normalized.AsSpan();
        for (var i = 0; i < n; i++)
        {
            if (float.IsNegativeInfinity(bestScore[i])) continue;

            var maxLen = Math.Min(_maxPieceLength, n - i);
            for (var len = 1; len <= maxLen; len++)
            {
                if (!_lookup.TryGetValue(span.Slice(i, len), out var score)) continue;
                var candidate = bestScore[i] + score;
                if (candidate > bestScore[i + len])
                {
                    bestScore[i + len] = candidate;
                    backPos[i + len] = i;
                    backUnknown[i + len] = false;
                }
            }

            // Fallback: 1 ký tự (hoặc cặp surrogate) không có trong từ điển → <unk>
            var charLen = char.IsHighSurrogate(span[i]) && i + 1 < n ? 2 : 1;
            var unkCandidate = bestScore[i] + _unknownScore;
            if (unkCandidate > bestScore[i + charLen])
            {
                bestScore[i + charLen] = unkCandidate;
                backPos[i + charLen] = i;
                backUnknown[i + charLen] = true;
            }
        }

        // Truy vết ngược
        var pos = n;
        var reversed = new List<(string Piece, bool Unknown)>();
        while (pos > 0)
        {
            var prev = backPos[pos];
            reversed.Add((normalized[prev..pos], backUnknown[pos]));
            pos = prev;
        }
        reversed.Reverse();

        // Gộp các <unk> liên tiếp thành một (giống sentencepiece)
        var lastWasUnknown = false;
        foreach (var (piece, unknown) in reversed)
        {
            if (unknown)
            {
                if (!lastWasUnknown) result.Add(UnknownPiece);
                lastWasUnknown = true;
            }
            else
            {
                result.Add(piece);
                lastWasUnknown = false;
            }
        }
        return result;
    }

    /// <summary>
    /// Chuẩn hoá gần giống nmt_nfkc: NFKC, gộp khoảng trắng, thêm tiền tố ▁ và thay dấu cách bằng ▁.
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var nfkc = text.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(nfkc.Length + 1);
        sb.Append(SpaceSymbol);
        var lastWasSpace = true;
        foreach (var c in nfkc)
        {
            if (char.IsControl(c) && c is not ('\t' or '\n' or '\r')) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(SpaceSymbol);
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        // bỏ ▁ thừa ở cuối
        while (sb.Length > 1 && sb[^1] == SpaceSymbol) sb.Length--;
        return sb.Length == 1 ? string.Empty : sb.ToString();
    }

    /// <summary>Chuyển danh sách piece về văn bản.</summary>
    public static string Decode(IEnumerable<string> pieces)
    {
        var text = string.Concat(pieces).Replace(SpaceSymbol, ' ');
        return text.Trim();
    }

    // ------------------------------------------------------------------ minimal protobuf reader

    private static IEnumerable<(string, float, PieceType)> ParseModelProto(byte[] data)
    {
        var list = new List<(string, float, PieceType)>();
        var pos = 0;
        while (pos < data.Length)
        {
            var tag = ReadVarint(data, ref pos);
            var field = (int)(tag >> 3);
            var wireType = (int)(tag & 7);

            if (field == 1 && wireType == 2) // repeated SentencePiece pieces = 1
            {
                var len = (int)ReadVarint(data, ref pos);
                list.Add(ParsePiece(data.AsSpan(pos, len)));
                pos += len;
            }
            else
            {
                SkipField(data, ref pos, wireType);
            }
        }
        return list;
    }

    private static (string, float, PieceType) ParsePiece(ReadOnlySpan<byte> span)
    {
        var data = span.ToArray();
        var pos = 0;
        var piece = string.Empty;
        var score = 0f;
        var type = PieceType.Normal;

        while (pos < data.Length)
        {
            var tag = ReadVarint(data, ref pos);
            var field = (int)(tag >> 3);
            var wireType = (int)(tag & 7);
            switch (field)
            {
                case 1 when wireType == 2:
                    var len = (int)ReadVarint(data, ref pos);
                    piece = Encoding.UTF8.GetString(data, pos, len);
                    pos += len;
                    break;
                case 2 when wireType == 5:
                    score = BitConverter.ToSingle(data, pos);
                    pos += 4;
                    break;
                case 3 when wireType == 0:
                    type = (PieceType)(int)ReadVarint(data, ref pos);
                    break;
                default:
                    SkipField(data, ref pos, wireType);
                    break;
            }
        }
        return (piece, score, type);
    }

    private static ulong ReadVarint(byte[] data, ref int pos)
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            if (pos >= data.Length) throw new InvalidDataException("Truncated varint in SentencePiece model");
            var b = data[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift > 63) throw new InvalidDataException("Invalid varint in SentencePiece model");
        }
    }

    private static void SkipField(byte[] data, ref int pos, int wireType)
    {
        switch (wireType)
        {
            case 0: ReadVarint(data, ref pos); break;
            case 1: pos += 8; break;
            case 2:
            {
                // Đọc độ dài trước: "pos += ReadVarint(ref pos)" lấy pos cũ trước khi ReadVarint dịch nó
                var len = (int)ReadVarint(data, ref pos);
                pos += len;
                break;
            }
            case 5: pos += 4; break;
            default: throw new InvalidDataException($"Unsupported protobuf wire type {wireType}");
        }
    }
}
