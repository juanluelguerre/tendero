using System.Text;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// One way the shop says no: a **code** a client branches on, the status it
/// travels with over HTTP, and its sentence in the shop's languages (invariant
/// 6). Data and nothing else — each transport spells it its own way: REST as an
/// RFC 9457 problem, MCP as a tool result marked as an error.
///
/// The code is the contract. The sentence is for whoever reads it — a person on
/// a screen, a model deciding what to do next — and may be reworded without a
/// client noticing; the code may not.
///
/// **How a slice answers with one.** With the framework's own problem result
/// and nothing of this project's between: <c>TypedResults.Problem(statusCode:
/// refusal.Status, extensions: refusal.Said(es: …, en: …))</c>, and the refusal
/// named in the endpoint's metadata (<see cref="Refuses"/>) so the contract
/// declares it. The host does the rest, once, for every slice: the title and the
/// detail in the language asked, the type, <c>Content-Language</c>. So the kernel
/// stays free of HTTP, and no slice writes a sentence's language by hand.
/// </summary>
public sealed record Refusal(string Code, int Status, LocalizedText Title)
{
    /// <summary>The member a problem's detail waits in, in both languages, until the host says it in one.</summary>
    public const string DetailMember = "$detail";

    /// <summary>The member what a refusal carries waits in, until the host writes its members beside the problem's own.</summary>
    public const string CarriedMember = "$carried";

    /// <summary>
    /// What this refusal hands over beside its sentences, when it does: the new
    /// total when a price moved, what a product lacks to be published. Its
    /// members travel beside the problem's own, and the contract declares them.
    /// </summary>
    public Type? Carries { get; init; }

    public Refusal(string code, int status, string es, string en)
        : this(code, status, LocalizedText.Both(en, es))
    {
    }

    /// <summary>
    /// The problem's `type` (RFC 9457): a URI that names the kind of problem.
    /// One place says where they hang from; nothing serves them, and the RFC
    /// does not ask that anything does.
    /// </summary>
    public string Type => $"{ProblemTypes.Base}{ProblemTypes.Slug(Code)}";

    /// <summary>
    /// This refusal as the members a problem carries beside RFC 9457's own: its
    /// code; what it is about this time, in both languages (<paramref name="es"/>,
    /// <paramref name="en"/>); the part of the request it concerns, as a JSONPath
    /// (<paramref name="path"/>, ACP's <c>param</c>); and what it hands over
    /// (<paramref name="carrying"/>, an instance of <see cref="Carries"/>).
    /// </summary>
    /// <param name="reason">
    /// The domain's own sentence, when the refusal is an invariant's: written
    /// once, in English, for a log and for whoever debugs — never the sentence
    /// a screen shows, which is the title's and the detail's, in two languages.
    /// </param>
    public Dictionary<string, object?> Said(
        string? es = null, string? en = null, string? path = null, object? carrying = null, string? reason = null)
    {
        var members = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = Code };
        if (!string.IsNullOrWhiteSpace(reason))
            members["reason"] = reason;
        if (es is not null && en is not null)
            members[DetailMember] = LocalizedText.Both(en, es);
        if (path is not null)
            members["path"] = path;
        if (carrying is not null)
            members[CarriedMember] = carrying;
        return members;
    }
}

/// <summary>
/// The refusals an operation can answer, as endpoint metadata: what makes the
/// contract declare them — each status as a problem whose <c>code</c> is one of
/// these and no other — and what tells the host which sentence a code has. An
/// endpoint that answers a refusal it did not name here answers one the
/// contract does not know, and the contract test says so.
/// </summary>
public sealed class Refuses(params Refusal[] refusals)
{
    public IReadOnlyList<Refusal> Refusals { get; } = refusals;
}

public static class ProblemTypes
{
    /// <summary>Where the shop's problem types hang from.</summary>
    public const string Base = "https://tendero.dev/problems/";

    /// <summary>`PriceChanged` as `price-changed`: a code is an identifier, and this is how one is written in a URI.</summary>
    public static string Slug(string code)
    {
        var slug = new StringBuilder(code.Length + 4);
        foreach (var letter in code)
        {
            if (char.IsUpper(letter) && slug.Length > 0)
                slug.Append('-');
            slug.Append(char.ToLowerInvariant(letter));
        }

        return slug.ToString();
    }
}

/// <summary>
/// The refusals that are nobody's in particular: what the pipeline answers
/// before a slice is reached, and what any slice may answer.
/// </summary>
public static class CommonRefusals
{
    public static readonly Refusal Unauthenticated = new(
        "Unauthenticated", 401, "Hace falta identificarse para esto.", "This needs somebody signed in.");

    public static readonly Refusal Forbidden = new(
        "Forbidden", 403, "Quien pregunta no tiene permiso para esto.", "Whoever is asking may not do this.");

    public static readonly Refusal NotFound = new(
        "NotFound", 404, "Aquí no hay nada con esa dirección.", "There is nothing at this address.");

    public static readonly Refusal MethodNotAllowed = new(
        "MethodNotAllowed", 405, "Esta dirección no admite ese método.", "This address does not take that method.");

    public static readonly Refusal UnsupportedMediaType = new(
        "UnsupportedMediaType", 415, "El cuerpo no está en un formato que esto admita.", "The body is not in a format this takes.");

    public static readonly Refusal TooManyRequests = new(
        "TooManyRequests", 429, "Demasiadas peticiones seguidas: espera un momento.", "Too many requests in a row: wait a moment.");

    /// <summary>The request does not hold together: a field missing, a value out of range, a query string that does not parse.</summary>
    public static readonly Refusal Invalid = new(
        "Invalid", 400, "La petición no es válida.", "The request is not valid.");

    /// <summary>What changes something that must not happen twice names itself: the key is missing, or longer than is kept.</summary>
    public static readonly Refusal IdempotencyKeyRequired = new(
        "IdempotencyKeyRequired", 400,
        "Hace falta una cabecera Idempotency-Key: un reintento no puede repetir la operación.",
        "An Idempotency-Key header is needed: a retry must not do this twice.");

    /// <summary>The key named another request: a key is one request's, and a new request needs a new one.</summary>
    public static readonly Refusal IdempotencyKeyReused = new(
        "IdempotencyKeyReused", 409,
        "Esa clave de idempotencia ya se usó para otra petición: una petición nueva necesita una clave nueva.",
        "This idempotency key was used for another request: a new request needs a new key.");

    /// <summary>The request the key names is still running: its answer is not known yet.</summary>
    public static readonly Refusal IdempotencyInProgress = new(
        "IdempotencyInProgress", 409,
        "La petición con esa clave de idempotencia sigue en curso: vuelve a preguntar por su respuesta.",
        "The request this idempotency key names is still running: ask again for its answer.");

    /// <summary>Every refusal the pipeline itself can answer, by status.</summary>
    public static readonly IReadOnlyDictionary<int, Refusal> ByStatus = new[]
        { Unauthenticated, Forbidden, NotFound, MethodNotAllowed, UnsupportedMediaType, TooManyRequests, Invalid }
        .ToDictionary(refusal => refusal.Status);

    /// <summary>Every refusal here by its code: what any operation may answer without naming it.</summary>
    public static readonly IReadOnlyDictionary<string, Refusal> ByCode = new[]
        {
            Unauthenticated, Forbidden, NotFound, MethodNotAllowed, UnsupportedMediaType, TooManyRequests, Invalid,
            IdempotencyKeyRequired, IdempotencyKeyReused, IdempotencyInProgress,
        }
        .ToDictionary(refusal => refusal.Code, StringComparer.Ordinal);
}
