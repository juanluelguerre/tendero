using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// The contract's version (phase 13b): a date a client can name and compare,
/// in the document's `info.version` and repeated by every answer in
/// <see cref="Header"/>. No negotiation — there is one version. The day there
/// are two, the header is where a client says which it speaks.
/// </summary>
internal static class ContractVersion
{
    public const string Current = "2026-10-13";

    public const string Header = "Tendero-Version";
}

/// <summary>
/// **A refusal the pipeline answers is a problem too** (RFC 9457). Nobody signed
/// in, somebody who may not, an address that names nothing, a limit passed: each
/// used to be a status and an empty body, which tells a client that something
/// went wrong and nothing it can branch on or show. Here each gains the shape
/// every other refusal has: a `code`, the shop's sentence in the language asked,
/// and `Content-Language` saying which.
///
/// It is ASP.NET's own mechanism — <c>AddProblemDetails</c> with status-code
/// pages — customised in one place, so a slice does nothing to get it.
/// </summary>
internal static class Problems
{
    public const string ContentType = "application/problem+json";

    /// <summary>
    /// Every problem the API writes passes here, and leaves as one shape.
    ///
    /// A slice's own names its refusal by code and hands over what it knows —
    /// its detail in both languages, what it carries; this says the title and
    /// the detail in the language asked, gives the problem its type, and writes
    /// what it carries as members beside the problem's own. One written by the
    /// framework has only a status, and becomes the refusal that status is.
    /// </summary>
    public static void Customize(ProblemDetailsContext context)
    {
        var http = context.HttpContext;
        var problem = context.ProblemDetails;
        var status = problem.Status ?? http.Response.StatusCode;

        var refusal = problem.Extensions.TryGetValue("code", out var named) && named is string code
            ? RefusalOf(http, code)
            : CommonRefusals.ByStatus.GetValueOrDefault(status);
        if (refusal is null)
            return;

        var culture = CultureOf(http);
        problem.Status = refusal.Status;
        problem.Type = refusal.Type;
        problem.Title = refusal.Title.In(culture);
        problem.Extensions["code"] = refusal.Code;

        if (problem.Extensions.Remove(Refusal.DetailMember, out var detail) && detail is LocalizedText sentences)
            problem.Detail = sentences.In(culture);

        // What the refusal carries, as members of the problem itself: written
        // with the API's own JSON rules, so its money and its closed sets are
        // said as they are everywhere else.
        if (problem.Extensions.Remove(Refusal.CarriedMember, out var carried) && carried is not null)
        {
            var json = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            foreach (var (name, value) in JsonSerializer.SerializeToNode(carried, carried.GetType(), json)!.AsObject())
                problem.Extensions[name] = value?.DeepClone();
        }

        http.Response.StatusCode = refusal.Status;
        http.Response.Headers.ContentLanguage = culture;
        http.Response.Headers.Vary = "Accept-Language";
    }

    /// <summary>The refusal a code names here: one the endpoint declared, or one any endpoint may answer.</summary>
    private static Refusal? RefusalOf(HttpContext http, string code) =>
        http.GetEndpoint()?.Metadata.GetOrderedMetadata<Refuses>()
            .SelectMany(declared => declared.Refusals)
            .FirstOrDefault(refusal => refusal.Code == code)
        ?? CommonRefusals.ByCode.GetValueOrDefault(code);

    /// <summary>ADR 0013's chain: <c>?culture=</c>, then <c>Accept-Language</c>, then the shop's default.</summary>
    public static string CultureOf(HttpContext http) =>
        CultureNegotiation.Resolve(http.Request.Query["culture"].FirstOrDefault(), http.Request.Headers.AcceptLanguage);

    /// <summary>
    /// Past a limit: when to come back, in seconds, as the limiter itself knows
    /// it — the body is the status page's, like any other refusal's.
    /// </summary>
    public static ValueTask OnRateLimited(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var until) ? until : TimeSpan.FromMinutes(1);
        context.HttpContext.Response.Headers.RetryAfter =
            Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        return ValueTask.CompletedTask;
    }
}
