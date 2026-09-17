using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Ucp.Catalog;

/// <summary>
/// The shop's own verdict about a query — <see cref="SearchAnswer"/>, layer A/B
/// of ADR 0032 — translated into UCP's <c>messages</c>.
///
/// **Why this is a translation and not a reuse of the storefront's own copy.**
/// The storefront renders a help card and a "closest match" banner; an agent
/// reading `search_catalog` has no banner, only `messages`, so the same fact
/// has to become a sentence in the field the protocol reserves for exactly
/// this: "I processed your request, and here is something you should know"
/// (see <see cref="UcpMessage"/>). No new decision is made here — the model
/// never enters this class — only the same three facts (exact, related, out
/// of scope, plus a help topic) said in the protocol's shape.
///
/// Content is chosen by culture with the same fallback the rest of the shop
/// uses: the answered language, falling back to <c>es</c>.
/// </summary>
internal static class UcpSearchNotices
{
    public static IReadOnlyList<UcpMessage> For(SearchAnswer? answer, string query, string culture)
    {
        if (answer is null)
            return [];

        List<UcpMessage> notices = [];

        switch (answer)
        {
            case { Match: SearchMatch.Related }:
                notices.Add(new UcpMessage
                {
                    Type = "info",
                    Code = "no_exact_match",
                    Content = Text(
                        culture,
                        es: $"Ningún producto coincide exactamente con «{query}»; estos son los más parecidos.",
                        en: $"No product matches “{query}” exactly; these are the closest."),
                    Path = "$.query"
                });
                break;

            case { Match: SearchMatch.OutOfScope, OutOfScopeLabel: { } label }:
                notices.Add(new UcpMessage
                {
                    Type = "info",
                    Code = "not_sold",
                    Content = Text(
                        culture,
                        es: $"Esta tienda no vende {label}.",
                        en: $"This shop does not sell {label}."),
                    Path = "$.query"
                });
                break;
        }

        if (answer.HelpTopic is { } topic && HelpTopicDestinations.For(topic) is { } destination)
        {
            notices.Add(new UcpMessage
            {
                Type = "info",
                Code = "help_available",
                Content = Text(
                    culture,
                    es: $"Hay información sobre devoluciones en {destination}.",
                    en: $"Information about returns is at {destination}."),
                Path = "$.query"
            });
        }

        return notices;
    }

    /// <summary>Explicit <c>en</c>; everything else falls back to <c>es</c> — the
    /// same rule the rest of the shop resolves culture with (ADR 0013).</summary>
    private static string Text(string culture, string es, string en) =>
        culture == "en" ? en : es;
}
