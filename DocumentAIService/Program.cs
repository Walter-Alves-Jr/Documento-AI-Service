using System.Threading.RateLimiting;
using DocumentAIService.Configuration;
using DocumentAIService.Security;
using DocumentAIService.Services;
using DocumentAIService.Services.V1;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DvsOptions>(builder.Configuration.GetSection(DvsOptions.SectionName));
var dvsOptions = builder.Configuration.GetSection(DvsOptions.SectionName).Get<DvsOptions>() ?? new DvsOptions();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = Math.Max(dvsOptions.Files.MaxBytes + (4 * 1024 * 1024), 15 * 1024 * 1024));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

// Serviços legados preservados
builder.Services.AddScoped<IOcrService, OcrService>();
builder.Services.AddScoped<IDocumentAnalysisService, DocumentAnalysisService>();
builder.Services.AddScoped<IPdfConverterService, PdfConverterService>();
builder.Services.AddSingleton<IDirecaoDefensivaConfigService, DirecaoDefensivaConfigService>();

// Plataforma v1: catálogo, extração, regras, minimização e auditoria de metadados.
builder.Services.AddSingleton<IValidationCatalogService, ValidationCatalogService>();
builder.Services.AddScoped<IExtractionAdapter, LegacyExtractionAdapter>();
builder.Services.AddSingleton<IRulesEngine, RulesEngine>();
builder.Services.AddSingleton<IResponseDataMinimizer, ResponseDataMinimizer>();
builder.Services.AddSingleton<IValidationAuditStore, InMemoryValidationAuditStore>();
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
builder.Services.AddScoped<IDocumentValidationV1Service, DocumentValidationV1Service>();
builder.Services.AddSingleton<IFileInspector, FileInspector>();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Validator", policy => policy
        .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .RequireRole("validator", "admin"));
    options.AddPolicy("Admin", policy => policy
        .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .RequireRole("admin"));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Restricted", policy =>
    {
        var origins = dvsOptions.Cors.AllowedOrigins.Where(x => Uri.TryCreate(x, UriKind.Absolute, out _)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (origins.Length > 0)
            policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
        else
            policy.SetIsOriginAllowed(_ => false).AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("validation", context =>
    {
        var partitionKey = context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, dvsOptions.RateLimit.PermitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, dvsOptions.RateLimit.WindowSeconds)),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SafeExceptionMiddleware>();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("Restricted");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.MapFallbackToFile("index.html");

app.Run();
