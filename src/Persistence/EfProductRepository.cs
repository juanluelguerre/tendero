using System.Runtime.CompilerServices;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// The adapter for the two Product ports that exist today. Two distinct
/// interfaces on purpose: importing needs to look up by external reference and
/// add; the index projection only needs to read by id.
/// </summary>
internal sealed class EfProductRepository(TenderoDbContext context)
    : IProductRepository, IProductReader, IProductCatalogReader
{
    // Two queries rather than one with in-memory paging: the total belongs to the
    // whole filter and not to the page, because a review queue needs to say how
    // many are left. Counting in SQL avoids pulling the catalogue in to discard it.
    public async Task<ProductPage> ListAsync(
        ProductStatus? status, string? text, string? missingCulture, int page, int pageSize, CancellationToken ct)
    {
        var query = context.Products.AsNoTracking();

        if (text?.Trim() is { Length: > 0 } typed)
        {
            var matching = Matching(typed);
            query = query.Where(product => matching.Contains((Guid)(object)product.Id));
        }

        if (missingCulture is { Length: > 0 } culture)
        {
            var missing = Missing(Culture.Normalize(culture));
            query = query.Where(product => missing.Contains((Guid)(object)product.Id));
        }

        // Per status before narrowing to one, as the orders list does: the
        // filters say what each would show for this text (the redesign's R-4.3).
        var counts = await query
            .GroupBy(product => product.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, ct);

        if (status is not null)
            query = query.Where(product => product.Status == status);

        var total = await query.CountAsync(ct);

        var items = await query
            // Most recently touched first: in a review queue, what has just been
            // imported is what is waiting for a decision.
            .OrderByDescending(product => product.UpdatedAt)
            .ThenBy(product => product.Id)   // a stable tie-break, or two pages repeat a row
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new ProductPage(items, total, counts);
    }

    public async Task<IReadOnlyList<string>> SkusOfProductsMatchingAsync(string text, CancellationToken ct)
    {
        if (text.Trim() is not { Length: > 0 } typed)
            return [];

        var matching = Matching(typed);
        var products = await context.Products
            .AsNoTracking()
            .Where(product => matching.Contains((Guid)(object)product.Id))
            .ToListAsync(ct);

        return [.. products.SelectMany(product => product.Variants.Select(variant => variant.Sku))];
    }

    /// <summary>The ids of the products whose name, in any culture, or code contains the text.</summary>
    private IQueryable<Guid> Matching(string typed)
    {
        // The name is jsonb behind a converter, so LINQ cannot reach inside
        // it. SQL can, over the VALUES only: `name::text` would also match
        // the keys, and typing "es" would find every product. Every culture
        // at once, which is what somebody typing a Spanish or an English name
        // wants. The ids that match are a subquery EF composes into this one
        // statement (`WHERE p.id IN (SELECT …)`); a FromSql over the whole
        // entity is what EF 11's preview cannot compose with its JSON
        // columns. The interpolated values are parameters, not text.
        //
        // **Accents are folded on both sides** (`unaccent`), as case is: the
        // author typed "portatil" and the products named "portátil" were not
        // there. The shop's own search folds them in its analyzer; this list
        // never passes through it.
        //
        // A sequential scan, and fine for a laboratory's hundred products;
        // the full catalogue wants a trigram index (the standing backlog).
        var pattern = $"%{Escaped(typed)}%";
        return context.Database.SqlQuery<Guid>(
            $"""
            SELECT p.id AS "Value" FROM catalog.products AS p
            WHERE unaccent(p.code) ILIKE unaccent({pattern}) ESCAPE '\'
               OR p.id::text = {typed.ToLowerInvariant()}
               OR EXISTS (SELECT 1 FROM jsonb_each_text(p.name) AS n WHERE unaccent(n.value) ILIKE unaccent({pattern}) ESCAPE '\')
            """);
    }

    public async Task<IReadOnlyList<Product>> LiveAsync(CancellationToken ct) =>
        await context.Products.AsNoTracking()
            .Where(product => product.Status != ProductStatus.Archived)
            .ToListAsync(ct);

    /// <summary>
    /// The ids of the products whose name has no value in <paramref name="culture"/> —
    /// the queue's "missing English" and Steward's gap (S-3a). A key test on the
    /// jsonb, the culture a parameter and not text; the same id subquery the text
    /// filter composes.
    /// </summary>
    private IQueryable<Guid> Missing(string culture) =>
        context.Database.SqlQuery<Guid>(
            $"""
            SELECT p.id AS "Value" FROM catalog.products AS p
            WHERE NOT (p.name ? {culture})
            """);

    /// <summary>What was typed, matched as typed: a % or _ in it is a character, not a wildcard.</summary>
    private static string Escaped(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>
    /// The product a URL names.
    ///
    /// It is ordinary LINQ over an indexed column, and that is the point of the
    /// code existing at all (ADR 0026). The version this replaced looked the
    /// URL up by slug, and because the slug lived in a jsonb column behind a
    /// value converter — EF seeing a <see cref="LocalizedText"/>, Postgres
    /// seeing an object, no expression tree bridging the two — it had to be
    /// hand-written SQL. That SQL cost three failures no unit test could reach:
    /// PascalCase identifiers against a snake_case schema, EF preview throwing
    /// inside <c>GenerateComplexJsonShaper</c> over a <c>FromSql</c> source
    /// carrying a complex JSON collection, and the <c>"Value"</c> column
    /// <c>SqlQuery</c> projects. All three were properties of looking a product
    /// up by a name-derived string, and all three went away with it.
    ///
    /// There is no tie-break here, and its absence is the improvement. Two
    /// products could slugify alike and the old query had to guess which one the
    /// URL meant; two products cannot share a code, because the unique index
    /// says so.
    /// </summary>
    public Task<Product?> FindByCodeAsync(string code, CancellationToken ct) =>
        context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(product => product.Code == code, ct);

    /// <summary>
    /// What a set of SKUs is called, in one query.
    ///
    /// It returns the variant's raw COORDINATES and renders nothing. Turning
    /// `{"COLOR": "NAVY_BLUE"}` into "azul marino" needs the attribute
    /// definitions, and a repository that held a translation catalogue would be
    /// a second renderer to keep in step with the product page's. The slice
    /// renders; this reads.
    /// </summary>
    public async Task<IReadOnlyList<SkuDescription>> DescribeSkusAsync(
        IReadOnlyCollection<string> skus, string culture, CancellationToken ct)
    {
        if (skus.Count == 0)
            return [];

        var wanted = skus.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var products = await context.Products
            .AsNoTracking()
            .Where(product => product.Variants.Any(variant => wanted.Contains(variant.Sku)))
            .ToListAsync(ct);

        return
        [
            .. products.SelectMany(product => product.Variants
                .Where(variant => wanted.Contains(variant.Sku))
                .Select(variant => new SkuDescription(
                    variant.Sku,
                    product.Code,
                    product.Name.In(culture),
                    product.Slug.In(culture),
                    product.VariantAxes,
                    variant.AxisValues)))
        ];
    }

    public Task<Product?> FindByExternalReferenceAsync(string source, string externalId, CancellationToken ct) =>
        context.Products.FirstOrDefaultAsync(
            product => product.ExternalReferences.Any(
                reference => reference.Source == source && reference.ExternalId == externalId),
            ct);

    public void Add(Product product) => context.Products.Add(product);

    // WITH tracking, unlike GetByIdAsync: whoever looks up by id from a slice is
    // doing it to mutate (publish, archive) and commit through IUnitOfWork. With
    // AsNoTracking the status change would be silently lost in SaveChanges.
    public Task<Product?> FindByIdAsync(ProductId id, CancellationToken ct) =>
        context.Products.FirstOrDefaultAsync(product => product.Id == id, ct);

    // The product a SKU belongs to, for the stock projection. AsNoTracking for
    // the same reason as below: the worker reads to project.
    public Task<Product?> FindBySkuAsync(string sku, CancellationToken ct) =>
        context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(product => product.Variants.Any(variant => variant.Sku == sku), ct);

    // AsNoTracking: the indexing worker reads to project, never to mutate.
    public Task<Product?> GetByIdAsync(ProductId id, CancellationToken ct) =>
        context.Products.AsNoTracking().FirstOrDefaultAsync(product => product.Id == id, ct);

    /// <summary>
    /// Every product, in batches, for the reindex to walk.
    ///
    /// **It reads in chunks rather than streaming, and that is a correctness
    /// fix and not a tuning one.** `AsAsyncEnumerable` keeps a reader open on
    /// the connection for the whole walk, and Npgsql has no MARS: the moment
    /// anything else asks the SAME `DbContext` a question mid-walk it throws
    /// `A command is already in progress`.
    ///
    /// Nothing did, until phase 4 gave the indexer an `IAvailabilityReader` —
    /// so projecting one product now asks Inventory whether its SKUs are in
    /// stock, on that same connection. Both halves were right; together they
    /// made the reindex endpoint a guaranteed 500, and no test saw it because
    /// the test double's reader is a list.
    ///
    /// The ids come back first in one query that finishes before anything is
    /// projected, and the products follow a chunk at a time. Memory stays
    /// bounded by the chunk, which is what `AsNoTracking` and the streaming
    /// were protecting: the change tracker holding 147k products for the whole
    /// reindex.
    ///
    /// At 147k the id list is a few megabytes and the walk is 1,470 queries.
    /// That is the shape the delivery plan already defers — "reindex as a
    /// background job", with its number recorded — and it is a different job
    /// from this one.
    /// </summary>
    public async IAsyncEnumerable<Product> StreamAllAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        const int batchSize = 100;

        var ids = await context.Products
            .AsNoTracking()
            .OrderBy(product => product.CreatedAt)
            .ThenBy(product => product.Id)
            .Select(product => product.Id)
            .ToListAsync(ct);

        foreach (var chunk in ids.Chunk(batchSize))
        {
            var batch = await context.Products
                .AsNoTracking()
                .Where(product => chunk.Contains(product.Id))
                .OrderBy(product => product.CreatedAt)
                .ThenBy(product => product.Id)
                .ToListAsync(ct);

            foreach (var product in batch)
                yield return product;
        }
    }
}
