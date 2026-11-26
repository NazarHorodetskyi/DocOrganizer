using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace DocOrganizer.Api.Auth;

public static class AuthConstants
{
    /// <summary>Claim з ідентифікатором користувача (MapInboundClaims = false, тож назва не перетворюється).</summary>
    public const string SubjectClaim = "sub";
}

/// <summary>Ролі застосунку (Roles в Auth0 → User Management → Roles).</summary>
public static class Roles
{
    /// <summary>Адміністратор: керує довідниками, бачить і змінює всі документи.</summary>
    public const string Admin = "admin";

    /// <summary>Учасник: веде власні профілі та документи (читання + запис).</summary>
    public const string Member = "member";

    /// <summary>Глядач: лише переглядає власні документи.</summary>
    public const string Viewer = "viewer";
}

/// <summary>Імена політик авторизації.</summary>
public static class Policies
{
    /// <summary>Читання: viewer, member, admin.</summary>
    public const string CanRead = nameof(CanRead);

    /// <summary>Запис: member, admin.</summary>
    public const string CanWrite = nameof(CanWrite);

    /// <summary>Керування довідниками та службові операції: лише admin.</summary>
    public const string AdminOnly = nameof(AdminOnly);

    /// <summary>Resource-based: власник ресурсу або admin.</summary>
    public const string OwnerOrAdmin = nameof(OwnerOrAdmin);
}

/// <summary>Вимога: користувач — власник ресурсу або адміністратор.</summary>
public sealed class OwnerOrAdminRequirement : IAuthorizationRequirement;

/// <summary>Ресурс, що має власника (AccountId = claim sub користувача, якому належить профіль).</summary>
/// <param name="AccountId">Ідентифікатор облікового запису власника.</param>
public sealed record OwnedResource(string AccountId);

/// <summary>
/// Власний обробник resource-based авторизації. Викликається з контролерів через
/// IAuthorizationService.AuthorizeAsync(User, new OwnedResource(...), Policies.OwnerOrAdmin).
/// Стану не має — Singleton.
/// </summary>
public sealed class OwnerOrAdminHandler : AuthorizationHandler<OwnerOrAdminRequirement, OwnedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnerOrAdminRequirement requirement,
        OwnedResource resource)
    {
        var userId = context.User.FindFirstValue(AuthConstants.SubjectClaim);

        if (context.User.IsInRole(Roles.Admin)
            || (userId is not null && string.Equals(resource.AccountId, userId, StringComparison.Ordinal)))
        {
            context.Succeed(requirement);
        }

        // Не викликаємо context.Fail(): інші обробники цієї ж вимоги (якщо з'являться) ще можуть дати дозвіл.
        return Task.CompletedTask;
    }
}
