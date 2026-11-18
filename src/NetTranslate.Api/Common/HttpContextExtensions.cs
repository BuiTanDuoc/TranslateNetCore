using NetTranslate.Application.Services;

namespace NetTranslate.Api.Common;

public static class HttpContextExtensions
{
    /// <summary>Header Web UI gắn vào mọi request (xem web/src/api/client.ts).</summary>
    public const string WebUiHeader = "X-NetTranslate-Client";

    public static string ClientIp(this HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Request đến từ Web UI đi kèm server: có header của UI và Origin (nếu có) trùng host.
    /// Lưu ý: header có thể bị giả mạo — chỉ nên dùng để miễn API key cho UI công khai,
    /// rate limit theo IP vẫn được áp dụng.
    /// </summary>
    public static bool IsWebUi(this HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey(WebUiHeader)) return false;

        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return true;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    public static AccessContext ToAccessContext(this HttpContext context, string? apiKey) =>
        new(apiKey, context.ClientIp(), context.IsWebUi());

    public static string BaseUrl(this HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}";
}
