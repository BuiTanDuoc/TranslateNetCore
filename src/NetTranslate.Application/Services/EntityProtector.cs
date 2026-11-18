using System.Text.RegularExpressions;

namespace NetTranslate.Application.Services;

/// <summary>
/// Bảo vệ các "thực thể" không được dịch (biển số xe, URL, email, số điện thoại, mã, ngày giờ...).
/// MarianMT hay làm hỏng các chuỗi này (ví dụ "51A-123.45" → "số 1- 1-3.45"), nên trước khi dịch
/// ta thay chúng bằng placeholder ngắn mà model thường chép nguyên (X1, X2...), dịch xong thì trả lại.
/// Model không chép placeholder ổn định 100%, nên mỗi câu được kiểm tra: placeholder nào cũng phải
/// xuất hiện đúng một lần; nếu không thì dịch lại câu đó với kiểu placeholder khác, cuối cùng mới
/// rơi về cách dịch thường (không che).
/// </summary>
public sealed class EntityProtector
{
    /// <summary>Các mẫu mặc định, dùng khi cấu hình Translate:ProtectedPatterns để trống.</summary>
    public static readonly IReadOnlyList<string> DefaultPatterns =
    [
        // Số tiền kèm đơn vị: 19.200.000 đ, 500.000₫, 20 USD, $1,200.50, ₫50.000
        @"(?<![\w.,])(?:\d{1,3}(?:[.,]\d{3})+(?:[.,]\d+)?|\d+(?:[.,]\d+)?)\s?(?i:đồng|vnđ|vnd|usd|eur|đ|₫)(?![\p{L}\d])",
        @"(?<![\w.,])[$€£¥₫]\s?(?:\d{1,3}(?:[.,]\d{3})+(?:[.,]\d+)?|\d+(?:[.,]\d+)?)\b",
        // URL (bỏ dấu câu dính ở cuối)
        @"\b(?:https?://|www\.)[^\s<>""]+?(?=[.,;:!?)\]]*(?:\s|$))",
        // Email
        @"\b[\w.+-]+@[\w-]+(?:\.[\w-]+)+\b",
        // Chuỗi chữ/số có dấu nối . - / : và chứa ít nhất một chữ số:
        // biển số 51A-123.45, ngày 2024-05-12, giờ 10:30, mã SKU-9921, số thập phân 3.5
        @"\b(?=[A-Za-z0-9.\-/:]*\d)[A-Za-z0-9]+(?:[-./:][A-Za-z0-9]+)+\b",
        // Số điện thoại có dấu cách: 0901 234 567, +84 912 345 678
        @"(?<![\w+])\+?\d{2,4}(?: \d{2,4}){2,4}\b",
        // Mã trộn chữ và số: XPLATE01, AB12CD
        @"\b(?=[A-Za-z]*\d)(?=\d*[A-Za-z])[A-Za-z0-9]{3,}\b",
        // Số dài (từ 4 chữ số) hoặc số có phân cách hàng nghìn
        @"\b\d{1,3}(?:,\d{3})+\b",
        @"\b\d{4,}\b",
    ];

    /// <summary>Các kiểu placeholder lần lượt thử. Đã đo trên opus-mt-en-vi: đây là các kiểu được chép lại tốt nhất.</summary>
    private static readonly string[] Styles = ["X", "XY", "Q"];

    /// <summary>Kiểu nào dùng hậu tố chữ (XYZ, XYW...) thay vì số.</summary>
    private static readonly HashSet<int> LetterStyles = [1];

    private static readonly string[] Letters = ["Z", "W", "V", "U", "T", "S", "R", "P", "N", "M"];

    private readonly Regex _regex;
    private readonly bool _detectNames;
    private readonly bool _keepNameDiacritics;

    public EntityProtector(IEnumerable<string>? patterns, bool detectNames = true, bool keepNameDiacritics = false)
    {
        var list = (patterns ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (list.Count == 0) list = DefaultPatterns.ToList();
        _regex = new Regex(
            string.Join("|", list.Select(p => $"(?:{p})")),
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));
        _detectNames = detectNames;
        _keepNameDiacritics = keepNameDiacritics;
    }

    public static int StyleCount => Styles.Length;

