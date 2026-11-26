using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DocOrganizer.Api.Infrastructure;

/// <summary>
/// Атрибут [Produces("application/json")] на контролері перезаписує Content-Type для ВСІХ відповідей,
/// зокрема для помилок. Цей фільтр виконується після нього (Order = int.MaxValue) і для тіл
/// ProblemDetails / ValidationProblemDetails повертає стандартний тип application/problem+json (RFC 9457).
/// </summary>
public sealed class ProblemDetailsContentTypeFilter : IResultFilter, IOrderedFilter
{
    public int Order => int.MaxValue;

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails } result)
        {
            result.ContentTypes.Clear();
            result.ContentTypes.Add("application/problem+json");
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
