using System.Text;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

public enum ProductStatus
{
    Draft,      // imported or AI-generated, awaiting review
    Active,     // visible in the storefront, indexable
    Archived    // out of the catalogue, kept for order history
}

/// <summary>One value on one of a product's variant axes: <c>COLOR</c>, <c>NAVY_BLUE</c>.</summary>
public sealed record AxisValue(string Axis, string Value);

/// <summary>
/// A product image. It stores the KEY in our own store, not a URL: the URL is
/// composed on read, so changing CDN or domain is not an UPDATE over millions of
/// rows. The alternative text is user-facing, hence LocalizedText (invariant 6):
/// a screen reader in English cannot read out Spanish.
/// </summary>
public sealed record ProductImage(ImageId Id, LocalizedText? Alt, int SortOrder)
{
    /// <summary>
    /// **What the picture shows of the product's variants**: at most one value of
    /// each axis — the colour, usually. A picture that says nothing shows the
    /// product and no variant in particular: the back, a detail, the box.
    ///
    /// The picture says it, and not the variant: a colour then has as many
    /// pictures as say so, every size of the colour has them without repeating
    /// anything, and which variant a picture is of is one fact in one place
    /// (`docs/analysis/catalogue-model.md`, proposal 1).
    ///
    /// Never null, whatever set it: a picture stored before pictures said
    /// anything has no such key in its JSON, and what is not said is nothing.
    /// </summary>
    public IReadOnlyList<AxisValue> Shows
    {
        get => field ?? [];
        init;
    }

