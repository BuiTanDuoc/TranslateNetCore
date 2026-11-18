using Microsoft.Extensions.Options;
using NetTranslate.Application.Abstractions;
using NetTranslate.Application.Options;
using NetTranslate.Domain.Entities;
using NetTranslate.Domain.Exceptions;

namespace NetTranslate.Application.Services;

/// <param name="ApiKey">Giá trị api_key client gửi lên (có thể null).</param>
/// <param name="ClientIp">IP client, dùng làm bucket rate limit khi không có key.</param>
/// <param name="IsWebUi">Request đến từ Web UI cùng origin.</param>
public sealed record AccessContext(string? ApiKey, string ClientIp, bool IsWebUi);

/// <summary>
/// Kiểm tra API key, giới hạn request/phút, giới hạn ký tự và batch —
/// tương đương decorator access_check của LibreTranslate.
/// </summary>
public sealed class AccessGuard(
    IApiKeyRepository apiKeys,
    IRateLimiter rateLimiter,
    IOptions<TranslateOptions> options)
{
    private readonly TranslateOptions _options = options.Value;

    public async Task<ApiKey?> CheckAsync(
        AccessContext context,
        int charCount,
        int batchCount = 1,
        CancellationToken cancellationToken = default)
    {
        ApiKey? key = null;
        var charLimit = _options.CharLimit;

        if (_options.ApiKeysEnabled && !string.IsNullOrWhiteSpace(context.ApiKey))
        {
            key = await apiKeys.FindByKeyAsync(context.ApiKey.Trim(), cancellationToken);
            if (key is null || !key.IsActive)
                throw new ForbiddenException("Invalid API key");

            if (key.ReqLimit > 0 && !rateLimiter.TryAcquire($"key:{key.Id}", key.ReqLimit))
                throw new RateLimitExceededException($"Too many requests: limit is {key.ReqLimit} per 1 minute");

            if (key.CharLimit is { } kl) charLimit = kl;

            // Chỉ ghi LastUsedAt tối đa mỗi phút một lần để tránh ghi DB liên tục
            if (key.LastUsedAt is null || DateTime.UtcNow - key.LastUsedAt > TimeSpan.FromMinutes(1))
            {
                key.LastUsedAt = DateTime.UtcNow;
                await apiKeys.UpdateAsync(key, cancellationToken);
            }
        }
        else
        {
            var exemptWebUi = context.IsWebUi && _options.AllowWebUiWithoutKey;
            if (_options.ApiKeysEnabled && _options.RequireApiKey && !exemptWebUi)
                throw new ForbiddenException("An API key is required. Please contact the server operator to get one.");

            if (_options.ReqLimit > 0 && !rateLimiter.TryAcquire($"ip:{context.ClientIp}", _options.ReqLimit))
                throw new RateLimitExceededException($"Too many requests: limit is {_options.ReqLimit} per 1 minute");
        }

        if (charLimit >= 0 && charCount > charLimit)
            throw new ValidationException($"Invalid request: request ({charCount}) exceeds text limit ({charLimit})");

        if (_options.BatchLimit >= 0 && batchCount > _options.BatchLimit)
            throw new ValidationException($"Invalid request: request ({batchCount}) exceeds batch limit ({_options.BatchLimit})");

        return key;
    }
}
