namespace NetTranslate.Application.Abstractions;

/// <summary>Một cặp ngôn ngữ mà engine dịch trực tiếp được (ví dụ en → vi).</summary>
public sealed record LanguagePair(string Source, string Target);

/// <summary>Thông tin một model đã cài (cho trang quản trị).</summary>
public sealed record ModelInfo(string Source, string Target, string Name, bool Loaded, string? Error);

/// <summary>
/// Engine dịch máy cấp thấp: nhận danh sách câu đã tách sẵn, trả về câu đã dịch theo đúng thứ tự.
/// Không biết gì về pivot, HTML hay auto-detect — các việc đó do TranslationService đảm nhận.
/// </summary>
public interface ITranslationEngine
{
    IReadOnlyList<LanguagePair> GetAvailablePairs();

    bool CanTranslate(string source, string target);

    Task<IReadOnlyList<string>> TranslateAsync(
        string source,
        string target,
        IReadOnlyList<string> sentences,
        CancellationToken cancellationToken = default);

    IReadOnlyList<ModelInfo> GetModels();

    /// <summary>Quét lại thư mục model (khi admin thêm model mới).</summary>
    void Reload();
}
