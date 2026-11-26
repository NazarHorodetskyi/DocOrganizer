using DocOrganizer.Api.Contracts;
using DocOrganizer.Api.Data;
using DocOrganizer.Api.Domain;
using DocOrganizer.Api.Infrastructure;
using DocOrganizer.Api.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DocOrganizer.Api.Services;

public interface IReminderService
{
    Task<IReadOnlyList<ReminderDto>> GetForDocumentAsync(int documentId, CancellationToken ct);
    Task<ReminderDto?> GetByIdAsync(int documentId, int reminderId, CancellationToken ct);
    Task<ReminderDto> CreateAsync(int documentId, CreateReminderDto dto, CancellationToken ct);
    Task<bool> DeleteAsync(int documentId, int reminderId, CancellationToken ct);
    Task<PagedResult<DueReminderDto>> GetDueAsync(DueReminderQuery query, CancellationToken ct);
    Task<bool> MarkSentAsync(int reminderId, CancellationToken ct);
}

public sealed class ReminderService(AppDbContext db, ApiMapper mapper, TimeProvider time) : IReminderService
{
    public async Task<IReadOnlyList<ReminderDto>> GetForDocumentAsync(int documentId, CancellationToken ct)
        => await mapper.ProjectToDto(db.Reminders.AsNoTracking()
                .Where(r => r.DocumentId == documentId)
                .OrderBy(r => r.RemindOn))
            .ToListAsync(ct);

    public Task<ReminderDto?> GetByIdAsync(int documentId, int reminderId, CancellationToken ct)
        => mapper.ProjectToDto(db.Reminders.AsNoTracking()
                .Where(r => r.Id == reminderId && r.DocumentId == documentId))
            .FirstOrDefaultAsync(ct);

    public async Task<ReminderDto> CreateAsync(int documentId, CreateReminderDto dto, CancellationToken ct)
    {
        var document = await db.Documents.FindAsync([documentId], ct)
            ?? throw new NotFoundException("Документ", documentId);

        // Бізнес-правила (422): нагадування має сенс лише для чинного документа з датою завершення.
        if (document.ExpiryDate is not { } expiry)
            throw new BusinessRuleException("Документ безстроковий",
                "Для документа без дати завершення дії нагадування створити неможливо.");
        if (document.Status != DocumentStatus.Active)
            throw new BusinessRuleException("Документ неактивний",
                $"Документ має статус {document.Status}; нагадування створюються лише для активних документів.");

        var remindOn = expiry.AddDays(-dto.DaysBefore);
        if (remindOn < time.Today())
            throw new BusinessRuleException("Дата нагадування в минулому",
                $"Нагадування за {dto.DaysBefore} дн. припадало б на {remindOn:yyyy-MM-dd}, а ця дата вже минула.");

        // Унікальність (409): одне нагадування з тим самим відступом на документ.
        if (await db.Reminders.AnyAsync(r => r.DocumentId == documentId && r.DaysBefore == dto.DaysBefore, ct))
            throw new ConflictException("Нагадування вже існує",
                $"Для цього документа вже є нагадування за {dto.DaysBefore} дн. до завершення дії.");

        var reminder = new Reminder
        {
            DocumentId = documentId, DaysBefore = dto.DaysBefore, RemindOn = remindOn, Channel = dto.Channel
        };
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(ct);
        return mapper.ToDto(reminder);
    }

    public async Task<bool> DeleteAsync(int documentId, int reminderId, CancellationToken ct)
        => await db.Reminders.Where(r => r.Id == reminderId && r.DocumentId == documentId).ExecuteDeleteAsync(ct) > 0;

    public Task<PagedResult<DueReminderDto>> GetDueAsync(DueReminderQuery q, CancellationToken ct)
    {
        var onDate = q.OnDate ?? time.Today();

        // Використовує індекс IX_Reminders_IsSent_RemindOn.
        var query = db.Reminders.AsNoTracking()
            .Where(r => !r.IsSent && r.RemindOn <= onDate && r.Document.Status == DocumentStatus.Active);

        var ordered = (q.SortBy?.ToLowerInvariant()) switch
        {
            "expirydate" => query.OrderByDir(r => r.Document.ExpiryDate, q.SortDir),
            _ => query.OrderByDir(r => r.RemindOn, q.SortDir)
        };

        return ordered.ThenBy(r => r.Id)
            .Select(r => new DueReminderDto(r.Id, r.DocumentId, r.Document.Title, r.Document.Owner.FullName,
                r.Document.Owner.Email, r.Document.ExpiryDate, r.RemindOn, r.Channel))
            .ToPagedResultAsync(q, ct);
    }

    public async Task<bool> MarkSentAsync(int reminderId, CancellationToken ct)
    {
        var reminder = await db.Reminders.FindAsync([reminderId], ct);
        if (reminder is null) return false;
        if (reminder.IsSent)
            throw new ConflictException("Нагадування вже надіслано",
                $"Нагадування Id={reminderId} позначено надісланим {reminder.SentAt:u}.");

        reminder.IsSent = true;
        reminder.SentAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
