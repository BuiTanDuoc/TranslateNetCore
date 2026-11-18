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

/// <summary>Tải, kiểm tra và cài model ONNX vào thư mục models (chạy nền, từng model một).</summary>
public interface IModelInstaller
{
    /// <summary>Bắt đầu tải. Ném ValidationException nếu tham số sai hoặc đang có lượt tải khác.</summary>
    ModelDownloadStatus Start(ModelDownloadRequest request);

    /// <summary>Các lượt tải gần đây (mới nhất trước).</summary>
    IReadOnlyList<ModelDownloadStatus> List();

    ModelDownloadStatus? Get(string id);

    bool Cancel(string id);

    /// <summary>Xoá thư mục model đã cài (theo tên thư mục) và quét lại.</summary>
    void Delete(string folderName);
}
