using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NetTranslate.Application.Services;

public enum EntityKind
{
    /// <summary>Biển số, URL, email, số tiền... — giữ nguyên (trừ đơn vị tiền).</summary>
    Generic,
    /// <summary>Tên người — giữ nguyên, bỏ dấu khi dịch sang ngôn ngữ khác.</summary>
    Name,
    /// <summary>Địa danh có đơn vị hành chính (phường, quận, đường...) — dịch đơn vị, giữ tên.</summary>
    Place,
}

public readonly record struct EntitySpan(int Start, int Length, EntityKind Kind);

/// <summary>
/// Nhận diện tên người và địa danh trong câu tiếng Việt bằng quy tắc (không cần model):
/// <list type="number">
/// <item>Địa danh nổi tiếng viết tắt: TP HCM, TP.HCM, Sài Gòn, Hà Nội...</item>
/// <item>Đơn vị hành chính + tên: "phường Bình Trị Đông", "quận 1", "đường số 14", "TP Thủ Đức".</item>
/// <item>Chuỗi 2–4 âm tiết viết hoa liền nhau: "Nguyễn Thanh Phong". Nếu đứng đầu câu thì âm tiết đầu
/// phải là họ phổ biến (để không nhầm chữ hoa đầu câu). Bỏ qua câu viết hoa kiểu tiêu đề.</item>
/// <item>Từ xưng hô / chức danh + tên: "anh Phong", "tài xế Hùng", "bác sĩ Lan".</item>
/// </list>
/// </summary>
public static class VietnameseNameDetector
{
    private const string Syllable = @"\p{Lu}\p{Ll}*";
    private const string NoLetterBefore = @"(?<![\p{L}\p{N}])";
    private const string NoLetterAfter = @"(?![\p{L}\p{N}])";

    private static readonly HashSet<string> Surnames = new(StringComparer.Ordinal)
    {
        "Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng", "Bùi", "Đỗ",
        "Hồ", "Ngô", "Dương", "Lý", "Đào", "Đoàn", "Vương", "Trịnh", "Trương", "Lâm", "Mai", "Đinh",
        "Lưu", "Cao", "Tạ", "Châu", "Hà", "Tôn", "Thái", "Lương", "Quách", "Tăng", "Kiều", "Mạc",
        "Văn", "Thân", "La", "Từ", "Lục", "Âu", "Diệp", "Triệu", "Phùng", "Khúc", "Cù", "Chu",
    };

    /// <summary>Đơn vị hành chính → tiếng Anh. Dài trước ngắn sau để "thành phố" không bị "phố" ăn mất.</summary>
    private static readonly (string Vi, string En)[] Units =
    [
        ("thành phố", "City"), ("thị xã", "Town"), ("thị trấn", "Town"), ("khu phố", "Quarter"),
        ("phường", "Ward"), ("quận", "District"), ("huyện", "District"), ("xã", "Commune"),
        ("tỉnh", "Province"), ("đường", "Street"), ("phố", "Street"), ("ấp", "Hamlet"), ("thôn", "Village"),
        ("tp.", "City"), ("tp", "City"),
    ];

    /// <summary>Địa danh hay gặp có tên tiếng Anh riêng.</summary>
    private static readonly (Regex Pattern, string En)[] Gazetteer =
    [
        (new Regex($@"{NoLetterBefore}(?i:(?:thành phố|tp\.?)\s*(?:hcm|hồ chí minh)|tp\.?hcm|sài gòn){NoLetterAfter}|{NoLetterBefore}HCM{NoLetterAfter}", RegexOptions.CultureInvariant), "Ho Chi Minh City"),
        (new Regex($@"{NoLetterBefore}(?i:(?:thành phố|tp\.?)\s*)?Hà Nội{NoLetterAfter}", RegexOptions.CultureInvariant), "Hanoi"),
    ];

    private static readonly Regex UnitName = new(
        $@"{NoLetterBefore}(?<unit>(?i:{string.Join("|", Units.Select(u => Regex.Escape(u.Vi)))}))\s*" +
        $@"(?<name>(?i:số\s*)?\d+[A-Za-z]?|{Syllable}(?: {Syllable}){{0,4}}){NoLetterAfter}",
        RegexOptions.CultureInvariant);

