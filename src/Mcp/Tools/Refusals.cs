using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using ModelContextProtocol;

namespace ElGuerre.Tendero.Mcp.Tools;

/// <summary>
/// A rejected request, in the words of this transport.
///
/// The dispatcher's validation step throws the same <see cref="ValidationException"/>
/// whoever asked, and each transport says it in its own protocol: over HTTP the
/// API's <c>ValidationExceptionHandler</c> turns it into a ProblemDetails, and
/// here it becomes a JSON-RPC error carrying the same sentences. This is the one
/// thing in the MCP project that is not a straight dispatch, and it is the
/// transport's own job rather than logic: without it a one-character query
/// reaches the agent as an unhandled exception and a stack trace, which tells it
/// nothing about how to ask again.
/// </summary>
internal static partial class Refusals
{
    /// <summary>
    /// The same for a synchronous check on the way in. A malformed argument is
    /// the caller's mistake and it should read like one.
    /// </summary>
    public static void Guard(Action check)
    {
        try
        {
            check();
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    /// <summary>
    /// **One refusal, two transports** (phase 13b). The shop's own tools refuse
    /// with the problem a REST refusal is — its code, the shop's sentence in the
    /// language asked, what it is about this time — as the text of the error an
    /// agent reads: a tool result marked `isError`, which is where MCP puts a
    /// refusal a model can correct itself from. JSON, because the code is for
    /// whoever branches and the sentences for whoever reads, and a model does both.
    /// </summary>
    public static ToolRefused Problem(
        Refusal refusal, string culture, string? es = null, string? en = null, IReadOnlyDictionary<string, string[]>? errors = null,
        string? reason = null)
    {
        var problem = new JsonObject
        {
            ["code"] = refusal.Code,
            ["title"] = refusal.Title.In(culture),
        };
        if (es is not null && en is not null)
            problem["detail"] = LocalizedText.Both(en, es).In(culture);
        if (errors is { Count: > 0 })
            problem["errors"] = JsonSerializer.SerializeToNode(errors);
        if (reason is not null)
            problem["reason"] = reason;

        return new ToolRefused(refusal.Code, problem.ToJsonString(Readably));
    }

    /// <summary>
    /// A call whose arguments are not the tool's: one left out or misnamed, or
    /// one of another type. The SDK binds them before the tool runs and throws
    /// what it would throw for a tool that broke, which reaches an agent as *An
    /// error occurred invoking…* — nothing it can correct. It is the request
    /// that is wrong, so it is `Invalid`, naming the argument when the binder
    /// named it; the binder's own sentence travels as the reason.
    /// </summary>
    public static ToolRefused NotTheToolsArguments(Exception thrown, string culture)
    {
        var reason = thrown.Message.Replace(" (Parameter 'arguments')", string.Empty, StringComparison.Ordinal);

        return MissingArgument().Match(thrown.Message) is { Success: true } named
            ? Problem(CommonRefusals.Invalid, culture,
                es: $"Falta el argumento «{named.Groups[1].Value}».", en: $"The argument '{named.Groups[1].Value}' is missing.", reason: reason)
            : Problem(CommonRefusals.Invalid, culture,
                es: "Los argumentos no son los que pide la herramienta.", en: "The arguments are not the ones the tool takes.", reason: reason);
    }

    [System.Text.RegularExpressions.GeneratedRegex("required parameter '([^']+)'")]
    private static partial System.Text.RegularExpressions.Regex MissingArgument();

    /// <summary>As a person would write it: «NOPE» and é, not their escapes — a model reads this text.</summary>
    private static readonly JsonSerializerOptions Readably = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The shop's own tools: a request the dispatcher's validation refused is
    /// the problem `Invalid`, with every failure under the argument it is about
    /// — every one, not the first: an agent that fixes one rule at a time takes
    /// one round trip per mistake.
    /// </summary>
    public static async Task<T> Readable<T>(string culture, Func<Task<T>> dispatch)
    {
        try
        {
            return await dispatch();
        }
        catch (ValidationException exception)
        {
            throw Problem(CommonRefusals.Invalid, culture, errors: exception.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray()));
        }
    }

    /// <summary>UCP's tools, whose refusals are UCP's own to word: the sentences, as its binding says them.</summary>
    public static async Task<T> Readable<T>(Func<Task<T>> dispatch)
    {
        try
        {
            return await dispatch();
        }
        catch (ValidationException exception)
        {
            // Every failure, not the first: an agent that fixes one rule at a
            // time takes one round trip per mistake.
            throw new McpException(string.Join(" ", exception.Errors.Select(e => e.ErrorMessage)));
        }
    }
}

/// <summary>
/// A refusal of the shop's own tools: the problem as its message, and its code
/// beside it for whoever counts. An <see cref="McpException"/>, so anything
/// that only knows those still reads it as the refusal it is.
/// </summary>
internal sealed class ToolRefused(string code, string problem) : McpException(problem)
{
    public string Code { get; } = code;
}

/// <summary>What the shop's own tools refuse beyond a request that does not validate, by code.</summary>
internal static class ToolRefusals
{
    public static readonly Refusal ProductNotFound = new(
        "ProductNotFound", 404, "Ese producto no existe en la tienda.", "The shop has no such product.");

    public static readonly Refusal TooFewToCompare = new(
        "TooFewToCompare", 400,
        "Para comparar hacen falta al menos dos productos; para uno, get_product_specs.",
        "Compare at least two products; use get_product_specs for one.");
}
