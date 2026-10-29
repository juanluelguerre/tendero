using ElGuerre.Tendero.Shopify;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>
/// A store's HTML description as the catalogue's plain text (spec decision 3):
/// paragraphs kept as paragraphs, a list's items as lines, the markup and its
/// entities gone. What only the layout said — a table's columns — is lost, and
/// <see cref="ShopifyHtml.HasLayout"/> says so for the import's notes.
/// </summary>
public sealed class ShopifyHtmlTests
{
    [Theory]
    [InlineData("<p>De goteo.</p><p>Jarra de vidrio.</p>", "De goteo.\n\nJarra de vidrio.")]
    [InlineData("<ul><li>Gres</li><li>350 ml</li></ul>", "Gres\n350 ml")]
    [InlineData("Una línea<br>y otra<br/>más", "Una línea\ny otra\nmás")]
    [InlineData("<p>Para <strong>diez</strong>   tazas &amp; m&aacute;s</p>", "Para diez tazas & más")]
    [InlineData("<h2>Medidas</h2><p>20 cm</p>", "Medidas\n\n20 cm")]
    [InlineData("Texto sin etiquetas", "Texto sin etiquetas")]
    [InlineData("<p>Uno</p>\n\n\n<p>Dos</p>", "Uno\n\nDos")]
    [InlineData("", "")]
    public void Html_becomes_plain_text_with_its_paragraphs(string html, string text) =>
        Assert.Equal(text, ShopifyHtml.ToText(html));

    [Theory]
    [InlineData("<table><tr><td>S</td><td>38</td></tr></table>", true)]
    [InlineData("<ul><li>Gres</li></ul>", true)]
    [InlineData("<p>Solo texto</p>", false)]
    public void A_table_or_a_list_is_layout_plain_text_loses(string html, bool layout) =>
        Assert.Equal(layout, ShopifyHtml.HasLayout(html));
}
