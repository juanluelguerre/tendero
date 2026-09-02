using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// The dispatcher's validation step throws ValidationException; here it is
/// translated into a 400 with problem details. Slices do not write HTTP error codes.
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
