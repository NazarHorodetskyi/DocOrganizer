using System.ComponentModel.DataAnnotations;

namespace DocOrganizer.Api.Contracts;

/// <summary>Профіль власника документів (для читання).</summary>
/// <param name="Id">Ідентифікатор профілю.</param>
/// <param name="FullName">ПІБ.</param>
/// <param name="Email">Електронна пошта (унікальна).</param>
/// <param name="BirthDate">Дата народження (необов'язково).</param>
/// <param name="DocumentsCount">Кількість документів профілю.</param>
public sealed record OwnerDto(int Id, string FullName, string Email, DateOnly? BirthDate, int DocumentsCount);

/// <summary>Дані для створення профілю власника.</summary>
public sealed record CreateOwnerDto(
    [Required(ErrorMessage = "ПІБ є обов'язковим.")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "ПІБ має містити від 3 до 150 символів.")]
    string FullName,

    [Required(ErrorMessage = "Email є обов'язковим.")]
    [EmailAddress(ErrorMessage = "Некоректний формат email.")]
    [StringLength(256)]
    string Email,

    DateOnly? BirthDate);

/// <summary>Дані для повної заміни профілю власника (PUT).</summary>
public sealed record UpdateOwnerDto(
    [Required(ErrorMessage = "ПІБ є обов'язковим.")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "ПІБ має містити від 3 до 150 символів.")]
    string FullName,

    [Required(ErrorMessage = "Email є обов'язковим.")]
    [EmailAddress(ErrorMessage = "Некоректний формат email.")]
    [StringLength(256)]
    string Email,

    DateOnly? BirthDate);

/// <summary>Фільтри списку власників. SortBy: fullName | email | birthDate | id.</summary>
public sealed class OwnerQuery : PagedQuery
{
    /// <summary>Пошук за частиною ПІБ або email.</summary>
    [StringLength(100)]
    public string? Search { get; set; }
}
