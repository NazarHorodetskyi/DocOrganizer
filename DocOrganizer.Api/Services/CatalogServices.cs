using DocOrganizer.Api.Contracts;
using DocOrganizer.Api.Data;
using DocOrganizer.Api.Infrastructure;
using DocOrganizer.Api.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DocOrganizer.Api.Services;

public interface IDocumentTypeService
{
    Task<PagedResult<DocumentTypeDto>> GetAsync(DocumentTypeQuery query, CancellationToken ct);
    Task<DocumentTypeDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<DocumentTypeDto> CreateAsync(SaveDocumentTypeDto dto, CancellationToken ct);
    Task<bool> UpdateAsync(int id, SaveDocumentTypeDto dto, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);
}

public sealed class DocumentTypeService(AppDbContext db, ApiMapper mapper) : IDocumentTypeService
{
    public Task<PagedResult<DocumentTypeDto>> GetAsync(DocumentTypeQuery q, CancellationToken ct)
    {
        var query = db.DocumentTypes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
            query = query.Where(t => EF.Functions.Like(t.Name, q.Search.ToLikePattern()));

        var ordered = (q.SortBy?.ToLowerInvariant()) switch
        {
            "defaultreminderdays" => query.OrderByDir(t => t.DefaultReminderDays, q.SortDir),
            "id" => query.OrderByDir(t => t.Id, q.SortDir),
            _ => query.OrderByDir(t => t.Name, q.SortDir)
        };
        return mapper.ProjectToDto(ordered.ThenBy(t => t.Id)).ToPagedResultAsync(q, ct);
    }

    public Task<DocumentTypeDto?> GetByIdAsync(int id, CancellationToken ct)
        => mapper.ProjectToDto(db.DocumentTypes.AsNoTracking().Where(t => t.Id == id)).FirstOrDefaultAsync(ct);

    public async Task<DocumentTypeDto> CreateAsync(SaveDocumentTypeDto dto, CancellationToken ct)
    {
        await EnsureNameUniqueAsync(dto.Name, null, ct);
        var entity = mapper.ToEntity(dto);
        db.DocumentTypes.Add(entity);
        await db.SaveChangesAsync(ct);
        return mapper.ToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, SaveDocumentTypeDto dto, CancellationToken ct)
    {
        var entity = await db.DocumentTypes.FindAsync([id], ct);
        if (entity is null) return false;
        await EnsureNameUniqueAsync(dto.Name, id, ct);
        mapper.Apply(dto, entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var entity = await db.DocumentTypes.FindAsync([id], ct);
        if (entity is null) return false;

        var used = await db.Documents.CountAsync(d => d.DocumentTypeId == id, ct);
        if (used > 0)
            throw new ConflictException("Тип використовується",
                $"Тип «{entity.Name}» мають документів: {used}. Видалити його неможливо.");

        db.DocumentTypes.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureNameUniqueAsync(string name, int? exceptId, CancellationToken ct)
    {
        if (await db.DocumentTypes.AnyAsync(t => t.Name == name && t.Id != exceptId, ct))
            throw new ConflictException("Тип уже існує", $"Тип документа «{name}» уже існує.");
    }
}

public interface ITagService
{
    Task<PagedResult<TagDto>> GetAsync(TagQuery query, CancellationToken ct);
    Task<TagDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<TagDto> CreateAsync(CreateTagDto dto, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);
}

/// <summary>Фільтри списку міток. SortBy: name | id.</summary>
public sealed class TagQuery : PagedQuery
{
    /// <summary>Пошук за частиною назви.</summary>
    public string? Search { get; set; }
}

public sealed class TagService(AppDbContext db, ApiMapper mapper) : ITagService
{
    public Task<PagedResult<TagDto>> GetAsync(TagQuery q, CancellationToken ct)
    {
        var query = db.Tags.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
            query = query.Where(t => EF.Functions.Like(t.Name, q.Search.ToLikePattern()));
        var ordered = q.SortBy?.ToLowerInvariant() == "id"
            ? query.OrderByDir(t => t.Id, q.SortDir)
            : query.OrderByDir(t => t.Name, q.SortDir);
        return mapper.ProjectToDto(ordered.ThenBy(t => t.Id)).ToPagedResultAsync(q, ct);
    }

    public Task<TagDto?> GetByIdAsync(int id, CancellationToken ct)
        => mapper.ProjectToDto(db.Tags.AsNoTracking().Where(t => t.Id == id)).FirstOrDefaultAsync(ct);

    public async Task<TagDto> CreateAsync(CreateTagDto dto, CancellationToken ct)
    {
        if (await db.Tags.AnyAsync(t => t.Name == dto.Name, ct))
            throw new ConflictException("Мітка вже існує", $"Мітка «{dto.Name}» уже існує.");
        var tag = new Domain.Tag { Name = dto.Name };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);
        return mapper.ToDto(tag);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
        // Зв'язки DocumentTags видаляє каскад у БД; самі документи залишаються.
        => await db.Tags.Where(t => t.Id == id).ExecuteDeleteAsync(ct) > 0;
}
