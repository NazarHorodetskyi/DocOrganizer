using System.ComponentModel.DataAnnotations;

namespace DocOrganizer.Api.Contracts;

/// <summary>Напрям сортування.</summary>
public enum SortDirection
{
    Asc,
    Desc
}

/// <summary>Базові параметри пагінації й сортування для кінцевих точок, що повертають колекції.</summary>
public abstract class PagedQuery
{
    /// <summary>Номер сторінки (від 1).</summary>
    [Range(1, 100_000, ErrorMessage = "Номер сторінки має бути від 1 до 100000.")]
    public int Page { get; set; } = 1;

    /// <summary>Розмір сторінки (1–100).</summary>
    [Range(1, 100, ErrorMessage = "Розмір сторінки має бути від 1 до 100.")]
    public int PageSize { get; set; } = 20;

    /// <summary>Поле сортування (перелік допустимих значень — в описі конкретної кінцевої точки).</summary>
    [StringLength(30)]
    public string? SortBy { get; set; }

    /// <summary>Напрям сортування: Asc або Desc.</summary>
    public SortDirection SortDir { get; set; } = SortDirection.Asc;
}

/// <summary>Сторінка результатів разом із метаданими пагінації.</summary>
/// <typeparam name="T">Тип елемента.</typeparam>
/// <param name="Items">Елементи поточної сторінки.</param>
/// <param name="Page">Номер поточної сторінки.</param>
/// <param name="PageSize">Розмір сторінки.</param>
/// <param name="TotalCount">Загальна кількість елементів, що відповідають фільтру.</param>
/// <param name="TotalPages">Загальна кількість сторінок.</param>
/// <param name="HasPrevious">Чи існує попередня сторінка.</param>
/// <param name="HasNext">Чи існує наступна сторінка.</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext)
{
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PagedResult<T>(items, page, pageSize, totalCount, totalPages, page > 1, page < totalPages);
    }
}
