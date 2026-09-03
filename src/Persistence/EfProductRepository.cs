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
    /// The product a URL names, in two steps: SQL resolves the id, EF materialises
    /// the aggregate.
    ///
    /// The first step has to be SQL because <c>slug</c> is jsonb behind a value
    /// converter — EF sees a <see cref="LocalizedText"/>, Postgres sees an object,
    /// and no expression tree bridges the two.
    ///
    /// The second step is a separate query because EF Core 11 preview cannot
    /// shape a complex JSON collection over a <c>FromSql</c> source: the product
    /// carries its images that way, and the first version of this threw
    /// <c>NullReferenceException</c> inside <c>GenerateComplexJsonShaper</c>. The
    /// split is not only the way round it, though — it is the better shape. SQL
    /// does the one thing only SQL can do here, and materialising an aggregate
    /// with its variants and its JSON columns stays on the path EF is good at and
    /// the rest of this class already uses.
    ///
    /// The column is aliased "Value" because <c>SqlQuery</c> wraps this as a
    /// subquery and projects that one name; without the alias Postgres answers
    /// <c>42703: column s.Value does not exist</c>, which is an error about our
    /// SQL that never mentions ours.
    ///
    /// The identifiers are snake_case because <c>SnakeCaseNames</c> rewrites the
    /// whole model on build, and hand-written SQL is the one place the convention
    /// is not applied for you. Written in PascalCase first, it compiled, passed
    /// every unit test, and failed only against a real database — which is what
    /// the integration test exists to make happen at once rather than later.
    ///
    /// The two halves of the WHERE cost differently. Containment on the requested
    /// culture is the one that runs in practice, because every link the shop
    /// emits is already in the right language, and it is indexed by
    /// <c>ix_products_slug</c> — a GIN index over the whole column, which stays
    /// correct when a third culture arrives where an expression index per culture
    /// would not. Walking the object's values is NOT indexable; it is the
    /// redirect path, somebody following an English link to a Spanish slug. At
    /// six products it is free, and the day it is not the answer is a
    /// (culture, slug) table rather than a cleverer query.
    ///
    /// The ORDER BY is the tie-break. Two products can only collide here if they
    /// slugify to the same string, and when they do, the one that owns the slug in
    /// the culture being asked for is the one that URL means.
    /// </summary>
    public async Task<Product?> FindBySlugAsync(string slug, string culture, CancellationToken ct)
    {
        var id = await context.Database
            .SqlQuery<Guid>(
                $"""
                 SELECT p.id AS "Value" FROM catalog.products p
                 WHERE p.slug @> jsonb_build_object({culture}::text, {slug}::text)
                    OR EXISTS (
                        SELECT 1 FROM jsonb_each_text(p.slug) entry WHERE entry.value = {slug})
                 ORDER BY (p.slug @> jsonb_build_object({culture}::text, {slug}::text)) DESC,
                          p.created_at
                 """)
            .FirstOrDefaultAsync(ct);

        return id == Guid.Empty ? null : await GetByIdAsync(new ProductId(id), ct);
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
