using System.Globalization;
using System.Text;
using NetTranslate.Application.Abstractions;

namespace NetTranslate.Infrastructure.Detection;

/// <summary>
/// Bộ nhận diện ngôn ngữ nhẹ, không cần model:
/// 1) theo hệ chữ Unicode (Hangul, Kana, Hán, Thái, Kirin, Ả Rập...);
/// 2) với chữ Latin: chấm điểm theo ký tự đặc trưng (ơ ư đ, ñ, ß, ç...) và từ dừng phổ biến.
/// Đủ tốt cho việc chọn model; có thể thay bằng implementation khác (vd. fastText lid.176) qua DI.
/// </summary>
public sealed class HeuristicLanguageDetector : ILanguageDetector
{
    private static readonly Dictionary<string, HashSet<string>> StopWords = new()
    {
        ["en"] = Set("the of and to in is that it for was on are with as be this have from or by not but what all were we when your can said there use an each which she do how their if will up other about out many then them these so some her would make like him into has look two more write go see number no way could people my than first been who its now find long down day did get come made may part you i he they"),
        ["vi"] = Set("và của là có không được cho một những các này trong với người đã để khi thì như từ ra về lại cũng nhưng tôi bạn chúng ta đó rất nhiều sẽ đến làm mà nào gì nếu vì"),
        ["fr"] = Set("le la les de des du et est un une que qui dans pour pas sur au aux ce cette il elle nous vous ils sont avec par plus ne se son sa ses mais ou été être avoir je tu"),
        ["de"] = Set("der die das und ist nicht ein eine zu den mit von sich des auf für im dem an auch es als wie bei sind noch nach wird aus er sie wir ihr ich du aber oder kann"),
        ["es"] = Set("el la los las de del y que en un una es por con no para se lo al como más pero sus le ya o este sí porque esta entre cuando muy sin sobre también me hay yo tú"),
        ["it"] = Set("il lo la gli le di del della e che è un una per non con su da in sono si ma come anche più questo questa ci io tu lui lei noi voi loro ho ha"),
        ["pt"] = Set("o a os as de do da dos das e que em um uma é para com não se por mais como mas ao foi ele ela são seu sua isso eu você também já está muito"),
        ["nl"] = Set("de het een en van in is dat op te zijn voor met niet aan er die ook als bij maar om hij zij we ik je wat nog naar uit dan"),
        ["id"] = Set("dan yang di ini itu dengan untuk dari tidak ada ke dalam akan pada juga saya kami kita mereka adalah bisa sudah atau karena seperti oleh"),
        ["tr"] = Set("ve bir bu da de için ile çok ne o var gibi daha ama ben sen biz siz onlar mi mı değil olarak kadar sonra"),
        ["pl"] = Set("i w na z że się nie do to jest jak co ale o po tak za od jego jej są być przez dla ja ty my wy oni"),
        ["sv"] = Set("och i att det som en på är av för med till den har de inte om ett men jag du vi ni han hon var kan"),
        ["ro"] = Set("și în de la cu nu un o pe care este că să din pentru mai ce se sunt sau dar eu tu"),
        ["cs"] = Set("a v se na je že to s z do o ve jako pro ale by jsem jsou jak tak není"),
        ["ms"] = Set("dan yang di ini itu dengan untuk dari tidak ada ke dalam akan pada juga saya kami kita mereka ialah boleh sudah atau kerana"),
    };

    // Ký tự đặc trưng cho từng ngôn ngữ Latin (trọng số cộng thêm mỗi lần xuất hiện)
    private static readonly (string Chars, string Lang, double Weight)[] LatinHints =
    [
        ("ơưđăĂƠƯĐ", "vi", 3),
        ("ñ¿¡", "es", 2),
        ("ß", "de", 3), ("äöü", "de", 0.6),
        ("ãõ", "pt", 2),
        ("ğşıİ", "tr", 3),
        ("ąęłśźżń", "pl", 2.5),
        ("ěřůčš", "cs", 2),
        ("ăâîșț", "ro", 1.5),
        ("åÅ", "sv", 1.5),
        ("çœæ", "fr", 0.8), ("èêë", "fr", 0.6),
        ("ìò", "it", 0.6),
    ];

    private static HashSet<string> Set(string words) =>
        words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    public IReadOnlyList<DetectionResult> Detect(string text, IReadOnlyCollection<string> candidates)
    {
        var candidateSet = candidates.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scores = Score(text);

        var results = scores
            .Where(s => candidateSet.Contains(s.Key) && s.Value > 0)
            .ToList();

        var total = results.Sum(r => r.Value);
        var list = results
            .Select(r => new DetectionResult(r.Key, Math.Round(Math.Min(100, r.Value / total * 100), 1)))
            .OrderByDescending(r => r.Confidence)
            .ToList();

        if (list.Count == 0)
        {
            // Không đủ tín hiệu → mặc định tiếng Anh (hoặc ngôn ngữ đầu tiên có sẵn), độ tin cậy 0
            var fallback = candidateSet.Contains("en") ? "en" : candidates.First();
            list.Add(new DetectionResult(fallback, 0));
        }
        return list;
    }

