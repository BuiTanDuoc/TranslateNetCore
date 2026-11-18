using System.Text;
using NetTranslate.Application.Abstractions;

namespace NetTranslate.Infrastructure.Documents;

/// <summary>Dịch file văn bản thuần (.txt, .md), giữ nguyên xuống dòng.</summary>
public sealed class TextDocumentTranslator : IDocumentTranslator
{
    public IReadOnlyCollection<string> Extensions { get; } = [".txt", ".md"];

    public async Task TranslateAsync(Stream input, Stream output, SegmentTranslator translate, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);

        // Dịch theo từng khối ~ 2000 ký tự (cắt ở ranh giới dòng) để auto-detect có mẫu và tránh request khổng lồ
        var blocks = SplitBlocks(text, 2000);
        var translated = await translate(blocks, cancellationToken);

        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        foreach (var block in translated) await writer.WriteAsync(block);
    }

    internal static List<string> SplitBlocks(string text, int maxChars)
    {
        var blocks = new List<string>();
        var sb = new StringBuilder();
        var start = 0;
        while (start < text.Length)
        {
            var nl = text.IndexOf('\n', start);
            var end = nl < 0 ? text.Length : nl + 1;
            if (sb.Length > 0 && sb.Length + (end - start) > maxChars)
            {
                blocks.Add(sb.ToString());
                sb.Clear();
            }
            sb.Append(text, start, end - start);
            start = end;
        }
        if (sb.Length > 0) blocks.Add(sb.ToString());
        return blocks;
    }
}
