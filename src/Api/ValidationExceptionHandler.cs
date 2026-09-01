using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// El paso de validación del dispatcher lanza ValidationException; aquí se
/// traduce a 400 con problem details. Los slices no escriben códigos HTTP de error.
/// </summary>
internal sealed class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validationException)
            return false;

        var errors = validationException.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray());

        var problem = TypedResults.ValidationProblem(errors, title: "The request is not valid.");
        await problem.ExecuteAsync(httpContext);

        return true;
    }
}
