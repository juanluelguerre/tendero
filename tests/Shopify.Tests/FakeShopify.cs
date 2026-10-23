using System.Net;
using System.Text;
using System.Text.Json;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>
/// A Shopify store as the Admin API answers, from recorded fixtures (phase 16,
/// P16-3): the client-credentials token, the GraphQL endpoint — a bulk operation
/// started, polled and finished, the shop's facts — and the signed file a
/// finished bulk operation points at. No network, the same answers every run.
/// </summary>
internal sealed class FakeShopify : HttpMessageHandler
{
    public const string Store = "tendero-dev.myshopify.com";
    public static readonly Uri ResultsFile = new("https://storage.googleapis.com/shopify-bulk/results.jsonl");

    private int polls;

    public List<HttpRequestMessage> Requests { get; } = [];

    public int TokensIssued { get; private set; }

    /// <summary>What each token request posted, as the form it sent.</summary>
    public List<string> TokenRequests { get; } = [];

    /// <summary>A status every GraphQL call answers with instead, when set: a store that is down.</summary>
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>Whether starting a bulk operation is refused with a user error.</summary>
    public bool RefuseBulk { get; set; }

    /// <summary>How many GraphQL answers say THROTTLED before the real one.</summary>
    public int ThrottleFirst { get; set; }

    /// <summary>How many GraphQL calls are refused with 401 before the real one.</summary>
    public int RefuseFirst { get; set; }

    /// <summary>What the bulk operation ends as, and its error code when it fails.</summary>
    public (string Status, string? ErrorCode) BulkEnd { get; set; } = ("COMPLETED", null);

    public string ResultsFixture { get; set; } = "products.jsonl";

    public string InventoryFixture { get; set; } = "inventory.jsonl";

    /// <summary>Two collections: "cocina" with both products, "ofertas" with none.</summary>
    public string CollectionsFixture { get; set; } = "collections.jsonl";

    /// <summary>The store's locations: Madrid and Barcelona mapped, Bilbao not.</summary>
    public string Locations { get; set; } =
        """[{"id":"gid://shopify/Location/71","name":"Almacén central (Madrid)","isActive":true},{"id":"gid://shopify/Location/72","name":"Almacén norte (Barcelona)","isActive":true},{"id":"gid://shopify/Location/73","name":"Tienda de Bilbao","isActive":true}]""";

    public string? LastBulkQuery { get; private set; }

    /// <summary>The store's published languages, primary first.</summary>
    public string ShopLocales { get; set; } =
        """[{"locale":"es","primary":true},{"locale":"en","primary":false},{"locale":"fr","primary":false}]""";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

        if (request.RequestUri == ResultsFile)
        {
            if (FailWith is { } down)
                return new HttpResponseMessage(down);

            // The file the last bulk query wrote: the stock's, the collections', or the catalogue's.
            var fixture = LastBulkQuery switch
            {
                { } bulk when bulk.Contains("productVariants", StringComparison.Ordinal) => InventoryFixture,
                { } bulk when bulk.Contains("{ collections", StringComparison.Ordinal) => CollectionsFixture,
                _ => ResultsFixture,
            };
            return Text(File.ReadAllText(Fixture(fixture)), "application/jsonl");
        }

        if (request.RequestUri!.AbsolutePath == "/admin/oauth/access_token")
        {
            TokensIssued++;
            TokenRequests.Add(body);
            return Json($$"""{"access_token":"token-{{TokensIssued}}","scope":"read_products,read_translations,read_locales","expires_in":86399}""");
        }

        if (FailWith is { } failing)
            return new HttpResponseMessage(failing);

        if (RefuseFirst > 0)
        {
            RefuseFirst--;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        if (ThrottleFirst > 0)
        {
            ThrottleFirst--;
            return Json("""{"errors":[{"message":"Throttled","extensions":{"code":"THROTTLED"}}]}""");
        }

        var query = JsonDocument.Parse(body).RootElement.GetProperty("query").GetString()!;

        // A query the store finds wrong: GraphQL answers 200 with errors.
        if (query.Contains("{ wrong }", StringComparison.Ordinal))
            return Json("""{"errors":[{"message":"Field 'wrong' doesn't exist on type 'QueryRoot'"}]}""");

        if (query.Contains("bulkOperationRunQuery", StringComparison.Ordinal) && RefuseBulk)
            return Json("""{"data":{"bulkOperationRunQuery":{"bulkOperation":null,"userErrors":[{"field":["query"],"message":"A bulk query operation for this app and shop is already in progress"}]}}}""");

        if (query.Contains("bulkOperationRunQuery", StringComparison.Ordinal))
        {
            LastBulkQuery = query;
            return Json("""{"data":{"bulkOperationRunQuery":{"bulkOperation":{"id":"gid://shopify/BulkOperation/1","status":"CREATED"},"userErrors":[]}}}""");
        }

        if (query.Contains("BulkOperation", StringComparison.Ordinal))
        {
            polls++;
            if (polls == 1)
                return Json("""{"data":{"node":{"status":"RUNNING","errorCode":null,"objectCount":"3","url":null}}}""");

            var (status, error) = BulkEnd;
            var url = status == "COMPLETED" ? $"\"{ResultsFile}\"" : "null";
            var code = error is null ? "null" : $"\"{error}\"";
            return Json("{\"data\":{\"node\":{\"status\":\"" + status + "\",\"errorCode\":" + code + ",\"objectCount\":\"7\",\"url\":" + url + "}}}");
        }

        if (query.Contains("locations(", StringComparison.Ordinal))
            return Json("{\"data\":{\"locations\":{\"nodes\":" + Locations + "}}}");

        if (query.Contains("shopLocales", StringComparison.Ordinal))
            return Json("{\"data\":{\"shop\":{\"currencyCode\":\"EUR\"},\"shopLocales\":" + ShopLocales + "}}");

        return Json("""{"data":{}}""");
    }

    public static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static HttpResponseMessage Json(string json) => Text(json, "application/json");

    private static HttpResponseMessage Text(string text, string mediaType) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, mediaType) };
}
