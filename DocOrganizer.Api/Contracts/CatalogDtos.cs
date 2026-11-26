using System.ComponentModel.DataAnnotations;
using DocOrganizer.Api.Domain;

namespace DocOrganizer.Api.Contracts;

// ───────────── Типи документів ─────────────

/// <summary>Тип документа.</summary>
/// <param name="Id">Ідентифікатор.</param>
/// <param name="Name">Назва (унікальна).</param>
/// <param name="DefaultReminderDays">За скільки днів до завершення нагадувати за замовчуванням.</param>
public sealed record DocumentTypeDto(int Id, string Name, int DefaultReminderDays);

/// <summary>Дані для створення або заміни типу документа.</summary>
public sealed record SaveDocumentTypeDto(
    [Required(ErrorMessage = "Назва є обов'язковою.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Назва має містити від 2 до 100 символів.")]
    string Name,

    [Range(1, 365, ErrorMessage = "Кількість днів має бути від 1 до 365.")]
    int DefaultReminderDays);

/// <summary>Фільтри списку типів. SortBy: name | defaultReminderDays | id.</summary>
public sealed class DocumentTypeQuery : PagedQuery
{
    /// <summary>Пошук за частиною назви.</summary>
    [StringLength(100)]
    public string? Search { get; set; }
}

// ───────────── Мітки ─────────────

/// <summary>Мітка документа.</summary>
/// <param name="Id">Ідентифікатор.</param>
/// <param name="Name">Назва.</param>
public sealed record TagDto(int Id, string Name);

/// <summary>Дані для створення мітки.</summary>
public sealed record CreateTagDto(
    [Required(ErrorMessage = "Назва є обов'язковою.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Назва має містити від 2 до 50 символів.")]
    string Name);

// ───────────── Нагадування ─────────────

/// <summary>Нагадування про завершення строку дії.</summary>
/// <param name="Id">Ідентифікатор.</param>
/// <param name="DocumentId">Документ.</param>
/// <param name="DaysBefore">За скільки днів до завершення дії.</param>
/// <param name="RemindOn">Дата надсилання.</param>
/// <param name="Channel">Канал (Email, Push, Sms).</param>
/// <param name="IsSent">Чи надіслано.</param>
/// <param name="SentAt">Коли надіслано (UTC).</param>
public sealed record ReminderDto(
    int Id, int DocumentId, int DaysBefore, DateOnly RemindOn, ReminderChannel Channel, bool IsSent, DateTime? SentAt);

/// <summary>Дані для створення нагадування.</summary>
public sealed record CreateReminderDto(
    [Range(0, 365, ErrorMessage = "Кількість днів має бути від 0 до 365.")]
    int DaysBefore,

    ReminderChannel Channel = ReminderChannel.Email);

/// <summary>Нагадування, яке настав час надіслати (для робота розсилки).</summary>
/// <param name="ReminderId">Ідентифікатор нагадування.</param>
/// <param name="DocumentId">Документ.</param>
/// <param name="DocumentTitle">Назва документа.</param>
/// <param name="OwnerFullName">ПІБ власника.</param>
/// <param name="OwnerEmail">Email власника.</param>
/// <param name="ExpiryDate">Дата завершення дії.</param>
/// <param name="RemindOn">Дата нагадування.</param>
/// <param name="Channel">Канал.</param>
public sealed record DueReminderDto(
    int ReminderId, int DocumentId, string DocumentTitle, string OwnerFullName, string OwnerEmail,
    DateOnly? ExpiryDate, DateOnly RemindOn, ReminderChannel Channel);

/// <summary>Параметри списку нагадувань до надсилання.</summary>
public sealed class DueReminderQuery : PagedQuery
{
    /// <summary>Дата, на яку шукати ненадіслані нагадування (за замовчуванням — сьогодні).</summary>
    public DateOnly? OnDate { get; set; }
}

// ───────────── Поточний користувач ─────────────

/// <summary>Відомості про автентифікованого користувача, отримані з маркера доступу.</summary>
/// <param name="AccountId">Ідентифікатор (claim sub).</param>
/// <param name="UserName">Ім'я користувача.</param>
/// <param name="Roles">Ролі.</param>
/// <param name="Claims">Усі claims маркера (для налагодження).</param>
public sealed record CurrentUserDto(string AccountId, string? UserName, IReadOnlyList<string> Roles,
    IReadOnlyDictionary<string, string> Claims);
