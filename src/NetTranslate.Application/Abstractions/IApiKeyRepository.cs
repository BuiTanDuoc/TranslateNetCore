using NetTranslate.Domain.Entities;

namespace NetTranslate.Application.Abstractions;

public interface IApiKeyRepository
{
    Task<ApiKey?> FindByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken cancellationToken = default);
    Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
}
