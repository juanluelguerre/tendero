using ElGuerre.Tendero.Catalog.Adapters;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// The catalogue as it leaves the shop (phase 16, E-2): every live product in one
/// culture, as values an adapter project may hold — the names, the branch, the
/// pictures, the attributes and each variant's options rendered as the product
/// page renders them, and nothing the shop no longer sells.
/// </summary>
public sealed class CatalogueExportReaderTests
{
    private static readonly TestClock Clock = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LocalizedText Text(string es, string en) => new(new Dictionary<string, string> { ["es"] = es, ["en"] = en });

    private static readonly CategoryTree Tree = Categories();

    private static CategoryTree Categories()
    {
        var apparel = Category.Define(Clock, "APPAREL", Text("Ropa", "Clothing"));
        var shirts = Category.Define(Clock, "SHIRT", Text("Camisetas", "T-shirts"), apparel);
        return new CategoryTree([apparel, shirts]);
    }

    private static AttributeDefinitions Definitions()
    {
        AttributeDefinition Option(string code, LocalizedText label, bool axis, params (string Code, string Es, string En)[] options)
        {
            var definition = AttributeDefinition.Define(Clock, code, label, AttributeKind.Option, isVariantAxis: axis);
            foreach (var (option, es, en) in options)
                definition.AddOption(Clock, option, Text(es, en));
            return definition;
        }

        return new AttributeDefinitions(
        [
            Option("COLOR", Text("color", "colour"), axis: true, ("NAVY_BLUE", "azul marino", "navy blue"), ("BLACK", "negro", "black")),
            Option("SIZE", Text("talla", "size"), axis: true, ("M", "M", "M")),
            Option("GENDER", Text("género", "gender"), axis: false, ("WOMEN", "mujer", "women")),
        ]);
    }

    private static Product AShirt()
    {
        var shirt = Product.Create(Clock, Text("Camiseta técnica", "Technical T-shirt"), new Money(24.90m, "EUR"),
            description: Text("Transpirable.", "Breathable."), brand: "Trail Co", category: "SHIRT");
        shirt.DefineAxes(Clock, ["COLOR", "SIZE"]);
        shirt.AddVariant(Clock, "SHIRT-NAVY-M", new Money(24.90m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "NAVY_BLUE", ["SIZE"] = "M" });
        shirt.AddVariant(Clock, "SHIRT-BLACK-M", new Money(26.90m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "BLACK", ["SIZE"] = "M" });
        shirt.AddVariant(Clock, "SHIRT-BLACK-XS", new Money(20m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "BLACK", ["SIZE"] = "XS" });
        shirt.DiscontinueVariant(Clock, "SHIRT-BLACK-XS");
        shirt.SetVariantBarcode(Clock, "SHIRT-NAVY-M", "8412345678905");
        shirt.SetVariantTaxClass(Clock, "SHIRT-BLACK-M", "reduced");
        shirt.SetAttribute(Clock, AttributeValue.Option("GENDER", "WOMEN"));
        shirt.SetAttribute(Clock, AttributeValue.Plain("NOT_DEFINED", "anything"));
        shirt.AddImage(Clock, new ImageId("front"), Text("De frente", "Front"));
        shirt.AddImage(Clock, new ImageId("back"));
        shirt.Publish(Clock);
        return shirt;
    }

    private static CatalogueExportReader Reader(params Product[] products) =>
        new(new InMemoryProductCatalogReader(products), new StaticDefinitions(Definitions()), new StaticCategories(Tree));

    [Fact]
    public async Task A_product_leaves_in_the_culture_asked_with_its_branch_pictures_and_attributes()
    {
        var shirt = AShirt();

        var exported = Assert.Single(await Reader(shirt).ProductsAsync("en", Ct));

        Assert.Equal((shirt.Code, true, "Technical T-shirt", shirt.Slug.In("en"), "Breathable.", "Trail Co"),
            (exported.Code, exported.OnSale, exported.Name, exported.Slug, exported.Description, exported.Brand));
        Assert.Equal([new ExportedCategoryStep("APPAREL", "Clothing"), new ExportedCategoryStep("SHIRT", "T-shirts")], exported.Category);
        Assert.Equal([new ExportedPicture("front", "Front"), new ExportedPicture("back", null)], exported.Pictures);
        Assert.Equal([new ExportedAttribute("GENDER", "gender", "WOMEN", "women")], exported.Attributes);
    }

