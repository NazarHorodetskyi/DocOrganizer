using DocOrganizer.Api.Auth;
using DocOrganizer.Api.Contracts;
using DocOrganizer.Api.Data;
using DocOrganizer.Api.Domain;
using DocOrganizer.Api.Infrastructure;
using DocOrganizer.Api.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocOrganizer.Api.Services;

/// <summary>
/// Перемикач оптимізацій для навантажувального тестування «до / після».
/// false — наївна реалізація (відстеження змін, Include усіх зв'язків, фільтрація і пагінація в пам'яті, без кешу);
/// true — AsNoTracking, проєкція в DTO, пагінація в БД, OutputCache.
/// </summary>
public sealed class PerformanceOptions
{
    public const string Section = "Performance";
    public bool Optimized { get; set; } = true;
}

public interface IDocumentService
{
    Task<PagedResult<DocumentListItemDto>> GetAsync(DocumentQuery query, CancellationToken ct);
    Task<DocumentDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<string?> GetAccountIdAsync(int id, CancellationToken ct);
    Task<DocumentDto> CreateAsync(CreateDocumentDto dto, CancellationToken ct);
    Task<bool> UpdateAsync(int id, UpdateDocumentDto dto, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);
    Task<DocumentDto> RenewAsync(int id, RenewDocumentDto dto, CancellationToken ct);
    Task<PagedResult<DocumentListItemDto>> GetExpiryCalendarAsync(ExpiryCalendarQuery query, CancellationToken ct);
    Task<IReadOnlyList<DocumentListItemDto>> GetForExportAsync(DocumentQuery query, CancellationToken ct);
}

