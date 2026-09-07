using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

public sealed record CategoryChanged(string Code, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// A catalogue category, with a name per culture.
///
/// `Product.Category` used to be the string `COOKWARE` and nothing else. That
/// code is nobody's vocabulary: "induction cookware" did not match because the
/// index held no word "cookware", it held a code. The field was even taken out
/// of the searchable fields for that reason — mapped as a `keyword`, it promised
/// a match that could never happen.
///
/// The CODE is still the identity and does not change; what is added is the
/// translatable name and the place in the tree.
/// </summary>
public sealed class Category : AggregateRoot
{
    public string Code { get; private set; } = default!;
    public LocalizedText Name { get; private set; } = default!;
    public string? ParentCode { get; private set; }

    /// <summary>
    /// The full path of codes, <c>HOME/KITCHEN/COOKWARE</c>. Stored rather than
    /// walked, because the index needs the whole branch — "Hogar Cocina Menaje"
    /// — and climbing by pointers on every projection would be one query per
    /// level per product.
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

    /// <summary>The branch's codes, from the root down to this one.</summary>
    public IReadOnlyList<string> Ancestry => Path.Split('/');
}

/// <summary>
/// The whole tree, resolvable by code. Same as with the attributes: they are
/// dozens of rows that change rarely, and a projection querying per product
/// would be N+1 on every reindex.
/// </summary>
public sealed class CategoryTree(IReadOnlyList<Category> categories)
{
    public static readonly CategoryTree Empty = new([]);

    private readonly Dictionary<string, Category> byCode =
        categories.ToDictionary(category => category.Code, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Category> All { get; } = categories;

    public Category? ByCode(string? code) =>
        code is null ? null : this.byCode.GetValueOrDefault(AttributeDefinition.Normalise(code));

    /// <summary>
    /// A category's searchable text in one culture: the names of its whole
    /// branch, "Hogar Cocina Menaje de cocina". The whole branch and not just
    /// the leaf, because somebody searching "cocina" expects to find what is inside it.
    /// </summary>
    /// <summary>
    /// A category's whole branch as CODES, root first: `["HOME", "KITCHEN",
    /// "COOKWARE"]`.
    ///
    /// It is the filtering half of what `PathTextIn` does for matching, and it
    /// exists for the same reason: somebody browsing "Cocina" expects what is
    /// inside it. A term filter on the leaf alone would answer "Cocina" with the
    /// products filed directly under it and none of the twenty-five below,
    /// which is a shop that hides its own stock.
    /// </summary>
    public IReadOnlyList<string> BranchCodes(string? code) =>
        ByCode(code) is { } category ? category.Ancestry : [];

    public string? PathTextIn(string? code, string culture)
    {
        var category = ByCode(code);
        if (category is null)
            return null;

        var names = category.Ancestry
            .Select(ancestor => ByCode(ancestor)?.Name.In(culture))
            .Where(name => !String.IsNullOrWhiteSpace(name));

        return String.Join(' ', names);
    }
}
