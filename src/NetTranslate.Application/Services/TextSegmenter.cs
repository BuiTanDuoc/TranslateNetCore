using System.Text;
using System.Text.RegularExpressions;
using NetTranslate.Domain.Languages;

namespace NetTranslate.Application.Services;

/// <summary>
/// Tách văn bản thành các câu để đưa vào model (MarianMT dịch tốt nhất ở mức câu),
/// đồng thời giữ nguyên xuống dòng, thụt đầu dòng và khoảng trắng để ghép lại.
/// </summary>
public static partial class TextSegmenter
{
    public enum PieceKind
    {
        /// <summary>Giữ nguyên (xuống dòng, khoảng trắng đầu/cuối dòng).</summary>
        Literal,
        /// <summary>Một câu cần dịch.</summary>
        Sentence,
        /// <summary>Khoảng cách giữa hai câu trong cùng dòng.</summary>
        Gap,
    }

    public readonly record struct Piece(PieceKind Kind, string Text);

    // Ngắt câu sau . ! ? … (kèm ngoặc/nháy đóng) khi theo sau là khoảng trắng,
    // hoặc ngay sau dấu câu CJK 。！？
    [GeneratedRegex(@"(?<=[.!?…][""'”’)\]]*)\s+(?=\S)|(?<=[。！？])", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundary();

    [GeneratedRegex(@"\r\n|\n|\r", RegexOptions.CultureInvariant)]
    private static partial Regex LineBreak();

    /// <summary>Độ dài tối đa (ký tự) của một câu trước khi bị cắt nhỏ thêm.</summary>
    private const int MaxSentenceChars = 400;

    public static List<Piece> Split(string text)
    {
        var pieces = new List<Piece>();
        if (string.IsNullOrEmpty(text)) return pieces;

        var position = 0;
        foreach (Match lb in LineBreak().Matches(text))
        {
            SplitLine(text[position..lb.Index], pieces);
            pieces.Add(new Piece(PieceKind.Literal, lb.Value));
            position = lb.Index + lb.Length;
        }
        SplitLine(text[position..], pieces);
        return pieces;
    }

    private static void SplitLine(string line, List<Piece> pieces)
    {
        if (line.Length == 0) return;

        var start = 0;
        while (start < line.Length && char.IsWhiteSpace(line[start])) start++;
        var end = line.Length;
        while (end > start && char.IsWhiteSpace(line[end - 1])) end--;

        if (start > 0) pieces.Add(new Piece(PieceKind.Literal, line[..start]));
        if (end > start)
        {
            var core = line[start..end];
            var first = true;
            foreach (var sentence in SentenceBoundary().Split(core))
            {
                if (string.IsNullOrWhiteSpace(sentence)) continue;
                foreach (var chunk in ChunkLongSentence(sentence.Trim()))
                {
                    if (!first) pieces.Add(new Piece(PieceKind.Gap, " "));
                    pieces.Add(IsTranslatable(chunk)
                        ? new Piece(PieceKind.Sentence, chunk)
                        : new Piece(PieceKind.Literal, chunk));
                    first = false;
                }
            }
        }
        if (end < line.Length) pieces.Add(new Piece(PieceKind.Literal, line[end..]));
    }

    /// <summary>Câu quá dài → cắt theo dấu phẩy/chấm phẩy, rồi theo khoảng trắng.</summary>
    private static IEnumerable<string> ChunkLongSentence(string sentence)
    {
        if (sentence.Length <= MaxSentenceChars)
        {
            yield return sentence;
            yield break;
        }

        var sb = new StringBuilder();
        foreach (var word in sentence.Split(' '))
        {
            if (sb.Length > 0 && sb.Length + word.Length + 1 > MaxSentenceChars)
            {
                yield return sb.ToString();
                sb.Clear();
            }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(word);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>Chuỗi chỉ toàn số/ký hiệu thì không cần qua model.</summary>
    private static bool IsTranslatable(string s)
    {
        foreach (var c in s)
            if (char.IsLetter(c)) return true;
        return false;
    }

    public static string Join(IReadOnlyList<Piece> pieces, IReadOnlyDictionary<string, string> translations, string targetLanguage)
    {
        var gap = LanguageCatalog.IsScriptWithoutSpaces(targetLanguage) ? string.Empty : " ";
        var sb = new StringBuilder();
        foreach (var piece in pieces)
        {
            switch (piece.Kind)
            {
                case PieceKind.Sentence:
                    sb.Append(translations.TryGetValue(piece.Text, out var t) ? t : piece.Text);
                    break;
                case PieceKind.Gap:
                    sb.Append(gap);
                    break;
                default:
                    sb.Append(piece.Text);
                    break;
            }
        }
        return sb.ToString();
    }
}