    /// <summary>Tìm các thực thể cần bảo vệ: tên riêng (nếu nguồn là tiếng Việt) rồi tới các regex.</summary>
    public List<EntitySpan> FindSpans(string text, string? sourceLanguage = null)
    {
        var spans = _detectNames && string.Equals(sourceLanguage, "vi", StringComparison.OrdinalIgnoreCase)
            ? VietnameseNameDetector.Find(text)
            : [];

        foreach (Match m in _regex.Matches(text))
        {
            var end = m.Index + m.Length;
            if (m.Length == 0 || spans.Any(s => m.Index < s.Start + s.Length && s.Start < end)) continue;
            spans.Add(new EntitySpan(m.Index, m.Length, EntityKind.Generic));
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return spans;
    }

    /// <summary>
    /// Thay thực thể bằng placeholder kiểu <paramref name="style"/>.
    /// Trả về null nếu câu đã có sẵn chuỗi trùng placeholder (khi đó không dùng được kiểu này).
    /// </summary>
    public string? Mask(string text, int style, string? sourceLanguage, out IReadOnlyList<(string Value, EntityKind Kind)> entities)
    {
        // Văn bản dán từ web/Word hay ở dạng tổ hợp (NFD: "ồ" = o + dấu mũ + dấu huyền),
        // khi đó "đồng" không khớp regex. Đưa về NFC trước (model cũng chuẩn hoá NFKC nên không đổi nghĩa).
        text = text.Normalize(System.Text.NormalizationForm.FormC);
        var spans = FindSpans(text, sourceLanguage);
        entities = spans.Select(s => (text.Substring(s.Start, s.Length), s.Kind)).ToList();
        if (spans.Count == 0) return text;

        // Phần chữ còn lại (ngoài thực thể) đã có chuỗi giống placeholder kiểu này (ví dụ chữ "XYZ"
        // hay "X2" đứng riêng) → không dùng được kiểu này, vì lúc trả lại sẽ nhầm.
        if (StyleRegex(style).IsMatch(Build(text, spans, _ => " "))) return null;

        return Build(text, spans, i => Placeholder(style, i));
    }

    private static string Build(string text, List<EntitySpan> spans, Func<int, string> replacement)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var pos = 0;
        for (var i = 0; i < spans.Count; i++)
        {
            sb.Append(text, pos, spans[i].Start - pos).Append(replacement(i));
            pos = spans[i].Start + spans[i].Length;
        }
        return sb.Append(text, pos, text.Length - pos).ToString();
    }

    /// <summary>
    /// Trả thực thể về chỗ placeholder. Trả về null nếu bản dịch làm mất, nhân đôi hoặc bịa thêm placeholder.
    /// </summary>
    public string? Unmask(string translated, int style, IReadOnlyList<(string Value, EntityKind Kind)> entities, string? targetLanguage = null)
    {
        // Model bịa thêm placeholder không có trong câu gốc (ví dụ X3 khi chỉ có X1, X2)
        if (StyleRegex(style).Matches(translated).Count != entities.Count) return null;

        var result = translated;
        for (var i = 0; i < entities.Count; i++)
        {
            var rx = PlaceholderRegex(Placeholder(style, i));
            if (rx.Matches(result).Count != 1) return null;
            var entity = Render(entities[i].Value, entities[i].Kind, targetLanguage);
            result = rx.Replace(result, _ => entity, 1);
        }
        return result;
    }

    /// <summary>
    /// Viết lại thực thể cho hợp ngôn ngữ đích khác tiếng Việt: đơn vị tiền Việt (đ, ₫, đồng, VNĐ) → "VND"
    /// (con số giữ nguyên); tên người bỏ dấu (trừ khi KeepNameDiacritics); địa danh dịch đơn vị hành chính.
    /// </summary>
    public string Render(string entity, EntityKind kind, string? targetLanguage)
    {
        if (targetLanguage is null || targetLanguage.Equals("vi", StringComparison.OrdinalIgnoreCase))
            return entity;

        if (kind != EntityKind.Generic)
            return VietnameseNameDetector.Render(entity, kind, _keepNameDiacritics);

        var suffix = VndSuffix.Match(entity);
        if (suffix.Success) return entity[..suffix.Index] + " VND";
        var prefix = VndPrefix.Match(entity);
        if (prefix.Success) return entity[prefix.Length..] + " VND";
        return entity;
    }

    private static readonly Regex VndSuffix =
        new(@"\s?(?i:đồng|vnđ|đ)$|\s?₫$", RegexOptions.CultureInvariant);

    private static readonly Regex VndPrefix = new(@"^₫\s?", RegexOptions.CultureInvariant);

    /// <summary>Câu chỉ toàn thực thể (không còn chữ nào để dịch) → giữ nguyên, khỏi đưa vào model.</summary>
    public static bool HasNothingToTranslate(string masked, int style, int entityCount)
    {
        var rest = masked;
        for (var i = 0; i < entityCount; i++) rest = PlaceholderRegex(Placeholder(style, i)).Replace(rest, " ");
        return !rest.Any(char.IsLetter);
    }

    // Kiểu số: X1, X2... (PlaceholderRegex có ranh giới nên X1 không khớp vào X10).
    // Kiểu chữ: XYZ, XYW, XYV... (không có chữ số, model hay giữ nguyên như tên riêng).
    private static string Placeholder(int style, int index)
    {
        var prefix = Styles[style];
        if (!LetterStyles.Contains(style)) return prefix + (index + 1);
        var round = index / Letters.Length;
        return prefix + Letters[index % Letters.Length] + (round == 0 ? "" : round.ToString());
    }

    /// <summary>Khớp mọi placeholder của một kiểu (X1, X27... hoặc XYZ, XYW3...).</summary>
    private static Regex StyleRegex(int style) =>
        new($@"(?<![A-Za-z0-9]){Regex.Escape(Styles[style])}{(LetterStyles.Contains(style) ? "[A-Z]\\d*" : "\\d+")}(?![A-Za-z0-9])",
            RegexOptions.CultureInvariant);

    private static Regex PlaceholderRegex(string placeholder) =>
        new($@"(?<![A-Za-z0-9]){Regex.Escape(placeholder)}(?![A-Za-z0-9])", RegexOptions.CultureInvariant);
}
