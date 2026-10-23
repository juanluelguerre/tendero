using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// The store's Admin GraphQL API over <c>HttpClient</c>, with no package (phase
/// 16, spec decision 8): a handful of queries do not earn ShopifySharp and the
/// Newtonsoft.Json it brings.
///
/// It knows three things about talking to a store and nothing about products:
/// the token goes in a header and is asked for again once if refused; a
/// throttled answer is waited out and asked again; and a bulk operation is
/// started, polled until it ends, and its file read line by line.
/// </summary>
internal sealed class ShopifyAdmin(
    HttpClient http, ShopifyAccessTokens tokens, IOptions<ShopifyOptions> options, TimeProvider clock)
{
    private const int Attempts = 5;

    /// <summary>Sends a query or mutation and returns its <c>data</c>.</summary>
    public async Task<JsonElement> QueryAsync(string query, CancellationToken ct)
    {
        var refused = false;

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint())
            {
                Content = JsonContent.Create(new { query }),
            };
            request.Headers.Add("X-Shopify-Access-Token", await tokens.CurrentAsync(ct));

            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (refused)
                    throw new ShopifyException(ShopifyFailure.Unauthorized, "The store refused a fresh token.");

                refused = true;
                tokens.Forget();
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests || await IsThrottledAsync(response, ct))
            {
                if (attempt == Attempts)
                    throw new ShopifyException(ShopifyFailure.Throttled, $"The store stayed throttled after {Attempts} attempts.");

                await Task.Delay(options.Value.ThrottleBackoff * attempt, clock, ct);
                continue;
            }

            response.EnsureSuccessStatusCode();
            using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            if (answer.RootElement.TryGetProperty("errors", out var errors))
                throw new ShopifyException(ShopifyFailure.Rejected, $"The store rejected the query: {errors}");

            return answer.RootElement.GetProperty("data").Clone();
        }
    }

    /// <summary>
    /// Runs a bulk query to its end and returns the file it wrote, or null when
    /// it found nothing. Bulk operations have none of the cost and rate limits a
    /// single query has, and are Shopify's way to read a whole catalogue.
    /// </summary>
    public async Task<Uri?> RunBulkAsync(string query, CancellationToken ct)
    {
        var started = await QueryAsync(query, ct);
        var run = started.GetProperty("bulkOperationRunQuery");
        UserErrors(run);
        var id = run.GetProperty("bulkOperation").GetProperty("id").GetString()!;

        while (true)
        {
            var data = await QueryAsync(ShopifyQueries.BulkOperation(id), ct);
            var operation = data.GetProperty("node");
            var status = operation.GetProperty("status").GetString();

            switch (status)
            {
                case "COMPLETED":
                    return operation.GetProperty("url").GetString() is { } url ? new Uri(url) : null;
                case "FAILED" or "CANCELED" or "CANCELING" or "EXPIRED":
                    var code = operation.TryGetProperty("errorCode", out var error) ? error.ToString() : "";
                    throw new ShopifyException(ShopifyFailure.BulkOperationFailed, $"The bulk operation ended {status} {code}".Trim());
                default:
                    await Task.Delay(options.Value.BulkPollInterval, clock, ct);
                    break;
            }
        }
    }

    /// <summary>
    /// Reads a bulk operation's file a line at a time. It is a signed link to
    /// cloud storage and goes without the store's token.
    /// </summary>
    public async IAsyncEnumerable<JsonElement> ReadLinesAsync(Uri file, [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await http.GetAsync(file, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
                continue;

            using var document = JsonDocument.Parse(line);
            yield return document.RootElement.Clone();
        }
    }

    private Uri Endpoint() =>
        new($"https://{options.Value.Store}/admin/api/{options.Value.ApiVersion}/graphql.json");

    private static async Task<bool> IsThrottledAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            return false;

        // Buffered, so the body can still be read after this look at it.
        await response.Content.LoadIntoBufferAsync(ct);
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        return answer.RootElement.TryGetProperty("errors", out var errors)
               && errors.EnumerateArray().Any(error =>
                   error.TryGetProperty("extensions", out var extensions)
                   && extensions.TryGetProperty("code", out var code)
                   && code.GetString() == "THROTTLED");
    }

    private static void UserErrors(JsonElement payload)
    {
        if (payload.TryGetProperty("userErrors", out var errors) && errors.GetArrayLength() > 0)
            throw new ShopifyException(ShopifyFailure.Rejected, $"The store refused the operation: {errors}");
    }
}
