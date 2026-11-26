using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DocOrganizer.Api.Infrastructure;

/// <summary>
/// Глобальний обробник винятків (IExceptionHandler, .NET 8+). Перетворює відомі винятки
/// на коди стану, а решту — на 500 БЕЗ внутрішніх деталей. Текст непередбачених винятків
/// додається в detail лише в середовищі Development.
/// Реєструється AddExceptionHandler — Singleton (стану не має).
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment env,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            // Винятки предметної області: повідомлення сформоване для клієнта — показуємо завжди.
            DomainException e => (e.StatusCode, e.Title, e.Message),

            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Конфлікт паралельного редагування",
                "Запис змінив або видалив інший користувач. Отримайте актуальну версію і повторіть."),

            // Запасний захист від гонки: дві паралельні вставки пройшли перевірку AnyAsync,
            // але унікальний індекс у БД відхилив другу (SQL Server 2601/2627).
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
                (StatusCodes.Status409Conflict, "Порушення унікальності", "Запис із такими даними вже існує."),

            DbUpdateException { InnerException: SqlException { Number: 547 } } =>
                (StatusCodes.Status409Conflict, "Порушення цілісності даних",
                 "Операція суперечить зв'язкам між записами (обмеження FOREIGN KEY або CHECK)."),

            BadHttpRequestException e => (e.StatusCode, "Некоректний запит", e.Message),

            FormatException => (StatusCodes.Status400BadRequest, "Некоректний формат даних",
                "Одне з полів має неправильний формат."),

            OperationCanceledException when context.RequestAborted.IsCancellationRequested =>
                (499, "Запит скасовано клієнтом", null),

            _ => (StatusCodes.Status500InternalServerError, "Внутрішня помилка сервера",
                  env.IsDevelopment() ? exception.ToString() : null)
        };

        if (status >= 500)
            logger.LogError(exception, "Необроблений виняток під час {Method} {Path}",
                context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("Запит завершено з кодом {Status}: {Message}", status, exception.Message);

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Detail = detail,
                Type = status switch
                {
                    400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                    404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                    409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                    422 => "https://tools.ietf.org/html/rfc9110#section-15.5.21",
                    _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1"
                }
            }
        });
    }
}
