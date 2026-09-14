namespace ElGuerre.Tendero.Mcp.Tools;

/// <summary>
/// The culture the answer is in, travelling as data because this protocol has
/// nowhere else to put it.
///
/// Over HTTP that fact is a header — `Content-Language`, which ADR 0013 requires
/// of every endpoint returning localized text — and JSON-RPC has no headers, so
/// it goes in the payload. This is not the transport inventing a contract for
/// the shop's data: the payload underneath is the query's own result, unmapped.
/// It is the one header that has to become a field.
///
/// It matters most exactly when it is least expected. An agent asking for `fr`
/// is answered in Spanish, and an agent asking for nothing is answered in
/// whatever the chain decided — in both cases the words come back with no clue
/// as to which language they are, and a model reading them will guess from the
/// text. `culture` is the difference between an answer and an answer it can
/// trust.
///
/// **What this does NOT carry is the per-item truth.** `Content-Language` states
/// the negotiated culture, not each row's, and ADR 0013 is explicit that a
/// product which fell back travels as data on the item itself. It already does:
/// `ProductDetail.MissingCultures` says which languages that product has not
/// been translated into, so `get_product` answers "in English" and names English
/// among the missing when that is the truth.
/// </summary>
public sealed record Answered<T>(string Culture, T Result);

public static class Answered
{
    /// <summary>
    /// Resolve once, use it for the query AND for the label on the answer. The
    /// alternative — resolving in the tool and again in the envelope — is two
    /// calls that can disagree, which is the whole failure this type exists to
    /// prevent.
    /// </summary>
    public static async Task<Answered<T>> In<T>(string culture, Func<string, Task<T>> answer) =>
        new(culture, await answer(culture));
}
