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
///
/// **And the attributes its products have** (ADR 0056): a category declares
/// which of the catalogue's attributes belong to it, and a category below adds
/// its own. That is what a product of the category is offered — to the extractor
/// that reads its description, to the questions its picture is asked — and it
/// is not a rule: a product may hold an attribute its category never declared.
///
/// **And which of them a product must have** (ADR 0056, amended): a declared
/// attribute is *expected* or *required*. Required is still not a rule an
/// import is refused by — a product without one is kept, and shown as
/// incomplete (<see cref="Product.MissingRequired"/>).
/// </summary>
public sealed class Category : AggregateRoot
{
    private readonly List<string> attributes = [];
    private readonly List<string> required = [];

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

    /// <summary>
    /// The attributes this category declares for itself, by code, in the order
    /// declared. What its products have is these and its ancestors':
    /// <see cref="CategoryTree.AttributesOf"/>.
    /// </summary>
    public IReadOnlyList<string> Attributes => this.attributes;

    /// <summary>
    /// Those of <see cref="Attributes"/> a product of this category must have,
    /// as this category says it. What a product must have is these and its
    /// ancestors': <see cref="CategoryTree.RequiredOf"/>.
    /// </summary>
    public IReadOnlyList<string> Required => this.required;

    private Category() { } // EF Core

    /// <param name="required">The declared attributes a product must have. One named here and not in
    /// <paramref name="attributes"/> is declared by being required: nothing is required that is not offered.</param>
    public static Category Define(
        TimeProvider clock, string code, LocalizedText name, Category? parent = null, int sortOrder = 0,
        IEnumerable<string>? attributes = null, IEnumerable<string>? required = null)
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

        category.required.AddRange((required ?? []).Select(AttributeDefinition.Normalise).Distinct(StringComparer.Ordinal));
        category.attributes.AddRange(
            (attributes ?? []).Select(AttributeDefinition.Normalise).Concat(category.required).Distinct(StringComparer.Ordinal));

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
    /// The deepest category every one of these is in, itself or below it (phase
    /// 16, P16-11): how a store's collection becomes a category. Null when they
    /// share none — or one has no category, or one this tree does not know —
    /// because a collection that is no category's page is said, never guessed.
    /// </summary>
    public string? SharedBy(IEnumerable<string?> codes)
    {
        IReadOnlyList<string>? shared = null;
        foreach (var code in codes)
        {
            if (ByCode(code) is not { } category)
                return null;

            shared = shared is null
                ? category.Ancestry
                : [.. shared.Zip(category.Ancestry).TakeWhile(pair => pair.First == pair.Second).Select(pair => pair.First)];
        }

        return shared is { Count: > 0 } ? shared[^1] : null;
    }

    /// <summary>
    /// The attributes a product of this category has: what its whole branch
    /// declares, root first, each code once (ADR 0056).
    ///
    /// **Null when the branch declares nothing** — or there is no such category.
    /// That is not "no attributes": nobody has said. Whoever asks then offers
    /// what it offered before there were declarations, so a catalogue that has
    /// none keeps working, and one that is half declared is not half silent.
    /// </summary>
    public IReadOnlyList<string>? AttributesOf(string? code)
    {
        if (ByCode(code) is not { } category)
            return null;

        string[] declared =
        [
            .. category.Ancestry
                .SelectMany(ancestor => ByCode(ancestor)?.Attributes ?? [])
                .Distinct(StringComparer.Ordinal),
        ];

        return declared.Length == 0 ? null : declared;
    }

    /// <summary>
    /// The attributes a product of this category must have: what its whole
    /// branch says is required, root first, each code once (ADR 0056, amended).
    ///
    /// A category below can raise what an ancestor only expects, by requiring
    /// it, and cannot lower what an ancestor requires — there is no way to say
    /// *not required*, so the union is the rule. Empty, not null, when nobody
    /// requires anything: unlike what is offered, *nothing is required* is an
    /// answer, and it is what a catalogue with no declarations means.
    /// </summary>
    public IReadOnlyList<string> RequiredOf(string? code) =>
        ByCode(code) is { } category
            ? [.. category.Ancestry.SelectMany(ancestor => ByCode(ancestor)?.Required ?? []).Distinct(StringComparer.Ordinal)]
            : [];

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
