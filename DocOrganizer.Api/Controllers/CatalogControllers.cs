using Asp.Versioning;
using DocOrganizer.Api.Auth;
using DocOrganizer.Api.Contracts;
using DocOrganizer.Api.Infrastructure;
using DocOrganizer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace DocOrganizer.Api.Controllers;

/// <summary>Довідник типів документів. Читати можуть усі ролі, змінювати — лише admin.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/document-types")]
[Tags("Document types")]
[Authorize(Policy = Policies.CanRead)]
public sealed class DocumentTypesController(IDocumentTypeService service) : ApiControllerBase
{
    /// <summary>Повертає сторінку типів документів.</summary>
    /// <remarks>SortBy: name (за замовчуванням) | defaultReminderDays | id.</remarks>
    /// <param name="query">Пошук, пагінація та сортування.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Сторінку сформовано.</response>
    /// <response code="400">Некоректні параметри.</response>
    [HttpGet]
    [OutputCache(PolicyName = CachePolicies.PerUser, Tags = [CachePolicies.CatalogTag])]
    [ProducesResponseType<PagedResult<DocumentTypeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<DocumentTypeDto>>> GetDocumentTypes(
        [FromQuery] DocumentTypeQuery query, CancellationToken ct)
        => Ok(await service.GetAsync(query, ct));

    /// <summary>Повертає тип документа за ідентифікатором.</summary>
    /// <param name="id">Ідентифікатор типу.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Тип знайдено.</response>
    /// <response code="404">Типу немає.</response>
    [HttpGet("{id:int}", Name = nameof(GetDocumentType))]
    [ProducesResponseType<DocumentTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentType(int id, CancellationToken ct)
        => await service.GetByIdAsync(id, ct) is { } type ? Ok(type) : NotFoundProblem("Тип документа", id);

    /// <summary>Створює тип документа.</summary>
    /// <param name="dto">Назва та типовий відступ нагадування.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="201">Тип створено.</response>
    /// <response code="400">Помилка валідації.</response>
    /// <response code="403">Потрібна роль admin.</response>
    /// <response code="409">Тип із такою назвою вже існує.</response>
    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    [Consumes("application/json")]
    [EvictCache(CachePolicies.CatalogTag)]
    [ProducesResponseType<DocumentTypeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentTypeDto>> CreateDocumentType(SaveDocumentTypeDto dto, CancellationToken ct)
    {
        var created = await service.CreateAsync(dto, ct);
        return CreatedAtRoute(nameof(GetDocumentType), new { id = created.Id, version = RequestedApiVersion }, created);
    }

    /// <summary>Повністю замінює тип документа.</summary>
    /// <param name="id">Ідентифікатор типу.</param>
    /// <param name="dto">Нові дані.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="204">Тип оновлено.</response>
    /// <response code="400">Помилка валідації.</response>
    /// <response code="403">Потрібна роль admin.</response>
    /// <response code="404">Типу немає.</response>
    /// <response code="409">Назва зайнята іншим типом.</response>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    [Consumes("application/json")]
    [EvictCache(CachePolicies.CatalogTag, CachePolicies.DocumentsTag)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateDocumentType(int id, SaveDocumentTypeDto dto, CancellationToken ct)
        => await service.UpdateAsync(id, dto, ct) ? NoContent() : NotFoundProblem("Тип документа", id);

    /// <summary>Видаляє тип документа (лише якщо він не використовується).</summary>
    /// <param name="id">Ідентифікатор типу.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="204">Тип видалено.</response>
    /// <response code="403">Потрібна роль admin.</response>
    /// <response code="404">Типу немає.</response>
    /// <response code="409">Тип мають документи.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    [EvictCache(CachePolicies.CatalogTag)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDocumentType(int id, CancellationToken ct)
        => await service.DeleteAsync(id, ct) ? NoContent() : NotFoundProblem("Тип документа", id);
}

/// <summary>Довідник міток. Читати можуть усі ролі, змінювати — лише admin.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/tags")]
[Tags("Tags")]
[Authorize(Policy = Policies.CanRead)]
public sealed class TagsController(ITagService service) : ApiControllerBase
{
    /// <summary>Повертає сторінку міток.</summary>
    /// <remarks>SortBy: name (за замовчуванням) | id.</remarks>
    /// <param name="query">Пошук, пагінація та сортування.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Сторінку сформовано.</response>
    /// <response code="400">Некоректні параметри.</response>
    [HttpGet]
    [OutputCache(PolicyName = CachePolicies.PerUser, Tags = [CachePolicies.CatalogTag])]
    [ProducesResponseType<PagedResult<TagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<TagDto>>> GetTags([FromQuery] TagQuery query, CancellationToken ct)
        => Ok(await service.GetAsync(query, ct));

    /// <summary>Повертає мітку за ідентифікатором.</summary>
    /// <param name="id">Ідентифікатор мітки.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Мітку знайдено.</response>
    /// <response code="404">Мітки немає.</response>
    [HttpGet("{id:int}", Name = nameof(GetTag))]
    [ProducesResponseType<TagDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTag(int id, CancellationToken ct)
        => await service.GetByIdAsync(id, ct) is { } tag ? Ok(tag) : NotFoundProblem("Мітка", id);

    /// <summary>Створює мітку.</summary>
    /// <param name="dto">Назва мітки.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="201">Мітку створено.</response>
    /// <response code="400">Помилка валідації.</response>
    /// <response code="403">Потрібна роль admin.</response>
    /// <response code="409">Мітка з такою назвою вже існує.</response>
    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    [Consumes("application/json")]
    [EvictCache(CachePolicies.CatalogTag)]
    [ProducesResponseType<TagDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TagDto>> CreateTag(CreateTagDto dto, CancellationToken ct)
    {
        var created = await service.CreateAsync(dto, ct);
        return CreatedAtRoute(nameof(GetTag), new { id = created.Id, version = RequestedApiVersion }, created);
    }

    /// <summary>Видаляє мітку (з документів вона знімається автоматично).</summary>
    /// <param name="id">Ідентифікатор мітки.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="204">Мітку видалено.</response>
    /// <response code="403">Потрібна роль admin.</response>
    /// <response code="404">Мітки немає.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    [EvictCache(CachePolicies.CatalogTag, CachePolicies.DocumentsTag)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTag(int id, CancellationToken ct)
        => await service.DeleteAsync(id, ct) ? NoContent() : NotFoundProblem("Мітка", id);
}
