using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

public sealed record CategoryChanged(string Code, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Una categoría del catálogo, con nombre por cultura.
///
/// Antes `Product.Category` era la cadena `COOKWARE` y nada más. Ese código no
/// es vocabulario de nadie: "induction cookware" no casaba porque en el índice
/// no había la palabra "cookware", había un código. El campo llegó a salir de
/// los campos buscables por eso mismo — estaba mapeado como `keyword` y prometía
/// una coincidencia que no podía ocurrir.
///
/// El CÓDIGO sigue siendo la identidad y no cambia; lo que se añade es el nombre
/// traducible y el sitio en el árbol.
/// </summary>
public sealed class Category : AggregateRoot
{
    public string Code { get; private set; } = default!;
    public LocalizedText Name { get; private set; } = default!;
    public string? ParentCode { get; private set; }

    /// <summary>
    /// La ruta completa de códigos, <c>HOME/KITCHEN/COOKWARE</c>. Se guarda en
    /// vez de recorrerse porque el índice necesita la rama entera —"Hogar Cocina
    /// Menaje"— y subir por punteros en cada proyección sería una consulta por
    /// nivel y por producto.
    /// </summary>
    public string Path { get; private set; } = default!;

    public int SortOrder { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Category() { } // EF Core

    public static Category Define(
        TimeProvider clock, string code, LocalizedText name, Category? parent = null, int sortOrder = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var normalised = AttributeDefinition.Normalise(code);
        var now = clock.GetUtcNow();

        var category = new Category
        {
            Code = normalised,
            Name = name,
            ParentCode = parent?.Code,
            Path = parent is null ? normalised : $"{parent.Path}/{normalised}",
            SortOrder = sortOrder,
            UpdatedAt = now
        };

        category.Raise(new CategoryChanged(normalised, now));
        return category;
    }

    public void Rename(TimeProvider clock, LocalizedText name)
    {
        Name = name;
        UpdatedAt = clock.GetUtcNow();
        Raise(new CategoryChanged(Code, UpdatedAt));
    }

    /// <summary>Los códigos de la rama, de la raíz a esta.</summary>
    public IReadOnlyList<string> Ancestry => Path.Split('/');
}

/// <summary>
/// El árbol entero, resoluble por código. Igual que con los atributos: son
/// decenas de filas que cambian poco, y una proyección que consultase por
/// producto haría N+1 en cada reindexado.
/// </summary>
public sealed class CategoryTree(IReadOnlyList<Category> categories)
{
    public static readonly CategoryTree Empty = new([]);

    private readonly Dictionary<string, Category> _byCode =
        categories.ToDictionary(category => category.Code, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Category> All { get; } = categories;

    public Category? ByCode(string? code) =>
        code is null ? null : _byCode.GetValueOrDefault(AttributeDefinition.Normalise(code));

    /// <summary>
    /// El texto buscable de una categoría en una cultura: los nombres de toda su
    /// rama, "Hogar Cocina Menaje de cocina". La rama entera y no sólo la hoja,
    /// porque quien busca "cocina" espera encontrar lo que hay dentro.
    /// </summary>
    public string? PathTextIn(string? code, string culture)
    {
        var category = ByCode(code);
        if (category is null)
            return null;

        var names = category.Ancestry
            .Select(ancestor => ByCode(ancestor)?.Name.In(culture))
            .Where(name => !string.IsNullOrWhiteSpace(name));

        return string.Join(' ', names);
    }
}
