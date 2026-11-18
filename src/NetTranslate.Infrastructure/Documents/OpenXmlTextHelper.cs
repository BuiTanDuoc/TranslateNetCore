using DocumentFormat.OpenXml;
using NetTranslate.Application.Abstractions;

namespace NetTranslate.Infrastructure.Documents;

/// <summary>
/// Dịch theo đoạn (paragraph) cho file Office: ghép các run trong một đoạn thành một câu hoàn chỉnh
/// để model hiểu ngữ cảnh, dịch xong ghi vào run đầu tiên và xoá text các run còn lại.
/// Đánh đổi: định dạng khác nhau trong cùng một đoạn (in đậm một từ...) sẽ theo run đầu tiên.
/// </summary>
internal static class OpenXmlTextHelper
{
    public static async Task TranslateParagraphsAsync(
        IReadOnlyList<IReadOnlyList<OpenXmlLeafTextElement>> paragraphs,
        SegmentTranslator translate,
        CancellationToken ct,
        Action<OpenXmlLeafTextElement>? afterSet = null)
    {
        var targets = new List<IReadOnlyList<OpenXmlLeafTextElement>>();
        var segments = new List<string>();

        foreach (var runs in paragraphs)
        {
            if (runs.Count == 0) continue;
            var text = string.Concat(runs.Select(r => r.Text));
            if (string.IsNullOrWhiteSpace(text) || !text.Any(char.IsLetter)) continue;
            targets.Add(runs);
            segments.Add(text);
        }

        if (segments.Count == 0) return;
        var translated = await translate(segments, ct);

        for (var i = 0; i < targets.Count; i++)
        {
            var runs = targets[i];
            // Giữ khoảng trắng đầu/cuối gốc của đoạn
            var original = segments[i];
            var lead = original[..(original.Length - original.TrimStart().Length)];
            var trail = original[original.TrimEnd().Length..];

            runs[0].Text = lead + translated[i].Trim() + trail;
            afterSet?.Invoke(runs[0]);
            for (var k = 1; k < runs.Count; k++) runs[k].Text = string.Empty;
        }
    }
}
