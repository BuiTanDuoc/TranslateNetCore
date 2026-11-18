using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NetTranslate.Application.Abstractions;
using NetTranslate.Domain.Entities;

namespace NetTranslate.Infrastructure.Persistence;

/// <summary>
/// Repository API key. Kết quả tra cứu theo key được cache 30 giây
/// để mỗi request dịch không phải truy vấn SQLite.
/// </summary>
public sealed class ApiKeyRepository(AppDbContext db, IMemoryCache cache) : IApiKeyRepository
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static string CacheKey(string key) => $"apikey:{key}";

    public async Task<ApiKey?> FindByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey(key), out ApiKey? cached)) return cached;

        var entity = await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Key == key, cancellationToken);
        cache.Set(CacheKey(key), entity, CacheDuration);
        return entity;
    }

    public async Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(cancellationToken);

    public Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

    public async Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        db.ApiKeys.Add(apiKey);
        await db.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey(apiKey.Key));
    }

    public async Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        db.ApiKeys.Update(apiKey);
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(apiKey).State = EntityState.Detached;
        cache.Remove(CacheKey(apiKey.Key));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
        if (entity is null) return false;
        db.ApiKeys.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey(entity.Key));
        return true;
    }
}
