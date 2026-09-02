using System.Text;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

public enum ProductStatus
{
    Draft,      // importado o generado por IA, pendiente de revisión
    Active,     // visible en storefront, indexable
    Archived    // fuera de catálogo, se mantiene por histórico de pedidos
}

/// <summary>
/// Una imagen del producto. Guarda la CLAVE en nuestro almacén, no una URL: la
/// URL se compone al leer, así que cambiar de CDN o de dominio no es un UPDATE
/// sobre millones de filas. El texto alternativo es de cara al usuario, luego
/// LocalizedText (invariante 6): un lector de pantalla en inglés no puede oír
/// español.
/// </summary>
public sealed record ProductImage(ImageId Id, LocalizedText? Alt, int SortOrder);

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

    /// <summary>
    /// La foto de portada: la de menor <see cref="ProductImage.SortOrder"/>. Vive
    /// aquí porque es una regla del catálogo, no de quien pinta: el listado del
    /// backoffice y el documento de búsqueda la calculaban por separado y uno de
    /// los dos se limitaba a coger la primera de la lista, así que el mismo
    /// producto podía enseñar una imagen distinta según por dónde se mirase.
    /// </summary>
    public ProductImage? PrimaryImage =>
        _images.Count == 0 ? null : _images.MinBy(image => image.SortOrder);
    public IReadOnlyDictionary<string, string> Attributes => _attributes;
    public IReadOnlyList<ExternalReference> ExternalReferences => _externalReferences;

    private Product() { } // EF Core

    /// <summary>
    /// Alta de un producto en Draft. Marca y categoría entran aquí y no en un
    /// <see cref="UpdateDetails"/> posterior: crear un producto es UN hecho, y
    /// partirlo en dos mutaciones emitía dos ProductUpserted por alta.
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
    /// Añade o reemplaza la traducción de una cultura concreta.
    /// Es el punto de entrada del slice de enriquecimiento con IA
    /// (traducción automática + revisión humana en el backoffice).
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

    public void SetPrice(TimeProvider clock, Money price)
    {
        if (price.Amount < 0)
            throw new InvalidOperationException("Price cannot be negative.");
        Price = price;
        Touch(clock);
    }

    public void SetAttribute(TimeProvider clock, string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _attributes[name.Trim()] = value;
        Touch(clock);
    }

    // Idempotente por identidad de contenido: reimportar la misma foto no la
    // duplica, porque el hash del contenido ES la clave.
    public void AddImage(TimeProvider clock, ImageId id, LocalizedText? alt = null)
    {
        if (_images.Any(i => i.Id == id)) return;
        _images.Add(new ProductImage(id, alt, _images.Count));
        Touch(clock);
    }

    // Idempotente: reimportar desde el mismo origen no duplica referencias.
    public void LinkExternal(TimeProvider clock, string source, string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        var reference = new ExternalReference(source.ToLowerInvariant(), externalId);
        if (!_externalReferences.Contains(reference))
        {
            _externalReferences.Add(reference);
            Touch(clock);
        }
    }

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
        Raise(new ProductArchived(Id, UpdatedAt)); // el worker lo saca de los índices
    }

    private void Touch(TimeProvider clock)
    {
        UpdatedAt = clock.GetUtcNow();
        Raise(new ProductUpserted(Id, UpdatedAt));
    }

    // Un slug por cultura, generado del nombre en esa cultura (SEO multilenguaje).
    private static LocalizedText Slugify(LocalizedText name) =>
        new(name.Values.ToDictionary(kv => kv.Key, kv => Slugify(kv.Value)));

    /// <summary>
    /// Plegado explícito de diacríticos. Lo natural sería
    /// <c>Normalize(NormalizationForm.FormD)</c> y descartar las marcas
    /// combinantes — y es lo que se escribió primero. No funciona: el repo
    /// compila con <c>InvariantGlobalization=true</c>, y ahí la normalización
    /// Unicode es un NO-OP silencioso. Devuelve la cadena intacta, sin excepción
    /// y sin aviso, así que el código parecía correcto y "café" seguía saliendo
    /// como <c>café</c> dentro de la URL.
    ///
    /// Una tabla es más fea y es la que sí funciona. Cubre lo que un catálogo
    /// es/en puede traer; lo que no esté aquí pasa a ser separador, que es el
    /// fallo seguro: un slug feo, nunca un carácter que haya que escapar.
    /// </summary>
    private const string Accented = "áàâäãåéèêëíìîïóòôöõúùûüñçýÿšžœæø";
    private const string Folded   = "aaaaaaeeeeiiiiooooouuuuncyyszoao";

    /// <summary>
    /// Letras y dígitos ASCII; todo lo demás separa. Partir sólo por espacios
    /// dejaba paréntesis y tildes dentro de la URL — "Cafetera Espresso
    /// (12 tazas)" salía como <c>cafetera-espresso-(12-tazas)</c>.
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
