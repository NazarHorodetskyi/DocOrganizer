using DocOrganizer.Api.Auth;
using DocOrganizer.Api.Contracts;
using DocOrganizer.Api.Data;
using DocOrganizer.Api.Infrastructure;
using DocOrganizer.Api.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DocOrganizer.Api.Services;

public interface IOwnerService
{
    /// <summary>Сторінка профілів: звичайний користувач бачить лише свої, admin — усі.</summary>
    Task<PagedResult<OwnerDto>> GetAsync(OwnerQuery query, CancellationToken ct);
    Task<OwnerDto?> GetByIdAsync(int id, CancellationToken ct);
    /// <summary>AccountId власника профілю або null, якщо профілю немає (для resource-based перевірки).</summary>
    Task<string?> GetAccountIdAsync(int id, CancellationToken ct);
    Task<OwnerDto> CreateAsync(CreateOwnerDto dto, CancellationToken ct);
    Task<bool> UpdateAsync(int id, UpdateOwnerDto dto, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);
}

public sealed class OwnerService(
    AppDbContext db,
    ApiMapper mapper,
    ICurrentUser currentUser,
    ILogger<OwnerService> logger) : IOwnerService
{
    public Task<PagedResult<OwnerDto>> GetAsync(OwnerQuery q, CancellationToken ct)
    {
        var query = db.Owners.AsNoTracking();

        if (!currentUser.IsAdmin)
        {
            var accountId = currentUser.AccountId;
            query = query.Where(o => o.AccountId == accountId);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = q.Search.ToLikePattern();
            query = query.Where(o => EF.Functions.Like(o.FullName, pattern) || EF.Functions.Like(o.Email, pattern));
        }

        var ordered = (q.SortBy?.ToLowerInvariant()) switch
        {
            "email" => query.OrderByDir(o => o.Email, q.SortDir),
            "birthdate" => query.OrderByDir(o => o.BirthDate, q.SortDir),
            "id" => query.OrderByDir(o => o.Id, q.SortDir),
            _ => query.OrderByDir(o => o.FullName, q.SortDir)
        };

        // Проєкція в DTO прямо в SQL: COUNT документів рахує СКБД, сутності не відстежуються.
        return ordered.ThenBy(o => o.Id)
            .Select(o => new OwnerDto(o.Id, o.FullName, o.Email, o.BirthDate, o.Documents.Count))
            .ToPagedResultAsync(q, ct);
    }

    public Task<OwnerDto?> GetByIdAsync(int id, CancellationToken ct)
        => db.Owners.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new OwnerDto(o.Id, o.FullName, o.Email, o.BirthDate, o.Documents.Count))
            .FirstOrDefaultAsync(ct);

    public Task<string?> GetAccountIdAsync(int id, CancellationToken ct)
        => db.Owners.Where(o => o.Id == id).Select(o => o.AccountId).FirstOrDefaultAsync(ct);

    public async Task<OwnerDto> CreateAsync(CreateOwnerDto dto, CancellationToken ct)
    {
        await EnsureEmailUniqueAsync(dto.Email, exceptId: null, ct);

        var owner = mapper.ToEntity(dto);
        owner.AccountId = currentUser.AccountId;   // власник профілю — автор запиту (з токена)

        db.Owners.Add(owner);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Створено профіль власника {OwnerId} для акаунта {AccountId}", owner.Id, owner.AccountId);

        return new OwnerDto(owner.Id, owner.FullName, owner.Email, owner.BirthDate, 0);
    }

    public async Task<bool> UpdateAsync(int id, UpdateOwnerDto dto, CancellationToken ct)
    {
        var owner = await db.Owners.FindAsync([id], ct);
        if (owner is null) return false;

        await EnsureEmailUniqueAsync(dto.Email, id, ct);
        mapper.Apply(dto, owner);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var owner = await db.Owners.FindAsync([id], ct);
        if (owner is null) return false;

        // Бізнес-правило: документи — юридично важливі дані, власника з документами видаляти не можна.
        var documents = await db.Documents.CountAsync(d => d.OwnerId == id, ct);
        if (documents > 0)
            throw new ConflictException("Власник має документи",
                $"Неможливо видалити профіль: до нього прив'язано документів — {documents}. Спочатку видаліть або перенесіть їх.");

        db.Owners.Remove(owner);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureEmailUniqueAsync(string email, int? exceptId, CancellationToken ct)
    {
        var exists = await db.Owners.AnyAsync(o => o.Email == email && o.Id != exceptId, ct);
        if (exists)
            throw new ConflictException("Email уже використовується", $"Профіль з email '{email}' уже існує.");
    }
}
