using Asp.Versioning;
using DocOrganizer.Api.Auth;
using DocOrganizer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOrganizer.Api.Controllers;

/// <summary>
/// Базовий контролер: спільні атрибути (JSON, коди 401/403/500 для всіх дій)
/// і допоміжні методи для ProblemDetails та resource-based авторизації.
/// </summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>404 у форматі ProblemDetails.</summary>
    protected ObjectResult NotFoundProblem(string resource, int id)
        => Problem(statusCode: StatusCodes.Status404NotFound, title: "Ресурс не знайдено",
            detail: $"{resource} з Id={id} не існує.");

    /// <summary>422 у форматі ProblemDetails.</summary>
    protected ObjectResult UnprocessableProblem(string title, string detail)
        => Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: title, detail: detail);

    /// <summary>
    /// Resource-based перевірка: чи може поточний користувач працювати з ресурсом, що належить accountId.
    /// Викликає власний обробник <see cref="OwnerOrAdminHandler"/> через IAuthorizationService.
    /// </summary>
    protected async Task<bool> IsOwnerOrAdminAsync(string accountId)
    {
        var authz = HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var result = await authz.AuthorizeAsync(User, new OwnedResource(accountId), Policies.OwnerOrAdmin);
        return result.Succeeded;
    }

    /// <summary>
    /// Типовий сценарій для документа: 404, якщо його немає; 403, якщо він чужий; null — доступ дозволено.
    /// </summary>
    protected async Task<IActionResult?> AuthorizeDocumentAsync(IDocumentService documents, int documentId, CancellationToken ct)
    {
        var accountId = await documents.GetAccountIdAsync(documentId, ct);
        if (accountId is null) return NotFoundProblem("Документ", documentId);
        return await IsOwnerOrAdminAsync(accountId) ? null : Forbid();
    }

    /// <summary>Те саме для профілю власника.</summary>
    protected async Task<IActionResult?> AuthorizeOwnerAsync(IOwnerService owners, int ownerId, CancellationToken ct)
    {
        var accountId = await owners.GetAccountIdAsync(ownerId, ct);
        if (accountId is null) return NotFoundProblem("Власник", ownerId);
        return await IsOwnerOrAdminAsync(accountId) ? null : Forbid();
    }

    /// <summary>Версія API з маршруту — потрібна для CreatedAtRoute (вона є частиною шаблону URI).</summary>
    /// <remarks>
    /// В Asp.Versioning 10 метод GetRequestedApiVersion() замінено extension-властивістю
    /// HttpContext.RequestedApiVersion (C# 14 extension members).
    /// </remarks>
    protected string? RequestedApiVersion => HttpContext.RequestedApiVersion?.ToString();
}
