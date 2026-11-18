namespace NetTranslate.Application.Abstractions;

public interface IRateLimiter
{
    /// <summary>
    /// Ghi nhận một request vào "bucket" (IP hoặc API key).
    /// Trả về false nếu bucket đã vượt <paramref name="limitPerMinute"/> trong 60 giây gần nhất.
    /// </summary>
    bool TryAcquire(string bucket, int limitPerMinute);
}
