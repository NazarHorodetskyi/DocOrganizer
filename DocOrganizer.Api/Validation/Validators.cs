using System.Text.RegularExpressions;
using FluentValidation;
using DocOrganizer.Api.Contracts;
using DocOrganizer.Api.Services;

namespace DocOrganizer.Api.Validation;

// Валідатори FluentValidation доповнюють атрибути DataAnnotations правилами,
// які залежать від кількох полів або від поточної дати.
// Стану не мають — реєструються як Singleton.

public sealed partial class CreateDocumentDtoValidator : AbstractValidator<CreateDocumentDto>
{
    public CreateDocumentDtoValidator(TimeProvider time)
    {
        RuleFor(x => x.Number)
            .Matches(NumberRegex()).WithMessage("Номер може містити лише літери, цифри, пробіл, '-' та '/'.");

        RuleFor(x => x.IssueDate)
            .GreaterThanOrEqualTo(new DateOnly(1900, 1, 1)).WithMessage("Дата видачі не може бути раніше 1900 року.")
            .Must(d => d <= time.Today()).WithMessage("Дата видачі не може бути в майбутньому.");

        RuleFor(x => x.ExpiryDate)
            .GreaterThanOrEqualTo(x => x.IssueDate)
            .When(x => x.ExpiryDate is not null)
            .WithMessage("Дата завершення дії не може бути раніше дати видачі.");

        RuleFor(x => x.TagIds)
            .Must(ids => ids!.Length <= 10).WithMessage("Не більше 10 міток на документ.")
            .Must(ids => ids!.All(id => id > 0)).WithMessage("Ідентифікатори міток мають бути додатними.")
            .When(x => x.TagIds is not null);
    }

    [GeneratedRegex(@"^[\p{L}\p{Nd}\-/ ]+$")]
    internal static partial Regex NumberRegex();
}

public sealed class UpdateDocumentDtoValidator : AbstractValidator<UpdateDocumentDto>
{
    public UpdateDocumentDtoValidator(TimeProvider time)
    {
        RuleFor(x => x.Number)
            .Matches(CreateDocumentDtoValidator.NumberRegex())
            .WithMessage("Номер може містити лише літери, цифри, пробіл, '-' та '/'.");

        RuleFor(x => x.IssueDate)
            .GreaterThanOrEqualTo(new DateOnly(1900, 1, 1)).WithMessage("Дата видачі не може бути раніше 1900 року.")
            .Must(d => d <= time.Today()).WithMessage("Дата видачі не може бути в майбутньому.");

        RuleFor(x => x.ExpiryDate)
            .GreaterThanOrEqualTo(x => x.IssueDate)
            .When(x => x.ExpiryDate is not null)
            .WithMessage("Дата завершення дії не може бути раніше дати видачі.");

        RuleFor(x => x.Status).IsInEnum();

        RuleFor(x => x.TagIds)
            .Must(ids => ids!.Length <= 10).WithMessage("Не більше 10 міток на документ.")
            .When(x => x.TagIds is not null);

        RuleFor(x => x.Version)
            .Must(BeRowVersion).WithMessage("Version має бути значенням, отриманим у GET (base64, 8 байтів).");
    }

    private static bool BeRowVersion(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        Span<byte> buffer = stackalloc byte[16];
        return Convert.TryFromBase64String(value, buffer, out var written) && written == 8;
    }
}

public sealed class RenewDocumentDtoValidator : AbstractValidator<RenewDocumentDto>
{
    public RenewDocumentDtoValidator(TimeProvider time)
    {
        RuleFor(x => x.NewNumber)
            .Matches(CreateDocumentDtoValidator.NumberRegex())
            .WithMessage("Номер може містити лише літери, цифри, пробіл, '-' та '/'.");
        RuleFor(x => x.IssueDate)
            .Must(d => d <= time.Today()).WithMessage("Дата видачі не може бути в майбутньому.");
        RuleFor(x => x.ExpiryDate)
            .GreaterThan(x => x.IssueDate)
            .When(x => x.ExpiryDate is not null)
            .WithMessage("Дата завершення дії має бути пізніше дати видачі.");
    }
}

public sealed class CreateOwnerDtoValidator : AbstractValidator<CreateOwnerDto>
{
    public CreateOwnerDtoValidator(TimeProvider time)
    {
        RuleFor(x => x.BirthDate)
            .Must(d => d <= time.Today()).WithMessage("Дата народження не може бути в майбутньому.")
            .GreaterThan(new DateOnly(1900, 1, 1)).WithMessage("Дата народження не може бути раніше 1900 року.")
            .When(x => x.BirthDate is not null);
    }
}

public sealed class UpdateOwnerDtoValidator : AbstractValidator<UpdateOwnerDto>
{
    public UpdateOwnerDtoValidator(TimeProvider time)
    {
        RuleFor(x => x.BirthDate)
            .Must(d => d <= time.Today()).WithMessage("Дата народження не може бути в майбутньому.")
            .GreaterThan(new DateOnly(1900, 1, 1)).WithMessage("Дата народження не може бути раніше 1900 року.")
            .When(x => x.BirthDate is not null);
    }
}

public sealed class ExpiryCalendarQueryValidator : AbstractValidator<ExpiryCalendarQuery>
{
    public ExpiryCalendarQueryValidator()
    {
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From is not null && x.To is not null)
            .WithMessage("Дата 'to' не може бути раніше за 'from'.");
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.To.Value.DayNumber - x.From.Value.DayNumber <= 366 * 2)
            .WithName("To")
            .WithMessage("Період календаря не може перевищувати 2 роки.");
    }
}
