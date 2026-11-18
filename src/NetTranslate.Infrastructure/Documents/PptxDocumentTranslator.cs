using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using NetTranslate.Application.Abstractions;
using A = DocumentFormat.OpenXml.Drawing;

namespace NetTranslate.Infrastructure.Documents;

/// <summary>Dịch file PowerPoint (.pptx): text trong slide và ghi chú (notes).</summary>
public sealed class PptxDocumentTranslator : IDocumentTranslator
{
    public IReadOnlyCollection<string> Extensions { get; } = [".pptx"];

    public async Task TranslateAsync(Stream input, Stream output, SegmentTranslator translate, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using (var doc = PresentationDocument.Open(buffer, isEditable: true))
        {
            var presentation = doc.PresentationPart ?? throw new InvalidDataException("Invalid .pptx: missing presentation part");

            var roots = new List<OpenXmlPartRootElement>();
            foreach (var slidePart in presentation.SlideParts)
            {
                if (slidePart.Slide is not null) roots.Add(slidePart.Slide);
                if (slidePart.NotesSlidePart?.NotesSlide is { } notes) roots.Add(notes);
            }

            var paragraphs = roots
                .SelectMany(r => r.Descendants<A.Paragraph>())
                .Select(p => (IReadOnlyList<OpenXmlLeafTextElement>)p.Descendants<A.Text>().Cast<OpenXmlLeafTextElement>().ToList())
                .ToList();

            await OpenXmlTextHelper.TranslateParagraphsAsync(paragraphs, translate, cancellationToken);

            foreach (var root in roots) root.Save();
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(output, cancellationToken);
    }
}
