using System.Net;
using System.Text.RegularExpressions;

namespace ElGuerre.Tendero.Catalog.Connectors.Shopify;

/// <summary>
/// A store's description is HTML (<c>descriptionHtml</c>); the catalogue keeps
/// text, which is what search, Tino and the agents read (spec decision 3). The
/// store's own <c>description</c> is that text on one line, so the paragraphs are
/// kept here instead: a block ends a paragraph, a list item or a line break ends a
/// line, every other tag goes, and entities are decoded.
///
/// A third party's markup is never stored: what is kept is text.
/// </summary>
internal static partial class ShopifyHtml
{
    public static string ToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var text = LineBreak().Replace(html, "\n");
        text = BlockEnd().Replace(text, "\n\n");
        text = ItemEnd().Replace(text, "\n");
        text = AnyTag().Replace(text, "");
        text = WebUtility.HtmlDecode(text);

        var lines = text.Split('\n').Select(line => Spaces().Replace(line, " ").Trim());
        return BlankLines().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    /// <summary>
    /// Whether the description held a table or a list — layout plain text keeps
    /// only as lines. The import notes it, so a store that leans on it shows up.
    /// </summary>
    public static bool HasLayout(string? html) =>
        html is not null && Layout().IsMatch(html);

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"</(p|div|h[1-6]|ul|ol|table|blockquote)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"</(li|tr)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ItemEnd();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t\r\f\v ]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"<(table|ul|ol)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Layout();
}
