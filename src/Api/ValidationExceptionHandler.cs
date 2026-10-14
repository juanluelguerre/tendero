using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// The dispatcher's validation step throws ValidationException; here it is
/// translated into a 400 with problem details. Slices do not write HTTP error codes.
///
/// The problem is the one every refusal is (phase 13b): the code `Invalid`, the
/// shop's sentence in the language asked, and — what makes it this refusal —
/// the `errors` map, a field to what is wrong with it.
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

        var refusal = CommonRefusals.Invalid;
        var culture = Problems.CultureOf(httpContext);
        httpContext.Response.Headers.ContentLanguage = culture;
        httpContext.Response.Headers.Vary = "Accept-Language";

        var problem = TypedResults.ValidationProblem(
            errors, title: refusal.Title.In(culture), type: refusal.Type,
            extensions: new Dictionary<string, object?> { ["code"] = refusal.Code });
        await problem.ExecuteAsync(httpContext);

        return true;
    }
}
