using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using NetTranslate.Application.Abstractions;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace NetTranslate.Infrastructure.Documents;

/// <summary>Dịch file Word (.docx): thân văn bản, bảng, header, footer, footnote, endnote.</summary>
public sealed class DocxDocumentTranslator : IDocumentTranslator
{
    public IReadOnlyCollection<string> Extensions { get; } = [".docx"];

    public async Task TranslateAsync(Stream input, Stream output, SegmentTranslator translate, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using (var doc = WordprocessingDocument.Open(buffer, isEditable: true))
        {
            var main = doc.MainDocumentPart ?? throw new InvalidDataException("Invalid .docx: missing main document part");

            var roots = new List<OpenXmlElement>();
            if (main.Document is not null) roots.Add(main.Document);
            roots.AddRange(main.HeaderParts.Select(p => p.Header).OfType<OpenXmlElement>());
            roots.AddRange(main.FooterParts.Select(p => p.Footer).OfType<OpenXmlElement>());
            if (main.FootnotesPart?.Footnotes is { } fn) roots.Add(fn);
            if (main.EndnotesPart?.Endnotes is { } en) roots.Add(en);

            var paragraphs = new List<IReadOnlyList<OpenXmlLeafTextElement>>();
            foreach (var root in roots)
            {
                foreach (var p in root.Descendants<W.Paragraph>())
                {
                    // Chỉ lấy text thuộc trực tiếp đoạn này (không lấy đoạn lồng trong text box)
                    var texts = p.Descendants<W.Text>()
                        .Where(t => t.Ancestors<W.Paragraph>().FirstOrDefault() == p)
                        .Cast<OpenXmlLeafTextElement>()
                        .ToList();
                    paragraphs.Add(texts);
                }
            }

            await OpenXmlTextHelper.TranslateParagraphsAsync(paragraphs, translate, cancellationToken,
                t => ((W.Text)t).Space = SpaceProcessingModeValues.Preserve);

            main.Document?.Save();
            foreach (var h in main.HeaderParts) h.Header?.Save();
            foreach (var f in main.FooterParts) f.Footer?.Save();
            main.FootnotesPart?.Footnotes?.Save();
            main.EndnotesPart?.Endnotes?.Save();
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(output, cancellationToken);
    }
}
