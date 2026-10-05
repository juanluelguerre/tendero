using System.Net;
using System.Net.Http.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Assist.Tino;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// POST /api/assistant/turns and GET /api/assistant/status (phase 12b): anyone may
/// ask Tino, signed in or not, so what bounds the endpoint is how much is asked —
/// twelve turns a minute per client — and never who asks.
/// </summary>
public sealed class TinoEndpointTests
{
    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        public List<TinoTurnCommand> Asked { get; } = [];

        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        {
            var turn = (TinoTurnCommand)(object)command;
            lock (Asked)
                Asked.Add(turn);
            return Task.FromResult((TResult)(object)new TinoAnswer(
                TinoStatus.Answered, null, TinoKind.Ask, [], [], "¿Para qué la quieres?", null, [], turn.Messages[^1].Text, 0, null,
                "scripted", false, turn.ConversationId));
        }
    }

    /// <summary>Whoever the X-Test-User header names, and nobody without it.</summary>
    private sealed class HeaderPrincipalAccessor(IHttpContextAccessor http) : IPrincipalAccessor
    {
        public CommercePrincipal Current
        {
            get
            {
                var user = http.HttpContext?.Request.Headers["X-Test-User"].ToString();
                return string.IsNullOrEmpty(user) ? CommercePrincipal.Anonymous : new(null, null, user, user, []);
            }
        }
    }

    private static (HttpClient Client, RecordingDispatcher Dispatcher, IDisposable Owner) Compose(
        TinoOptions? options = null, IReadOnlyDictionary<string, ModelProfile>? profiles = null)
    {
        var dispatcher = new RecordingDispatcher();
        var root = new TenderoApiFactory();
        var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ICommandDispatcher>(dispatcher);
            services.AddHttpContextAccessor();
            services.AddScoped<IPrincipalAccessor, HeaderPrincipalAccessor>();
            if (options is not null)
                services.AddSingleton(Options.Create(options));
            if (profiles is not null)
                services.AddSingleton(new ModelProfiles(profiles));
        }));
        return (factory.CreateClient(), dispatcher, root);
    }

    private static HttpRequestMessage Turn(string? user = null, string? conversationId = null, string? language = null, object? context = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/turns")
        {
            Content = JsonContent.Create(new { messages = new[] { new { role = "user", text = "una sartén" } }, context, conversationId }),
        };
        if (user is not null)
            request.Headers.Add("X-Test-User", user);
        if (language is not null)
            request.Headers.AcceptLanguage.ParseAdd(language);
        return request;
    }

    [Fact]
    public async Task Nobody_signed_in_asks_and_the_body_becomes_a_turn_in_their_language_with_a_conversation_minted()
    {
        var (client, dispatcher, owner) = Compose();
        using var _ = owner;

        var response = await client.SendAsync(
            Turn(language: "en-GB", context: new { kind = "Product", key = "PAN-1" }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var turn = Assert.Single(dispatcher.Asked);
        Assert.Equal(("en", "una sartén", new TinoContext(TinoContextKind.Product, "PAN-1")), (turn.Culture, turn.Messages.Single().Text, turn.Context));
        Assert.False(string.IsNullOrWhiteSpace(turn.ConversationId));

        var answer = await response.Content.ReadFromJsonAsync<TinoAnswer>(
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { WireJson.Enums() } },
            TestContext.Current.CancellationToken);
        Assert.Equal((turn.ConversationId, TinoKind.Ask, "una sartén"), (answer!.ConversationId, answer.Kind, answer.SearchText));
        Assert.Equal("en", response.Content.Headers.ContentLanguage.Single());
    }

    [Fact]
    public async Task A_conversation_the_client_names_is_the_one_continued_and_no_context_is_none()
    {
        var (client, dispatcher, owner) = Compose();
        using var _ = owner;

        await client.SendAsync(Turn(conversationId: "conv-7"), TestContext.Current.CancellationToken);

        var turn = Assert.Single(dispatcher.Asked);
        Assert.Equal(("conv-7", (TinoContext?)null, "es"), (turn.ConversationId, turn.Context, turn.Culture));
    }

    /// <summary>
    /// Whoever is not signed in is counted by where they call from; a person by who
    /// they are, on a count of their own. (The test server gives every caller the
    /// same address — none — so "another address" is not something it can show.)
    /// </summary>
    [Fact]
    public async Task The_thirteenth_turn_in_a_minute_is_refused_and_a_signed_in_person_has_a_count_of_their_own()
    {
        var (client, _, owner) = Compose();
        using var _ = owner;

        for (var turn = 0; turn < 12; turn++)
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Turn(), TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Turn(), TestContext.Current.CancellationToken)).StatusCode);

        for (var turn = 0; turn < 12; turn++)
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Turn("lucia"), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Turn("lucia"), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Turn("marta"), TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>Public is a decision written in the code (invariant 10), and these two are that decision.</summary>
    [Theory]
    [InlineData("/api/assistant/turns")]
    [InlineData("/api/assistant/status")]
    public void Tinos_endpoints_are_public_by_decision(string route)
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == route);

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    private static readonly Dictionary<string, ModelProfile> Local = new() { ["local"] = new ModelProfile { Kind = ModelKind.Scripted } };

    public static TheoryData<string, TinoOptions?, bool, bool> Availabilities => new()
    {
        { "as the shop ships: off", null, false, false },
        { "on, with its profile", new TinoOptions { Enabled = true, Model = "local" }, true, true },
        { "on, with only its fallback registered", new TinoOptions { Enabled = true, Model = "hosted", Fallback = "local" }, true, true },
        { "off, though its profile is there", new TinoOptions { Enabled = false, Model = "local" }, true, false },
        { "on, and no profile of that name", new TinoOptions { Enabled = true, Model = "hosted" }, true, false },
        { "on, and no profile named", new TinoOptions { Enabled = true }, true, false },
    };

    /// <summary>What the storefront asks before drawing Tino's door: configured, never probed — no model is called.</summary>
    [Theory]
    [MemberData(nameof(Availabilities))]
    public async Task Tino_is_available_when_it_is_on_and_a_profile_it_names_is_registered(
        string what, TinoOptions? options, bool withProfile, bool available)
    {
        var (client, dispatcher, owner) = Compose(options, withProfile ? Local : null);
        using var _ = owner;

        var status = await client.GetFromJsonAsync<TinoAvailability>("/api/assistant/status", TestContext.Current.CancellationToken);

        Assert.True(status!.Available == available, what);
        Assert.Empty(dispatcher.Asked);
    }
}
