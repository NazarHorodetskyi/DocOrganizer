using Asp.Versioning;
using DocOrganizer.Api.Auth;
using DocOrganizer.Api.Contracts;
using DocOrganizer.Api.Infrastructure;
using DocOrganizer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOrganizer.Api.Controllers;

/// <summary>Нагадування конкретного документа (вкладений ресурс /documents/{documentId}/reminders).</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/documents/{documentId:int}/reminders")]
[Tags("Reminders")]
[Authorize(Policy = Policies.CanRead)]
public sealed class DocumentRemindersController(IReminderService service, IDocumentService documents)
    : ApiControllerBase
{
    /// <summary>Повертає всі нагадування документа (впорядковані за датою).</summary>
    /// <param name="documentId">Ідентифікатор документа.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Список нагадувань.</response>
    /// <response code="403">Документ належить іншому користувачу.</response>
    /// <response code="404">Документа немає.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReminderDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReminders(int documentId, CancellationToken ct)
    {
        if (await AuthorizeDocumentAsync(documents, documentId, ct) is { } denied) return denied;
        return Ok(await service.GetForDocumentAsync(documentId, ct));
    }

    /// <summary>Повертає одне нагадування документа.</summary>
    /// <param name="documentId">Ідентифікатор документа.</param>
    /// <param name="id">Ідентифікатор нагадування.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Нагадування знайдено.</response>
    /// <response code="403">Документ належить іншому користувачу.</response>
    /// <response code="404">Документа або нагадування немає.</response>
    [HttpGet("{id:int}", Name = nameof(GetReminder))]
    [ProducesResponseType<ReminderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReminder(int documentId, int id, CancellationToken ct)
    {
        if (await AuthorizeDocumentAsync(documents, documentId, ct) is { } denied) return denied;
        var reminder = await service.GetByIdAsync(documentId, id, ct);
        return reminder is null ? NotFoundProblem("Нагадування", id) : Ok(reminder);
    }

    /// <summary>Додає нагадування до документа.</summary>
    /// <param name="documentId">Ідентифікатор документа.</param>
    /// <param name="dto">За скільки днів нагадати і яким каналом.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="201">Нагадування створено.</response>
    /// <response code="400">Помилка валідації.</response>
    /// <response code="403">Немає права запису або документ чужий.</response>
    /// <response code="404">Документа немає.</response>
    /// <response code="409">Нагадування з таким відступом уже існує.</response>
    /// <response code="422">Документ безстроковий/неактивний або дата нагадування вже минула.</response>
    [HttpPost]
    [Authorize(Policy = Policies.CanWrite)]
    [Consumes("application/json")]
    [ProducesResponseType<ReminderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateReminder(int documentId, CreateReminderDto dto, CancellationToken ct)
    {
        if (await AuthorizeDocumentAsync(documents, documentId, ct) is { } denied) return denied;
        var created = await service.CreateAsync(documentId, dto, ct);
        return CreatedAtRoute(nameof(GetReminder),
            new { documentId, id = created.Id, version = RequestedApiVersion }, created);
    }

    /// <summary>Видаляє нагадування.</summary>
    /// <param name="documentId">Ідентифікатор документа.</param>
    /// <param name="id">Ідентифікатор нагадування.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="204">Нагадування видалено.</response>
    /// <response code="403">Немає права запису або документ чужий.</response>
    /// <response code="404">Документа або нагадування немає.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.CanWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteReminder(int documentId, int id, CancellationToken ct)
    {
        if (await AuthorizeDocumentAsync(documents, documentId, ct) is { } denied) return denied;
        return await service.DeleteAsync(documentId, id, ct) ? NoContent() : NotFoundProblem("Нагадування", id);
    }
}

/// <summary>Службові операції з нагадуваннями для робота розсилки (лише admin).</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reminders")]
[Tags("Reminders")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class RemindersController(IReminderService service) : ApiControllerBase
{
    /// <summary>Повертає ненадіслані нагадування, дата яких настала (черга розсилки).</summary>
    /// <remarks>SortBy: remindOn (за замовчуванням) | expiryDate.</remarks>
    /// <param name="query">Дата та пагінація.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Черга нагадувань.</response>
    /// <response code="400">Некоректні параметри.</response>
    /// <response code="403">Потрібна роль admin.</response>
    [HttpGet("due")]
    [ProducesResponseType<PagedResult<DueReminderDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<DueReminderDto>>> GetDue([FromQuery] DueReminderQuery query, CancellationToken ct)
        => Ok(await service.GetDueAsync(query, ct));

    /// <summary>Позначає нагадування надісланим.</summary>
    /// <param name="id">Ідентифікатор нагадування.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="204">Позначено.</response>
    /// <response code="403">Потрібна роль admin.</response>
    /// <response code="404">Нагадування немає.</response>
    /// <response code="409">Нагадування вже було надіслано.</response>
    [HttpPost("{id:int}/sent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkSent(int id, CancellationToken ct)
        => await service.MarkSentAsync(id, ct) ? NoContent() : NotFoundProblem("Нагадування", id);
}
