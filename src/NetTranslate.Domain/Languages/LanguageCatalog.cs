namespace NetTranslate.Domain.Languages;

/// <summary>Bảng mã ngôn ngữ ISO 639-1 → tên tiếng Anh (giống LibreTranslate).</summary>
public static class LanguageCatalog
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["af"] = "Afrikaans", ["ar"] = "Arabic", ["az"] = "Azerbaijani", ["bg"] = "Bulgarian",
        ["bn"] = "Bengali", ["ca"] = "Catalan", ["cs"] = "Czech", ["da"] = "Danish",
        ["de"] = "German", ["el"] = "Greek", ["en"] = "English", ["eo"] = "Esperanto",
        ["es"] = "Spanish", ["et"] = "Estonian", ["eu"] = "Basque", ["fa"] = "Persian",
        ["fi"] = "Finnish", ["fr"] = "French", ["ga"] = "Irish", ["gl"] = "Galician",
        ["he"] = "Hebrew", ["hi"] = "Hindi", ["hu"] = "Hungarian", ["hy"] = "Armenian",
        ["id"] = "Indonesian", ["it"] = "Italian", ["ja"] = "Japanese", ["ka"] = "Georgian",
        ["km"] = "Khmer", ["ko"] = "Korean", ["lo"] = "Lao", ["lt"] = "Lithuanian",
        ["lv"] = "Latvian", ["ms"] = "Malay", ["my"] = "Burmese", ["nb"] = "Norwegian Bokmål",
        ["nl"] = "Dutch", ["pl"] = "Polish", ["pt"] = "Portuguese", ["ro"] = "Romanian",
        ["ru"] = "Russian", ["sk"] = "Slovak", ["sl"] = "Slovenian", ["sq"] = "Albanian",
        ["sr"] = "Serbian", ["sv"] = "Swedish", ["ta"] = "Tamil", ["th"] = "Thai",
        ["tl"] = "Tagalog", ["tr"] = "Turkish", ["uk"] = "Ukrainian", ["ur"] = "Urdu",
        ["vi"] = "Vietnamese", ["zh"] = "Chinese", ["zt"] = "Chinese (traditional)",
    };

    public static string GetName(string code) =>
        Names.TryGetValue(code, out var name) ? name : code;

    /// <summary>Ngôn ngữ viết liền không có dấu cách giữa các câu.</summary>
    public static bool IsScriptWithoutSpaces(string code) =>
        code is "zh" or "zt" or "ja" or "th" or "lo" or "km" or "my";
}
