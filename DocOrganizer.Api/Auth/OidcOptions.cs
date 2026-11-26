namespace DocOrganizer.Api.Auth;

/// <summary>
/// Налаштування зовнішнього постачальника OpenID Connect — Auth0 (секція "Oidc" в appsettings.json).
/// Значення береш у консолі Auth0 (див. README, розділ «Auth0 setup»).
/// </summary>
public sealed class OidcOptions
{
    public const string Section = "Oidc";

    /// <summary>
    /// Домен тенанта Auth0 з https:// і косою рискою в кінці, напр. "https://dev-abc123.eu.auth0.com/".
    /// З нього API читає /.well-known/openid-configuration і ключі підпису (JWKS).
    /// Коса риска в кінці важлива: саме так Auth0 записує issuer (claim iss) у токен.
    /// </summary>
    public string Authority { get; set; } = "";

    /// <summary>Identifier API в Auth0 (Applications → APIs). Потрапляє в claim aud токена.</summary>
    public string Audience { get; set; } = "https://doc-organizer-api";

    /// <summary>Client ID застосунку Auth0 типу Single Page Application (для Scalar і Postman).</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Claim з ролями. Auth0 сам ролі в токен не кладе — їх додає Post-Login Action з простором імен.</summary>
    public string RoleClaimType { get; set; } = "https://doc-organizer/roles";

    /// <summary>Claim з ім'ям користувача (email додає та сама Action).</summary>
    public string NameClaimType { get; set; } = "https://doc-organizer/email";

    /// <summary>Scopes, які запитує клієнт (задаються в appsettings).</summary>
    /// <remarks>
    /// Значення за замовчуванням навмисно порожнє: binder конфігурації ДОПИСУЄ елементи масиву
    /// до вже ініціалізованих, і "openid" з'явився б двічі.
    /// </remarks>
    public string[] Scopes { get; set; } = [];

    /// <summary>Scopes без дублікатів; якщо в конфігурації порожньо — openid profile email.</summary>
    public string[] EffectiveScopes => Scopes.Length > 0 ? Scopes.Distinct().ToArray() : ["openid", "profile", "email"];

    /// <summary>Кінцева точка авторизації Auth0 (Authorization Code + PKCE).</summary>
    public string AuthorizationUrl => Authority.TrimEnd('/') + "/authorize";

    /// <summary>Кінцева точка отримання токена Auth0.</summary>
    public string TokenUrl => Authority.TrimEnd('/') + "/oauth/token";
}
