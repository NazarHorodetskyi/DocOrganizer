using DocOrganizer.DataLayer.Data;
using DocOrganizer.DataLayer.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocOrganizer.DataLayer.DataAccess.Ef;

/// <summary>
/// 9. LINQ-запити (фільтрація, сортування, DTO-проєкція, пов'язані дані, GroupBy, пагінація).
/// Методи Build*Query повертають IQueryable — його можна виконати або вивести через ToQueryString (п. 13).
/// </summary>
public sealed class EfDocumentQueries(IDbContextFactory<DocumentsDbContext> factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    // ---------------------------------------------------------------------
    // 1) Фільтрація за кількома параметрами (read-only сутності → AsNoTracking)
    // ---------------------------------------------------------------------

    public static IQueryable<Document> BuildFilterQuery(DocumentsDbContext db, DocumentSearchFilter filter, DateOnly today)
    {
        IQueryable<Document> query = db.Documents
            .AsNoTracking()
            .Include(d => d.Owner)
            .Include(d => d.DocumentType);

        // Умови додаються поступово; запит виконається ОДИН раз — при ToListAsync.
        if (filter.OwnerId is { } ownerId)
            query = query.Where(d => d.OwnerId == ownerId);

        if (filter.DocumentTypeId is { } typeId)
            query = query.Where(d => d.DocumentTypeId == typeId);

        if (filter.Status is { } status)
            query = query.Where(d => d.Status == status);

        if (filter.TagId is { } tagId)
            query = query.Where(d => d.DocumentTags.Any(dt => dt.TagId == tagId));

        if (filter.ExpiringWithinDays is not null)
        {
            var horizon = today.AddDays(filter.ExpiringWithinDays.Value);
            query = query.Where(d => d.ExpiryDate != null && d.ExpiryDate <= horizon);
        }

        if (!filter.IncludeExpired)
            query = query.Where(d => d.ExpiryDate == null || d.ExpiryDate >= today);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(d => d.Title.Contains(search) || d.Number.Contains(search));
        }

        return query
            .OrderBy(d => d.ExpiryDate == null)
            .ThenBy(d => d.ExpiryDate)
            .ThenBy(d => d.Id);
    }

    public async Task<List<Document>> FilterDocumentsAsync(DocumentSearchFilter filter, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await BuildFilterQuery(db, filter, Today).ToListAsync(ct);
    }

    // ---------------------------------------------------------------------
    // 2) Сортування (стабільне: завжди ThenBy(Id))
    // ---------------------------------------------------------------------

    public static IQueryable<Document> ApplySort(IQueryable<Document> query, DocumentSort sort, bool descending)
        => (sort, descending) switch
        {
            (DocumentSort.Title, false) => query.OrderBy(d => d.Title).ThenBy(d => d.Id),
            (DocumentSort.Title, true) => query.OrderByDescending(d => d.Title).ThenBy(d => d.Id),
            (DocumentSort.Owner, false) => query.OrderBy(d => d.Owner.FullName).ThenBy(d => d.ExpiryDate).ThenBy(d => d.Id),
            (DocumentSort.Owner, true) => query.OrderByDescending(d => d.Owner.FullName).ThenBy(d => d.ExpiryDate).ThenBy(d => d.Id),
            (_, false) => query.OrderBy(d => d.ExpiryDate == null).ThenBy(d => d.ExpiryDate).ThenBy(d => d.Id),
            (_, true) => query.OrderBy(d => d.ExpiryDate == null).ThenByDescending(d => d.ExpiryDate).ThenBy(d => d.Id)
        };

    public async Task<List<DocumentListItemDto>> GetSortedDocumentsAsync(
        DocumentSort sort, bool descending, int take, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await ApplySort(db.Documents, sort, descending)
            .Take(take)
            .Select(ToListItem)
            .ToListAsync(ct);
    }

    // ---------------------------------------------------------------------
    // 3) DTO-проєкція з навігаційними властивостями (EF сам додає JOIN)
    // ---------------------------------------------------------------------

    public async Task<List<DocumentListItemDto>> GetExpiringSoonAsync(int days, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var today = Today;
        var horizon = today.AddDays(days);

        return await db.Documents
            .Where(d => d.Status == DocumentStatus.Active
                        && d.ExpiryDate != null
                        && d.ExpiryDate >= today
                        && d.ExpiryDate <= horizon)
            .OrderBy(d => d.ExpiryDate)
            .ThenBy(d => d.Id)
            .Select(d => new DocumentListItemDto(
                d.Id, d.Title, d.Number, d.Owner.FullName, d.DocumentType.Name, d.ExpiryDate, d.Status))
            .ToListAsync(ct);
    }

    // ---------------------------------------------------------------------
    // 4) Запит із пов'язаними даними: Include / ThenInclude (read-only → AsNoTracking)
    // ---------------------------------------------------------------------

    public async Task<Owner?> GetOwnerWithDocumentsAsync(int ownerId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Owners
            .AsNoTracking()
            .Include(o => o.Documents)
                .ThenInclude(d => d.DocumentType)
            .Include(o => o.Documents)
                .ThenInclude(d => d.DocumentTags)
                    .ThenInclude(dt => dt.Tag)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == ownerId, ct);
    }

    // ---------------------------------------------------------------------
    // 5) GroupBy з агрегатами → GROUP BY у SQL
    // ---------------------------------------------------------------------

    public static IQueryable<OwnerStatsDto> BuildOwnerStatsQuery(DocumentsDbContext db, DateOnly today, int soonDays)
    {
        var horizon = today.AddDays(soonDays);

        return db.Documents
            .Where(d => d.Status == DocumentStatus.Active)
            .GroupBy(d => new { d.OwnerId, d.Owner.FullName })
            .OrderBy(g => g.Key.FullName)
            .Select(g => new OwnerStatsDto(
                g.Key.OwnerId,
                g.Key.FullName,
                g.Count(),
                g.Count(d => d.ExpiryDate < today),
                g.Count(d => d.ExpiryDate >= today && d.ExpiryDate <= horizon),
                g.Min(d => d.ExpiryDate)));
    }

    public async Task<List<OwnerStatsDto>> GetOwnerStatsAsync(int soonDays, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await BuildOwnerStatsQuery(db, Today, soonDays).ToListAsync(ct);
    }

    /// <summary>«Календар завершення строків»: скільки чинних документів спливає в кожному місяці.</summary>
    public async Task<List<ExpiryCalendarItemDto>> GetExpiryCalendarAsync(int months, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var from = Today;
        var to = from.AddMonths(months);

        return await db.Documents
            .Where(d => d.Status == DocumentStatus.Active
                        && d.ExpiryDate != null
                        && d.ExpiryDate >= from
                        && d.ExpiryDate < to)
            .GroupBy(d => new { d.ExpiryDate!.Value.Year, d.ExpiryDate!.Value.Month })
            .OrderBy(g => g.Key.Year)
            .ThenBy(g => g.Key.Month)
            .Select(g => new ExpiryCalendarItemDto(g.Key.Year, g.Key.Month, g.Count()))
            .ToListAsync(ct);
    }

    // ---------------------------------------------------------------------
    // 6) Пагінація (offset): Count + Skip/Take, pageSize обмежено
    // ---------------------------------------------------------------------

    public static IQueryable<DocumentListItemDto> BuildPageQuery(
        DocumentsDbContext db, DocumentSort sort, bool descending, int pageNumber, int pageSize)
        => ApplySort(db.Documents, sort, descending)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(ToListItem);

    public async Task<PagedResult<DocumentListItemDto>> GetDocumentsPageAsync(
        int pageNumber, int pageSize, DocumentSort sort, bool descending, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        pageNumber = Math.Max(pageNumber, 1);

        await using var db = await factory.CreateDbContextAsync(ct);

        var total = await db.Documents.CountAsync(ct);
        var items = await BuildPageQuery(db, sort, descending, pageNumber, pageSize).ToListAsync(ct);

        return new PagedResult<DocumentListItemDto>(items, total, pageNumber, pageSize);
    }

    // ---------------------------------------------------------------------
    // Зведення по власниках одним запитом (без N+1)
    // ---------------------------------------------------------------------

    public async Task<List<OwnerSummaryDto>> GetOwnerSummariesAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Owners
            .OrderBy(o => o.FullName)
            .Select(o => new OwnerSummaryDto(
                o.Id,
                o.FullName,
                o.Email,
                o.Documents.Count(),
                o.Documents.Count(d => d.Status == DocumentStatus.Active)))
            .ToListAsync(ct);
    }

    /// <summary>Спільна проєкція у DTO списку (Expression → перекладається в SQL).</summary>
    private static readonly System.Linq.Expressions.Expression<Func<Document, DocumentListItemDto>> ToListItem =
        d => new DocumentListItemDto(d.Id, d.Title, d.Number, d.Owner.FullName, d.DocumentType.Name, d.ExpiryDate, d.Status);
}