    /// <summary>Whether a variant at these coordinates is one this picture shows: every value it names is the variant's.</summary>
    public bool IsOf(IReadOnlyDictionary<string, string> coordinates) =>
        Shows.Count > 0 && Shows.All(shown =>
            coordinates.TryGetValue(shown.Axis, out var value) && string.Equals(value, shown.Value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether it already shows exactly these values.</summary>
    public bool ShowsExactly(IReadOnlyList<AxisValue> values) =>
        Shows.Count == values.Count && Shows.All(values.Contains);

    /// <summary>
    /// Whether the picture is already described in exactly these words. Compared by
    /// the WORDS: <see cref="LocalizedText"/> has reference equality, and every
    /// import and every command builds a new one.
    /// </summary>
    public bool Says(LocalizedText? alt) =>
        Alt is null || alt is null
            ? Alt is null && alt is null
            : Alt.Values.Count == alt.Values.Count &&
              Alt.Values.All(pair => alt.Values.TryGetValue(pair.Key, out var other) && other == pair.Value);
}

// The key across N connectors: (source, external id). E.g. ("shopify", "gid://shopify/Product/123")
public sealed record ExternalReference(string Source, string ExternalId);

// The event the indexing worker consumes (Elasticsearch + Qdrant) via the Outbox.
public sealed record ProductUpserted(ProductId ProductId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record ProductArchived(ProductId ProductId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Product : AggregateRoot
{
    /// <summary>The longest name a translation may write. The seed's longest is 56
    /// characters; the column is jsonb, so this is the catalogue's rule, not the
    /// database's.</summary>
    public const int NameMaxLength = 200;

    public const int DescriptionMaxLength = 4000;

    /// <summary>What a picture shows, in one language, at most. A screen reader reads
    /// it whole, so it is a sentence or two; the limit is what refuses an essay.</summary>
    public const int ImageAltMaxLength = 300;

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
    ///
    /// **An axis is the variants' to say, never the product's.** A product sold in
    /// three colours holds no colour of its own: the pannier kept the one it had
    /// before, and its page read *oliva* with navy chosen (2026-10-15).
    /// </summary>
    public void SetAttribute(TimeProvider clock, AttributeValue value)
    {
        if (VariesBy(value.Code))
            throw new InvalidOperationException(
                $"The product varies by {value.Code}: its variants say it, and the product holds no value of its own.");

        this.attributes.RemoveAll(existing =>
            string.Equals(existing.Code, value.Code, StringComparison.OrdinalIgnoreCase));
        this.attributes.Add(value);
        Touch(clock);
    }

    /// <summary>Whether the product's variants are told apart by this attribute.</summary>
    public bool VariesBy(string code) =>
        this.variantAxes.Contains(code, StringComparer.OrdinalIgnoreCase);

    public AttributeValue? AttributeFor(string code) =>
        this.attributes.FirstOrDefault(value =>
            string.Equals(value.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The attributes its category requires and it has no value for (ADR 0056,
    /// amended) — what makes it *incomplete*. It is told, never refused: an
    /// incomplete product is imported, kept and may be published, with what it
    /// lacks shown.
    ///
    /// **An axis counts as held when a variant still sold holds it.** A shirt in
    /// two colours says no colour of its own — its variants say it — and it is
    /// not missing one.
    /// </summary>
    public IReadOnlyList<string> MissingRequired(CategoryTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        return
        [
            .. tree.RequiredOf(Category).Where(code =>
                AttributeFor(code) is null &&
                !Sold.Any(variant => variant.AxisValues.Keys.Contains(code, StringComparer.OrdinalIgnoreCase))),
        ];
    }

    // Idempotent by content identity: re-importing the same photo does not
    // duplicate it, because the content hash IS the key.
    public void AddImage(TimeProvider clock, ImageId id, LocalizedText? alt = null)
    {
        if (this.images.Any(i => i.Id == id)) return;
        this.images.Add(new ProductImage(id, alt, this.images.Count));
        Touch(clock);
    }

    /// <summary>
    /// The gallery becomes exactly this one, in this order — so the first image
    /// is the cover. What the source no longer lists leaves the product; the
    /// bytes stay in the content-addressed store, where an order that
    /// snapshotted one still finds it.
    ///
    /// It exists because <see cref="AddImage"/> only ever appends, and the cover
    /// is the lowest sort order: when the seed replaced six placeholders with
    /// illustrations, a re-import put each illustration BEHIND its placeholder
    /// and the placeholder stayed on the cover for good (UI review, 2026-09-11).
    ///
    /// The same gallery again is not a change — no timestamp, no event — or every
    /// import would reindex the whole catalogue for nothing.
    /// </summary>
    public void ReplaceImages(TimeProvider clock, IReadOnlyList<(ImageId Id, LocalizedText? Alt)> gallery)
    {
        ArgumentNullException.ThrowIfNull(gallery);

        // What the source does not say, the product keeps: a seed with no alt text
        // must not erase the one a person approved (phase 13a). A source that does
        // describe its picture has the last word.
        var replacement = gallery
            .DistinctBy(image => image.Id)
            .Select((image, index) => new ProductImage(
                image.Id, image.Alt ?? this.images.FirstOrDefault(kept => kept.Id == image.Id)?.Alt, index)
            {
                // And what a picture that stays shows: the source's gallery says nothing of it.
                Shows = this.images.FirstOrDefault(kept => kept.Id == image.Id)?.Shows ?? [],
            })
            .ToList();

        if (replacement.SequenceEqual(this.images.OrderBy(image => image.SortOrder), SameImage.Instance))
            return;

        this.images.Clear();
        this.images.AddRange(replacement);
        Touch(clock);
    }

    /// <summary>
    /// Says what one of the product's pictures shows (phase 13a): what a shopkeeper
    /// types, and what a model proposes and a person approves. The picture keeps its
    /// place in the gallery. The same words again are not a change — no timestamp,
    /// no event.
    /// </summary>
    /// <returns>False when the product has no such picture.</returns>
    public bool DescribeImage(TimeProvider clock, ImageId id, LocalizedText alt)
    {
        ArgumentNullException.ThrowIfNull(alt);

        var at = this.images.FindIndex(image => image.Id == id);
        if (at < 0)
            return false;
        if (this.images[at].Says(alt))
            return true;

        this.images[at] = this.images[at] with { Alt = alt };
        Touch(clock);
        return true;
    }

    /// <summary>
    /// Says which of the product's variants a picture shows: a value of one or
    /// more of its axes, or nothing — and then the picture is the product's and
    /// no variant's in particular.
    ///
    /// **Only what somebody sells.** An axis the product does not vary by, or a
    /// value no live variant holds, would be a picture that shows on nothing: it
    /// is refused, not stored. The values are kept in the order of the product's
    /// axes and as its variants spell them. The same tag again is not a change.
    /// </summary>
    /// <returns>False when the product has no such picture.</returns>
    public bool Show(TimeProvider clock, ImageId picture, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var at = this.images.FindIndex(image => image.Id == picture);
        if (at < 0)
            return false;

        List<AxisValue> shown = [];
        foreach (var axis in this.variantAxes)
        {
            var named = values.FirstOrDefault(value => string.Equals(value.Key.Trim(), axis, StringComparison.OrdinalIgnoreCase));
            if (named.Key is null)
                continue;

            var held = Sold
                .Select(variant => variant.AxisValues.GetValueOrDefault(axis))
                .FirstOrDefault(value => string.Equals(value, named.Value.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"No variant of this product is {axis} = {named.Value}: a picture cannot show it.");

            shown.Add(new AxisValue(axis, held));
        }

        if (shown.Count != values.Count)
        {
            var unknown = values.Keys.Where(key => !this.variantAxes.Contains(key.Trim(), StringComparer.OrdinalIgnoreCase));
            throw new InvalidOperationException(
                $"This product does not vary by {string.Join(", ", unknown)}: a picture cannot show it. " +
                $"Its axes: {(this.variantAxes.Count == 0 ? "(none)" : string.Join(", ", this.variantAxes))}.");
        }

        if (this.images[at].ShowsExactly(shown))
            return true;

        this.images[at] = this.images[at] with { Shows = shown };
        Touch(clock);
        return true;
    }

    /// <summary>
    /// A variant's pictures: those of the gallery that say they show it, in the
    /// gallery's order, and then those that say nothing — a detail of the sole is
    /// every colour's. Derived, never stored: every size of a colour has the
    /// colour's pictures without any of them being written twice.
    /// </summary>
    public IReadOnlyList<ProductImage> PicturesOf(Variant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        var gallery = this.images.OrderBy(image => image.SortOrder).ToList();

        return [.. gallery.Where(image => image.IsOf(variant.AxisValues)), .. gallery.Where(image => image.Shows.Count == 0)];
    }

    /// <summary>
    /// The picture a cart line, a search result and an agent's feed show for a
    /// variant: the first of its own. With none of its own and none untagged —
    /// every picture is another colour's — the cover, which is still the product.
    /// </summary>
    public ProductImage? PictureOf(Variant variant) =>
        PicturesOf(variant) is [var first, ..] ? first : PrimaryImage;

    /// <summary>The variants somebody can buy: what a picture may be said to show.</summary>
    private IEnumerable<Variant> Sold => this.variants.Where(variant => variant.Status != VariantStatus.Discontinued);

    /// <summary>A picture that shows a value no variant is sold in any more goes back to being the product's.</summary>
    private void ForgetWhatNobodySells()
    {
        for (var at = 0; at < this.images.Count; at++)
        {
            var image = this.images[at];
            if (image.Shows.Count > 0 && !Sold.Any(variant => image.IsOf(variant.AxisValues)))
                this.images[at] = image with { Shows = [] };
        }
    }

    /// <summary>
    /// Two gallery entries are the same when their id, position and alt text
    /// match. The alt is compared by its WORDS: <see cref="LocalizedText"/> has
    /// reference equality, and every import builds a new one, so comparing the
    /// records as they are would call an untouched gallery a change.
    /// </summary>
    private sealed class SameImage : IEqualityComparer<ProductImage>
    {
        public static readonly SameImage Instance = new();

        public bool Equals(ProductImage? a, ProductImage? b) =>
            a is not null && b is not null &&
            a.Id == b.Id &&
            a.SortOrder == b.SortOrder &&
            a.Says(b.Alt) &&
            a.ShowsExactly(b.Shows);

        public int GetHashCode(ProductImage image) => HashCode.Combine(image.Id, image.SortOrder);
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

        // From here the variants say each axis: a value the product held on one
        // of them would contradict every variant but one.
        this.attributes.RemoveAll(existing => VariesBy(existing.Code));

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
        string? taxClass = null)
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

        var variant = Variant.Create(sku, price, values, taxClass);
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
        ForgetWhatNobodySells();
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

    /// <summary>
    /// Takes a live product off sale and back to the review queue: a supplier's
    /// recall, a wrong price, a description to check again. Draft is where a
    /// product waits for a person, so it is where a withdrawn one goes, and only
    /// PublishProduct brings it back (ADR 0012). The index projection already
    /// removes a product that is no longer Active.
    /// </summary>
    public void Withdraw(TimeProvider clock)
    {
        if (Status != ProductStatus.Active)
            throw new InvalidOperationException($"Only an active product can be withdrawn; this one is {Status}.");
        Status = ProductStatus.Draft;
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