public sealed class DocumentService(
    AppDbContext db,
    ApiMapper mapper,
    ICurrentUser currentUser,
    TimeProvider time,
    IOptionsSnapshot<PerformanceOptions> performance,
    ILogger<DocumentService> logger) : IDocumentService
{
    private const int MaxExportRows = 5000;

    // ─────────────────────────── Читання ───────────────────────────

    public Task<PagedResult<DocumentListItemDto>> GetAsync(DocumentQuery q, CancellationToken ct)
        => performance.Value.Optimized ? GetOptimizedAsync(q, ct) : GetNaiveAsync(q, ct);

    /// <summary>
    /// ПІСЛЯ оптимізації: усі фільтри, сортування й пагінація виконуються в SQL;
    /// AsNoTracking + проєкція лише потрібних колонок (без завантаження тегів і нагадувань).
    /// </summary>
    private async Task<PagedResult<DocumentListItemDto>> GetOptimizedAsync(DocumentQuery q, CancellationToken ct)
    {
        var today = time.Today();
        var query = ApplySorting(ApplyFilters(VisibleDocuments().AsNoTracking(), q, today), q);

        return await query
            .Select(d => new DocumentRow(d.Id, d.OwnerId, d.Owner.FullName, d.DocumentType.Name,
                d.Number, d.Title, d.ExpiryDate, d.Status))
            .ToPagedResultAsync(q, row => row.ToDto(today), ct);
    }

    /// <summary>
    /// ДО оптимізації (навмисно наївна версія для порівняння): завантажує ВСІ документи користувача
    /// з усіма зв'язками у Change Tracker, а фільтрує, сортує й ділить на сторінки в пам'яті.
    /// </summary>
    private async Task<PagedResult<DocumentListItemDto>> GetNaiveAsync(DocumentQuery q, CancellationToken ct)
    {
        var today = time.Today();
        var all = await VisibleDocuments()
            .Include(d => d.Owner)
            .Include(d => d.DocumentType)
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.Reminders)
            .ToListAsync(ct);

        var filtered = ApplyFilters(all.AsQueryable(), q, today);
        var sorted = ApplySorting(filtered, q).ToList();
        var items = sorted.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(d => mapper.ToListItem(d, today)).ToList();
        return PagedResult<DocumentListItemDto>.Create(items, q.Page, q.PageSize, sorted.Count);
    }

    public async Task<DocumentDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking()
            .Include(d => d.Owner)
            .Include(d => d.DocumentType)
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        return document is null ? null : mapper.ToDto(document, time.Today());
    }

    public Task<string?> GetAccountIdAsync(int id, CancellationToken ct)
        => db.Documents.Where(d => d.Id == id).Select(d => d.Owner.AccountId).FirstOrDefaultAsync(ct);

    public Task<PagedResult<DocumentListItemDto>> GetExpiryCalendarAsync(ExpiryCalendarQuery q, CancellationToken ct)
    {
        var today = time.Today();
        var from = q.From ?? today;
        var to = q.To ?? from.AddDays(90);
        if (to < from)
            throw new BusinessRuleException("Некоректний період", "Дата 'to' не може бути раніше за 'from'.");

        var query = VisibleDocuments().AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Active && d.ExpiryDate >= from && d.ExpiryDate <= to);
        if (q.OwnerId is not null)
            query = query.Where(d => d.OwnerId == q.OwnerId);

        var ordered = (q.SortBy?.ToLowerInvariant()) switch
        {
            "title" => query.OrderByDir(d => d.Title, q.SortDir),
            _ => query.OrderByDir(d => d.ExpiryDate, q.SortDir)
        };

        return ordered.ThenBy(d => d.Id)
            .Select(d => new DocumentRow(d.Id, d.OwnerId, d.Owner.FullName, d.DocumentType.Name,
                d.Number, d.Title, d.ExpiryDate, d.Status))
            .ToPagedResultAsync(q, row => row.ToDto(today), ct);
    }

    public async Task<IReadOnlyList<DocumentListItemDto>> GetForExportAsync(DocumentQuery q, CancellationToken ct)
    {
        var today = time.Today();
        var rows = await ApplySorting(ApplyFilters(VisibleDocuments().AsNoTracking(), q, today), q)
            .Take(MaxExportRows)
            .Select(d => new DocumentRow(d.Id, d.OwnerId, d.Owner.FullName, d.DocumentType.Name,
                d.Number, d.Title, d.ExpiryDate, d.Status))
            .ToListAsync(ct);
        return rows.Select(r => r.ToDto(today)).ToList();
    }

    // ─────────────────────────── Запис ───────────────────────────

    public async Task<DocumentDto> CreateAsync(CreateDocumentDto dto, CancellationToken ct)
    {
        var type = await db.DocumentTypes.FindAsync([dto.DocumentTypeId], ct)
            ?? throw new BusinessRuleException("Невідомий тип документа", $"Тип документа з Id={dto.DocumentTypeId} не існує.");
        await EnsureOwnerExistsAsync(dto.OwnerId, ct);
        await EnsureNumberUniqueAsync(dto.DocumentTypeId, dto.Number, exceptId: null, ct);
        var tags = await LoadTagsAsync(dto.TagIds, ct);

        var document = mapper.ToEntity(dto);
        document.Status = DocumentStatus.Active;
        foreach (var tag in tags)
            document.DocumentTags.Add(new DocumentTag { TagId = tag.Id });

        // Нагадування за замовчуванням — за DefaultReminderDays типу, якщо дата ще не минула.
        if (dto.CreateDefaultReminder && dto.ExpiryDate is { } expiry)
        {
            var remindOn = expiry.AddDays(-type.DefaultReminderDays);
            if (remindOn >= time.Today())
                document.Reminders.Add(new Reminder
                {
                    DaysBefore = type.DefaultReminderDays, RemindOn = remindOn, Channel = ReminderChannel.Email
                });
        }

        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Створено документ {DocumentId} ({Number})", document.Id, document.Number);

        return (await GetByIdAsync(document.Id, ct))!;
    }

    public async Task<bool> UpdateAsync(int id, UpdateDocumentDto dto, CancellationToken ct)
    {
        var document = await db.Documents
            .Include(d => d.DocumentTags)
            .Include(d => d.Reminders)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null) return false;

        // Оптимістичне блокування: порівнюємо версію, яку бачив клієнт, з поточною.
        // EF додасть "WHERE Version = @original" до UPDATE; якщо рядок змінено — DbUpdateConcurrencyException → 409.
        var clientVersion = Convert.FromBase64String(dto.Version);
        db.Entry(document).Property(d => d.Version).OriginalValue = clientVersion;
        if (!document.Version.AsSpan().SequenceEqual(clientVersion))
            throw new ConflictException("Конфлікт паралельного редагування",
                "Документ змінив інший користувач. Отримайте актуальну версію (GET) і повторіть зміни.");

        if (!await db.DocumentTypes.AnyAsync(t => t.Id == dto.DocumentTypeId, ct))
            throw new BusinessRuleException("Невідомий тип документа", $"Тип документа з Id={dto.DocumentTypeId} не існує.");
        await EnsureOwnerExistsAsync(dto.OwnerId, ct);
        await EnsureNumberUniqueAsync(dto.DocumentTypeId, dto.Number, id, ct);
        var tags = await LoadTagsAsync(dto.TagIds, ct);

        // PUT — повна заміна ресурсу (ідемпотентна: повтор того самого тіла дає той самий стан).
        document.OwnerId = dto.OwnerId;
        document.DocumentTypeId = dto.DocumentTypeId;
        document.Number = dto.Number;
        document.Title = dto.Title;
        document.IssueDate = dto.IssueDate;
        document.IssuedBy = dto.IssuedBy;
        document.FilePath = dto.FilePath;
        document.Notes = dto.Notes;
        document.Status = dto.Status;

        if (document.ExpiryDate != dto.ExpiryDate)
        {
            document.ExpiryDate = dto.ExpiryDate;
            // Дата завершення змінилась — перераховуємо дати ще не надісланих нагадувань.
            foreach (var reminder in document.Reminders.Where(r => !r.IsSent).ToList())
            {
                if (dto.ExpiryDate is { } expiry) reminder.RemindOn = expiry.AddDays(-reminder.DaysBefore);
                else db.Reminders.Remove(reminder);   // безстроковий документ — нагадувати нема про що
            }
        }

        var newTagIds = tags.Select(t => t.Id).ToHashSet();
        foreach (var link in document.DocumentTags.Where(l => !newTagIds.Contains(l.TagId)).ToList())
            document.DocumentTags.Remove(link);
        foreach (var tagId in newTagIds.Except(document.DocumentTags.Select(l => l.TagId)))
            document.DocumentTags.Add(new DocumentTag { TagId = tagId });

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        // ExecuteDelete — один DELETE без попереднього SELECT; нагадування й мітки видаляє каскад у БД.
        var deleted = await db.Documents.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public async Task<DocumentDto> RenewAsync(int id, RenewDocumentDto dto, CancellationToken ct)
    {
        var old = await db.Documents
            .Include(d => d.DocumentType)
            .Include(d => d.DocumentTags)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Документ", id);

        // Бізнес-правило: продовжити можна лише чинний (Active) документ — інакше 409.
        if (old.Status != DocumentStatus.Active)
            throw new ConflictException("Документ неактивний",
                $"Документ має статус {old.Status}; продовжити можна лише документ зі статусом Active.");

        await EnsureNumberUniqueAsync(old.DocumentTypeId, dto.NewNumber, exceptId: null, ct);

        var renewed = new Document
        {
            OwnerId = old.OwnerId,
            DocumentTypeId = old.DocumentTypeId,
            Number = dto.NewNumber,
            Title = old.Title,
            IssueDate = dto.IssueDate,
            ExpiryDate = dto.ExpiryDate,
            IssuedBy = dto.IssuedBy ?? old.IssuedBy,
            Notes = $"Замінює документ №{old.Number} (Id={old.Id}).",
            Status = DocumentStatus.Active
        };
        foreach (var link in old.DocumentTags)
            renewed.DocumentTags.Add(new DocumentTag { TagId = link.TagId });
        if (dto.ExpiryDate is { } expiry && expiry.AddDays(-old.DocumentType.DefaultReminderDays) >= time.Today())
            renewed.Reminders.Add(new Reminder
            {
                DaysBefore = old.DocumentType.DefaultReminderDays,
                RemindOn = expiry.AddDays(-old.DocumentType.DefaultReminderDays),
                Channel = ReminderChannel.Email
            });

        old.Status = DocumentStatus.Replaced;
        db.Documents.Add(renewed);

        // Обидві зміни — в одній транзакції SaveChanges.
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(renewed.Id, ct))!;
    }

    // ─────────────────────────── Допоміжне ───────────────────────────

    /// <summary>Документи, які має право бачити поточний користувач (admin — усі).</summary>
    private IQueryable<Document> VisibleDocuments()
    {
        if (currentUser.IsAdmin) return db.Documents;
        var accountId = currentUser.AccountId;
        return db.Documents.Where(d => d.Owner.AccountId == accountId);
    }

    private static IQueryable<Document> ApplyFilters(IQueryable<Document> query, DocumentQuery q, DateOnly today)
    {
        if (q.OwnerId is not null) query = query.Where(d => d.OwnerId == q.OwnerId);
        if (q.DocumentTypeId is not null) query = query.Where(d => d.DocumentTypeId == q.DocumentTypeId);
        if (q.Status is not null) query = query.Where(d => d.Status == q.Status);
        if (q.TagId is not null) query = query.Where(d => d.DocumentTags.Any(t => t.TagId == q.TagId));

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(d => d.Title.Contains(s) || d.Number.Contains(s));
        }

        if (q.ExpiryState is { } state)
        {
            var soon = today.AddDays(ExpiryRules.SoonDays);
            query = state switch
            {
                ExpiryState.Inactive => query.Where(d => d.Status != DocumentStatus.Active),
                ExpiryState.Perpetual => query.Where(d => d.Status == DocumentStatus.Active && d.ExpiryDate == null),
                ExpiryState.Expired => query.Where(d => d.Status == DocumentStatus.Active && d.ExpiryDate < today),
                ExpiryState.ExpiringSoon => query.Where(d =>
                    d.Status == DocumentStatus.Active && d.ExpiryDate >= today && d.ExpiryDate <= soon),
                ExpiryState.Valid => query.Where(d => d.Status == DocumentStatus.Active && d.ExpiryDate > soon),
                _ => query
            };
        }
        return query;
    }

    private static IQueryable<Document> ApplySorting(IQueryable<Document> query, DocumentQuery q)
    {
        var ordered = (q.SortBy?.ToLowerInvariant()) switch
        {
            "issuedate" => query.OrderByDir(d => d.IssueDate, q.SortDir),
            "title" => query.OrderByDir(d => d.Title, q.SortDir),
            "number" => query.OrderByDir(d => d.Number, q.SortDir),
            "createdat" => query.OrderByDir(d => d.CreatedAt, q.SortDir),
            "id" => query.OrderByDir(d => d.Id, q.SortDir),
            _ => query.OrderByDir(d => d.ExpiryDate, q.SortDir)
        };
        return ordered.ThenBy(d => d.Id);   // стабільний порядок для пагінації
    }

    private async Task EnsureOwnerExistsAsync(int ownerId, CancellationToken ct)
    {
        if (!await db.Owners.AnyAsync(o => o.Id == ownerId, ct))
            throw new BusinessRuleException("Невідомий власник", $"Профіль власника з Id={ownerId} не існує.");
    }

    private async Task EnsureNumberUniqueAsync(int typeId, string number, int? exceptId, CancellationToken ct)
    {
        var exists = await db.Documents.AnyAsync(
            d => d.DocumentTypeId == typeId && d.Number == number && d.Id != exceptId, ct);
        if (exists)
            throw new ConflictException("Документ уже існує",
                $"Документ цього типу з номером '{number}' уже зареєстровано.");
    }

    private async Task<List<Tag>> LoadTagsAsync(int[]? tagIds, CancellationToken ct)
    {
        if (tagIds is null || tagIds.Length == 0) return [];
        var distinct = tagIds.Distinct().ToArray();
        var tags = await db.Tags.Where(t => distinct.Contains(t.Id)).ToListAsync(ct);
        var missing = distinct.Except(tags.Select(t => t.Id)).ToArray();
        if (missing.Length > 0)
            throw new BusinessRuleException("Невідомі мітки", $"Мітки з Id={string.Join(", ", missing)} не існують.");
        return tags;
    }

    /// <summary>Плоский рядок проєкції — читається з БД без сутностей.</summary>
    private sealed record DocumentRow(int Id, int OwnerId, string OwnerFullName, string TypeName,
        string Number, string Title, DateOnly? ExpiryDate, DocumentStatus Status)
    {
        public DocumentListItemDto ToDto(DateOnly today) => new(Id, OwnerId, OwnerFullName, TypeName, Number, Title,
            ExpiryDate, Status, ExpiryRules.GetState(ExpiryDate, Status, today), ExpiryRules.DaysLeft(ExpiryDate, today));
    }
}
