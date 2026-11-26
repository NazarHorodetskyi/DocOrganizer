using Asp.Versioning;
using DocOrganizer.Api.Auth;
using DocOrganizer.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOrganizer.Api.Controllers;

/// <summary>Відомості про поточного користувача та службові кінцеві точки.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Tags("Account")]
public sealed class AccountController : ApiControllerBase
{
    /// <summary>Повертає відомості про автентифікованого користувача з маркера доступу.</summary>
    /// <remarks>Зручно для перевірки, які claims і ролі передав постачальник OIDC.</remarks>
    /// <response code="200">Відомості про користувача.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    public ActionResult<CurrentUserDto> GetMe()
    {
        var roles = User.FindAll(c => c.Type == User.Identities.First().RoleClaimType).Select(c => c.Value).ToList();
        var claims = User.Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(c => c.Value)));

        return Ok(new CurrentUserDto(
            User.FindFirst(AuthConstants.SubjectClaim)?.Value ?? "",
            User.Identity?.Name,
            roles,
            claims));
    }

    /// <summary>Навмисно генерує необроблений виняток — для перевірки глобального обробника помилок.</summary>
    /// <remarks>
    /// У Development відповідь 500 містить текст винятку в detail, у Production — лише загальне повідомлення.
    /// Доступно тільки admin.
    /// </remarks>
    /// <response code="500">Завжди: «Внутрішня помилка сервера».</response>
    [HttpGet("diagnostics/error")]
    [Authorize(Policy = Policies.AdminOnly)]
    public IActionResult ThrowError()
        => throw new InvalidOperationException("Тестовий виняток: рядок підключення Server=secret-db;Password=P@ssw0rd");
}
