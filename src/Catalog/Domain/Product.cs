using System.Text;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

public enum ProductStatus
{
    Draft,      // imported or AI-generated, awaiting review
    Active,     // visible in the storefront, indexable
    Archived    // out of the catalogue, kept for order history
}

/// <summary>
/// A product image. It stores the KEY in our own store, not a URL: the URL is
/// composed on read, so changing CDN or domain is not an UPDATE over millions of
/// rows. The alternative text is user-facing, hence LocalizedText (invariant 6):
/// a screen reader in English cannot read out Spanish.
/// </summary>
public sealed record ProductImage(ImageId Id, LocalizedText? Alt, int SortOrder);

// The key across N connectors: (source, external id). E.g. ("shopify", "gid://shopify/Product/123")
public sealed record ExternalReference(string Source, string ExternalId);

// The event the indexing worker consumes (Elasticsearch + Qdrant) via the Outbox.
public sealed record ProductUpserted(ProductId ProductId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record ProductArchived(ProductId ProductId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Product : AggregateRoot
{
    private readonly List<ProductImage> images = [];
    private readonly List<AttributeValue> attributes = [];
    private readonly List<ExternalReference> externalReferences = [];
    private readonly List<Variant> variants = [];
    private readonly List<string> variantAxes = [];

    public ProductId Id { get; private set; }

    /// <summary>
    /// What the URL carries, and the only identifier this product ever shows the
    /// public. Minted once and never regenerated — renaming the product changes
    /// its slug and leaves this alone, which is what keeps a rename from
    /// breaking every link to it (ADR 0026).
    /// </summary>
    public string Code { get; private set; } = default!;

    public LocalizedText Name { get; private set; } = default!;
    public LocalizedText Slug { get; private set; } = default!;
    public LocalizedText? Description { get; private set; }
    public string? Brand { get; private set; }

    /// <summary>
    /// When the SOURCE first listed it. Null when the source did not say.
    ///
    /// It is not `CreatedAt`. That one records when this shop imported the row,
    /// which for a seeded catalogue is the same second for all hundred of them —
    /// so a "new arrivals" row sorted by it would be sorting by import order and
    /// calling it news. This is a fact the supplier stated, and the shop repeats
    /// it.
    /// </summary>
    public DateOnly? AvailableFrom { get; private set; }
    public string? Category { get; private set; }
    public Money Price { get; private set; }
    public ProductStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ProductImage> Images => this.images;

    /// <summary>
    /// The cover photo: the one with the lowest <see cref="ProductImage.SortOrder"/>.
    /// It lives here because it is a catalogue rule and not a rendering one: the
    /// backoffice listing and the search document computed it separately, and one
    /// of the two simply took the first of the list — so the same product could
    /// show a different image depending on where you looked.
    /// </summary>
    public ProductImage? PrimaryImage =>
        this.images.Count == 0 ? null : this.images.MinBy(image => image.SortOrder);

    public IReadOnlyList<AttributeValue> Attributes => this.attributes;
    public IReadOnlyList<ExternalReference> ExternalReferences => this.externalReferences;

    public IReadOnlyList<Variant> Variants => this.variants;

    /// <summary>
    /// The axes that tell the variants apart, IN ORDER. The order is catalogue
    /// data — "azul marino · 38" and not "38 · azul marino" — and a dictionary
    /// cannot supply it.
    /// </summary>
    public IReadOnlyList<string> VariantAxes => this.variantAxes;

    /// <summary>
    /// The lowest and highest price among the available variants. It is what a
    /// product card shows ("24,90 – 29,90 €") and what the index needs to filter
    /// by range without promising combinations that do not exist.
    /// </summary>
    public (Money From, Money To) PriceRange
    {
        get
        {
            var available = this.variants
                .Where(variant => variant.Status == VariantStatus.Available)
                .ToArray();

            // With no available variants the product's own price is still the
            // honest answer: it is its default variant's.
            if (available.Length == 0)
                return (Price, Price);

            var ordered = available.OrderBy(variant => variant.Price.Amount).ToArray();
            return (ordered[0].Price, ordered[^1].Price);
        }
    }

    public Variant? VariantBySku(string sku) =>
        this.variants.FirstOrDefault(variant =>
            string.Equals(variant.Sku, sku, StringComparison.OrdinalIgnoreCase));

    private Product() { } // EF Core

    /// <summary>
    /// Creates a product in Draft. Brand and category go in here rather than in a
    /// later <see cref="UpdateDetails"/>: creating a product is ONE fact, and
    /// splitting it into two mutations emitted two ProductUpserted per creation.
    /// </summary>
    public static Product Create(
        TimeProvider clock,
        LocalizedText name,
        Money price,
        LocalizedText? description = null,
        string? brand = null,
        string? category = null)
    {
        var now = clock.GetUtcNow();
        var product = new Product
        {
            Id = ProductId.New(),
            Code = ProductCode.New(),
            Name = name,
            Slug = Slugify(name),
            Description = description,
            Brand = brand,
            Category = category,
            Price = price,
            Status = ProductStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };
        product.Raise(new ProductUpserted(product.Id, now));
        return product;
    }

    public void UpdateDetails(
        TimeProvider clock, LocalizedText name, LocalizedText? description, string? brand, string? category)
    {
        Name = name;
        Slug = Slugify(name);
        Description = description;
        Brand = brand;
        Category = category;
        Touch(clock);
    }

    /// <summary>
    /// Adds or replaces one culture's translation.
    /// This is the entry point for the AI enrichment slice (machine translation
    /// plus human review in the backoffice).
    /// </summary>
    public void Localize(TimeProvider clock, string culture, string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = Name.With(culture, name);
        Slug = Slugify(Name);
        if (description is not null)
            Description = (Description ?? LocalizedText.From(culture, description)).With(culture, description);
        Touch(clock);
    }

    /// <summary>
    /// What the source says about when it was first listed.
    ///
    /// It does NOT raise a status change and it does not un-publish anything: it
    /// is a fact about the product, not a decision about it — the same call
    /// `UpdateDetails` already makes on a re-import (ADR 0012).
    /// </summary>
    public void SetAvailableFrom(TimeProvider clock, DateOnly? availableFrom)
    {
        if (AvailableFrom == availableFrom)
            return;

        AvailableFrom = availableFrom;
        Touch(clock);
    }

    public void SetPrice(TimeProvider clock, Money price)
    {
        if (price.Amount < 0)
            throw new InvalidOperationException("Price cannot be negative.");
        Price = price;
        Touch(clock);
    }

    /// <summary>
    /// Adds or replaces an attribute's value, by code. It used to take two
    /// strings and accept anything: <c>SetAttribute("colour", "banana")</c> went
    /// straight through, and "azul marino" was literally the data — so the
    /// English index contained Spanish.
    ///
    /// Validating against the definition is the calling slice's job, not this
    /// one's: the aggregate does not know the definition catalogue, and making it
    /// know would turn every `SetAttribute` into a query.
    /// </summary>
    public void SetAttribute(TimeProvider clock, AttributeValue value)
    {
        this.attributes.RemoveAll(existing =>
            string.Equals(existing.Code, value.Code, StringComparison.OrdinalIgnoreCase));
        this.attributes.Add(value);
        Touch(clock);
    }

    public AttributeValue? AttributeFor(string code) =>
        this.attributes.FirstOrDefault(value =>
            string.Equals(value.Code, code, StringComparison.OrdinalIgnoreCase));

    // Idempotent by content identity: re-importing the same photo does not
    // duplicate it, because the content hash IS the key.
    public void AddImage(TimeProvider clock, ImageId id, LocalizedText? alt = null)
    {
        if (this.images.Any(i => i.Id == id)) return;
        this.images.Add(new ProductImage(id, alt, this.images.Count));
        Touch(clock);
    }

    // Idempotent: re-importing from the same source does not duplicate references.
    public void LinkExternal(TimeProvider clock, string source, string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        var reference = new ExternalReference(source.ToLowerInvariant(), externalId);
        if (!this.externalReferences.Contains(reference))
        {
            this.externalReferences.Add(reference);
            Touch(clock);
        }
    }

    /// <summary>
    /// Declares which axes the product varies by. It happens before variants are
    /// added, because it is what gives their values meaning — and order.
    /// </summary>
    public void DefineAxes(TimeProvider clock, IReadOnlyList<string> axes)
    {
        var normalised = axes.Select(axis => axis.Trim()).Where(axis => axis.Length > 0).ToArray();

        if (normalised.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalised.Length)
            throw new InvalidOperationException("Variant axes must be distinct.");

        // A variant with NO coordinates is not described by the axes at all, so
        // declaring them cannot invalidate it. That distinction is not academic:
        // importing mints an implicit `{externalId}-DEFAULT` variant on every
        // product (ADR 0015), and a guard that counted variants rather than
        // coordinated ones made this method unreachable for the entire shipped
        // catalogue — the slice existed, was tested, and could not run on a
        // single real product.
        var coordinated = this.variants.Where(variant => variant.AxisValues.Count > 0).ToArray();

        if (coordinated.Length > 0 && !normalised.SequenceEqual(this.variantAxes, StringComparer.OrdinalIgnoreCase))
        {
            // Changing the axes with live variants would leave each of them
            // described by coordinates that no longer mean the same thing.
            throw new InvalidOperationException(
                "Variant axes cannot change while variants exist. Discontinue them first.");
        }

        this.variantAxes.Clear();
        this.variantAxes.AddRange(normalised);

        // The default variant is superseded by the matrix about to be generated:
        // "this product has exactly one purchasable thing" stops being true the
        // moment it has axes. Discontinued and not removed, because an order
        // placed against it still names its SKU — which is what
        // VariantStatus.Discontinued exists for.
        if (normalised.Length > 0)
        {
            foreach (var placeholder in this.variants.Where(variant => variant.AxisValues.Count == 0))
                placeholder.Discontinue();
        }

        Touch(clock);
    }

    /// <summary>
    /// Adds a variant. Idempotent by SKU, like everything that can arrive twice
    /// from a connector.
    /// </summary>
    public Variant AddVariant(
        TimeProvider clock,
        string sku,
        Money price,
        IReadOnlyDictionary<string, string>? axisValues = null,
        string? taxClass = null,
        ImageId? image = null)
    {
        if (VariantBySku(sku) is { } existing)
            return existing;

        var values = axisValues ?? new Dictionary<string, string>();

        var unknown = values.Keys
            .Where(axis => !this.variantAxes.Contains(axis, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"Unknown variant axes: {string.Join(", ", unknown)}. Declared: " +
                $"{(this.variantAxes.Count == 0 ? "(none)" : string.Join(", ", this.variantAxes))}.");
        }

        // Two variants with the same coordinates are one variant with two SKUs,
        // and that turns the PDP's picker into a lottery.
        if (values.Count > 0 && this.variants.Any(variant => variant.Matches(values)))
        {
            throw new InvalidOperationException(
                $"A variant already exists for {string.Join(", ", values.Select(v => $"{v.Key}={v.Value}"))}.");
        }

        var variant = Variant.Create(sku, price, values, taxClass, image);
        this.variants.Add(variant);
        Touch(clock);
        return variant;
    }

    public void SetVariantPrice(TimeProvider clock, string sku, Money price)
    {
        Required(sku).SetPrice(price);
        Touch(clock);
    }

    public void DiscontinueVariant(TimeProvider clock, string sku)
    {
        Required(sku).Discontinue();
        Touch(clock);
    }

    public void SetVariantImage(TimeProvider clock, string sku, ImageId? image)
    {
        Required(sku).SetImage(image);
        Touch(clock);
    }

    private Variant Required(string sku) =>
        VariantBySku(sku) ?? throw new InvalidOperationException(
            $"No variant with SKU '{sku}' on product {Id}.");

    public void Publish(TimeProvider clock)
    {
        if (Status == ProductStatus.Archived)
            throw new InvalidOperationException("Cannot publish an archived product.");
        Status = ProductStatus.Active;
        Touch(clock);
    }

    public void Archive(TimeProvider clock)
    {
        Status = ProductStatus.Archived;
        UpdatedAt = clock.GetUtcNow();
        Raise(new ProductArchived(Id, UpdatedAt)); // the worker takes it out of the indexes
    }

    private void Touch(TimeProvider clock)
    {
        UpdatedAt = clock.GetUtcNow();
        Raise(new ProductUpserted(Id, UpdatedAt));
    }

    // One slug per culture, generated from the name in that culture (multilingual SEO).
    private static LocalizedText Slugify(LocalizedText name) =>
        new(name.Values.ToDictionary(kv => kv.Key, kv => Slugify(kv.Value)));

    /// <summary>
    /// Explicit diacritic folding. The natural thing would be
    /// <c>Normalize(NormalizationForm.FormD)</c> followed by dropping the
    /// combining marks — and that is what was written first. It does not work:
    /// the repository builds with <c>InvariantGlobalization=true</c>, and there
    /// Unicode normalisation is a silent NO-OP. It returns the string intact,
    /// with no exception and no warning, so the code looked correct while "café"
    /// kept coming out as <c>café</c> inside the URL.
    ///
    /// A table is uglier and it is the one that works. It covers what an es/en
    /// catalogue can bring; anything not here becomes a separator, which is the
    /// safe failure: an ugly slug, never a character that has to be escaped.
    /// </summary>
    private const string Accented = "áàâäãåéèêëíìîïóòôöõúùûüñçýÿšžœæø";
    private const string Folded = "aaaaaaeeeeiiiiooooouuuuncyyszoao";

    /// <summary>
    /// ASCII letters and digits; everything else separates. Splitting on spaces
    /// alone left brackets and accents inside the URL — "Cafetera Espresso
    /// (12 tazas)" came out as <c>cafetera-espresso-(12-tazas)</c>.
    /// </summary>
    private static string Slugify(string name)
    {
        var words = new List<string>();
        var word = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            var lowered = char.ToLowerInvariant(character);
            var index = Accented.IndexOf(lowered, StringComparison.Ordinal);
            if (index >= 0)
                lowered = Folded[index];

            if (char.IsAsciiLetterOrDigit(lowered))
            {
                word.Append(lowered);
                continue;
            }

            if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
            words.Add(word.ToString());

        return string.Join('-', words);
    }

}
