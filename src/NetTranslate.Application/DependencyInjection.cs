using Microsoft.Extensions.DependencyInjection;
using NetTranslate.Application.Services;

namespace NetTranslate.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<TranslationService>();
        services.AddScoped<AccessGuard>();
        services.AddScoped<ApiKeyService>();
        services.AddSingleton<FileTranslationService>();
        return services;
    }
}
