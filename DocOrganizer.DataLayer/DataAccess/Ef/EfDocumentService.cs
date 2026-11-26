using DocOrganizer.DataLayer.Data;
using DocOrganizer.DataLayer.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOrganizer.DataLayer.DataAccess.Ef;

/// <summary>
/// CRUD через EF Core для трьох сутностей (Owners, Documents, Reminders)
/// та складна транзакційна операція над графом сутностей.
/// Кожен метод — коротка одиниця роботи: окремий DbContext з фабрики.
/// </summary>
public sealed class EfDocumentService(IDbContextFactory<DocumentsDbContext> factory, ILogger<EfDocumentService> logger)
{
    // =====================================================================
    // Owners
    // =====================================================================

    public async Task<int> CreateOwnerAsync(OwnerInput input, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var owner = new Owner { FullName = input.FullName, Email = input.Email, BirthDate = input.BirthDate };
        db.Owners.Add(owner);

        try
        {
            await db.SaveChangesAsync(ct);    // INSERT; згенерований Id записується в owner.Id
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            throw new DuplicateValueException($"Власник з email «{input.Email}» уже існує.", ex);
        }

        return owner.Id;
    }

    public async Task<OwnerDto?> GetOwnerAsync(int id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // DTO-проєкція не відстежується, AsNoTracking не потрібен.
        return await db.Owners
            .Where(o => o.Id == id)
            .Select(o => new OwnerDto(o.Id, o.FullName, o.Email, o.BirthDate))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<bool> UpdateOwnerAsync(int id, OwnerInput input, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // Tracking-запит: контекст знає початкові значення → UPDATE лише змінених стовпців.
        var owner = await db.Owners.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (owner is null)
            return false;

        owner.FullName = input.FullName;
        owner.Email = input.Email;
        owner.BirthDate = input.BirthDate;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            throw new DuplicateValueException($"Email «{input.Email}» уже зайнятий іншим власником.", ex);
        }

        return true;
    }

    public async Task<DeleteResult> DeleteOwnerAsync(int id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var owner = await db.Owners.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (owner is null)
            return DeleteResult.NotFound;

        db.Owners.Remove(owner);
        try
        {
            await db.SaveChangesAsync(ct);
            return DeleteResult.Deleted;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            // DeleteBehavior.Restrict → FK NO ACTION → SQL Server відхиляє DELETE.
            return DeleteResult.HasDependencies;
        }
    }

    // =====================================================================
    // Documents
    // =====================================================================

    /// <summary>Створює документ разом із тегами й нагадуваннями як граф — один SaveChanges.</summary>
    public async Task<int> CreateDocumentAsync(
        DocumentInput input, IReadOnlyCollection<int> tagIds, IReadOnlyCollection<int> reminderDays, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var document = new Document
        {
            OwnerId = input.OwnerId,
            DocumentTypeId = input.DocumentTypeId,
            Number = input.Number,
            Title = input.Title,
            IssueDate = input.IssueDate,
            ExpiryDate = input.ExpiryDate,
            FilePath = input.FilePath,
            Notes = input.Notes,
            Status = DocumentStatus.Active
        };

        foreach (var tagId in tagIds.Distinct())
            document.DocumentTags.Add(new DocumentTag { TagId = tagId });

        if (input.ExpiryDate is { } expiry)
        {
            foreach (var days in reminderDays.Distinct())
                document.Reminders.Add(new Reminder
                {
                    DaysBefore = days,
                    RemindOn = expiry.AddDays(-days),
                    Channel = ReminderChannel.Email
                });
        }

        db.Documents.Add(document);   // увесь граф отримує стан Added
        await SaveDocumentChangesAsync(db, input.Number, ct);
        return document.Id;
    }

    /// <summary>Read-only запит, що повертає сутність з пов'язаними даними → AsNoTracking.</summary>
    public async Task<Document?> GetDocumentAsync(int id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Documents
            .AsNoTracking()
            .Include(d => d.Owner)
            .Include(d => d.DocumentType)
            .Include(d => d.Reminders)
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .AsSplitQuery()   // кілька колекцій → окремі SELECT замість «декартового вибуху»
            .SingleOrDefaultAsync(d => d.Id == id, ct);
    }

