using System.Runtime.CompilerServices;
using System.Text;
using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// Importing the same product twice, with a different gallery the second time.
///
/// The seed gained an illustration for each of its six original products on
/// 2026-09-07, and the storefront kept showing the placeholders: a re-import
/// appended the new image behind the old one, and the cover is the lowest sort
/// order. Nothing was red because nothing had ever imported a product twice
/// with a different gallery — this slice had no test of its own at all.
/// </summary>
public sealed class ImportProductsTests
{
    private static readonly TestClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Uri Placeholder = new("file:///seed/placeholder.png");
    private static readonly Uri Illustration = new("file:///seed/illustration.webp");
    private static readonly Uri Missing = new("file:///seed/missing.webp");

    [Fact]
    public async Task Reimporting_puts_the_sources_first_image_on_the_cover()
    {
        var shop = new Shop();

        await shop.ImportAsync(Placeholder);
        await shop.ImportAsync(Illustration, Placeholder);

        Assert.Equal(Shop.IdOf(Illustration), shop.Product.PrimaryImage?.Id);
    }

    [Fact]
    public async Task An_image_the_source_dropped_leaves_the_product()
    {
        var shop = new Shop();

        await shop.ImportAsync(Placeholder);
        await shop.ImportAsync(Illustration);

        Assert.Equal(Shop.IdOf(Illustration), Assert.Single(shop.Product.Images).Id);
    }

    /// <summary>
    /// An image the source names and that cannot be read must not cost the
    /// product the gallery it already has — a CDN that times out once would
    /// otherwise strip the photos off a live product. The gallery is replaced
    /// only when every image the source lists arrived; until then the import
    /// only adds, and the next complete one converges.
    /// </summary>
    [Fact]
    public async Task An_image_that_could_not_be_read_does_not_cost_the_product_its_gallery()
    {
        var shop = new Shop();

        await shop.ImportAsync(Placeholder);
        await shop.ImportAsync(Missing, Illustration);

        Assert.Equal(Shop.IdOf(Placeholder), shop.Product.PrimaryImage?.Id);
        Assert.Contains(shop.Product.Images, image => image.Id == Shop.IdOf(Illustration));
    }

    // ---------- a variant's own picture (phase 13a, I-4d) ----------

    private static readonly ExternalVariant[] TwoColours =
    [
        new("OLIVE", new Dictionary<string, string> { ["color"] = "oliva" }, Image: 0),
        new("BLACK", new Dictionary<string, string> { ["color"] = "negro" }, Image: 1),
        new("GREY", new Dictionary<string, string> { ["color"] = "gris" }),
    ];

    /// <summary>
    /// A variant names its picture by its place in the source's list, and gets
    /// the picture that was stored for that place. One that names none has none
    /// of its own: the cover stands for it.
    /// </summary>
    [Fact]
    public async Task A_variant_gets_the_picture_the_source_says_is_its_own()
    {
        var shop = new Shop { Variants = TwoColours };

        await shop.ImportAsync(Placeholder, Illustration);

        Assert.Equal(
            [("B000000001-OLIVE", (ImageId?)Shop.IdOf(Placeholder)), ("B000000001-BLACK", Shop.IdOf(Illustration)), ("B000000001-GREY", null)],
            shop.Product.Variants.Select(variant => (variant.Sku, variant.Image)));
    }

    /// <summary>
    /// With an image missing, the list has no places to go by: no variant is
    /// given a picture that may be another's, and the next complete import sets
    /// them.
    /// </summary>
    [Fact]
    public async Task With_an_image_missing_no_variant_is_given_a_picture_until_the_gallery_is_whole()
    {
        var shop = new Shop { Variants = TwoColours };

        await shop.ImportAsync(Placeholder, Missing);
        Assert.All(shop.Product.Variants, variant => Assert.Null(variant.Image));

        await shop.ImportAsync(Placeholder, Illustration);
        Assert.Equal(Shop.IdOf(Illustration), shop.Product.Variants.Single(variant => variant.Sku.EndsWith("BLACK", StringComparison.Ordinal)).Image);
    }

    private sealed class Shop
    {
        private readonly InMemoryProductRepository repository = new();
        private readonly OneProductConnector connector = new();

        public Product Product { get; private set; } = null!;

        /// <summary>The ways the source sells it, from the next import on.</summary>
        public IReadOnlyList<ExternalVariant>? Variants
        {
            set => this.connector.Variants = value;
        }

        /// <summary>The same product, from the same source, with this gallery.</summary>
        public async Task ImportAsync(params Uri[] images)
        {
            this.connector.Images = images;

            var handler = new ImportProductsHandler(
                new OneSourceRegistry(this.connector),
                this.repository,
                new CountingUnitOfWork(),
                new ReadableImages(),
                new ContentAddressedStore(),
                new NoAttributeDefinitions(),
                Clock,
                NullLogger<ImportProductsHandler>.Instance);

            await handler.HandleAsync(new ImportProductsCommand(OneProductConnector.Key), Ct);

            Product = (await this.repository.FindByExternalReferenceAsync(
                OneProductConnector.Key, OneProductConnector.ExternalId, Ct))!;
        }

        /// <summary>What the store would call the bytes at this location: the
        /// images are content-addressed (ADR 0011), and here the content is the
        /// location's text.</summary>
        public static ImageId IdOf(Uri location) =>
            new(Convert.ToHexStringLower(Encoding.UTF8.GetBytes(location.AbsolutePath)));
    }

    private sealed class OneProductConnector : ICatalogSourceConnector
    {
        public const string Key = "test";
        public const string ExternalId = "B000000001";

        public IReadOnlyList<Uri> Images { get; set; } = [];

        public IReadOnlyList<ExternalVariant>? Variants { get; set; }

        public string Source => Key;

        public async IAsyncEnumerable<ExternalProduct> StreamProductsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;

            yield return new ExternalProduct(
                ExternalId,
                new Dictionary<string, string> { ["es"] = "Cafetera de goteo", ["en"] = "Drip coffee maker" },
                Descriptions: null,
                Brand: "Kuisina",
                Category: null,
                PriceAmount: 49.90m,
                PriceCurrency: "EUR",
                [.. Images.Select(location => new ExternalImage(location))],
                new Dictionary<string, string>(),
                Variants: Variants);
        }
    }

    private sealed class OneSourceRegistry(ICatalogSourceConnector connector) : ICatalogSourceRegistry
    {
        public IReadOnlyCollection<string> Sources => [connector.Source];

        public ICatalogSourceConnector Get(string source) => connector;
    }

    /// <summary>Every location opens, except the one called "missing".</summary>
    private sealed class ReadableImages : IExternalImageReader
    {
        public Task<StoredImage?> OpenAsync(ExternalImage image, CancellationToken ct = default) =>
            Task.FromResult(image.Location == Missing
                ? null
                : new StoredImage(new MemoryStream(Encoding.UTF8.GetBytes(image.Location.AbsolutePath)), "image/webp"));
    }

    private sealed class ContentAddressedStore : IImageStore
    {
        public async Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default)
        {
            using var reader = new StreamReader(content);
            return new ImageId(Convert.ToHexStringLower(Encoding.UTF8.GetBytes(await reader.ReadToEndAsync(ct))));
        }

        public Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default) =>
            Task.FromResult<StoredImage?>(null);
    }

    private sealed class NoAttributeDefinitions : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AttributeDefinitions.Empty);
    }
}
