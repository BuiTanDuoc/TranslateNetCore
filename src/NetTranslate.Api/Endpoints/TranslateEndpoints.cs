using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using NetTranslate.Api.Common;
using NetTranslate.Application.Abstractions;
using NetTranslate.Application.Options;
using NetTranslate.Application.Services;
using NetTranslate.Domain.Exceptions;
using NetTranslate.Domain.Languages;

namespace NetTranslate.Api.Endpoints;

/// <summary>Các endpoint tương thích LibreTranslate API.</summary>
public static class TranslateEndpoints
{
    public static IEndpointRouteBuilder MapTranslateEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("").WithTags("Translate");

        api.MapGet("/languages", GetLanguages)
            .WithSummary("Danh sách ngôn ngữ được hỗ trợ")
            .Produces<LanguageDoc[]>();

        api.MapPost("/translate", Translate)
            .WithSummary("Dịch văn bản")
            .Accepts<TranslateRequestDoc>("application/json", "application/x-www-form-urlencoded")
            .Produces<TranslateResponseDoc>()
            .Produces<ErrorDoc>(400).Produces<ErrorDoc>(403).Produces<ErrorDoc>(429).Produces<ErrorDoc>(500)
            .DisableAntiforgery();

        api.MapPost("/detect", Detect)
            .WithSummary("Nhận diện ngôn ngữ")
            .Accepts<DetectRequestDoc>("application/json", "application/x-www-form-urlencoded")
            .Produces<DetectedLanguageDoc[]>()
            .Produces<ErrorDoc>(400)
            .DisableAntiforgery();

        api.MapPost("/translate_file", TranslateFile)
            .WithSummary("Dịch file (.txt, .md, .html, .docx, .pptx)")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<TranslateFileResponseDoc>()
            .Produces<ErrorDoc>(400).Produces<ErrorDoc>(403)
            .DisableAntiforgery();

        api.MapGet("/download_file/{id}", DownloadFile)
            .WithSummary("Tải file đã dịch");

        api.MapGet("/frontend/settings", FrontendSettings)
            .WithSummary("Cấu hình cho Web UI");

        api.MapPost("/suggest", () => Results.Json(new { error = "Suggestions are disabled on this server." }, statusCode: 403))
            .WithSummary("Đề xuất bản dịch (chưa hỗ trợ)")
            .DisableAntiforgery();

        api.MapGet("/health", (ITranslationEngine engine) =>
            Results.Ok(new { status = "ok", models = engine.GetAvailablePairs().Count }))
            .WithSummary("Kiểm tra tình trạng server");

