using DocOrganizer.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace DocOrganizer.Api.Infrastructure;

/// <summary>
/// Трансформатор документа OpenAPI: загальні відомості про API та схеми безпеки
/// (OAuth 2.0 Authorization Code + PKCE через постачальника OIDC і ручний Bearer-токен).
/// </summary>
public sealed class ApiDocumentTransformer(IOptions<OidcOptions> oidc) : IOpenApiDocumentTransformer
{
    public const string OAuthScheme = "OAuth2";
    public const string BearerScheme = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        var o = oidc.Value;

        document.Info = new OpenApiInfo
        {
            Title = "Documents Organizer API",
            Version = "v1",
            Description = """
                Web API «Організатор документів зі строком дії».
                Власники документів, документи, нагадування про завершення строку дії, довідники типів і міток.

                **Автентифікація:** OpenID Connect (Auth0), маркер доступу JWT, потік Authorization Code + PKCE.
                **Ролі:** `admin` — керує довідниками й бачить усе; `member` — веде власні документи; `viewer` — лише перегляд.
                """,
            Contact = new OpenApiContact { Name = "Назар Городецький, ПЗ-36", Url = new Uri("https://lpnu.ua") },
            License = new OpenApiLicense { Name = "MIT" }
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[OAuthScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = "Вхід через постачальника OpenID Connect (Authorization Code + PKCE).",
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri(o.AuthorizationUrl),
                    TokenUrl = new Uri(o.TokenUrl),
                    Scopes = o.EffectiveScopes.ToDictionary(s => s, s => $"Scope '{s}'")
                }
            }
        };

        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Готовий маркер доступу (наприклад, скопійований із Postman)."
        };

        return Task.CompletedTask;
    }
}

/// <summary>
/// Трансформатор операцій: для дій з [Authorize] додає вимогу безпеки (замок у Scalar),
/// щоб інтерфейс документації автоматично підставляв маркер доступу.
/// </summary>
public sealed class AuthorizeOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (!metadata.OfType<IAuthorizeData>().Any() || metadata.OfType<IAllowAnonymous>().Any())
            return Task.CompletedTask;

        operation.Security ??= [];
        // Дві окремі вимоги = «АБО»: або OAuth2, або готовий Bearer-токен.
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(ApiDocumentTransformer.OAuthScheme, context.Document)] = ["openid"]
        });
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(ApiDocumentTransformer.BearerScheme, context.Document)] = []
        });
        return Task.CompletedTask;
    }
}
