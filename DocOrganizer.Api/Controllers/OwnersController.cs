using Asp.Versioning;
using DocOrganizer.Api.Auth;
using DocOrganizer.Api.Contracts;
using DocOrganizer.Api.Infrastructure;
using DocOrganizer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace DocOrganizer.Api.Controllers;

/// <summary>Профілі власників документів (члени сім'ї), які веде поточний користувач.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/owners")]
[Tags("Owners")]
[Authorize(Policy = Policies.CanRead)]
public sealed class OwnersController(IOwnerService service) : ApiControllerBase
{
    /// <summary>Повертає сторінку профілів власників.</summary>
    /// <remarks>
    /// Звичайний користувач бачить лише власні профілі, admin — усі.
    /// SortBy: fullName (за замовчуванням) | email | birthDate | id.
    /// </remarks>
    /// <param name="query">Пошук, пагінація та сортування.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Сторінку сформовано.</response>
    /// <response code="400">Некоректні параметри запиту.</response>
    [HttpGet]
    [OutputCache(PolicyName = CachePolicies.PerUser, Tags = [CachePolicies.OwnersTag])]
    [ProducesResponseType<PagedResult<OwnerDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<OwnerDto>>> GetOwners([FromQuery] OwnerQuery query, CancellationToken ct)
        => Ok(await service.GetAsync(query, ct));

    /// <summary>Повертає профіль власника за ідентифікатором.</summary>
    /// <param name="id">Ідентифікатор профілю.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="200">Профіль знайдено.</response>
    /// <response code="403">Профіль належить іншому користувачу.</response>
    /// <response code="404">Профілю з таким ідентифікатором немає.</response>
    [HttpGet("{id:int}", Name = nameof(GetOwner))]
    [ProducesResponseType<OwnerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOwner(int id, CancellationToken ct)
    {
        if (await AuthorizeOwnerAsync(service, id, ct) is { } denied) return denied;
        var owner = await service.GetByIdAsync(id, ct);
        return owner is null ? NotFoundProblem("Власник", id) : Ok(owner);
    }

    /// <summary>Створює профіль власника для поточного користувача.</summary>
    /// <param name="dto">Дані профілю.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="201">Профіль створено; заголовок Location містить його адресу.</response>
    /// <response code="400">Помилка валідації.</response>
    /// <response code="403">Роль viewer не має права запису.</response>
    /// <response code="409">Профіль з таким email уже існує.</response>
    [HttpPost]
    [Authorize(Policy = Policies.CanWrite)]
    [Consumes("application/json")]
    [EvictCache(CachePolicies.OwnersTag)]
    [ProducesResponseType<OwnerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OwnerDto>> CreateOwner(CreateOwnerDto dto, CancellationToken ct)
    {
        var created = await service.CreateAsync(dto, ct);
        return CreatedAtRoute(nameof(GetOwner), new { id = created.Id, version = RequestedApiVersion }, created);
    }

    /// <summary>Повністю замінює дані профілю власника.</summary>
    /// <param name="id">Ідентифікатор профілю.</param>
    /// <param name="dto">Нові дані.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="204">Дані оновлено.</response>
    /// <response code="400">Помилка валідації.</response>
    /// <response code="403">Немає права запису або профіль чужий.</response>
    /// <response code="404">Профілю немає.</response>
    /// <response code="409">Email уже використовує інший профіль.</response>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.CanWrite)]
    [Consumes("application/json")]
    [EvictCache(CachePolicies.OwnersTag, CachePolicies.DocumentsTag)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateOwner(int id, UpdateOwnerDto dto, CancellationToken ct)
    {
        if (await AuthorizeOwnerAsync(service, id, ct) is { } denied) return denied;
        return await service.UpdateAsync(id, dto, ct) ? NoContent() : NotFoundProblem("Власник", id);
    }

    /// <summary>Видаляє профіль власника (лише якщо в нього немає документів).</summary>
    /// <param name="id">Ідентифікатор профілю.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <response code="204">Профіль видалено.</response>
    /// <response code="403">Немає права запису або профіль чужий.</response>
    /// <response code="404">Профілю немає (зокрема, якщо його вже видалено).</response>
    /// <response code="409">До профілю прив'язані документи.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.CanWrite)]
    [EvictCache(CachePolicies.OwnersTag)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteOwner(int id, CancellationToken ct)
    {
        if (await AuthorizeOwnerAsync(service, id, ct) is { } denied) return denied;
        return await service.DeleteAsync(id, ct) ? NoContent() : NotFoundProblem("Власник", id);
    }
}
