using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NetTranslate.Application.Abstractions;
using NetTranslate.Application.Services;
using NetTranslate.Domain.Entities;

namespace NetTranslate.Api.Endpoints;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>Token quản trị, gửi qua header X-Admin-Token. Để trống = tắt Admin API.</summary>
    public string Token { get; set; } = string.Empty;
}

/// <summary>Quản lý API key và model (thay cho lệnh ltmanage của LibreTranslate).</summary>
public static class AdminEndpoints
{
    public const string TokenHeader = "X-Admin-Token";

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin")
            .WithTags("Admin")
            .AddEndpointFilter(RequireAdminToken)
            .DisableAntiforgery();

        admin.MapGet("/keys", async (ApiKeyService keys, CancellationToken ct) =>
            Results.Ok((await keys.ListAsync(ct)).Select(ToDto)));

        admin.MapPost("/keys", async (CreateApiKeyRequest request, ApiKeyService keys, CancellationToken ct) =>
        {
            var key = await keys.CreateAsync(request, ct);
            return Results.Created($"/admin/keys/{key.Id}", ToDto(key));
        });

        admin.MapPatch("/keys/{id:guid}", async (Guid id, UpdateApiKeyRequest request, ApiKeyService keys, CancellationToken ct) =>
            Results.Ok(ToDto(await keys.UpdateAsync(id, request, ct))));

        admin.MapDelete("/keys/{id:guid}", async (Guid id, ApiKeyService keys, CancellationToken ct) =>
        {
            await keys.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        admin.MapGet("/models", (ITranslationEngine engine) => Results.Ok(engine.GetModels()));

        admin.MapPost("/models/reload", (ITranslationEngine engine) =>
        {
            engine.Reload();
            return Results.Ok(engine.GetModels());
        });

        return app;
    }

    private static object ToDto(ApiKey k) => new
    {
        id = k.Id,
        key = k.Key,
        name = k.Name,
        reqLimit = k.ReqLimit,
        charLimit = k.CharLimit,
        isActive = k.IsActive,
        createdAt = k.CreatedAt,
        lastUsedAt = k.LastUsedAt,
    };

    private static async ValueTask<object?> RequireAdminToken(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value;
        if (string.IsNullOrEmpty(options.Token))
            return Results.Json(new { error = "Admin API is disabled. Set Admin:Token in configuration." }, statusCode: 404);

        var provided = context.HttpContext.Request.Headers[TokenHeader].ToString();
        var ok = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(options.Token)));

        if (!ok) return Results.Json(new { error = "Invalid admin token" }, statusCode: 401);
        return await next(context);
    }
}
