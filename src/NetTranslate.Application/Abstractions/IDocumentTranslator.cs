namespace NetTranslate.Application.Abstractions;

/// <summary>
/// Hàm dịch một loạt đoạn văn bản thuần (mỗi đoạn có thể gồm nhiều câu, nhiều dòng).
/// Do TranslationService cung cấp, đã xử lý pivot và tách câu.
/// </summary>
public delegate Task<IReadOnlyList<string>> SegmentTranslator(
    IReadOnlyList<string> segments,
    CancellationToken cancellationToken);

/// <summary>Bộ dịch cho một định dạng file (txt, html, docx, pptx...).</summary>
public interface IDocumentTranslator
{
    /// <summary>Các phần mở rộng hỗ trợ, có dấu chấm, chữ thường (".docx").</summary>
    IReadOnlyCollection<string> Extensions { get; }

    Task TranslateAsync(
        Stream input,
        Stream output,
        SegmentTranslator translate,
        CancellationToken cancellationToken = default);
}

/// <summary>Dịch chuỗi HTML (dùng cho format=html của /translate).</summary>
public interface IHtmlTranslator
{
    Task<string> TranslateAsync(string html, SegmentTranslator translate, CancellationToken cancellationToken = default);
}
