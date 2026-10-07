using System.Text.Json;
using ElGuerre.Tendero.Catalog.Adapters;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// The attributes a category declares (ADR 0056): a category says which of the
/// catalogue's attributes are its products', a category below it adds its own,
/// and a product's are those of its whole branch. What is declared is what a
/// product of that category is *offered* — to an extractor, to a picture's
/// questions — and never a rule an import is refused by.
/// </summary>
public sealed class CategoryAttributesTests
{
    private static readonly TestClock Clock = new();

    private static LocalizedText Text(string es, string en) => LocalizedText.From("es", es).With("en", en);

    private static CategoryTree Tree()
    {
        var home = Category.Define(Clock, "HOME", Text("Hogar", "Home"), attributes: ["material", "COLOR"]);
        var kitchen = Category.Define(Clock, "KITCHEN", Text("Cocina", "Kitchen"), home);
        var cookware = Category.Define(Clock, "COOKWARE", Text("Menaje", "Cookware"), kitchen, attributes: ["PIECES", "MATERIAL"]);
        var gifts = Category.Define(Clock, "GIFTS", Text("Regalos", "Gifts"));
        var cards = Category.Define(Clock, "CARDS", Text("Tarjetas", "Cards"), gifts);
        return new CategoryTree([home, kitchen, cookware, gifts, cards]);
    }

    [Fact]
    public void A_category_declares_attributes_by_their_code()
    {
        Assert.Equal(["MATERIAL", "COLOR"], Tree().ByCode("HOME")!.Attributes);
    }

    /// <summary>What a branch declares, root first and each code once: a product of the leaf has all of it.</summary>
    [Fact]
    public void A_category_has_the_attributes_of_its_whole_branch()
    {
        var tree = Tree();

        Assert.Equal(["MATERIAL", "COLOR", "PIECES"], tree.AttributesOf("COOKWARE"));
        Assert.Equal(["MATERIAL", "COLOR"], tree.AttributesOf("KITCHEN"));
    }

    /// <summary>
    /// A branch that declares nothing has not said "no attributes": it has not
    /// said. Null, so that whoever asks offers what it offered before — a
    /// catalogue with no declarations is a catalogue that still works.
    /// </summary>
    [Theory]
    [InlineData("CARDS")]
    [InlineData("NOT_A_CATEGORY")]
    [InlineData(null)]
    public void A_branch_that_declares_nothing_has_not_said(string? category)
    {
        Assert.Null(Tree().AttributesOf(category));
    }

    // ---------- the seed the repository ships ----------

    private static string Seed(string file)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Tendero.slnx")))
            folder = folder.Parent;
        return Path.Combine(folder!.FullName, "seed", file);
    }

    private static async Task<(CategoryTree Tree, AttributeDefinitions Definitions)> SeedCatalogue()
    {
        var ct = TestContext.Current.CancellationToken;
        var tree = await CategorySeedFile.LoadAsync(Seed("categories.sample.json"), Clock, ct);
        var definitions = new AttributeDefinitions(await AttributeDefinitionSeedFile.LoadAsync(Seed("attributes.sample.json"), Clock, ct));
        return (tree, definitions);
    }

    /// <summary>Every code a category declares is an attribute the catalogue defines: a typo would silently offer nothing.</summary>
    [Fact]
    public async Task Every_attribute_a_seed_category_declares_is_defined()
    {
        var (tree, definitions) = await SeedCatalogue();

        Assert.All(tree.All.SelectMany(category => category.Attributes), code => Assert.NotNull(definitions.ByCode(code)));
        Assert.Contains(tree.All, category => category.Attributes.Count > 0);
    }

    /// <summary>
    /// **The seed's own products are the measure of the declarations.** Every
    /// attribute a product carries is one its category's branch declares — or the
    /// declarations would stop offering an extractor the very attributes the
    /// catalogue already holds for that kind of product.
    /// </summary>
    [Fact]
    public async Task Every_attribute_a_seed_product_carries_is_declared_by_its_categorys_branch()
    {
        var (tree, definitions) = await SeedCatalogue();
        using var products = JsonDocument.Parse(await File.ReadAllTextAsync(Seed("products.sample.json"), TestContext.Current.CancellationToken));

        List<string> undeclared = [];
        foreach (var product in products.RootElement.EnumerateArray())
        {
            var type = product.GetProperty("product_type").GetString();
            var declared = tree.AttributesOf(type);
            Assert.NotNull(declared);

            foreach (var attribute in product.GetProperty("attributes").EnumerateObject())
            {
                var definition = definitions.ForSourceKey(attribute.Name);
                Assert.NotNull(definition);
                if (!declared.Contains(definition.Code))
                    undeclared.Add($"{type}: {definition.Code}");
            }
        }

        Assert.Empty(undeclared.Distinct());
    }

    /// <summary>The reason this exists (phase 13a): a pair of shoes is not two pieces, and footwear does not declare them.</summary>
    [Fact]
    public async Task Footwear_does_not_declare_pieces_and_cookware_does()
    {
        var (tree, _) = await SeedCatalogue();

        Assert.DoesNotContain("PIECES", tree.AttributesOf("SHOES")!);
        Assert.Contains("PIECES", tree.AttributesOf("COOKWARE")!);
    }

    /// <summary>What a picture can show is the attribute's own to say, and in the seed it is how many — and nothing else today.</summary>
    [Fact]
    public async Task The_seed_says_which_attributes_a_picture_shows()
    {
        var (_, definitions) = await SeedCatalogue();

        Assert.Equal(["PIECES"], definitions.All.Where(definition => definition.IsShownInPictures).Select(definition => definition.Code));
    }
}
