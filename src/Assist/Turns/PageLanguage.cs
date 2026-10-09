namespace ElGuerre.Tendero.Assist.Turns;

/// <summary>
/// The language both of the shop's agents answer in: the page's, the one the reader
/// chose with the shop's own switch — never the one a model reads into the last
/// message (the author, 2026-10-06, after <em>hi</em> got English in a Spanish
/// shop). It is the culture the endpoint negotiated (ADR 0013), so code knows it
/// and no model decides it (invariant 12).
/// </summary>
internal static class PageLanguage
{
    /// <summary>The language's name as the instructions write it.</summary>
    public static string Name(string culture) => culture == "en" ? "English" : "Spanish";
}
