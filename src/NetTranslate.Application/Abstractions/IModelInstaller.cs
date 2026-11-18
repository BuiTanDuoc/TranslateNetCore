namespace NetTranslate.Application.Abstractions;

/// <summary>Yêu cầu tải một model từ Hugging Face (trang quản trị).</summary>
/// <param name="Pair">Cặp ngôn ngữ, cũng là tên thư mục, vd. "ja-en".</param>
/// <param name="Repo">Repo Hugging Face; để trống = "Xenova/opus-mt-{Pair}".</param>
/// <param name="Quantized">Ưu tiên file *_quantized.onnx (nhỏ hơn ~4 lần).</param>
/// <param name="Overwrite">Thay thế model đã có cùng tên thư mục.</param>
/// <param name="TargetPrefix">Token chọn ngôn ngữ đích cho model đa ngôn ngữ, vd. "&gt;&gt;vie&lt;&lt;".</param>
public sealed record ModelDownloadRequest(
    string Pair,
    string? Repo = null,
    bool Quantized = false,
    bool Overwrite = false,
    string? TargetPrefix = null);

public sealed record ModelDownloadFileStatus(string Name, long Size, long Downloaded, string State);

/// <summary>
/// Trạng thái một lượt tải. State: queued, listing, downloading, validating, installing, done, failed, cancelled.
/// </summary>
public sealed record ModelDownloadStatus(
    string Id,
    string Pair,
    string Repo,
    bool Quantized,
    string State,
    string? Message,
    string? Error,
    long TotalBytes,
    long DownloadedBytes,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<ModelDownloadFileStatus> Files);

/// <summary>Một repo Hugging Face tải được cho một cặp ngôn ngữ (đã kiểm tra đủ file ONNX).</summary>
/// <param name="FullBytes">Tổng dung lượng bản đầy đủ (fp32); null nếu repo chỉ có bản quantized.</param>
/// <param name="QuantizedBytes">Tổng dung lượng bản int8; null nếu repo không có bản quantized.</param>
/// <param name="Downloads">Lượt tải trên HF (30 ngày); null nếu không rõ.</param>
/// <param name="Recommended">Đã được thử và khuyên dùng.</param>
/// <param name="Note">Ghi chú ngắn (vd. tên model gốc).</param>
/// <param name="Warning">Cảnh báo chất lượng, nếu có.</param>
/// <param name="TargetPrefix">Token ngôn ngữ đích cần điền sẵn (model đa ngôn ngữ đích).</param>
public sealed record ModelRepoCandidate(
    string Repo,
    long? FullBytes,
    long? QuantizedBytes,
    long? Downloads,
    bool Recommended,
    string? Note,
    string? Warning,
    string? TargetPrefix);

/// <summary>Tải, kiểm tra và cài model ONNX vào thư mục models (chạy nền, từng model một).</summary>
public interface IModelInstaller
{
    /// <summary>Tìm các repo trên Hugging Face dùng được cho cặp ngôn ngữ (đúng chiều, Marian, đủ file).</summary>
    Task<IReadOnlyList<ModelRepoCandidate>> SearchReposAsync(string pair, CancellationToken cancellationToken = default);

    /// <summary>Bắt đầu tải. Ném ValidationException nếu tham số sai hoặc đang có lượt tải khác.</summary>
    ModelDownloadStatus Start(ModelDownloadRequest request);

    /// <summary>Các lượt tải gần đây (mới nhất trước).</summary>
    IReadOnlyList<ModelDownloadStatus> List();

    ModelDownloadStatus? Get(string id);

    bool Cancel(string id);

    /// <summary>Xoá thư mục model đã cài (theo tên thư mục) và quét lại.</summary>
    void Delete(string folderName);
}