    /// <summary>Each variant with its options labelled in the culture and in the product's order of axes; a discontinued one is not there.</summary>
    [Fact]
    public async Task A_variant_leaves_with_its_options_its_barcode_and_its_tax_class()
    {
        var exported = Assert.Single(await Reader(AShirt()).ProductsAsync("es", Ct));

        Assert.Equal(["SHIRT-NAVY-M", "SHIRT-BLACK-M"], exported.Variants.Select(variant => variant.Sku));
        var navy = exported.Variants[0];
        Assert.Equal(("8412345678905", new Money(24.90m, "EUR"), (string?)null), (navy.Barcode, navy.Price, navy.TaxClass));
        Assert.Equal([new ExportedOption("COLOR", "color", "NAVY_BLUE", "azul marino"), new ExportedOption("SIZE", "talla", "M", "M")], navy.Options);
        Assert.Equal(["front", "back"], navy.PictureIds);
        Assert.Equal("reduced", exported.Variants[1].TaxClass);
    }

    [Fact]
    public async Task A_draft_leaves_said_as_not_on_sale_and_an_archived_product_does_not_leave()
    {
        var draft = Product.Create(Clock, Text("Taza", "Mug"), new Money(10m, "EUR"));
        draft.AddVariant(Clock, "MUG-DEFAULT", new Money(10m, "EUR"));
        var archived = Product.Create(Clock, Text("Vaso", "Glass"), new Money(5m, "EUR"));
        archived.AddVariant(Clock, "GLASS-DEFAULT", new Money(5m, "EUR"));
        archived.Archive(Clock);

        var exported = Assert.Single(await Reader(draft, archived).ProductsAsync("es", Ct));

        Assert.Equal((draft.Code, false), (exported.Code, exported.OnSale));
        Assert.Empty(exported.Category);
    }

    /// <summary>
    /// One product by its code, as the whole export says it (phase 16, S3): what a sales
    /// channel is told when that product changes. Archived or unknown, it is not there.
    /// </summary>
    [Fact]
    public async Task One_product_leaves_by_its_code_as_the_whole_export_says_it_and_an_archived_one_does_not()
    {
        var shirt = AShirt();
        var archived = Product.Create(Clock, Text("Vaso", "Glass"), new Money(5m, "EUR"));
        archived.AddVariant(Clock, "GLASS-DEFAULT", new Money(5m, "EUR"));
        archived.Archive(Clock);
        var reader = Reader(shirt, archived);

        var one = await reader.ProductAsync(shirt.Code, "en", Ct);

        Assert.Equal(Assert.Single(await reader.ProductsAsync("en", Ct)), one, ExportedProductComparer.Instance);
        Assert.Null(await reader.ProductAsync(archived.Code, "en", Ct));
        Assert.Null(await reader.ProductAsync("NO-SUCH-CODE", "en", Ct));
    }

    /// <summary>Records holding lists compare by reference; the same product compared by what it says.</summary>
    private sealed class ExportedProductComparer : IEqualityComparer<ExportedProduct?>
    {
        public static readonly ExportedProductComparer Instance = new();

        public bool Equals(ExportedProduct? x, ExportedProduct? y) =>
            x is not null && y is not null
            && System.Text.Json.JsonSerializer.Serialize(x) == System.Text.Json.JsonSerializer.Serialize(y);

        public int GetHashCode(ExportedProduct? obj) => obj?.Code.GetHashCode(StringComparison.Ordinal) ?? 0;
    }

    // The tests below were found by the exports' Stryker close: each mutant they name survived the suite above.

    /// <summary>The products leave by their code, so two exports of an unchanged catalogue are the same file.</summary>
    [Fact]
    public async Task Products_leave_in_the_order_of_their_codes()
    {
        var products = Enumerable.Range(0, 4).Select(_ => AShirt()).ToArray();

        var exported = await Reader(products).ProductsAsync("es", Ct);

        Assert.Equal(products.Select(product => product.Code).Order(StringComparer.Ordinal), exported.Select(product => product.Code));
    }

    /// <summary>A number leaves with its unit, since another vocabulary has no definition to render it by.</summary>
    [Fact]
    public async Task A_number_leaves_with_its_unit()
    {
        var pan = AShirt();
        pan.SetAttribute(Clock, AttributeValue.Numeric("CAPACITY_L", 1.5m));
        var capacity = AttributeDefinition.Define(Clock, "CAPACITY_L", Text("capacidad", "capacity"), AttributeKind.Number, unit: "L");

        var exported = Assert.Single(await new CatalogueExportReader(
            new InMemoryProductCatalogReader(pan),
            new StaticDefinitions(new AttributeDefinitions([.. Definitions().All, capacity])),
            new StaticCategories(Tree)).ProductsAsync("es", Ct));

        Assert.Equal(new ExportedAttribute("CAPACITY_L", "capacidad", "1.5", "1.5 L"), exported.Attributes.Single(attribute => attribute.Code == "CAPACITY_L"));
    }

    private sealed class StaticDefinitions(AttributeDefinitions definitions) : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(definitions);
    }

    private sealed class StaticCategories(CategoryTree tree) : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(tree);
    }
}