    /// <param name="expectedVersion">
    /// Значення Version, яке клієнт отримав під час читання. Якщо рядок у БД уже змінено —
    /// буде ConcurrencyConflictException (сценарій Web API / форми).
    /// </param>
    public async Task<bool> UpdateDocumentAsync(int id, DocumentInput input, byte[]? expectedVersion, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
            return false;

        if (expectedVersion is not null)
            db.Entry(document).Property(d => d.Version).OriginalValue = expectedVersion;

        // Переносимо лише дозволені до редагування поля.
        document.OwnerId = input.OwnerId;
        document.DocumentTypeId = input.DocumentTypeId;
        document.Number = input.Number;
        document.Title = input.Title;
        document.IssueDate = input.IssueDate;
        document.ExpiryDate = input.ExpiryDate;
        document.FilePath = input.FilePath;
        document.Notes = input.Notes;

        try
        {
            await SaveDocumentChangesAsync(db, input.Number, ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(
                "Документ уже змінив інший користувач. Оновіть дані й повторіть редагування.", ex);
        }

        return true;
    }

    public async Task<bool> DeleteDocumentAsync(int id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
            return false;

        db.Documents.Remove(document);   // Reminders/DocumentTags видалить ON DELETE CASCADE у БД
        await db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Reminders
    // =====================================================================

    public async Task<int> AddReminderAsync(int documentId, int daysBefore, ReminderChannel channel, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var info = await db.Documents
            .Where(d => d.Id == documentId)
            .Select(d => new { d.ExpiryDate, d.Status })
            .SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException($"Документ #{documentId} не знайдено.");

        if (info.ExpiryDate is null)
            throw new BusinessRuleException("Для безстрокового документа нагадування не створюються.");
        if (info.Status != DocumentStatus.Active)
            throw new BusinessRuleException("Нагадування створюються лише для чинних документів.");

        var reminder = new Reminder
        {
            DocumentId = documentId,
            DaysBefore = daysBefore,
            RemindOn = info.ExpiryDate.Value.AddDays(-daysBefore),
            Channel = channel
        };
        db.Reminders.Add(reminder);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            throw new DuplicateValueException($"Нагадування за {daysBefore} дн. для цього документа вже існує.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            throw new DataAccessException("Кількість днів має бути в межах 0–365 (CHECK).", ex);
        }

        return reminder.Id;
    }

    public async Task<IReadOnlyList<ReminderDto>> GetRemindersAsync(int documentId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Reminders
            .Where(r => r.DocumentId == documentId)
            .OrderBy(r => r.RemindOn)
            .Select(r => new ReminderDto(r.Id, r.DocumentId, r.DaysBefore, r.RemindOn, r.Channel, r.IsSent))
            .ToListAsync(ct);
    }

    public async Task<bool> MarkReminderSentAsync(int reminderId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var reminder = await db.Reminders.SingleOrDefaultAsync(r => r.Id == reminderId, ct);
        if (reminder is null)
            return false;

        reminder.IsSent = true;
        reminder.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);   // UPDATE лише IsSent і SentAt
        return true;
    }

    public async Task<bool> DeleteReminderAsync(int reminderId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // Масове видалення без завантаження сутності (EF Core 7+): одна команда DELETE.
        var deleted = await db.Reminders
            .Where(r => r.Id == reminderId)
            .ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    /// <summary>«Розсилка»: позначити надісланими всі нагадування, дата яких настала. Одна команда UPDATE.</summary>
    public async Task<int> MarkDueRemindersAsSentAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var now = DateTime.UtcNow;

        return await db.Reminders
            .Where(r => !r.IsSent && r.RemindOn <= today && r.Document.Status == DocumentStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsSent, true)
                .SetProperty(r => r.SentAt, now), ct);
    }

    // =====================================================================
    // 8. Складна бізнес-операція над графом сутностей у транзакції
    // =====================================================================

