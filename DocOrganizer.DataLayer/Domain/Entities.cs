namespace DocOrganizer.DataLayer.Domain;

/// <summary>Стан життєвого циклу документа. У БД зберігається рядком (nvarchar(20)).</summary>
public enum DocumentStatus
{
    Active,
    Replaced,
    Cancelled,
    Archived
}

/// <summary>Канал доставки нагадування.</summary>
public enum ReminderChannel
{
    Email,
    Push,
    Sms
}

public sealed class Owner
{
    public int Id { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }          // UNIQUE
    public DateOnly? BirthDate { get; set; }            // необов'язкове поле

    public ICollection<Document> Documents { get; set; } = [];
}

public sealed class DocumentType
{
    public int Id { get; set; }
    public required string Name { get; set; }           // UNIQUE
    public int DefaultReminderDays { get; set; }

    public ICollection<Document> Documents { get; set; } = [];
}

public sealed class Document
{
    public int Id { get; set; }

    public int OwnerId { get; set; }
    public Owner Owner { get; set; } = null!;

    public int DocumentTypeId { get; set; }
    public DocumentType DocumentType { get; set; } = null!;

    public required string Number { get; set; }
    public required string Title { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? FilePath { get; set; }
    public string? Notes { get; set; }

    /// Друга міграція
    public string? IssuedBy { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Active;
    public DateTime CreatedAt { get; set; }             // DEFAULT SYSUTCDATETIME()

    /// Concurrency token SQL Server rowversion
    public byte[] Version { get; set; } = [];

    public ICollection<Reminder> Reminders { get; set; } = [];
    public ICollection<DocumentTag> DocumentTags { get; set; } = [];
}

/// <summary>Нагадування про завершення строку дії. Залежна таблиця (1 документ — багато нагадувань).</summary>
public sealed class Reminder
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public int DaysBefore { get; set; }
    public DateOnly RemindOn { get; set; }
    public ReminderChannel Channel { get; set; }
    public bool IsSent { get; set; }
    public DateTime? SentAt { get; set; }
}

/// <summary>Мітка (тег) документа, наприклад «Подорожі», «Авто». Довідник (HasData).</summary>
public sealed class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }           // UNIQUE

    public ICollection<DocumentTag> DocumentTags { get; set; } = [];
}

/// <summary>
/// Явна проміжна сутність зв'язку багато-до-багатьох Documents ↔ Tags.
/// Складений ключ (DocumentId, TagId) + власний атрибут AddedAt.
/// </summary>
public sealed class DocumentTag
{
    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public DateTime AddedAt { get; set; }               // DEFAULT SYSUTCDATETIME()
}

/// <summary>Сталі Id довідника типів документів (збігаються з HasData).</summary>
public static class DocumentTypeIds
{
    public const int IdCard = 1;
    public const int ForeignPassport = 2;
    public const int Visa = 3;
    public const int DriverLicense = 4;
    public const int MedicalInsurance = 5;
    public const int CarInsurance = 6;
    public const int Certificate = 7;
    public const int Contract = 8;
}

/// <summary>Сталі Id довідника тегів (збігаються з HasData).</summary>
public static class TagIds
{
    public const int Personal = 1;
    public const int Travel = 2;
    public const int Car = 3;
    public const int Work = 4;
    public const int Finance = 5;
    public const int Urgent = 6;
}

/// <summary>Обчислюваний «стан строку дії» для відображення та фільтрів.</summary>
public static class ExpiryInfo
{
    public const int SoonDays = 30;

    public static string Describe(DateOnly? expiry, DocumentStatus status, DateOnly today)
    {
        if (status != DocumentStatus.Active)
        {
            return status switch
            {
                DocumentStatus.Replaced => "замінений",
                DocumentStatus.Cancelled => "анульований",
                DocumentStatus.Archived => "в архіві",
                _ => status.ToString()
            };
        }

        if (expiry is null)
            return "безстроковий";

        var days = expiry.Value.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => $"ПРОСТРОЧЕНО ({-days} дн.)",
            0 => "спливає СЬОГОДНІ",
            <= SoonDays => $"спливає через {days} дн.",
            _ => "чинний"
        };
    }
}