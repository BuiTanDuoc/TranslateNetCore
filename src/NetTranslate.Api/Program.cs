using Microsoft.AspNetCore.HttpOverrides;
using NetTranslate.Api.Common;
using NetTranslate.Api.Endpoints;
using NetTranslate.Application;
using NetTranslate.Application.Options;
using NetTranslate.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------ Services
builder.Services.Configure<TranslateOptions>(builder.Configuration.GetSection(TranslateOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));

var fileLimitMb = builder.Configuration.GetValue("Translate:FileSizeLimitMb", 10);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = (fileLimitMb + 1) * 1024L * 1024L);

var trustProxy = builder.Configuration.GetValue("TrustForwardedHeaders", false);
if (trustProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
#pragma warning disable ASPDEPR005 // KnownNetworks → KnownIPNetworks trong .NET 10
        o.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
        o.KnownProxies.Clear();
    });
}

var app = builder.Build();

// ------------------------------------------------------------------ Pipeline
if (trustProxy) app.UseForwardedHeaders();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();

// Web UI (React build trong wwwroot)
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi();                 // /openapi/v1.json
app.MapScalarApiReference();      // /scalar/v1 — tài liệu API tương tác

app.MapTranslateEndpoints();
app.MapAdminEndpoints();

// SPA fallback: chỉ áp dụng cho route không phải API
app.MapFallbackToFile("index.html");

await app.Services.InitializeInfrastructureAsync();

app.Run();
