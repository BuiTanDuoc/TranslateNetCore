using System.Security.Cryptography;
using NetTranslate.Application.Abstractions;
using NetTranslate.Domain.Entities;
using NetTranslate.Domain.Exceptions;

namespace NetTranslate.Application.Services;

public sealed record CreateApiKeyRequest(string? Name, int ReqLimit = 60, int? CharLimit = null);

public sealed record UpdateApiKeyRequest(string? Name, int? ReqLimit, int? CharLimit, bool? IsActive, bool ClearCharLimit = false);

/// <summary>Quản lý API key (tương đương lệnh ltmanage keys của LibreTranslate).</summary>
public sealed class ApiKeyService(IApiKeyRepository repository)
{
    public Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken ct = default) => repository.ListAsync(ct);

    public async Task<ApiKey> CreateAsync(CreateApiKeyRequest request, CancellationToken ct = default)
    {
        if (request.ReqLimit < 0) throw new ValidationException("reqLimit must be >= 0");
        if (request.CharLimit is < -1) throw new ValidationException("charLimit must be >= -1");

        var key = new ApiKey
        {
            Key = GenerateKey(),
            Name = string.IsNullOrWhiteSpace(request.Name) ? "Unnamed" : request.Name.Trim(),
            ReqLimit = request.ReqLimit,
            CharLimit = request.CharLimit,
        };
        await repository.AddAsync(key, ct);
        return key;
    }

    public async Task<ApiKey> UpdateAsync(Guid id, UpdateApiKeyRequest request, CancellationToken ct = default)
    {
        var key = await repository.GetAsync(id, ct) ?? throw new NotFoundException("API key not found");

        if (!string.IsNullOrWhiteSpace(request.Name)) key.Name = request.Name.Trim();
        if (request.ReqLimit is { } rl)
        {
            if (rl < 0) throw new ValidationException("reqLimit must be >= 0");
            key.ReqLimit = rl;
        }
        if (request.ClearCharLimit) key.CharLimit = null;
        else if (request.CharLimit is { } cl) key.CharLimit = cl;
        if (request.IsActive is { } active) key.IsActive = active;

        await repository.UpdateAsync(key, ct);
        return key;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (!await repository.DeleteAsync(id, ct))
            throw new NotFoundException("API key not found");
    }

    /// <summary>Key dạng UUID giống LibreTranslate, sinh bằng RNG mật mã.</summary>
    private static string GenerateKey() => new Guid(RandomNumberGenerator.GetBytes(16)).ToString();
}
