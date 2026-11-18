namespace NetTranslate.Domain.Exceptions;

/// <summary>Lỗi nghiệp vụ gốc. Tầng Api ánh xạ từng loại sang HTTP status.</summary>
public abstract class TranslateException(string message) : Exception(message);

/// <summary>Tham số không hợp lệ (HTTP 400).</summary>
public sealed class ValidationException(string message) : TranslateException(message);

/// <summary>Không có quyền / API key sai (HTTP 403).</summary>
public sealed class ForbiddenException(string message) : TranslateException(message);

/// <summary>Vượt giới hạn tần suất (HTTP 429).</summary>
public sealed class RateLimitExceededException(string message) : TranslateException(message);

/// <summary>Không tìm thấy tài nguyên (HTTP 404).</summary>
public sealed class NotFoundException(string message) : TranslateException(message);
