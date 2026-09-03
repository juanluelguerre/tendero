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
        ProductStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = context.Products.AsNoTracking();

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

        return new ProductPage(items, total);
    }

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

    // Same as above, and additionally untracked for a memory reason: the change
    // tracker would hold the full catalogue's 147k products for the whole
    // reindex, which is exactly what AsAsyncEnumerable avoids.
    public IAsyncEnumerable<Product> StreamAllAsync(CancellationToken ct) =>
        context.Products.AsNoTracking().OrderBy(product => product.CreatedAt).AsAsyncEnumerable();
}
