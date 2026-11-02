using ElGuerre.Tendero.Shopify;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>
/// The Admin API client (phase 16, P16-3): a token by the client-credentials
/// grant, kept until it is about to expire; queries to the pinned version; a
/// bulk operation started, polled and its file read as a stream; and what a
/// store answers when it is busy, refuses the token or fails the operation.
/// </summary>
public sealed class ShopifyAdminTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ShopifyAdmin Admin, FakeShopify Store, TestClock Clock) AStore()
    {
        var store = new FakeShopify();
        var clock = new TestClock();
        var options = Options.Create(new ShopifyOptions
        {
            Store = FakeShopify.Store,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            BulkPollInterval = TimeSpan.Zero,
            ThrottleBackoff = TimeSpan.Zero,
        });
        var http = new HttpClient(store);
        return (new ShopifyAdmin(http, new ShopifyAccessTokens(http, options, clock), options, clock), store, clock);
    }

    [Fact]
    public async Task A_query_goes_to_the_pinned_version_with_the_token()
    {
        var (admin, store, _) = AStore();

        await admin.QueryAsync("{ shop { currencyCode } }", Ct);

        var query = store.Requests.Last();
        Assert.Equal($"https://{FakeShopify.Store}/admin/api/{ShopifyOptions.PinnedApiVersion}/graphql.json", query.RequestUri!.ToString());
        Assert.Equal(["token-1"], query.Headers.GetValues("X-Shopify-Access-Token"));
    }

    /// <summary>
    /// A mutation's data travels as GraphQL variables, never spliced into its text:
    /// a product's name or description is the shopkeeper's, quotes and all (phase 16, S3).
    /// </summary>
    [Fact]
    public async Task Variables_travel_beside_the_query_and_are_not_spliced_into_it()
    {
        var (admin, store, _) = AStore();

        await admin.QueryAsync("query ($handle: String!) { shop { currencyCode } }", new { handle = "taza \"de\" gres" }, Ct);

        Assert.Equal("query ($handle: String!) { shop { currencyCode } }", store.Queries.Last());
        Assert.Equal("taza \"de\" gres", store.Variables.Last().GetProperty("handle").GetString());
    }

    /// <summary>
    /// A token lasts 24 hours; it is asked for once and kept, and asked for again
    /// a few minutes before it would expire rather than after a refusal.
    /// </summary>
    [Fact]
    public async Task A_token_is_asked_for_once_and_again_just_before_it_expires()
    {
        var (admin, store, clock) = AStore();

        await admin.QueryAsync("{ shop { currencyCode } }", Ct);
        await admin.QueryAsync("{ shop { currencyCode } }", Ct);
        clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(56));
        await admin.QueryAsync("{ shop { currencyCode } }", Ct);

        Assert.Equal(2, store.TokensIssued);
    }

    /// <summary>
    /// The token is the client-credentials grant's: the app's id and secret,
    /// posted as a form; and while it is valid it is not asked for again.
    /// Found by phase 16's Stryker close.
    /// </summary>
    [Fact]
    public async Task A_token_is_asked_for_with_the_apps_credentials_and_kept_while_valid()
    {
        var (admin, store, _) = AStore();

        await admin.QueryAsync("{ shop { currencyCode } }", Ct);
        await admin.QueryAsync("{ shop { currencyCode } }", Ct);
        await admin.QueryAsync("{ shop { currencyCode } }", Ct);

        var form = System.Web.HttpUtility.ParseQueryString(Assert.Single(store.TokenRequests));
        Assert.Equal(("client_credentials", "client-id", "client-secret"), (form["grant_type"], form["client_id"], form["client_secret"]));
    }

    /// <summary>A store that stays busy is said to be, after a few tries, rather than asked forever.</summary>
    [Fact]
    public async Task A_store_that_stays_throttled_says_so()
    {
        var (admin, store, _) = AStore();
        store.ThrottleFirst = 50;

        var throttled = await Assert.ThrowsAsync<ShopifyException>(() => admin.QueryAsync(ShopifyQueries.Shop, Ct));

        Assert.Equal(ShopifyFailure.Throttled, throttled.Failure);
        Assert.Equal(45, store.ThrottleFirst);
    }

    /// <summary>A store that is down fails the call, for its query and for its bulk file alike.</summary>
    [Fact]
    public async Task A_store_that_is_down_fails_the_call()
    {
        var (admin, store, _) = AStore();
        var file = await admin.RunBulkAsync(ShopifyQueries.Products([]), Ct);
        store.FailWith = System.Net.HttpStatusCode.InternalServerError;

        await Assert.ThrowsAsync<HttpRequestException>(() => admin.QueryAsync(ShopifyQueries.Shop, Ct));
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in admin.ReadLinesAsync(file!, Ct)) { }
        });
    }

    /// <summary>A query the store finds wrong, and a bulk operation it will not start, are said with the store's words.</summary>
    [Fact]
    public async Task What_the_store_rejects_is_said_with_its_words()
    {
        var (admin, store, _) = AStore();

        var wrong = await Assert.ThrowsAsync<ShopifyException>(() => admin.QueryAsync("{ wrong }", Ct));
        store.RefuseBulk = true;
        var busy = await Assert.ThrowsAsync<ShopifyException>(() => admin.RunBulkAsync(ShopifyQueries.Products([]), Ct));

        Assert.Equal(ShopifyFailure.Rejected, wrong.Failure);
        Assert.Contains("doesn't exist", wrong.Message, StringComparison.Ordinal);
        Assert.Contains("already in progress", busy.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_token_is_asked_for_again_once()
    {
        var (admin, store, _) = AStore();
        store.RefuseFirst = 1;

        await admin.QueryAsync("{ shop { currencyCode } }", Ct);

        Assert.Equal(2, store.TokensIssued);
    }

    [Fact]
    public async Task A_store_that_keeps_refusing_says_so()
    {
        var (admin, store, _) = AStore();
        store.RefuseFirst = 2;

        var refused = await Assert.ThrowsAsync<ShopifyException>(() => admin.QueryAsync("{ shop { currencyCode } }", Ct));

        Assert.Equal(ShopifyFailure.Unauthorized, refused.Failure);
    }

    /// <summary>A busy store answers THROTTLED; the query waits and is asked again.</summary>
    [Fact]
    public async Task A_throttled_query_is_asked_again()
    {
        var (admin, store, _) = AStore();
        store.ThrottleFirst = 2;

        var data = await admin.QueryAsync(ShopifyQueries.Shop, Ct);

        Assert.Equal("EUR", data.GetProperty("shop").GetProperty("currencyCode").GetString());
    }

    /// <summary>
    /// One bulk operation reads the whole catalogue: started, polled until it
    /// finishes, and its file read line by line. The file is a signed link and
    /// is fetched without the store's token.
    /// </summary>
    [Fact]
    public async Task A_bulk_operation_is_run_polled_and_its_file_read_without_the_token()
    {
        var (admin, store, _) = AStore();

        var file = await admin.RunBulkAsync(ShopifyQueries.Products(["en"]), Ct);
        var lines = new List<string>();
        await foreach (var line in admin.ReadLinesAsync(file!, Ct))
            lines.Add(line.GetProperty("id").GetString()!);

        Assert.Equal(FakeShopify.ResultsFile, file);
        Assert.Equal(9, lines.Count);
        Assert.Contains("groupObjects: true", store.LastBulkQuery, StringComparison.Ordinal);
        Assert.Contains("metafields(namespace: \\\"tendero\\\")", store.LastBulkQuery, StringComparison.Ordinal);
        Assert.False(store.Requests.Last().Headers.Contains("X-Shopify-Access-Token"));
    }

    /// <summary>Every way a bulk operation can end without a file is a failure, not a wait.</summary>
    [Theory]
    [InlineData("CANCELED")]
    [InlineData("CANCELING")]
    [InlineData("EXPIRED")]
    public async Task A_bulk_operation_that_ends_without_a_file_fails(string status)
    {
        var (admin, store, _) = AStore();
        store.BulkEnd = (status, null);

        var failed = await Assert.ThrowsAsync<ShopifyException>(() => admin.RunBulkAsync(ShopifyQueries.Products([]), Ct));

        Assert.Contains(status, failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_bulk_operation_says_why()
    {
        var (admin, store, _) = AStore();
        store.BulkEnd = ("FAILED", "ACCESS_DENIED");

        var failed = await Assert.ThrowsAsync<ShopifyException>(() => admin.RunBulkAsync(ShopifyQueries.Products([]), Ct));

        Assert.Equal(ShopifyFailure.BulkOperationFailed, failed.Failure);
        Assert.Contains("ACCESS_DENIED", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_shop_says_its_currency_its_language_and_the_ones_it_publishes()
    {
        var (admin, _, _) = AStore();

        var shop = await ShopifyShop.ReadAsync(admin, Ct);

        Assert.Equal(("EUR", "es"), (shop.Currency, shop.PrimaryLocale));
        Assert.Equal(["es", "en", "fr"], shop.Locales);
    }

    /// <summary>
    /// A merchant's own store (P16-14): the install gives an offline token, which
    /// a custom app's does not expire, and it is used as it is — never exchanged,
    /// and with no client credentials beside it.
    /// </summary>
    [Fact]
    public async Task A_merchants_own_token_is_used_as_it_is_and_never_exchanged()
    {
        var store = new FakeShopify();
        var clock = new TestClock();
        var options = Options.Create(new ShopifyOptions { Store = FakeShopify.Store, AccessToken = "shpat_merchant" });
        var http = new HttpClient(store);
        var admin = new ShopifyAdmin(http, new ShopifyAccessTokens(http, options, clock), options, clock);

        await admin.QueryAsync("{ shop { currencyCode } }", Ct);
        clock.Advance(TimeSpan.FromDays(30));
        await admin.QueryAsync("{ shop { currencyCode } }", Ct);

        Assert.All(store.Requests, request => Assert.Equal(["shpat_merchant"], request.Headers.GetValues("X-Shopify-Access-Token")));
        Assert.Equal(0, store.TokensIssued);
    }

    /// <summary>A store with either way in is a source; a store with neither is not.</summary>
    [Theory]
    [InlineData("shpat_merchant", null, null, true)]
    [InlineData(null, "client-id", "client-secret", true)]
    [InlineData(null, "client-id", null, false)]
    [InlineData(null, null, null, false)]
    public void A_store_is_configured_by_its_own_token_or_by_the_apps_credentials(string? token, string? id, string? secret, bool configured)
    {
        var options = new ShopifyOptions { Store = FakeShopify.Store, AccessToken = token, ClientId = id, ClientSecret = secret };

        Assert.Equal(configured, options.IsConfigured);
    }

    /// <summary>The store's own language is the one it marks primary, wherever Shopify lists it.</summary>
    [Fact]
    public async Task The_shops_language_is_the_one_marked_primary()
    {
        var (admin, store, _) = AStore();
        store.ShopLocales = """[{"locale":"fr","primary":false},{"locale":"es","primary":true}]""";

        var shop = await ShopifyShop.ReadAsync(admin, Ct);

        Assert.Equal("es", shop.PrimaryLocale);
    }
}