    /// <summary>
    /// Продовження документа засобами EF Core:
    /// 1) ExecuteUpdate — старий документ атомарно стає Replaced (лише якщо він ще Active);
    /// 2) ExecuteDelete — невідправлені нагадування старого документа;
    /// 3) граф Document + DocumentTags + Reminders → один SaveChanges.
    /// ExecuteUpdate/ExecuteDelete виконуються одразу й не входять у транзакцію SaveChanges,
    /// тому потрібна явна транзакція: помилка на кроці 3 скасовує кроки 1–2.
    /// </summary>
    public async Task<RenewResult> RenewDocumentAsync(RenewDocumentRequest request, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (request.NewExpiryDate < request.NewIssueDate)
            throw new BusinessRuleException("Дата завершення дії не може бути раніше дати видачі.");

        await using var db = await factory.CreateDbContextAsync(ct);

        // Читання графа старого документа — до транзакції, без відстеження.
        var old = await db.Documents
            .AsNoTracking()
            .Include(d => d.DocumentType)
            .Include(d => d.DocumentTags)
            .SingleOrDefaultAsync(d => d.Id == request.DocumentId, ct)
            ?? throw new BusinessRuleException($"Документ #{request.DocumentId} не знайдено.");

        if (old.ExpiryDate is { } oldExpiry && request.NewExpiryDate <= oldExpiry)
            throw new BusinessRuleException(
                $"Новий строк дії ({request.NewExpiryDate:dd.MM.yyyy}) має бути пізнішим за поточний ({oldExpiry:dd.MM.yyyy}).");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Крок 1: атомарна зміна статусу. 0 рядків → документ уже не чинний (хтось продовжив раніше).
            var affected = await db.Documents
                .Where(d => d.Id == old.Id && d.Status == DocumentStatus.Active)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DocumentStatus.Replaced), ct);
            if (affected == 0)
                throw new BusinessRuleException($"Документ #{old.Id} уже не чинний — продовжити його не можна.");

            // Крок 2: невідправлені нагадування старого документа.
            var removed = await db.Reminders
                .Where(r => r.DocumentId == old.Id && !r.IsSent)
                .ExecuteDeleteAsync(ct);

            // Крок 3: новий документ як граф.
            var renewed = new Document
            {
                OwnerId = old.OwnerId,
                DocumentTypeId = old.DocumentTypeId,
                Number = request.NewNumber,
                Title = old.Title,
                IssueDate = request.NewIssueDate,
                ExpiryDate = request.NewExpiryDate,
                FilePath = request.FilePath ?? old.FilePath,
                Notes = $"Продовження документа #{old.Id}",
                Status = DocumentStatus.Active
            };

            foreach (var tag in old.DocumentTags)
                renewed.DocumentTags.Add(new DocumentTag { TagId = tag.TagId });

            foreach (var days in new[] { old.DocumentType.DefaultReminderDays, 7 }.Distinct())
            {
                var remindOn = request.NewExpiryDate.AddDays(-days);
                if (remindOn >= today)
                    renewed.Reminders.Add(new Reminder { DaysBefore = days, RemindOn = remindOn, Channel = ReminderChannel.Email });
            }

            db.Documents.Add(renewed);
            await db.SaveChangesAsync(ct);   // INSERT Documents → отримує Id → INSERT DocumentTags, Reminders

            await transaction.CommitAsync(ct);
            return new RenewResult(renewed.Id, renewed.Reminders.Count, renewed.DocumentTags.Count, removed);
        }
        catch (Exception ex)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackError)
            {
                logger.LogWarning(rollbackError, "Rollback failed.");
            }

            if (ex is DbUpdateException { InnerException: SqlException { Number: 2627 or 2601 } })
                throw new DuplicateValueException(
                    $"Документ цього типу з номером «{request.NewNumber}» уже існує. Усі зміни скасовано (Rollback).", ex);

            throw;
        }
    }

    // =====================================================================

    private static async Task SaveDocumentChangesAsync(DocumentsDbContext db, string number, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw;   // нащадок DbUpdateException — пропускаємо далі без перетворення
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            throw new DuplicateValueException($"Документ цього типу з номером «{number}» уже існує.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            throw new DataAccessException(
                "Порушено обмеження: неіснуючий власник/тип або дата завершення раніше дати видачі.", ex);
        }
    }
}
