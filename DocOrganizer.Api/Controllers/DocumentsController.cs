using Asp.Versioning;
using DocOrganizer.Api.Auth;
using DocOrganizer.Api.Contracts;
using DocOrganizer.Api.Infrastructure;
using DocOrganizer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace DocOrganizer.Api.Controllers;

/// <summary>Документи зі строком дії: CRUD, продовження, календар завершення строків, експорт.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/documents")]
[Tags("Documents")]
[Authorize(Policy = Policies.CanRead)]
public sealed class DocumentsController(IDocumentService service, IOwnerService owners) : ApiControllerBase
{
    /// <summary>Повертає сторінку документів із фільтрацією та сортуванням.</summary>
    /// <remarks>
    /// Фільтри: ownerId, documentTypeId, status, expiryState (Perpetual | Valid | ExpiringSoon | Expired | Inactive),
    /// tagId, search (частина назви або номера).
    /// SortBy: expiryDate (за замовчуванням) | issueDate | title | number | createdAt | id.
    /// Звичайний користувач бачить лише документи своїх профілів.
    /// </remarks>
    /// <param name="query">Фільтри, пагінація та сортування.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Сторінку сформовано (Items + метадані пагінації).</response>
    /// <response code="400">Некоректні параметри запиту.</response>
    [HttpGet]
    [OutputCache(PolicyName = CachePolicies.PerUser, Tags = [CachePolicies.DocumentsTag])]
    [ProducesResponseType<PagedResult<DocumentListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<DocumentListItemDto>>> GetDocuments(
        [FromQuery] DocumentQuery query, CancellationToken ct)
        => Ok(await service.GetAsync(query, ct));

    /// <summary>Календар завершення строків дії: активні документи, що спливають у заданому періоді.</summary>
    /// <remarks>За замовчуванням — від сьогодні на 90 днів уперед. SortBy: expiryDate | title.</remarks>
    /// <param name="query">Період, власник, пагінація.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Календар сформовано.</response>
    /// <response code="400">Некоректний період.</response>
    [HttpGet("expiry-calendar")]
    [OutputCache(PolicyName = CachePolicies.PerUser, Tags = [CachePolicies.DocumentsTag])]
    [ProducesResponseType<PagedResult<DocumentListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<DocumentListItemDto>>> GetExpiryCalendar(
        [FromQuery] ExpiryCalendarQuery query, CancellationToken ct)
        => Ok(await service.GetExpiryCalendarAsync(query, ct));

    /// <summary>Експортує відфільтрований список документів у файл (csv або json).</summary>
    /// <remarks>Реалізацію експортера обрано за ключем (keyed services) під час виконання. Максимум 5000 рядків.</remarks>
    /// <param name="format">Формат: csv або json.</param>
    /// <param name="query">Ті самі фільтри, що й у списку (пагінація ігнорується).</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Файл експорту.</response>
    /// <response code="400">Формат не підтримується.</response>
    [HttpGet("export/{format}")]
    [Produces("text/csv", "application/json")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "text/csv", "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Export(string format, [FromQuery] DocumentQuery query, CancellationToken ct)
    {
        var key = format.ToLowerInvariant();
        var exporter = HttpContext.RequestServices.GetKeyedService<IDocumentExporter>(key);
        if (exporter is null)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Формат не підтримується", detail: $"Формат '{format}' не підтримується. Доступні: csv, json.");

        var documents = await service.GetForExportAsync(query, ct);
        return File(exporter.Export(documents), exporter.ContentType, $"documents.{exporter.Extension}");
    }

    /// <summary>Повертає документ за ідентифікатором.</summary>
    /// <param name="id">Ідентифікатор документа.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Документ знайдено.</response>
    /// <response code="403">Документ належить іншому користувачу.</response>
    /// <response code="404">Документа немає.</response>
    [HttpGet("{id:int}", Name = nameof(GetDocument))]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocument(int id, CancellationToken ct)
    {
        if (await AuthorizeDocumentAsync(service, id, ct) is { } denied) return denied;
        var document = await service.GetByIdAsync(id, ct);
        return document is null ? NotFoundProblem("Документ", id) : Ok(document);
    }

    /// <summary>Створює документ.</summary>
    /// <remarks>
    /// Якщо задано expiryDate і createDefaultReminder = true, автоматично створюється нагадування
    /// за DefaultReminderDays типу документа.
    /// </remarks>
    /// <param name="dto">Дані документа.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="201">Документ створено; Location містить його адресу.</response>
    /// <response code="400">Помилка валідації (зокрема expiryDate раніше issueDate).</response>
    /// <response code="403">Немає права запису або профіль власника чужий.</response>
    /// <response code="409">Документ цього типу з таким номером уже існує.</response>
    /// <response code="422">Профіль власника, тип або мітки не існують.</response>
    [HttpPost]
    [Authorize(Policy = Policies.CanWrite)]
    [Consumes("application/json")]
    [EvictCache(CachePolicies.DocumentsTag, CachePolicies.OwnersTag)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateDocument(CreateDocumentDto dto, CancellationToken ct)
    {
        // Resource-based: документ можна додати лише до власного профілю (admin — до будь-якого).
        var ownerAccount = await owners.GetAccountIdAsync(dto.OwnerId, ct);
        if (ownerAccount is null)
            return UnprocessableProblem("Невідомий власник", $"Профіль власника з Id={dto.OwnerId} не існує.");
        if (!await IsOwnerOrAdminAsync(ownerAccount)) return Forbid();

        var created = await service.CreateAsync(dto, ct);
        return CreatedAtRoute(nameof(GetDocument), new { id = created.Id, version = RequestedApiVersion }, created);
    }

    /// <summary>Повністю замінює дані документа (оптимістичне блокування за полем version).</summary>
    /// <param name="id">Ідентифікатор документа.</param>
    /// <param name="dto">Нові дані та version, отриманий у GET.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="204">Документ оновлено.</response>
    /// <response code="400">Помилка валідації.</response>
    /// <response code="403">Немає права запису або документ (чи новий власник) чужий.</response>
    /// <response code="404">Документа немає.</response>
    /// <response code="409">Номер зайнятий або документ уже змінив інший користувач (version застарів).</response>
    /// <response code="422">Профіль власника, тип або мітки не існують.</response>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.CanWrite)]
    [Consumes("application/json")]
    [EvictCache(CachePolicies.DocumentsTag)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateDocument(int id, UpdateDocumentDto dto, CancellationToken ct)
    {
        if (await AuthorizeDocumentAsync(service, id, ct) is { } denied) return denied;

        // Переносити документ можна лише до власного профілю.
        var newOwnerAccount = await owners.GetAccountIdAsync(dto.OwnerId, ct);
        if (newOwnerAccount is null)
            return UnprocessableProblem("Невідомий власник", $"Профіль власника з Id={dto.OwnerId} не існує.");
        if (!await IsOwnerOrAdminAsync(newOwnerAccount)) return Forbid();

        return await service.UpdateAsync(id, dto, ct) ? NoContent() : NotFoundProblem("Документ", id);
    }

    /// <summary>Видаляє документ разом із його нагадуваннями та мітками.</summary>
    /// <param name="id">Ідентифікатор документа.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="204">Документ видалено.</response>
    /// <response code="403">Немає права запису або документ чужий.</response>
    /// <response code="404">Документа немає (повторний DELETE теж повертає 404 — стан сервера не змінюється).</response>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.CanWrite)]
    [EvictCache(CachePolicies.DocumentsTag, CachePolicies.OwnersTag)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(int id, CancellationToken ct)
    {
        if (await AuthorizeDocumentAsync(service, id, ct) is { } denied) return denied;
        return await service.DeleteAsync(id, ct) ? NoContent() : NotFoundProblem("Документ", id);
    }

    /// <summary>Продовжує документ: створює новий (з новим номером і строком), а старий позначає Replaced.</summary>
    /// <param name="id">Ідентифікатор документа, який замінюється.</param>
    /// <param name="dto">Дані нового документа.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="201">Створено новий документ; Location — його адреса.</response>
    /// <response code="400">Помилка валідації.</response>
    /// <response code="403">Немає права запису або документ чужий.</response>
    /// <response code="404">Документа немає.</response>
    /// <response code="409">Документ не активний (уже замінений/анульований) або новий номер зайнятий.</response>
    [HttpPost("{id:int}/renewal")]
    [Authorize(Policy = Policies.CanWrite)]
    [Consumes("application/json")]
    [EvictCache(CachePolicies.DocumentsTag, CachePolicies.OwnersTag)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RenewDocument(int id, RenewDocumentDto dto, CancellationToken ct)
    {
        if (await AuthorizeDocumentAsync(service, id, ct) is { } denied) return denied;
        var renewed = await service.RenewAsync(id, dto, ct);
        return CreatedAtRoute(nameof(GetDocument), new { id = renewed.Id, version = RequestedApiVersion }, renewed);
    }
}
