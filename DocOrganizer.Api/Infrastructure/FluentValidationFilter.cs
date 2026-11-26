using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DocOrganizer.Api.Infrastructure;

/// <summary>
/// Глобальний фільтр дій: для кожного аргументу дії шукає зареєстрований IValidator&lt;T&gt;
/// (FluentValidation) і, якщо є помилки, повертає 400 ValidationProblemDetails.
/// Пакет FluentValidation.AspNetCore з автоматичною валідацією більше не підтримується,
/// тому валідатори викликаються явно — тут, в одному місці, а не в кожній дії.
/// Атрибути DataAnnotations перевіряє [ApiController] ще до цього фільтра.
/// </summary>
public sealed class FluentValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var (name, argument) in context.ActionArguments)
        {
            if (argument is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);

            foreach (var error in result.Errors)
                context.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        if (!context.ModelState.IsValid && context.Controller is ControllerBase controller)
        {
            context.Result = controller.ValidationProblem(context.ModelState);
            return;
        }

        await next();
    }
}