    private static Dictionary<string, double> Score(string text)
    {
        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        void Add(string lang, double value) => scores[lang] = scores.GetValueOrDefault(lang) + value;

        if (text.Length > 5000) text = text[..(char.IsHighSurrogate(text[4999]) ? 4999 : 5000)];
        try { text = text.Normalize(NormalizationForm.FormC); } catch (ArgumentException) { /* chuỗi lỗi surrogate */ }

        // ------------------------------------------------ 1. Hệ chữ
        int latin = 0, han = 0, kana = 0, hangul = 0, thai = 0, cyrillic = 0, arabic = 0, hebrew = 0,
            greek = 0, devanagari = 0, bengali = 0, tamil = 0, georgian = 0, armenian = 0, khmer = 0, lao = 0, myanmar = 0,
            vietSpecific = 0;
        bool ukrainian = false, persian = false, urdu = false;

        foreach (var rune in text.EnumerateRunes())
        {
            var v = rune.Value;
            if (v is >= 0x1EA0 and <= 0x1EF9) { vietSpecific++; latin++; } // ạ ả ấ ầ ... ỹ
            else if (Rune.IsLetter(rune) && v < 0x250) latin++;
            else if (v is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF) han++;
            else if (v is >= 0x3040 and <= 0x30FF) kana++;
            else if (v is >= 0xAC00 and <= 0xD7AF or >= 0x1100 and <= 0x11FF) hangul++;
            else if (v is >= 0x0E00 and <= 0x0E7F) thai++;
            else if (v is >= 0x0400 and <= 0x04FF)
            {
                cyrillic++;
                if (v is 0x0456 or 0x0457 or 0x0454 or 0x0491) ukrainian = true; // і ї є ґ
            }
            else if (v is >= 0x0600 and <= 0x06FF)
            {
                arabic++;
                if (v is 0x067E or 0x0686 or 0x0698 or 0x06AF) persian = true; // پ چ ژ گ
                if (v is 0x0679 or 0x0688 or 0x0691 or 0x06BA or 0x06D2) urdu = true;
            }
            else if (v is >= 0x0590 and <= 0x05FF) hebrew++;
            else if (v is >= 0x0370 and <= 0x03FF) greek++;
            else if (v is >= 0x0900 and <= 0x097F) devanagari++;
            else if (v is >= 0x0980 and <= 0x09FF) bengali++;
            else if (v is >= 0x0B80 and <= 0x0BFF) tamil++;
            else if (v is >= 0x10A0 and <= 0x10FF) georgian++;
            else if (v is >= 0x0530 and <= 0x058F) armenian++;
            else if (v is >= 0x1780 and <= 0x17FF) khmer++;
            else if (v is >= 0x0E80 and <= 0x0EFF) lao++;
            else if (v is >= 0x1000 and <= 0x109F) myanmar++;
        }

        if (hangul > 0) Add("ko", hangul * 2);
        if (kana > 0) Add("ja", (kana + han) * 2);
        else if (han > 0) { Add("zh", han * 2); Add("zt", han * 1.5); }
        if (thai > 0) Add("th", thai * 2);
        if (cyrillic > 0)
        {
            Add(ukrainian ? "uk" : "ru", cyrillic * 2);
            Add(ukrainian ? "ru" : "uk", cyrillic * 0.3);
            Add("bg", cyrillic * 0.3);
        }
        if (arabic > 0) Add(urdu ? "ur" : persian ? "fa" : "ar", arabic * 2);
        if (hebrew > 0) Add("he", hebrew * 2);
        if (greek > 0) Add("el", greek * 2);
        if (devanagari > 0) Add("hi", devanagari * 2);
        if (bengali > 0) Add("bn", bengali * 2);
        if (tamil > 0) Add("ta", tamil * 2);
        if (georgian > 0) Add("ka", georgian * 2);
        if (armenian > 0) Add("hy", armenian * 2);
        if (khmer > 0) Add("km", khmer * 2);
        if (lao > 0) Add("lo", lao * 2);
        if (myanmar > 0) Add("my", myanmar * 2);

        if (latin == 0) return scores;

        // ------------------------------------------------ 2. Chữ Latin
        if (vietSpecific > 0) Add("vi", vietSpecific * 3);

        foreach (var c in text)
            foreach (var (chars, lang, weight) in LatinHints)
                if (chars.Contains(c)) Add(lang, weight);

        var words = Tokenize(text);
        foreach (var word in words)
            foreach (var (lang, set) in StopWords)
                if (set.Contains(word)) Add(lang, 1);

        // Văn bản Latin không khớp gì → nghiêng nhẹ về tiếng Anh
        if (!scores.Keys.Any(k => StopWords.ContainsKey(k)))
            Add("en", 0.5);

        return scores;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetter(c) || c == '\'') sb.Append(c);
            else if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
