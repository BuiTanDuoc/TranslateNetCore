using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetTranslate.Application.Abstractions;
using NetTranslate.Infrastructure.Detection;
using NetTranslate.Infrastructure.Documents;
using NetTranslate.Infrastructure.Persistence;
using NetTranslate.Infrastructure.RateLimiting;
using NetTranslate.Infrastructure.Storage;
using NetTranslate.Infrastructure.Translation;

namespace NetTranslate.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ModelOptions>(configuration.GetSection(ModelOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        // Engine dịch & nhận diện ngôn ngữ
        services.AddSingleton<ITranslationEngine, OnnxMarianTranslationEngine>();
        services.AddSingleton<ILanguageDetector, HeuristicLanguageDetector>();

        // Tải model từ Hugging Face (trang quản trị)
        services.AddHttpClient(HuggingFaceModelInstaller.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("NetTranslate/1.0");
        });
        services.AddSingleton<IModelInstaller, HuggingFaceModelInstaller>();

        // Bộ dịch tài liệu
        services.AddSingleton<HtmlDocumentTranslator>();
        services.AddSingleton<IHtmlTranslator>(sp => sp.GetRequiredService<HtmlDocumentTranslator>());
        services.AddSingleton<IDocumentTranslator>(sp => sp.GetRequiredService<HtmlDocumentTranslator>());
        services.AddSingleton<IDocumentTranslator, TextDocumentTranslator>();
        services.AddSingleton<IDocumentTranslator, DocxDocumentTranslator>();
        services.AddSingleton<IDocumentTranslator, PptxDocumentTranslator>();

        // Lưu trữ
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=data/nettranslate.db";
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
        services.AddMemoryCache();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddSingleton<IRateLimiter, SlidingWindowRateLimiter>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddHostedService<FileCleanupService>();

        return services;
    }

    /// <summary>Tạo thư mục data và database SQLite nếu chưa có.</summary>
    public static async Task InitializeInfrastructureAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dataSource = db.Database.GetDbConnection().DataSource;
        if (!string.IsNullOrEmpty(dataSource))
        {
            // SQLite tính đường dẫn tương đối theo thư mục làm việc hiện tại
            var dir = Path.GetDirectoryName(Path.GetFullPath(dataSource));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        await db.Database.EnsureCreatedAsync();
    }
}
