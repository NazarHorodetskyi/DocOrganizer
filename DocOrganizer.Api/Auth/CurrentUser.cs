using System.Security.Claims;

namespace DocOrganizer.Api.Auth;

/// <summary>Абстракція над автентифікованим користувачем поточного запиту.</summary>
public interface ICurrentUser
{
    /// <summary>Ідентифікатор користувача з claim sub.</summary>
    string AccountId { get; }

    bool IsAdmin { get; }

    ClaimsPrincipal Principal { get; }
}

/// <summary>
/// Реалізація через IHttpContextAccessor. Scoped: користувач свій для кожного HTTP-запиту.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? throw new InvalidOperationException("Немає HttpContext.");

    public string AccountId =>
        Principal.FindFirstValue(AuthConstants.SubjectClaim)
        ?? throw new InvalidOperationException("У маркері доступу немає claim 'sub'.");

    public bool IsAdmin => Principal.IsInRole(Roles.Admin);
}
