using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using Microsoft.Extensions.AI;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace ElGuerre.Tendero.Search.Qdrant;

/// <summary>
/// The products as vectors in Qdrant: one point per (product, culture).
///
/// **Per product and not per variant**, which is the roadmap's B1 argument made
/// concrete: variants share the name and the description that get embedded, so a
/// point per variant would let one product take eight nearest-neighbour slots with
/// eight copies of itself. The lexical side collapses its variants to products
/// (ADR 0015), so both rankings are lists of the same unit and fuse without a
/// reconciliation step.
///
/// **A mixed vector space is refused three times** (roadmap B2). The collection is
/// named after the model, its revision and its dimensions, so another model has a
/// different collection. The layer check refuses a collection of the wrong size or
/// a model server serving another revision. And every point carries the model
/// that wrote it, asserted when it is read — the check that still holds if
/// somebody writes into the right collection from the wrong process.
/// </summary>
internal sealed class QdrantProductVectors(
    QdrantClient client,
    IEmbeddingGenerator<string, Embedding<float>> generator,
    EmbeddingModelDescriptor model,
    QdrantVectorLayer layer,
    IAttributeDefinitionReader attributeDefinitions,
    ICategoryReader categories) : IProductVectorIndexer, IVectorProductSearch
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public Task IndexAsync(Product product, CancellationToken ct = default) =>
        GuardAsync($"embedding {product.Id}", async () =>
        {
            using var activity = Telemetry.StartActivity("search.vectors.index");
            activity?.SetTag("search.product_id", product.Id.ToString());

            await EnsureAvailableAsync($"embedding {product.Id}", ct);

            var definitions = await attributeDefinitions.AllAsync(ct);
            var tree = await categories.AllAsync(ct);

            var texts = new List<(string Culture, Guid Point, string Text, string Fingerprint, string[] Branch)>();

            foreach (var culture in SearchCultures.Supported)
            {
                // The product-level fields are the same on every variant's
                // document, so the first one carries them. No document means no
                // variant can be bought, and the lexical index holds none either.
                if (ProductSearchDocument.ForVariants(product, culture, definitions, tree).FirstOrDefault() is not { } document)
                {
                    await DeleteAsync(product.Id, ct);
                    return;
                }

                var text = VectorText.For(document);
                texts.Add((culture, VectorPointId.For(product.Id, culture), text,
                    VectorText.Fingerprint(text, model), [.. document.CategoryCodes]));
            }

            // An unchanged text is not embedded again. A product raises
            // ProductUpserted on every mutation — about eight per imported product
            // (the standing backlog's outbox row) — and most of them change nothing
            // a shopper reads: a price, an image, a stock count. On a CPU an
            // embedding costs half a second, so this is the difference between an
            // import that re-embeds the catalogue eight times and one that does not.
            var stored = await client.RetrieveAsync(
                model.CollectionName, [.. texts.Select(text => (PointId)text.Point)],
                withPayload: true, withVectors: false, cancellationToken: ct);

            var current = stored
                .Where(point => point.Payload.TryGetValue(PointFields.Fingerprint, out var fingerprint)
                                && texts.Any(text => text.Point.ToString() == point.Id.Uuid && text.Fingerprint == fingerprint.StringValue))
                .Select(point => point.Id.Uuid)
                .ToHashSet(StringComparer.Ordinal);

            var changed = texts.Where(text => !current.Contains(text.Point.ToString())).ToList();
            activity?.SetTag("search.vectors.embedded", changed.Count);

            if (changed.Count == 0)
                return;

            var embeddings = await generator.GenerateAsync(changed.Select(text => text.Text), cancellationToken: ct);

            var points = changed.Select((text, index) => new PointStruct
            {
                Id = text.Point,
                Vectors = Checked(embeddings[index].Vector).ToArray(),
                Payload =
                {
                    [PointFields.ProductId] = product.Id.ToString(),
                    [PointFields.Culture] = text.Culture,
                    [PointFields.ModelId] = model.ModelId,
                    [PointFields.ModelRevision] = model.Revision,
                    [PointFields.CategoryCodes] = text.Branch,
                    [PointFields.Fingerprint] = text.Fingerprint,
                    [PointFields.Text] = text.Text
                }
            }).ToList();

            await client.UpsertAsync(model.CollectionName, points, cancellationToken: ct);
        });

    public Task RemoveAsync(ProductId productId, CancellationToken ct = default) =>
        GuardAsync($"removing {productId}", async () =>
        {
            await EnsureAvailableAsync($"removing {productId}", ct);
            await DeleteAsync(productId, ct);
        });

    /// <summary>
    /// Drops the collection and creates it again from the descriptor.
    ///
    /// It deliberately does not ask the layer first: a collection of the wrong
    /// size is exactly what makes the layer unavailable, and this is the repair.
    /// </summary>
    public Task RecreateAsync(CancellationToken ct = default) =>
        GuardAsync("recreating the vector collection", async () =>
        {
            if (await client.CollectionExistsAsync(model.CollectionName, ct))
                await client.DeleteCollectionAsync(model.CollectionName, cancellationToken: ct);

            layer.Forget();
            await layer.CreateCollectionAsync(ct);
            await EnsureAvailableAsync("recreating the vector collection", ct);
        });

    public Task<bool> IsEmptyAsync(CancellationToken ct = default) =>
        GuardAsync("counting vectors", async () =>
        {
            await EnsureAvailableAsync("counting vectors", ct);
            return await client.CountAsync(model.CollectionName, exact: true, cancellationToken: ct) == 0;
        });

    public Task<IReadOnlyList<VectorMatch>> SearchAsync(
        string text, string culture, string? category, int limit, CancellationToken ct = default) =>
        GuardAsync<IReadOnlyList<VectorMatch>>("vector search", async () =>
        {
            await EnsureAvailableAsync("vector search", ct);

            var query = await generator.GenerateAsync([text], cancellationToken: ct);

            Filter filter = Conditions.MatchKeyword(PointFields.Culture, culture);

            // The branch, like the lexical filter: a point carries every code
            // above its category, and matching one element of an array is a match.
            if (!string.IsNullOrWhiteSpace(category))
                filter &= Conditions.MatchKeyword(PointFields.CategoryCodes, category);

            var points = await client.SearchAsync(
                model.CollectionName,
                Checked(query[0].Vector),
                filter,
                limit: (ulong)limit,
                payloadSelector: new[] { PointFields.ProductId, PointFields.ModelId, PointFields.ModelRevision },
                cancellationToken: ct);

            return [.. points.Select(point => new VectorMatch(ProductOf(point.Payload), point.Score))];
        });

    public Task<IReadOnlyDictionary<string, string>> TextsAsync(
        IReadOnlyCollection<string> productIds, string culture, CancellationToken ct = default) =>
        GuardAsync<IReadOnlyDictionary<string, string>>("reading embedded texts", async () =>
        {
            await EnsureAvailableAsync("reading embedded texts", ct);

            var ids = productIds
                .Select(id => (PointId)VectorPointId.For(new ProductId(Guid.Parse(id)), culture))
                .ToList();

            var points = await client.RetrieveAsync(
                model.CollectionName, ids, withPayload: true, withVectors: false, cancellationToken: ct);

            return points.ToDictionary(
                point => ProductOf(point.Payload),
                point => point.Payload.TryGetValue(PointFields.Text, out var value) ? value.StringValue : "",
                StringComparer.Ordinal);
        });

    private async Task DeleteAsync(ProductId productId, CancellationToken ct) =>
        await client.DeleteAsync(
            model.CollectionName,
            [.. SearchCultures.Supported.Select(culture => VectorPointId.For(productId, culture))],
            cancellationToken: ct);

    private async Task EnsureAvailableAsync(string operation, CancellationToken ct)
    {
        var status = await layer.CheckAsync(ct);
        if (!status.IsAvailable)
            throw new SearchUnavailableException(operation, status.Reason);
    }

    /// <summary>
    /// The third refusal of a mixed space: a point written by another model is not
    /// ranked. The layer check proved the SERVER serves the right revision; this
    /// proves the POINT was written by it.
    /// </summary>
    private string ProductOf(IDictionary<string, Value> payload)
    {
        var writtenBy = payload.TryGetValue(PointFields.ModelId, out var id) ? id.StringValue : "";
        var revision = payload.TryGetValue(PointFields.ModelRevision, out var sha) ? sha.StringValue : "";

        if (writtenBy != model.ModelId || revision != model.Revision)
        {
            throw new SearchUnavailableException(
                "vector search",
                $"a point in {model.CollectionName} was written by {writtenBy}@{revision}, not by " +
                $"{model.ModelId}@{model.Revision}; ranking it would mix two vector spaces");
        }

        return payload[PointFields.ProductId].StringValue;
    }

    private ReadOnlyMemory<float> Checked(ReadOnlyMemory<float> vector) =>
        vector.Length == model.Dimensions
            ? vector
            : throw new SearchUnavailableException(
                "embedding",
                $"the model server returned {vector.Length} dimensions and {model.ModelId} has {model.Dimensions}");

    /// <summary>
    /// Qdrant's and the model server's own failures, as the one exception the
    /// search side knows: an engine that did not answer is a retryable 503 on the
    /// reindex endpoint and a degradation on the search path, never a 500.
    /// </summary>
    private static async Task<T> GuardAsync<T>(string operation, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (exception is not (SearchUnavailableException or OperationCanceledException))
        {
            throw new SearchUnavailableException(operation, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static Task GuardAsync(string operation, Func<Task> action) =>
        GuardAsync(operation, async () =>
        {
            await action();
            return true;
        });
}

/// <summary>The payload's field names, in one place.</summary>
internal static class PointFields
{
    public const string ProductId = "productId";
    public const string Culture = "culture";
    public const string ModelId = "modelId";
    public const string ModelRevision = "modelRevision";
    public const string CategoryCodes = "categoryCodes";
    public const string Fingerprint = "fingerprint";
    public const string Text = "text";
}

/// <summary>
/// A point's id, derived rather than stored.
///
/// Qdrant ids are unsigned integers or UUIDs, and a product needs two points —
/// one per culture — so the product's own id cannot be used as it is. Deriving it
/// from (product, culture) means removing a product is deleting two ids this code
/// can compute, with no lookup and nothing to keep in step: the same reason the
/// lexical index sweeps by product id rather than remembering document ids.
/// </summary>
internal static class VectorPointId
{
    public static Guid For(ProductId product, string culture)
    {
        var hash = SHA256.HashData([.. product.Value.ToByteArray(), .. Encoding.UTF8.GetBytes(culture)]);
        var bytes = hash.AsSpan(0, 16).ToArray();

        // RFC 9562 version 8, "custom": the bytes are a hash and say so, rather
        // than dressing up as a v4 somebody could mistake for random.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }
}

/// <summary>What a product is embedded from, in one culture.</summary>
internal static class VectorText
{
    /// <summary>
    /// The words a shopper would read about the product, most identifying first:
    /// the name, the brand, the whole category branch, the attributes and then the
    /// description. The same rendered text the lexical index analyses, so "navy
    /// blue" reaches the English vector for the reason it reaches the English
    /// index — and a change to how attributes are rendered changes both.
    /// </summary>
    public static string For(ProductSearchDocument document) =>
        string.Join(". ",
            new[] { document.Name, document.Brand, document.CategoryPathText, document.AttributesText, document.Description }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim()));

    /// <summary>The text and the model that embedded it: either changing is a reason to embed again.</summary>
    public static string Fingerprint(string text, EmbeddingModelDescriptor model) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{model.ModelId}@{model.Revision}\n{text}")));
}