    private static readonly Regex FullName = new(
        $@"{NoLetterBefore}{Syllable}(?: {Syllable}){{1,3}}{NoLetterAfter}", RegexOptions.CultureInvariant);

    private static readonly Regex Honorific = new(
        $@"{NoLetterBefore}(?i:tài xế|bác sĩ|giám đốc|khách hàng|anh|chị|ông|bà|cô|chú|bác|em|thầy)\s+" +
        $@"(?<name>{Syllable}(?: {Syllable}){{0,3}}){NoLetterAfter}",
        RegexOptions.CultureInvariant);

    private static readonly Regex Word = new(@"\p{L}+", RegexOptions.CultureInvariant);

    public static List<EntitySpan> Find(string text)
    {
        var spans = new List<EntitySpan>();

        foreach (var (pattern, _) in Gazetteer)
            foreach (Match m in pattern.Matches(text))
                TryAdd(spans, m.Index, m.Length, EntityKind.Place);

        foreach (Match m in UnitName.Matches(text))
            TryAdd(spans, m.Index, m.Length, EntityKind.Place);

        if (!IsTitleCase(text))
        {
            foreach (Match m in FullName.Matches(text))
            {
                if (IsSentenceStart(text, m.Index))
                {
                    var first = m.Value[..(m.Value.IndexOf(' ') is var sp and > 0 ? sp : m.Value.Length)];
                    if (!Surnames.Contains(first)) continue;
                }
                TryAdd(spans, m.Index, m.Length, EntityKind.Name);
            }
        }

        foreach (Match m in Honorific.Matches(text))
        {
            var g = m.Groups["name"];
            TryAdd(spans, g.Index, g.Length, EntityKind.Name);
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return spans;
    }

    /// <summary>Viết thực thể tiếng Việt cho ngôn ngữ đích khác tiếng Việt.</summary>
    public static string Render(string value, EntityKind kind, bool keepDiacritics)
    {
        string Name(string s) => keepDiacritics ? s : StripDiacritics(s);

        if (kind == EntityKind.Name) return Name(value);
        if (kind != EntityKind.Place) return value;

        foreach (var (pattern, english) in Gazetteer)
            if (pattern.Match(value) is { Success: true } g && g.Length == value.Length) return english;

        var m = UnitName.Match(value);
        if (!m.Success || m.Length != value.Length) return Name(value);

        var unit = m.Groups["unit"].Value.ToLowerInvariant();
        var en = Units.First(u => u.Vi == unit).En;
        var name = m.Groups["name"].Value;
        var number = Regex.Match(name, @"^(?i:số\s*)?(\d+[A-Za-z]?)$");
        return number.Success
            ? $"{en} {number.Groups[1].Value}"     // quận 1 → District 1, đường số 14 → Street 14
            : $"{Name(name)} {en}";                // phường Bình Trị Đông → Binh Tri Dong Ward
    }

    public static string StripDiacritics(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch { 'đ' => 'd', 'Đ' => 'D', _ => c });
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void TryAdd(List<EntitySpan> spans, int start, int length, EntityKind kind)
    {
        var end = start + length;
        if (spans.Any(s => start < s.Start + s.Length && s.Start < end)) return;
        spans.Add(new EntitySpan(start, length, kind));
    }

    /// <summary>Đầu câu: chỉ có khoảng trắng/dấu câu mở đứng trước, hoặc ngay sau . ! ? xuống dòng.</summary>
    private static bool IsSentenceStart(string text, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) && c is not ('\n' or '\r')) continue;
            if (c is '"' or '\'' or '(' or '“' or '-' or '–' or '•' or '*') continue;
            return c is '.' or '!' or '?' or '\n' or '\r' or '…';
        }
        return true;
    }

    /// <summary>Câu kiểu tiêu đề ("Hướng Dẫn Sử Dụng Phần Mềm") — đa số từ viết hoa, không đoán tên được.</summary>
    private static bool IsTitleCase(string text)
    {
        var words = Word.Matches(text).Select(m => m.Value).ToList();
        if (words.Count < 4) return false;
        var capitalized = words.Count(w => char.IsUpper(w[0]));
        return capitalized * 10 >= words.Count * 7;
    }
}
