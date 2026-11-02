using System.Net;
using System.Security.Cryptography;
using System.Text;
using ElGuerre.Tendero.Ordering.Features.ReceiveChannelOrders;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Shopify;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The store's webhook at the edge (phase 16, S3; ADR 0070): with no store connected it
/// is not there; a body the store did not sign is refused before anything is read; a
/// topic Tendero does not handle is answered and dropped; and a new order is received
/// by the id the store names, through Ordering's own command.
/// </summary>
public sealed class ShopifyWebhookEndpointTests
{
    private const string Secret = "shpss_lab_secret";
    private const string NewOrder = """{"id":7001,"admin_graphql_api_id":"gid://shopify/Order/7001","name":"#1042"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (HttpClient Client, Recording Dispatcher, IDisposable Owner) Compose(string? secret = Secret)
    {
        var dispatcher = new Recording();
        var root = new TenderoApiFactory();
        var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<ShopifyOptions>(options => options.ClientSecret = secret);
            services.AddScoped<ICommandDispatcher>(_ => dispatcher);
        }));
        return (factory.CreateClient(), dispatcher, root);
    }

    private static HttpRequestMessage Delivery(string body, string topic = "orders/create", string? signedWith = Secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ShopifyWebhookEndpoint.Path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Shopify-Topic", topic);
        if (signedWith is not null)
            request.Headers.Add("X-Shopify-Hmac-SHA256",
                Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signedWith), Encoding.UTF8.GetBytes(body))));
        return request;
    }

    [Fact]
    public async Task A_new_order_the_store_signed_is_received_by_the_id_it_names()
    {
        var (client, dispatcher, owner) = Compose();
        using (owner)
        {
            using var answer = await client.SendAsync(Delivery(NewOrder), Ct);

            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            Assert.Equal(new ReceiveChannelOrderCommand("shopify", "gid://shopify/Order/7001"), Assert.Single(dispatcher.Sent));
        }
    }

    [Fact]
    public async Task A_body_another_secret_signed_is_refused_and_nothing_is_received()
    {
        var (client, dispatcher, owner) = Compose();
        using (owner)
        {
            using var answer = await client.SendAsync(Delivery(NewOrder, signedWith: "another-secret"), Ct);

            Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
            Assert.Empty(dispatcher.Sent);
        }
    }

    /// <summary>Answered, so the store does not retry it eight times and delete the subscription; and dropped.</summary>
    [Fact]
    public async Task A_topic_Tendero_does_not_handle_is_answered_and_dropped()
    {
        var (client, dispatcher, owner) = Compose();
        using (owner)
        {
            using var answer = await client.SendAsync(Delivery(NewOrder, topic: "products/update"), Ct);

            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            Assert.Empty(dispatcher.Sent);
        }
    }

    [Fact]
    public async Task With_no_store_connected_there_is_no_webhook()
    {
        var (client, dispatcher, owner) = Compose(secret: null);
        using (owner)
        {
            using var answer = await client.SendAsync(Delivery(NewOrder), Ct);

            Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
            Assert.Empty(dispatcher.Sent);
        }
    }

    private sealed class Recording : ICommandDispatcher
    {
        public List<object> Sent { get; } = [];

        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        {
            Sent.Add(command);
            return Task.FromResult((TResult)(object)new ChannelOrdersReceived(1, 0, 0, DateTimeOffset.UnixEpoch, []));
        }
    }
}
