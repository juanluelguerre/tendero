using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ElGuerre.Tendero.Assist.Steward;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// POST /api/steward/turns (S-3b): the body becomes a turn in the negotiated
/// language, a conversation id is minted on the first turn, and each person may
/// take twenty turns a minute — a loop or an abuse costs a bounded amount.
/// </summary>
public sealed class StewardTurnEndpointTests
{
    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        public List<StewardTurnCommand> Asked { get; } = [];

        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        {
            var turn = (StewardTurnCommand)(object)command;
            lock (Asked)
                Asked.Add(turn);
            return Task.FromResult((TResult)(object)new StewardAnswer(
                StewardStatus.Answered, "Hello.", [], [], null, "scripted", false, false, turn.ConversationId));
        }
    }

    /// <summary>Authenticates whoever the X-Test-User header names, and lets them through.</summary>
    private sealed class HeaderPrincipal : IPolicyEvaluator
    {
        public Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context)
        {
            var user = context.Request.Headers["X-Test-User"].ToString();
            var identity = new ClaimsIdentity([new Claim("sub", user), new Claim(ClaimTypes.NameIdentifier, user)], "test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "test")));
        }

        public Task<PolicyAuthorizationResult> AuthorizeAsync(
            AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object? resource) =>
            Task.FromResult(PolicyAuthorizationResult.Success());
    }

    private sealed class HeaderPrincipalAccessor(IHttpContextAccessor http) : IPrincipalAccessor
    {
        public CommercePrincipal Current
        {
            get
            {
                var user = http.HttpContext?.Request.Headers["X-Test-User"].ToString();
                return string.IsNullOrEmpty(user) ? CommercePrincipal.Anonymous : new(null, null, user, user, ["shopkeeper"]);
            }
        }
    }

    private static (HttpClient Client, RecordingDispatcher Dispatcher, IDisposable Owner) Compose()
    {
        var dispatcher = new RecordingDispatcher();
        var root = new TenderoApiFactory();
        var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ICommandDispatcher>(dispatcher);
            services.AddSingleton<IPolicyEvaluator, HeaderPrincipal>();
            // The real accessor resolves the person in the database; here the header is the person.
            services.AddHttpContextAccessor();
            services.AddScoped<IPrincipalAccessor, HeaderPrincipalAccessor>();
        }));
        return (factory.CreateClient(), dispatcher, root);
    }

    private static HttpRequestMessage Turn(string user, string? conversationId = null, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/steward/turns")
        {
            Content = JsonContent.Create(new
            {
                messages = new[] { new { role = "user", text = "hello" } },
                screen = new { page = "Products", subject = (string?)null },
                conversationId,
            }),
        };
        request.Headers.Add("X-Test-User", user);
        if (language is not null)
            request.Headers.AcceptLanguage.ParseAdd(language);
        return request;
    }

    [Fact]
    public async Task The_body_becomes_a_turn_in_the_readers_language_and_a_conversation_is_minted()
    {
        var (client, dispatcher, owner) = Compose();
        using var _ = owner;

        var response = await client.SendAsync(Turn("lucia", language: "en-GB"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var turn = Assert.Single(dispatcher.Asked);
        Assert.Equal(("en", "Products", "hello"), (turn.Culture, turn.Screen!.Page, turn.Messages.Single().Text));
        Assert.False(string.IsNullOrWhiteSpace(turn.ConversationId));
        var answer = await response.Content.ReadFromJsonAsync<StewardAnswer>(
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { WireJson.Enums() } },
            TestContext.Current.CancellationToken);
        Assert.Equal(turn.ConversationId, answer!.ConversationId);
        Assert.Equal("en", response.Content.Headers.ContentLanguage.Single());
    }

    [Fact]
    public async Task A_conversation_the_client_names_is_the_one_continued()
    {
        var (client, dispatcher, owner) = Compose();
        using var _ = owner;

        await client.SendAsync(Turn("lucia", conversationId: "conv-7"), TestContext.Current.CancellationToken);

        Assert.Equal("conv-7", Assert.Single(dispatcher.Asked).ConversationId);
    }

    [Fact]
    public async Task The_twenty_first_turn_in_a_minute_is_refused_and_another_persons_is_not()
    {
        var (client, _, owner) = Compose();
        using var _ = owner;

        for (var turn = 0; turn < 20; turn++)
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Turn("lucia"), TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Turn("lucia"), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Turn("marta"), TestContext.Current.CancellationToken)).StatusCode);
    }
}
