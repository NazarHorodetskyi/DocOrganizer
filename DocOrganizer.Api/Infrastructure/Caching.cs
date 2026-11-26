using DocOrganizer.Api.Auth;
using DocOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;

namespace DocOrganizer.Api.Infrastructure;

public static class CachePolicies
{
    /// <summary>Політика кешу для захищених GET: ключ кешу враховує користувача (sub) і всі параметри запиту.</summary>
    public const string PerUser = nameof(PerUser);

    public const string DocumentsTag = "documents";
    public const string OwnersTag = "owners";
    public const string CatalogTag = "catalog";
}

/// <summary>
/// Стандартна політика OutputCache НЕ кешує запити із заголовком Authorization (щоб не віддати
/// чужі дані). Ця політика дозволяє кеш для автентифікованих запитів, але робить ключ кешу
/// залежним від claim sub — кожен користувач має власну копію відповіді.
/// Кеш вмикається лише при Performance:Optimized = true (для порівняння «до / після»).
/// </summary>
public sealed class PerUserOutputCachePolicy : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken ct)
    {
        var http = context.HttpContext;
        var optimized = http.RequestServices.GetRequiredService<IOptionsMonitor<PerformanceOptions>>().CurrentValue.Optimized;
        var isRead = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);
        var user = http.User.FindFirst(AuthConstants.SubjectClaim)?.Value;

        var allow = optimized && isRead && user is not null;
        context.EnableOutputCaching = true;
        context.AllowCacheLookup = allow;
        context.AllowCacheStorage = allow;
        context.AllowLocking = true;
        context.CacheVaryByRules.QueryKeys = "*";
        if (user is not null)
            context.CacheVaryByRules.VaryByValues["sub"] = user;
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken ct)
    {
        // Кешуємо лише успішні відповіді.
        if (context.HttpContext.Response.StatusCode != StatusCodes.Status200OK)
            context.AllowCacheStorage = false;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Після успішної операції запису (2xx) видаляє з OutputCache записи з указаними тегами,
/// щоб наступний GET не повернув застарілі дані.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EvictCacheAttribute(params string[] tags) : Attribute, IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var executed = await next();
        var status = executed.HttpContext.Response.StatusCode;
        if (status is >= 200 and < 300)
        {
            var store = context.HttpContext.RequestServices.GetRequiredService<IOutputCacheStore>();
            foreach (var tag in tags)
                await store.EvictByTagAsync(tag, context.HttpContext.RequestAborted);
        }
    }
}
