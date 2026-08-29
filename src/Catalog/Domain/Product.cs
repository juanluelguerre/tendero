using Tendero.SharedKernel;

namespace Tendero.Catalog.Domain;

public enum ProductStatus
{
    Draft,      // importado o generado por IA, pendiente de revisión
    Active,     // visible en storefront, indexable
    Archived    // fuera de catálogo, se mantiene por histórico de pedidos
}

public sealed record ProductImage(Uri Url, string? Alt, int SortOrder);

// La clave de los N conectores: (origen, id externo). Ej.: ("shopify", "gid://shopify/Product/123")
public sealed record ExternalReference(string Source, string ExternalId);

// Evento que consume el worker de indexación (Elasticsearch + Qdrant) vía Outbox.
public sealed record ProductUpserted(ProductId ProductId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record ProductArchived(ProductId ProductId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Product : AggregateRoot
{
    private readonly List<ProductImage> _images = [];
    private readonly Dictionary<string, string> _attributes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ExternalReference> _externalReferences = [];

    public ProductId Id { get; private set; }
    public LocalizedText Name { get; private set; } = default!;
    public LocalizedText Slug { get; private set; } = default!;
    public LocalizedText? Description { get; private set; }
    public string? Brand { get; private set; }
    public string? Category { get; private set; }
    public Money Price { get; private set; }
    public ProductStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ProductImage> Images => _images;
    public IReadOnlyDictionary<string, string> Attributes => _attributes;
    public IReadOnlyList<ExternalReference> ExternalReferences => _externalReferences;

    private Product() { } // EF Core

    public static Product Create(LocalizedText name, Money price, LocalizedText? description = null)
    {
        var now = DateTimeOffset.UtcNow;
        var product = new Product
        {
            Id = ProductId.New(),
            Name = name,
            Slug = Slugify(name),
            Description = description,
            Price = price,
            Status = ProductStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };
        product.Raise(new ProductUpserted(product.Id, now));
        return product;
    }

    public void UpdateDetails(LocalizedText name, LocalizedText? description, string? brand, string? category)
    {
        Name = name;
        Slug = Slugify(name);
        Description = description;
        Brand = brand;
        Category = category;
        Touch();
    }

    /// <summary>
    /// Añade o reemplaza la traducción de una cultura concreta.
    /// Es el punto de entrada del slice de enriquecimiento con IA
    /// (traducción automática + revisión humana en el backoffice).
    /// </summary>
    public void Localize(string culture, string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = Name.With(culture, name);
        Slug = Slugify(Name);
        if (description is not null)
            Description = (Description ?? LocalizedText.From(culture, description)).With(culture, description);
        Touch();
    }

    public void SetPrice(Money price)
    {
        if (price.Amount < 0)
            throw new InvalidOperationException("Price cannot be negative.");
        Price = price;
        Touch();
    }

    public void SetAttribute(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _attributes[name.Trim()] = value;
        Touch();
    }

    public void AddImage(Uri url, string? alt = null)
    {
        if (_images.Any(i => i.Url == url)) return; // idempotente
        _images.Add(new ProductImage(url, alt, _images.Count));
        Touch();
    }

    // Idempotente: reimportar desde el mismo origen no duplica referencias.
    public void LinkExternal(string source, string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        var reference = new ExternalReference(source.ToLowerInvariant(), externalId);
        if (!_externalReferences.Contains(reference))
        {
            _externalReferences.Add(reference);
            Touch();
        }
    }

    public void Publish()
    {
        if (Status == ProductStatus.Archived)
            throw new InvalidOperationException("Cannot publish an archived product.");
        Status = ProductStatus.Active;
        Touch();
    }

    public void Archive()
    {
        Status = ProductStatus.Archived;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new ProductArchived(Id, UpdatedAt)); // el worker lo saca de los índices
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new ProductUpserted(Id, UpdatedAt));
    }

    // Un slug por cultura, generado del nombre en esa cultura (SEO multilenguaje).
    private static LocalizedText Slugify(LocalizedText name) =>
        new(name.Values.ToDictionary(
            kv => kv.Key,
            kv => string.Join('-', kv.Value.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries))));
}
