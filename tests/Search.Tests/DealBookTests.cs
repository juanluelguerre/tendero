using ElGuerre.Tendero.Search.Contracts;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// What a card asks Pricing for: the variant and its branch as a path. A product
/// with no category has no branch, and asks with none — not with an empty path,
/// which Pricing would read as a category that matches nothing (13c's scoped
/// Stryker run found it unheld).
/// </summary>
public sealed class DealBookTests
{
    private static (DealBook Book, List<(string Sku, string? Path)> Asked) Recording()
    {
        var asked = new List<(string, string?)>();
        return (new DealBook((sku, path) =>
        {
            asked.Add((sku, path));
            return [];
        }), asked);
    }

    [Fact]
    public void A_branch_is_asked_as_its_path_root_first()
    {
        var (book, asked) = Recording();

        book.For("PAN-BLACK", ["HOME", "KITCHEN", "COOKWARE"]);

        Assert.Equal([("PAN-BLACK", "HOME/KITCHEN/COOKWARE")], asked);
    }

    [Fact]
    public void No_branch_is_asked_with_no_path_rather_than_an_empty_one()
    {
        var (book, asked) = Recording();

        book.For("PAN-BLACK", []);

        Assert.Equal([("PAN-BLACK", (string?)null)], asked);
    }
}