        return app;
    }

    private static IResult GetLanguages(TranslationService translation) =>
        Results.Ok(translation.GetLanguages().Select(l => new { code = l.Code, name = l.Name, targets = l.Targets }));

    private static async Task<IResult> Translate(
        HttpContext context,
        TranslationService translation,
        AccessGuard guard,
        CancellationToken ct)
    {
        var v = await RequestValues.ReadAsync(context.Request, ct);

        var q = v.GetList("q");
        if (q.Count == 0 || (!v.IsArray("q") && string.IsNullOrEmpty(q[0])))
            throw new ValidationException("Invalid request: missing q parameter");

        var source = v.Get("source") ?? TranslationService.AutoSource;
        var target = v.Get("target") ?? throw new ValidationException("Invalid request: missing target parameter");
        var format = v.Get("format") ?? "text";
        if (format is not ("text" or "html"))
            throw new ValidationException("Invalid request: format must be 'text' or 'html'");
        var alternatives = Math.Max(0, v.GetInt("alternatives"));

        await guard.CheckAsync(context.ToAccessContext(v.Get("api_key")), q.Sum(s => s.Length), q.Count, ct);

        var results = await translation.TranslateAsync(q, source, target, format, ct);
        var isAuto = source.Equals(TranslationService.AutoSource, StringComparison.OrdinalIgnoreCase);

        static object? Detected(TranslateResult r) =>
            r.DetectedLanguage is { } d ? new { confidence = d.Confidence, language = d.Language } : null;

        if (v.IsArray("q"))
        {
            var body = new Dictionary<string, object?> { ["translatedText"] = results.Select(r => r.TranslatedText).ToList() };
            if (isAuto) body["detectedLanguage"] = results.Select(Detected).ToList();
            if (alternatives > 0) body["alternatives"] = results.Select(_ => Array.Empty<string>()).ToList();
            return Results.Json(body);
        }
        else
        {
            var r = results[0];
            var body = new Dictionary<string, object?> { ["translatedText"] = r.TranslatedText };
            if (isAuto) body["detectedLanguage"] = Detected(r);
            if (alternatives > 0) body["alternatives"] = Array.Empty<string>();
            return Results.Json(body);
        }
    }

    private static async Task<IResult> Detect(
        HttpContext context,
        TranslationService translation,
        AccessGuard guard,
        CancellationToken ct)
    {
        var v = await RequestValues.ReadAsync(context.Request, ct);
        var q = v.Get("q");
        if (string.IsNullOrWhiteSpace(q))
            throw new ValidationException("Invalid request: missing q parameter");

        await guard.CheckAsync(context.ToAccessContext(v.Get("api_key")), q.Length, 1, ct);

        var results = translation.Detect(q);
        return Results.Ok(results.Select(r => new { confidence = r.Confidence, language = r.Language }));
    }

    private static async Task<IResult> TranslateFile(
        HttpContext context,
        FileTranslationService files,
        AccessGuard guard,
        CancellationToken ct)
    {
        if (!files.Enabled) throw new ForbiddenException("Files translation are disabled on this server.");
        if (!context.Request.HasFormContentType)
            throw new ValidationException("Invalid request: expected multipart/form-data");

        var v = await RequestValues.ReadAsync(context.Request, ct);
        var file = v.Files?.GetFile("file") ?? throw new ValidationException("Invalid request: missing file parameter");
        var source = v.Get("source") ?? TranslationService.AutoSource;
        var target = v.Get("target") ?? throw new ValidationException("Invalid request: missing target parameter");

        await guard.CheckAsync(context.ToAccessContext(v.Get("api_key")), 0, 1, ct);

        await using var input = file.OpenReadStream();
        var stored = await files.TranslateAsync(input, file.FileName, file.Length, source, target, ct);

        return Results.Ok(new { translatedFileUrl = $"{context.Request.BaseUrl()}/download_file/{stored.Id}" });
    }

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private static IResult DownloadFile(string id, IFileStorage storage)
    {
        var file = storage.Find(id) ?? throw new NotFoundException("File not found or expired");
        if (!ContentTypes.TryGetContentType(file.FileName, out var contentType))
            contentType = "application/octet-stream";
        return Results.File(file.FullPath, contentType, file.FileName);
    }

    private static IResult FrontendSettings(
        IOptions<TranslateOptions> options,
        FileTranslationService files)
    {
        var o = options.Value;
        string Name(string code) => code == TranslationService.AutoSource ? "Auto Detect" : LanguageCatalog.GetName(code);

        return Results.Ok(new
        {
            charLimit = o.CharLimit,
            frequencyLimit = o.ReqLimit > 0 ? o.ReqLimit : -1,
            batchLimit = o.BatchLimit,
            apiKeys = o.ApiKeysEnabled,
            keyRequired = o.ApiKeysEnabled && o.RequireApiKey && !o.AllowWebUiWithoutKey,
            suggestions = false,
            filesTranslation = files.Enabled,
            fileSizeLimitMb = o.FileSizeLimitMb,
            supportedFilesFormat = files.SupportedFormats,
            language = new
            {
                source = new { code = o.DefaultSource, name = Name(o.DefaultSource) },
                target = new { code = o.DefaultTarget, name = Name(o.DefaultTarget) },
            },
        });
    }
}
