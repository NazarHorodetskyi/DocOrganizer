namespace DocOrganizer.Api.Domain;

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

/// <summary>
/// Власник документів (член сім'ї). Кожен профіль належить обліковому запису користувача
/// постачальника OpenID Connect — <see cref="AccountId"/> містить claim <c>sub</c>.
/// Один акаунт може вести кілька профілів (себе, дітей, батьків).
/// </summary>
public sealed class Owner
{
    public int Id { get; set; }

    /// <summary>Ідентифікатор користувача OIDC (claim sub), якому належить профіль. Основа resource-based авторизації.</summary>
    public string AccountId { get; set; } = "";

    public required string FullName { get; set; }
    public required string Email { get; set; }          // UNIQUE
    public DateOnly? BirthDate { get; set; }

    public ICollection<Document> Documents { get; set; } = [];
}

/// <summary>Довідник типів документів (паспорт, віза, страховка…). Керує лише адміністратор.</summary>
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
    public string? IssuedBy { get; set; }
    public string? FilePath { get; set; }
    public string? Notes { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Active;
    public DateTime CreatedAt { get; set; }             // DEFAULT SYSUTCDATETIME()

    /// <summary>Concurrency token (SQL Server rowversion) — захист від втрачених оновлень, 409 Conflict.</summary>
    public byte[] Version { get; set; } = [];

    public ICollection<Reminder> Reminders { get; set; } = [];
    public ICollection<DocumentTag> DocumentTags { get; set; } = [];
}

/// <summary>Нагадування про завершення строку дії (1 документ — багато нагадувань).</summary>
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

/// <summary>Мітка (тег) документа: «Подорожі», «Авто» тощо.</summary>
public sealed class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }           // UNIQUE

    public ICollection<DocumentTag> DocumentTags { get; set; } = [];
}

/// <summary>Проміжна сутність зв'язку багато-до-багатьох Documents ↔ Tags.</summary>
public sealed class DocumentTag
{
    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public DateTime AddedAt { get; set; }
}

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

public static class TagIds
{
    public const int Personal = 1;
    public const int Travel = 2;
    public const int Car = 3;
    public const int Work = 4;
    public const int Finance = 5;
    public const int Urgent = 6;
}

/// <summary>Обчислюваний стан строку дії документа (не зберігається в БД).</summary>
public enum ExpiryState
{
    /// <summary>Строк дії не обмежений.</summary>
    Perpetual,
    /// <summary>Строк дії спливає не раніше ніж через <see cref="ExpiryRules.SoonDays"/> днів.</summary>
    Valid,
    /// <summary>Спливає протягом найближчих <see cref="ExpiryRules.SoonDays"/> днів.</summary>
    ExpiringSoon,
    /// <summary>Строк дії минув.</summary>
    Expired,
    /// <summary>Документ неактивний (замінений, анульований, в архіві).</summary>
    Inactive
}

public static class ExpiryRules
{
    public const int SoonDays = 30;

    public static ExpiryState GetState(DateOnly? expiry, DocumentStatus status, DateOnly today)
    {
        if (status != DocumentStatus.Active) return ExpiryState.Inactive;
        if (expiry is null) return ExpiryState.Perpetual;
        var days = expiry.Value.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => ExpiryState.Expired,
            <= SoonDays => ExpiryState.ExpiringSoon,
            _ => ExpiryState.Valid
        };
    }

    public static int? DaysLeft(DateOnly? expiry, DateOnly today)
        => expiry is null ? null : expiry.Value.DayNumber - today.DayNumber;
}
