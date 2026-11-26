using System.ComponentModel.DataAnnotations;
using DocOrganizer.Api.Domain;

namespace DocOrganizer.Api.Contracts;

/// <summary>Документ (повне представлення для читання).</summary>
/// <param name="Id">Ідентифікатор документа.</param>
/// <param name="OwnerId">Ідентифікатор власника.</param>
/// <param name="OwnerFullName">ПІБ власника.</param>
/// <param name="DocumentTypeId">Ідентифікатор типу документа.</param>
/// <param name="DocumentTypeName">Назва типу документа.</param>
/// <param name="Number">Номер документа.</param>
/// <param name="Title">Назва.</param>
/// <param name="IssueDate">Дата видачі.</param>
/// <param name="ExpiryDate">Дата завершення дії (null — безстроковий).</param>
/// <param name="IssuedBy">Ким видано.</param>
/// <param name="Status">Стан документа.</param>
/// <param name="ExpiryState">Обчислений стан строку дії.</param>
/// <param name="DaysLeft">Днів до завершення дії (від'ємне — прострочено).</param>
/// <param name="FilePath">Шлях до файлу скан-копії.</param>
/// <param name="Notes">Примітки.</param>
/// <param name="Tags">Назви міток.</param>
/// <param name="CreatedAt">Час створення запису (UTC).</param>
/// <param name="Version">Маркер версії (base64 від rowversion) — передається в PUT для оптимістичного блокування.</param>
public sealed record DocumentDto(
    int Id,
    int OwnerId,
    string OwnerFullName,
    int DocumentTypeId,
    string DocumentTypeName,
    string Number,
    string Title,
    DateOnly IssueDate,
    DateOnly? ExpiryDate,
    string? IssuedBy,
    DocumentStatus Status,
    ExpiryState ExpiryState,
    int? DaysLeft,
    string? FilePath,
    string? Notes,
    IReadOnlyList<string> Tags,
    DateTime CreatedAt,
    string Version);

/// <summary>Документ у списку (скорочене представлення).</summary>
/// <param name="Id">Ідентифікатор.</param>
/// <param name="OwnerId">Ідентифікатор власника.</param>
/// <param name="OwnerFullName">ПІБ власника.</param>
/// <param name="DocumentTypeName">Назва типу.</param>
/// <param name="Number">Номер.</param>
/// <param name="Title">Назва.</param>
/// <param name="ExpiryDate">Дата завершення дії.</param>
/// <param name="Status">Стан документа.</param>
/// <param name="ExpiryState">Обчислений стан строку дії.</param>
/// <param name="DaysLeft">Днів до завершення дії.</param>
public sealed record DocumentListItemDto(
    int Id,
    int OwnerId,
    string OwnerFullName,
    string DocumentTypeName,
    string Number,
    string Title,
    DateOnly? ExpiryDate,
    DocumentStatus Status,
    ExpiryState ExpiryState,
    int? DaysLeft);

/// <summary>Дані для створення документа.</summary>
public sealed record CreateDocumentDto(
    [Range(1, int.MaxValue, ErrorMessage = "Потрібно вказати власника.")]
    int OwnerId,

    [Range(1, int.MaxValue, ErrorMessage = "Потрібно вказати тип документа.")]
    int DocumentTypeId,

    [Required(ErrorMessage = "Номер документа є обов'язковим.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Номер має містити від 2 до 50 символів.")]
    string Number,

    [Required(ErrorMessage = "Назва є обов'язковою.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Назва має містити від 2 до 200 символів.")]
    string Title,

    DateOnly IssueDate,
    DateOnly? ExpiryDate,

    [StringLength(200)] string? IssuedBy,
    [StringLength(500)] string? FilePath,
    [StringLength(1000)] string? Notes,

    int[]? TagIds,

    // true — створити нагадування за DefaultReminderDays типу документа
    bool CreateDefaultReminder = true);

/// <summary>Дані для повної заміни документа (PUT). Version обов'язковий — оптимістичне блокування.</summary>
public sealed record UpdateDocumentDto(
    [Range(1, int.MaxValue, ErrorMessage = "Потрібно вказати власника.")]
    int OwnerId,

    [Range(1, int.MaxValue, ErrorMessage = "Потрібно вказати тип документа.")]
    int DocumentTypeId,

    [Required(ErrorMessage = "Номер документа є обов'язковим.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Номер має містити від 2 до 50 символів.")]
    string Number,

    [Required(ErrorMessage = "Назва є обов'язковою.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Назва має містити від 2 до 200 символів.")]
    string Title,

    DateOnly IssueDate,
    DateOnly? ExpiryDate,
    [StringLength(200)] string? IssuedBy,
    [StringLength(500)] string? FilePath,
    [StringLength(1000)] string? Notes,
    DocumentStatus Status,
    int[]? TagIds,

    [Required(ErrorMessage = "Потрібно передати Version, отриманий у GET.")]
    string Version);

/// <summary>Дані для продовження (заміни) документа новим: старий отримує статус Replaced.</summary>
public sealed record RenewDocumentDto(
    [Required(ErrorMessage = "Номер нового документа є обов'язковим.")]
    [StringLength(50, MinimumLength = 2)]
    string NewNumber,

    DateOnly IssueDate,
    DateOnly? ExpiryDate,
    [StringLength(200)] string? IssuedBy);

/// <summary>
/// Фільтри списку документів.
/// SortBy: expiryDate | issueDate | title | number | createdAt | id.
/// </summary>
public sealed class DocumentQuery : PagedQuery
{
    /// <summary>Фільтр за власником.</summary>
    public int? OwnerId { get; set; }

    /// <summary>Фільтр за типом документа.</summary>
    public int? DocumentTypeId { get; set; }

    /// <summary>Фільтр за станом документа (Active, Replaced, Cancelled, Archived).</summary>
    public DocumentStatus? Status { get; set; }

    /// <summary>Фільтр за станом строку дії (Perpetual, Valid, ExpiringSoon, Expired, Inactive).</summary>
    public ExpiryState? ExpiryState { get; set; }

    /// <summary>Фільтр за міткою (Id).</summary>
    public int? TagId { get; set; }

    /// <summary>Пошук за частиною назви або номера.</summary>
    [StringLength(100)]
    public string? Search { get; set; }
}

/// <summary>Параметри календаря завершення строків дії.</summary>
public sealed class ExpiryCalendarQuery : PagedQuery
{
    /// <summary>Початок періоду (за замовчуванням — сьогодні).</summary>
    public DateOnly? From { get; set; }

    /// <summary>Кінець періоду (за замовчуванням — From + 90 днів).</summary>
    public DateOnly? To { get; set; }

    /// <summary>Фільтр за власником.</summary>
    public int? OwnerId { get; set; }
}
