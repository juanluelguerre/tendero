using System.Text.Json;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Embeddings;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace ElGuerre.Tendero.Search.Qdrant;

/// <summary>
/// The startup check of P8-3, run on demand and remembered for a while.
///
/// Three questions, and any "no" makes the layer unavailable rather than throwing:
/// does the model server answer, is it serving the revision this build's vectors
/// were made with, and does the collection named after that revision hold vectors
/// of the size and distance the model produces. A missing collection is created —
/// that is the one gap the check closes rather than reports.
///
/// **On demand and not once at startup**, because nothing waits for the vector
/// layer (invariant 8) and the first start of the model server downloads 2.2 GB
/// of weights: a check that ran once would find it absent and remember that for
/// the life of the process. An answer is kept for a minute when the layer is
/// available and for ten seconds when it is not, so a search asks Qdrant and the
/// model server at most that often, and a layer that comes up is noticed quickly.
/// </summary>
internal sealed class QdrantVectorLayer(
    QdrantClient client,
    ModelServerInfo modelServer,
    EmbeddingModelDescriptor model,
    TimeProvider clock,
    ILogger<QdrantVectorLayer> logger) : IVectorLayer
{
    internal static readonly TimeSpan RememberAvailable = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan RememberUnavailable = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim probing = new(1, 1);
    private (VectorLayerStatus Status, DateTimeOffset Until)? remembered;

    public async Task<VectorLayerStatus> CheckAsync(CancellationToken ct = default)
    {
        if (Fresh() is { } status)
            return status;

        await this.probing.WaitAsync(ct);
        try
        {
            // Somebody else may have probed while this call waited.
            if (Fresh() is { } probed)
                return probed;

            var previous = this.remembered?.Status;
            var current = await ProbeAsync(ct);

            if (previous?.IsAvailable != current.IsAvailable || previous.Reason != current.Reason)
            {
                if (current.IsAvailable)
                    logger.LogInformation("The vector layer is available: {Description}", current.Reason);
                else
                    logger.LogWarning("The vector layer is unavailable, and search degrades to lexical: {Reason}", current.Reason);
            }

            this.remembered = (current, clock.GetUtcNow() + (current.IsAvailable ? RememberAvailable : RememberUnavailable));
            return current;
        }
        finally
        {
            this.probing.Release();
        }
    }

    /// <summary>Drops the remembered answer, after the collection was recreated.</summary>
    public void Forget() => this.remembered = null;

    private VectorLayerStatus? Fresh() =>
        this.remembered is { } entry && clock.GetUtcNow() < entry.Until ? entry.Status : null;

    private async Task<VectorLayerStatus> ProbeAsync(CancellationToken ct)
    {
        ServedModel served;
        try
        {
            served = await modelServer.ReadAsync(ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException
                                          && !ct.IsCancellationRequested)
        {
            return VectorLayerStatus.Unavailable($"the model server does not answer ({exception.Message})");
        }

        if (!string.Equals(served.ModelId, model.ModelId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(served.Revision, model.Revision, StringComparison.OrdinalIgnoreCase))
        {
            return VectorLayerStatus.Unavailable(
                $"the model server serves {Describe(served.ModelId, served.Revision)}, " +
                $"and this build's vectors are {Describe(model.ModelId, model.Revision)}");
        }

        try
        {
            if (!await client.CollectionExistsAsync(model.CollectionName, ct))
                await CreateCollectionAsync(ct);

            var info = await client.GetCollectionInfoAsync(model.CollectionName, ct);
            var vectors = info.Config.Params.VectorsConfig.Params;

            if (vectors is null || vectors.Size != (ulong)model.Dimensions || vectors.Distance != Distance.Cosine)
            {
                return VectorLayerStatus.Unavailable(
                    $"collection {model.CollectionName} holds {vectors?.Size.ToString() ?? "named"} " +
                    $"{vectors?.Distance.ToString() ?? "multi-vector"} vectors, and {model.ModelId} produces " +
                    $"{model.Dimensions} Cosine — POST /api/search/reindex?target=vector&recreate=true rebuilds it");
            }
        }
        catch (RpcException exception) when (!ct.IsCancellationRequested)
        {
            return VectorLayerStatus.Unavailable($"Qdrant does not answer ({exception.Status.StatusCode}: {exception.Status.Detail})");
        }

        return VectorLayerStatus.Available(
            $"{Describe(model.ModelId, model.Revision)} in collection {model.CollectionName}");
    }

    internal async Task CreateCollectionAsync(CancellationToken ct)
    {
        try
        {
            await client.CreateCollectionAsync(
                model.CollectionName,
                new VectorParams { Size = (ulong)model.Dimensions, Distance = Distance.Cosine },
                cancellationToken: ct);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.AlreadyExists)
        {
            // The API and the worker both check, and the second one to create the
            // collection finds the first one's. That is the outcome both wanted.
        }
    }

    private static string Describe(string modelId, string revision) =>
        $"{(modelId.Length == 0 ? "an unnamed model" : modelId)}@{(revision.Length == 0 ? "no pinned revision" : revision)}";
}
