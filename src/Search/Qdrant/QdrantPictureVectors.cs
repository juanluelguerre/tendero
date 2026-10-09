using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace ElGuerre.Tendero.Search.Qdrant;

/// <summary>
/// The catalogue's pictures as vectors in Qdrant (phase 13a, P13-3): **one point
/// a picture**, keyed on the picture's id.
///
/// A picture's id is the hash of its bytes, so the same file under two products
/// is one point whose payload names both — embedded once — and a product that
/// raises <c>ProductUpserted</c> for a price or a stock count finds its pictures
/// already there and embeds nothing. That is the dedup the roadmap called *for
/// free*: it is free because of how the store is keyed, not because anybody
/// compares anything.
///
/// **Never mixed with the words' vectors, or with another model's.** The
/// collection is named for the picture model, its revision and its dimensions —
/// not the words' collection, whose model is another — and every point carries
/// the model that wrote it, asserted when it is read.
///
/// The collection is made when it is first written to. There is no layer check
/// to ask, as the words have: the model is in this process, not a server that
/// may be serving another revision.
/// </summary>
internal sealed class QdrantPictureVectors(
    QdrantClient client,
    IImageEmbeddingGenerator pictures,
    IImageStore store) : IPictureVectorIndexer, IPictureSearch
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    private EmbeddingModelDescriptor Model => pictures.Model;

    /// <summary>The collection: <c>pictures_siglip2-base-patch16-224_75de2d5_768</c>.</summary>
    internal string Collection => PictureCollection.NameFor(Model);

    public Task IndexAsync(Product product, CancellationToken ct = default) =>
        GuardAsync($"embedding the pictures of {product.Id}", async () =>
        {
            using var activity = Telemetry.StartActivity("search.pictures.index");
            activity?.SetTag("search.product_id", product.Id.ToString());

            await EnsureCollectionAsync(ct);

            var id = product.Id.ToString();
            var gallery = product.Images.Select(image => image.Id).Distinct().ToList();
            var held = await PointsOfAsync(id, ct);
            var heldImages = held.Select(point => point.Payload[PictureFields.ImageId].StringValue).ToHashSet(StringComparer.Ordinal);

            var embedded = 0;
            foreach (var picture in gallery.Where(picture => !heldImages.Contains(picture.Value)))
            {
                var point = PicturePointId.For(picture);
                var existing = await client.RetrieveAsync(Collection, [(PointId)point], withPayload: true, withVectors: false, cancellationToken: ct);

                if (existing is [var shared])
                {
                    // Somebody's already: the same bytes are the same vector, so this product is named beside them.
                    await SetProductsAsync(point, [.. ProductsOf(shared.Payload), id], ct);
                    continue;
                }

                if (await store.OpenAsync(picture, ct) is not { } stored)
                    continue; // a picture the store does not hold has nothing to embed: the next upsert tries again

                byte[] bytes;
                await using (stored.Content)
                {
                    using var buffer = new MemoryStream();
                    await stored.Content.CopyToAsync(buffer, ct);
                    bytes = buffer.ToArray();
                }

                var vector = Checked(await pictures.EmbedPictureAsync(bytes, ct));
                await client.UpsertAsync(Collection,
                [
                    new PointStruct
                    {
                        Id = point,
                        Vectors = vector.ToArray(),
                        Payload =
                        {
                            [PictureFields.ImageId] = picture.Value,
                            [PictureFields.Products] = new[] { id },
                            [PointFields.ModelId] = Model.ModelId,
                            [PointFields.ModelRevision] = Model.Revision,
                        },
                    },
                ], cancellationToken: ct);
                embedded++;
            }

            // And the pictures that were the product's and no longer are.
            var current = gallery.Select(picture => picture.Value).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in held.Where(point => !current.Contains(point.Payload[PictureFields.ImageId].StringValue)))
                await DetachAsync(gone, id, ct);

            activity?.SetTag("search.pictures.embedded", embedded);
        });

    public Task RemoveAsync(ProductId productId, CancellationToken ct = default) =>
        GuardAsync($"removing the pictures of {productId}", async () =>
        {
            if (!await client.CollectionExistsAsync(Collection, ct))
                return;

            var id = productId.ToString();
            foreach (var point in await PointsOfAsync(id, ct))
                await DetachAsync(point, id, ct);
        });

    public Task RecreateAsync(CancellationToken ct = default) =>
        GuardAsync("recreating the picture collection", async () =>
        {
            if (await client.CollectionExistsAsync(Collection, ct))
                await client.DeleteCollectionAsync(Collection, cancellationToken: ct);
            await EnsureCollectionAsync(ct);
        });

    public Task<bool> IsEmptyAsync(CancellationToken ct = default) =>
        GuardAsync("counting picture vectors", async () =>
            !await client.CollectionExistsAsync(Collection, ct)
            || await client.CountAsync(Collection, exact: true, cancellationToken: ct) == 0);

    public Task<IReadOnlyList<PictureMatch>> NearestAsync(ReadOnlyMemory<float> vector, int limit, CancellationToken ct = default) =>
        GuardAsync<IReadOnlyList<PictureMatch>>("picture search", async () =>
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

            if (!await client.CollectionExistsAsync(Collection, ct))
                return [];

            // A product with four pictures can take four of the nearest places: more
            // points are asked for than products are wanted, and each product is kept once.
            var points = await client.QueryAsync(
                Collection,
                query: Checked(vector).ToArray(),
                limit: (ulong)(limit * PicturesAProduct),
                payloadSelector: true,
                cancellationToken: ct);

            List<PictureMatch> nearest = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (var point in points)
            {
                AssertWrittenByThisModel(point.Payload);
                foreach (var product in ProductsOf(point.Payload).Where(seen.Add))
                    nearest.Add(new PictureMatch(product, point.Payload[PictureFields.ImageId].StringValue, point.Score));
            }

            return [.. nearest.Take(limit)];
        });

    /// <summary>How many points are asked for a product wanted: the seed's largest gallery is four.</summary>
    private const int PicturesAProduct = 4;

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (await client.CollectionExistsAsync(Collection, ct))
            return;

        await client.CreateCollectionAsync(
            Collection, new VectorParams { Size = (ulong)Model.Dimensions, Distance = Distance.Cosine }, cancellationToken: ct);
        // The one field every write filters by: which products a picture is of.
        await client.CreatePayloadIndexAsync(Collection, PictureFields.Products, PayloadSchemaType.Keyword, cancellationToken: ct);
    }

    private async Task<IReadOnlyList<RetrievedPoint>> PointsOfAsync(string productId, CancellationToken ct)
    {
        var page = await client.ScrollAsync(
            Collection, filter: Conditions.MatchKeyword(PictureFields.Products, productId),
            limit: MaxPicturesRead, payloadSelector: true, cancellationToken: ct);
        return [.. page.Result];
    }

    /// <summary>More pictures than any product of the catalogue has, read in one page.</summary>
    private const uint MaxPicturesRead = 256;

    /// <summary>The product no longer shows this picture: it is taken off the point, and the point goes when nobody is left.</summary>
    private async Task DetachAsync(RetrievedPoint point, string productId, CancellationToken ct)
    {
        string[] left = [.. ProductsOf(point.Payload).Where(product => product != productId)];
        if (left.Length == 0)
            await client.DeleteAsync(Collection, [Guid.Parse(point.Id.Uuid)], cancellationToken: ct);
        else
            await SetProductsAsync(Guid.Parse(point.Id.Uuid), left, ct);
    }

    private Task SetProductsAsync(Guid point, IEnumerable<string> products, CancellationToken ct) =>
        client.SetPayloadAsync(
            Collection,
            new Dictionary<string, Value> { [PictureFields.Products] = products.Distinct(StringComparer.Ordinal).ToArray() },
            point, cancellationToken: ct);

    private static IEnumerable<string> ProductsOf(IDictionary<string, Value> payload) =>
        payload.TryGetValue(PictureFields.Products, out var products) ? products.ListValue.Values.Select(value => value.StringValue) : [];

    /// <summary>A point written by another model is not ranked: the collection's name already says the model, and the point says it again.</summary>
    private void AssertWrittenByThisModel(IDictionary<string, Value> payload)
    {
        var writtenBy = payload.TryGetValue(PointFields.ModelId, out var id) ? id.StringValue : "";
        var revision = payload.TryGetValue(PointFields.ModelRevision, out var sha) ? sha.StringValue : "";

        if (writtenBy != Model.ModelId || revision != Model.Revision)
        {
            throw new SearchUnavailableException(
                "picture search",
                $"a point in {Collection} was written by {writtenBy}@{revision}, not by {Model.ModelId}@{Model.Revision}; ranking it would mix two vector spaces");
        }
    }

    private ReadOnlyMemory<float> Checked(ReadOnlyMemory<float> vector) =>
        vector.Length == Model.Dimensions
            ? vector
            : throw new SearchUnavailableException("picture embedding", $"a vector of {vector.Length} dimensions, and {Model.ModelId} has {Model.Dimensions}");

    /// <summary>
    /// The store's and the model's own failures as the one exception the search
    /// side knows. A stage this process never had is let through as what it is:
    /// not a failure, and not counted as one.
    /// </summary>
    private static async Task<T> GuardAsync<T>(string operation, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (exception is not (SearchUnavailableException or SearchStageNotConfiguredException or OperationCanceledException or ArgumentException))
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

/// <summary>The picture payload's own fields; the model's are the words' (<see cref="PointFields"/>).</summary>
internal static class PictureFields
{
    public const string ImageId = "imageId";
    public const string Products = "products";
}

/// <summary>The collection a picture model writes to, named as the words' is and never the same: <c>pictures_…</c>.</summary>
internal static class PictureCollection
{
    public static string NameFor(EmbeddingModelDescriptor model) =>
        "pictures" + model.CollectionName[model.CollectionName.IndexOf('_', StringComparison.Ordinal)..];
}

/// <summary>
/// A picture's point, derived from the picture's id — itself the hash of its
/// bytes — so the same picture is the same point whoever shows it, and removing
/// one is an id this code can compute.
/// </summary>
internal static class PicturePointId
{
    public static Guid For(ImageId picture)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(picture.Value)).AsSpan(0, 16).ToArray();

        // RFC 9562 version 8, "custom", as the words' points are.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }
}
