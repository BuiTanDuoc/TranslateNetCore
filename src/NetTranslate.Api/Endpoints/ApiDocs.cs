namespace NetTranslate.Api.Endpoints;

// Các kiểu chỉ dùng để mô tả request/response trong OpenAPI (/openapi/v1.json, /scalar).
// Endpoint thực tế đọc tham số linh hoạt qua RequestValues (JSON, form hoặc query) như LibreTranslate.

/// <param name="Q">Văn bản cần dịch (chuỗi hoặc mảng chuỗi).</param>
/// <param name="Source">Mã ngôn ngữ nguồn hoặc "auto".</param>
/// <param name="Target">Mã ngôn ngữ đích.</param>
/// <param name="Format">"text" (mặc định) hoặc "html".</param>
/// <param name="Alternatives">Số bản dịch thay thế (hiện luôn trả về rỗng).</param>
/// <param name="Api_key">API key (tuỳ chọn).</param>
public sealed record TranslateRequestDoc(string Q, string Source, string Target, string? Format, int? Alternatives, string? Api_key);

public sealed record DetectedLanguageDoc(double Confidence, string Language);

public sealed record TranslateResponseDoc(string TranslatedText, DetectedLanguageDoc? DetectedLanguage, string[]? Alternatives);

public sealed record DetectRequestDoc(string Q, string? Api_key);

public sealed record LanguageDoc(string Code, string Name, string[] Targets);

public sealed record TranslateFileResponseDoc(string TranslatedFileUrl);

public sealed record ErrorDoc(string Error);
