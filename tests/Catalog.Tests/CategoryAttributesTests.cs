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

    // ---------- required (ADR 0056, amended) ----------

    /// <summary>
    /// Home requires a material and expects a colour; cookware, below it,
    /// requires its pieces and — raising what its ancestor only expects — a
    /// colour; bags declare and require nothing of their own.
    /// </summary>
    private static CategoryTree Requiring()
    {
        var home = Category.Define(Clock, "HOME", Text("Hogar", "Home"), attributes: ["COLOR"], required: ["material"]);
        var kitchen = Category.Define(Clock, "KITCHEN", Text("Cocina", "Kitchen"), home);
        var cookware = Category.Define(Clock, "COOKWARE", Text("Menaje", "Cookware"), kitchen, required: ["PIECES", "COLOR"]);
        var bags = Category.Define(Clock, "BAGS", Text("Bolsas", "Bags"), attributes: ["MATERIAL"]);
        return new CategoryTree([home, kitchen, cookware, bags]);
    }

    /// <summary>A required attribute is a declared one: nothing is required that is not offered.</summary>
    [Fact]
    public void A_category_says_which_of_its_attributes_a_product_must_have()
    {
        var home = Requiring().ByCode("HOME")!;

        Assert.Equal(["MATERIAL"], home.Required);
        Assert.Equal(["COLOR", "MATERIAL"], home.Attributes);
        Assert.Empty(Requiring().ByCode("BAGS")!.Required);
    }

    /// <summary>
    /// Inherited down the branch, root first, each code once. A category below
    /// raises what an ancestor only expects and cannot lower what it requires.
    /// And *nothing is required* is an answer, not an absence: empty, never null.
    /// </summary>
    [Fact]
    public void What_is_required_is_the_whole_branchs_and_a_descendant_only_raises_it()
    {
        var tree = Requiring();

        Assert.Equal(["MATERIAL", "PIECES", "COLOR"], tree.RequiredOf("COOKWARE"));
        Assert.Equal(["MATERIAL"], tree.RequiredOf("KITCHEN"));
        Assert.Empty(tree.RequiredOf("BAGS"));
        Assert.Empty(tree.RequiredOf("NOT_A_CATEGORY"));
        Assert.Empty(tree.RequiredOf(null));
    }

    private static Product In(string? category, params string[] holds)
    {
        var product = Product.Create(Clock, Text("Sartén", "Pan"), new Money(10m, "EUR"), category: category);
        foreach (var code in holds)
            product.SetAttribute(Clock, AttributeValue.Plain(code, "x"));
        return product;
    }

    [Fact]
    public void A_product_lacks_what_its_category_requires_and_it_has_no_value_for()
    {
        var tree = Requiring();

        Assert.Equal(["MATERIAL", "PIECES", "COLOR"], In("COOKWARE").MissingRequired(tree));
        Assert.Equal(["PIECES"], In("COOKWARE", "material", "COLOR").MissingRequired(tree));
        Assert.Empty(In("COOKWARE", "MATERIAL", "PIECES", "COLOR").MissingRequired(tree));
        // Nothing is asked of a product with no category, or of one whose category nobody declared.
        Assert.Empty(In(null).MissingRequired(tree));
        Assert.Empty(In("GIFTS").MissingRequired(tree));
        Assert.Empty(In("COOKWARE").MissingRequired(CategoryTree.Empty));
    }

    /// <summary>
    /// A pan in two colours says no colour of its own: its variants say it, and
    /// it is not missing one. Once nobody sells a colour any more, it is.
    /// </summary>
    [Fact]
    public void An_axis_counts_as_held_while_a_variant_still_sold_holds_it()
    {
        var pan = In("COOKWARE", "MATERIAL", "PIECES");
        pan.DefineAxes(Clock, ["COLOR"]);
        pan.AddVariant(Clock, "PAN-BLACK", new Money(10m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "BLACK" });

        Assert.Empty(pan.MissingRequired(Requiring()));

        pan.DiscontinueVariant(Clock, "PAN-BLACK");

        Assert.Equal(["COLOR"], pan.MissingRequired(Requiring()));
    }

    /// <summary>The file says it either way: a plain code is expected, as it always was; an object may say required.</summary>
    [Fact]
    public async Task The_file_declares_an_attribute_as_a_code_or_as_a_code_and_its_level()
    {
        var file = Path.Combine(Path.GetTempPath(), $"tendero-categories-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file,
            """
            [
              { "code": "HOME", "name": { "es": "Hogar" },
                "attributes": [ "COLOR", { "code": "MATERIAL", "required": true }, { "code": "PIECES" }, { "code": "CUPS", "required": false } ] }
            ]
            """, TestContext.Current.CancellationToken);
        try
        {
            var home = (await CategorySeedFile.LoadAsync(file, Clock, TestContext.Current.CancellationToken)).ByCode("HOME")!;

            Assert.Equal(["COLOR", "MATERIAL", "PIECES", "CUPS"], home.Attributes);
            Assert.Equal(["MATERIAL"], home.Required);
        }
        finally
        {
            File.Delete(file);
        }
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

    /// <summary>
    /// **The seed starts complete.** What a seed category requires is what every
    /// seed product of it already holds — on itself or on its variants — so the
    /// first incomplete product the shop sees is a real one, and a declaration
    /// that no seed product could meet is caught here and not on a dashboard.
    /// </summary>
    [Fact]
    public async Task No_seed_product_lacks_what_its_category_requires_and_the_seed_does_require()
    {
        var (tree, definitions) = await SeedCatalogue();
        using var products = JsonDocument.Parse(await File.ReadAllTextAsync(Seed("products.sample.json"), TestContext.Current.CancellationToken));

        List<string> lacking = [];
        foreach (var product in products.RootElement.EnumerateArray())
        {
            var type = product.GetProperty("product_type").GetString();
            var words = product.GetProperty("attributes").EnumerateObject().Select(attribute => attribute.Name).ToList();
            if (product.TryGetProperty("variants", out var variants))
                words.AddRange(variants.EnumerateArray().SelectMany(variant => variant.GetProperty("attributes").EnumerateObject()).Select(attribute => attribute.Name));
            var held = words.Select(word => definitions.ForSourceKey(word)?.Code).ToHashSet();

            lacking.AddRange(tree.RequiredOf(type).Where(code => !held.Contains(code)).Select(code => $"{product.GetProperty("item_id").GetString()}: {code}"));
        }

        Assert.Empty(lacking);
        // And it is not empty by saying nothing: five attributes are required today.
        Assert.Equal(
            ["APPAREL: COLOR", "BAGS: MATERIAL", "COFFEE_MAKER: CUPS", "DESK_LAMP: COLOUR_TEMPERATURE", "KNIVES: PIECES"],
            tree.All.SelectMany(category => category.Required.Select(code => $"{category.Code}: {code}")).Order(StringComparer.Ordinal));
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
