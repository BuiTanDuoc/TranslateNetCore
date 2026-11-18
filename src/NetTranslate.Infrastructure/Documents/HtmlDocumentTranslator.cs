using System.Text;
using HtmlAgilityPack;
using NetTranslate.Application.Abstractions;

namespace NetTranslate.Infrastructure.Documents;

/// <summary>
/// Dịch HTML: chỉ dịch text node và một số thuộc tính (alt, title, placeholder, aria-label),
/// giữ nguyên cấu trúc thẻ. Bỏ qua script/style/code/pre và phần tử có translate="no" hoặc class "notranslate".
/// Dùng cho cả format=html của /translate và file .html.
/// </summary>
public sealed class HtmlDocumentTranslator : IHtmlTranslator, IDocumentTranslator
{
    private static readonly HashSet<string> SkippedElements =
        new(StringComparer.OrdinalIgnoreCase) { "script", "style", "code", "pre", "noscript", "kbd", "samp", "var", "svg", "math" };

    private static readonly string[] TranslatableAttributes = ["alt", "title", "placeholder", "aria-label"];

    public IReadOnlyCollection<string> Extensions { get; } = [".html", ".htm"];

    public async Task<string> TranslateAsync(string html, SegmentTranslator translate, CancellationToken cancellationToken = default)
    {
        var doc = CreateDocument();
        doc.LoadHtml(html);
        await TranslateDocumentAsync(doc, translate, cancellationToken);
        return doc.DocumentNode.OuterHtml;
    }

    public async Task TranslateAsync(Stream input, Stream output, SegmentTranslator translate, CancellationToken cancellationToken = default)
    {
        var doc = CreateDocument();
        doc.Load(input, true); // tự nhận encoding từ BOM
        await TranslateDocumentAsync(doc, translate, cancellationToken);

        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        doc.Save(writer);
    }

    private static HtmlDocument CreateDocument() => new()
    {
        OptionOutputOriginalCase = true,
        OptionWriteEmptyNodes = false,
    };

    private static async Task TranslateDocumentAsync(HtmlDocument doc, SegmentTranslator translate, CancellationToken ct)
    {
        var setters = new List<Action<string>>();
        var segments = new List<string>();

        foreach (var node in doc.DocumentNode.Descendants())
        {
            if (IsSkipped(node)) continue;

            if (node is HtmlTextNode textNode)
            {
                var raw = HtmlEntity.DeEntitize(textNode.Text);
                if (string.IsNullOrWhiteSpace(raw) || !raw.Any(char.IsLetter)) continue;

                var (lead, core, trail) = SplitWhitespace(raw);
                segments.Add(core);
                setters.Add(t => textNode.Text = lead + Escape(t) + trail);
            }
            else if (node.NodeType == HtmlNodeType.Element)
            {
                foreach (var name in TranslatableAttributes)
                {
                    var attr = node.Attributes[name];
                    if (attr is null) continue;
                    var value = HtmlEntity.DeEntitize(attr.Value);
                    if (string.IsNullOrWhiteSpace(value) || !value.Any(char.IsLetter)) continue;

                    segments.Add(value.Trim());
                    setters.Add(t => attr.Value = EscapeAttribute(t));
                }
            }
        }

        if (segments.Count == 0) return;

        var translated = await translate(segments, ct);
        for (var i = 0; i < setters.Count; i++) setters[i](translated[i]);
    }

    private static bool IsSkipped(HtmlNode node)
    {
        for (var n = node.NodeType == HtmlNodeType.Element ? node : node.ParentNode; n is not null; n = n.ParentNode)
        {
            if (n.NodeType != HtmlNodeType.Element) continue;
            if (SkippedElements.Contains(n.Name)) return true;
            if (string.Equals(n.GetAttributeValue("translate", ""), "no", StringComparison.OrdinalIgnoreCase)) return true;
            var cls = n.GetAttributeValue("class", "");
            if (cls.Length > 0 && cls.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("notranslate")) return true;
        }
        return false;
    }

    private static (string Lead, string Core, string Trail) SplitWhitespace(string s)
    {
        var start = 0;
        while (start < s.Length && char.IsWhiteSpace(s[start])) start++;
        var end = s.Length;
        while (end > start && char.IsWhiteSpace(s[end - 1])) end--;
        return (s[..start], s[start..end], s[end..]);
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string EscapeAttribute(string s) =>
        Escape(s).Replace("\"", "&quot;");
}
