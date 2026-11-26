using DocOrganizer.DataLayer.Domain;

namespace DocOrganizer.DataLayer.DataAccess;

// ===================== Вхідні моделі =====================

public sealed record OwnerInput(string FullName, string Email, DateOnly? BirthDate);

public sealed record DocumentInput(
    int OwnerId,
    int DocumentTypeId,
    string Number,
    string Title,
    DateOnly IssueDate,
    DateOnly? ExpiryDate,
    string? FilePath,
    string? Notes);

/// <summary>Фільтр списку документів. Усі параметри необов'язкові.</summary>
public sealed record DocumentSearchFilter(
    int? OwnerId = null,
    int? DocumentTypeId = null,
    DocumentStatus? Status = null,
    int? TagId = null,
    int? ExpiringWithinDays = null,
    bool IncludeExpired = true,
    string? Search = null);

/// <summary>Запит на продовження (заміну) документа новим.</summary>
public sealed record RenewDocumentRequest(
    int DocumentId,
    string NewNumber,
    DateOnly NewIssueDate,
    DateOnly NewExpiryDate,
    string? FilePath = null);

public enum DocumentSort
{
    ExpiryDate,
    Title,
    Owner
}

// ===================== DTO результатів =====================

public sealed record OwnerDto(int Id, string FullName, string Email, DateOnly? BirthDate);

public sealed record OwnerSummaryDto(int Id, string FullName, string Email, int DocumentCount, int ActiveCount);

public sealed record DocumentDetailsDto(
    int Id,
    int OwnerId,
    string OwnerName,
    int DocumentTypeId,
    string TypeName,
    string Number,
    string Title,
    DateOnly IssueDate,
    DateOnly? ExpiryDate,
    DocumentStatus Status,
    string? FilePath,
    string? Notes);

public sealed record DocumentListItemDto(
    int Id,
    string Title,
    string Number,
    string OwnerName,
    string TypeName,
    DateOnly? ExpiryDate,
    DocumentStatus Status);

public sealed record ReminderDto(int Id, int DocumentId, int DaysBefore, DateOnly RemindOn, ReminderChannel Channel, bool IsSent);

/// <summary>Статистика за типами документів (ADO.NET, GROUP BY).</summary>
public sealed record TypeExpiryStatsDto(
    int TypeId,
    string TypeName,
    int ActiveCount,
    int ExpiredCount,
    int ExpiringSoonCount,
    DateOnly? NearestExpiry);

/// <summary>Статистика за власниками (EF Core, GroupBy).</summary>
public sealed record OwnerStatsDto(
    int OwnerId,
    string OwnerName,
    int ActiveCount,
    int ExpiredCount,
    int ExpiringSoonCount,
    DateOnly? EarliestExpiry);

public sealed record ExpiryCalendarItemDto(int Year, int Month, int Count);

public sealed record RenewResult(
    int NewDocumentId,
    int RemindersCreated,
    int TagsCopied,
    int UnsentRemindersRemoved);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public enum DeleteResult
{
    Deleted,
    NotFound,
    HasDependencies
}

// ===================== Доменні винятки шару доступу до даних =====================

public class DataAccessException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Порушення UNIQUE (SQL Server 2627 / 2601).</summary>
public sealed class DuplicateValueException(string message, Exception? innerException = null)
    : DataAccessException(message, innerException);

/// <summary>Порушення бізнес-правила (перевіряється в коді).</summary>
public sealed class BusinessRuleException(string message)
    : DataAccessException(message);

/// <summary>Конфлікт оптимістичної конкуренції (rowversion змінився).</summary>
public sealed class ConcurrencyConflictException(string message, Exception? innerException = null)
    : DataAccessException(message, innerException);
