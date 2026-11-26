namespace DocOrganizer.Api.Infrastructure;

/// <summary>
/// Базовий виняток предметної області. Глобальний обробник (<see cref="GlobalExceptionHandler"/>)
/// перетворює його на відповідь ProblemDetails з кодом <see cref="StatusCode"/>.
/// Повідомлення цих винятків призначені для клієнта, тож вони потрапляють у detail в усіх середовищах.
/// </summary>
public abstract class DomainException(string title, string message) : Exception(message)
{
    public string Title { get; } = title;
    public abstract int StatusCode { get; }
}

/// <summary>409 Conflict — запит коректний, але суперечить поточному стану (унікальність, паралельне редагування).</summary>
public sealed class ConflictException(string title, string message) : DomainException(title, message)
{
    public override int StatusCode => StatusCodes.Status409Conflict;
}

/// <summary>422 Unprocessable Entity — порушено бізнес-правило (посилання на неіснуючий об'єкт, недопустимий перехід).</summary>
public sealed class BusinessRuleException(string title, string message) : DomainException(title, message)
{
    public override int StatusCode => StatusCodes.Status422UnprocessableEntity;
}

/// <summary>404 Not Found — ресурс не існує.</summary>
public sealed class NotFoundException(string resource, object id)
    : DomainException("Ресурс не знайдено", $"{resource} з Id={id} не існує.")
{
    public override int StatusCode => StatusCodes.Status404NotFound;
}
