using System.Text.Json.Serialization;
using Asp.Versioning;
using FluentValidation;
using DocOrganizer.Api.Auth;
using DocOrganizer.Api.Data;
using DocOrganizer.Api.Infrastructure;
using DocOrganizer.Api.Mapping;
using DocOrganizer.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════ Налаштування (Options pattern) ═══════════════════════════
builder.Services.Configure<OidcOptions>(builder.Configuration.GetSection(OidcOptions.Section));
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.Section));
builder.Services.Configure<PerformanceOptions>(builder.Configuration.GetSection(PerformanceOptions.Section));
var oidc = builder.Configuration.GetSection(OidcOptions.Section).Get<OidcOptions>() ?? new OidcOptions();

// Fail fast: без налаштувань Auth0 API не зможе перевіряти токени — краще одразу зрозуміле повідомлення.
if (string.IsNullOrWhiteSpace(oidc.Authority) || string.IsNullOrWhiteSpace(oidc.Audience))
    throw new InvalidOperationException(
        "Не налаштовано Auth0: задайте Oidc:Authority (https://<домен-тенанта>/) і Oidc:Audience в appsettings.json.");
var seed = builder.Configuration.GetSection(SeedOptions.Section).Get<SeedOptions>() ?? new SeedOptions();

// ═══════════════════════════ Дані ═══════════════════════════
// AddDbContext реєструє AppDbContext як Scoped — один контекст на HTTP-запит.
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(builder.Configuration.GetConnectionString("DocumentsDb")
                  ?? throw new InvalidOperationException("Не задано ConnectionStrings:DocumentsDb."))
    .UseSeeding((ctx, _) => SeedData.Seed(ctx, seed))
    .UseAsyncSeeding((ctx, _, ct) => SeedData.SeedAsync(ctx, seed, ct)));

// ═══════════════════════════ Сервіси застосунку (DI) ═══════════════════════════
builder.Services.AddSingleton(TimeProvider.System);          // системний годинник, без стану
builder.Services.AddSingleton<ApiMapper>();                  // згенерований Mapperly код, без стану
builder.Services.AddHttpContextAccessor();                   // Singleton (AsyncLocal усередині)
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>(); // користувач — свій для кожного запиту

// Бізнес-сервіси залежать від DbContext (Scoped) — тому теж Scoped.
builder.Services.AddScoped<IOwnerService, OwnerService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IReminderService, ReminderService>();
builder.Services.AddScoped<IDocumentTypeService, DocumentTypeService>();
builder.Services.AddScoped<ITagService, TagService>();

// Keyed services: реалізацію експортера обирає контролер за форматом із маршруту.
builder.Services.AddKeyedSingleton<IDocumentExporter, CsvDocumentExporter>("csv");
builder.Services.AddKeyedSingleton<IDocumentExporter, JsonDocumentExporter>("json");

// Валідатори FluentValidation — без стану, Singleton.
builder.Services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton);

// ═══════════════════════════ MVC, JSON, версіонування ═══════════════════════════
builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<FluentValidationFilter>();
        // Помилки — application/problem+json. Реєструємо ЕКЗЕМПЛЯР: тоді враховується його Order = int.MaxValue
        // і фільтр виконується після [Produces] контролера (Add<T>() обгорнув би його з Order = 0).
        options.Filters.Add(new ProblemDetailsContentTypeFilter());
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());   // enum — рядками ("Active")
        o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;     // число лише як число
    });
// Ті самі налаштування JSON для генератора OpenAPI (схеми enum і чисел у документі).
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.ApiVersionReader = new UrlSegmentApiVersionReader();   // версія — сегмент URI: /api/v1/...
        options.ReportApiVersions = true;                 // заголовок api-supported-versions
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";               // група "v1" → документ /openapi/v1.json
        options.SubstituteApiVersionInUrl = true;         // {version} → 1 у шляхах документа
    });

// ═══════════════════════════ Помилки ═══════════════════════════
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    });
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ═══════════════════════════ Автентифікація та авторизація ═══════════════════════════
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // API не виконує вхід сам: лише перевіряє JWT, отриманий клієнтом (Postman, Scalar) від Auth0 за
        // потоком Authorization Code + PKCE. Ключі підпису — з {Authority}/.well-known/openid-configuration.
        // Перевіряється: підпис (RS256), iss = Authority, aud = Audience, строк дії (exp).
        options.Authority = oidc.Authority;
        options.Audience = oidc.Audience;
        options.MapInboundClaims = false;                 // claim "sub" лишається "sub" (не перейменовується)
        options.TokenValidationParameters.NameClaimType = oidc.NameClaimType;
        options.TokenValidationParameters.RoleClaimType = oidc.RoleClaimType;
        options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CanRead, p => p.RequireAuthenticatedUser().RequireRole(Roles.Viewer, Roles.Member, Roles.Admin))
    .AddPolicy(Policies.CanWrite, p => p.RequireAuthenticatedUser().RequireRole(Roles.Member, Roles.Admin))
    .AddPolicy(Policies.AdminOnly, p => p.RequireAuthenticatedUser().RequireRole(Roles.Admin))
    .AddPolicy(Policies.OwnerOrAdmin, p => p.AddRequirements(new OwnerOrAdminRequirement()));
builder.Services.AddSingleton<IAuthorizationHandler, OwnerOrAdminHandler>();   // без стану

// CORS — щоб Scalar/SPA з іншого origin могли звертатися до API.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));

// ═══════════════════════════ Продуктивність ═══════════════════════════
builder.Services.AddOutputCache(options =>
    options.AddPolicy(CachePolicies.PerUser, b => b
        .AddPolicy<PerUserOutputCachePolicy>()
        .Expire(TimeSpan.FromSeconds(30))));
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});

// ═══════════════════════════ OpenAPI ═══════════════════════════
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer<ApiDocumentTransformer>();
    options.AddOperationTransformer<AuthorizeOperationTransformer>();
});

var app = builder.Build();

// ═══════════════════════════ Міграції та тестові дані ═══════════════════════════
if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();   // застосовує міграції та викликає UseAsyncSeeding
}

// ═══════════════════════════ Конвеєр обробки запитів ═══════════════════════════
app.UseExceptionHandler();      // першим — перехоплює винятки з усіх наступних компонентів
app.UseStatusCodePages();       // порожні 401/403/404/405 → тіло ProblemDetails

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();           // /openapi/v1.json
    app.MapScalarApiReference(options =>   // /scalar
    {
        var o = app.Services.GetRequiredService<IOptions<OidcOptions>>().Value;
        options
            .WithTitle("Documents Organizer API")
            .AddPreferredSecuritySchemes(ApiDocumentTransformer.OAuthScheme)
            .AddAuthorizationCodeFlow(ApiDocumentTransformer.OAuthScheme, flow =>
            {
                flow.ClientId = o.ClientId;
                flow.Pkce = Pkce.Sha256;
                // Публічний клієнт без секрету: client_id — у тілі запиту токена, а не в Basic-заголовку.
                flow.CredentialsLocation = CredentialsLocation.Body;
                flow.SelectedScopes = o.EffectiveScopes;
                // Без audience Auth0 видає непрозорий (не JWT) токен, який API не прийме.
                flow.AddQueryParameter("audience", o.Audience);
            });
    });
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();           // після авторизації — у кеш потрапляють лише дозволені відповіді
app.MapControllers();

app.Run();
