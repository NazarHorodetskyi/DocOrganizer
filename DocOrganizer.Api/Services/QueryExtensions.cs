using System.Linq.Expressions;
using DocOrganizer.Api.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DocOrganizer.Api.Services;

public static class QueryExtensions
{
    /// <summary>OrderBy/OrderByDescending залежно від напряму.</summary>
    public static IOrderedQueryable<T> OrderByDir<T, TKey>(
        this IQueryable<T> query, Expression<Func<T, TKey>> key, SortDirection dir)
        => dir == SortDirection.Desc ? query.OrderByDescending(key) : query.OrderBy(key);

    /// <summary>
    /// Пагінація на боці БД: COUNT(*) + OFFSET/FETCH. Повертає лише одну сторінку рядків.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PagedQuery paging, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .ToListAsync(ct);
        return PagedResult<T>.Create(items, paging.Page, paging.PageSize, total);
    }

    /// <summary>Те саме, але з перетворенням кожного рядка після матеріалізації.</summary>
    public static async Task<PagedResult<TOut>> ToPagedResultAsync<TIn, TOut>(
        this IQueryable<TIn> query, PagedQuery paging, Func<TIn, TOut> map, CancellationToken ct)
    {
        var page = await query.ToPagedResultAsync(paging, ct);
        return PagedResult<TOut>.Create(page.Items.Select(map).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>Екранування спецсимволів LIKE, щоб пошук «50%» не став шаблоном.</summary>
    public static string ToLikePattern(this string search)
        => "%" + search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
}

/// <summary>Поточна дата (за годинником застосунку). Через TimeProvider — щоб можна було підмінити в тестах.</summary>
public static class TimeProviderExtensions
{
    public static DateOnly Today(this TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}
