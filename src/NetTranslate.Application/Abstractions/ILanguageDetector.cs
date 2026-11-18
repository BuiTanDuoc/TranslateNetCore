namespace NetTranslate.Application.Abstractions;

/// <param name="Language">Mã ISO 639-1.</param>
/// <param name="Confidence">Độ tin cậy 0–100 (cùng thang với LibreTranslate).</param>
public sealed record DetectionResult(string Language, double Confidence);

public interface ILanguageDetector
{
    /// <summary>
    /// Nhận diện ngôn ngữ, chỉ trả về các ngôn ngữ nằm trong <paramref name="candidates"/>,
    /// sắp xếp theo độ tin cậy giảm dần.
    /// </summary>
    IReadOnlyList<DetectionResult> Detect(string text, IReadOnlyCollection<string> candidates);
}
