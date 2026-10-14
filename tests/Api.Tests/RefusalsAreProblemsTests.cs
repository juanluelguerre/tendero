using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Inventory.Features.StockHealth;
using ElGuerre.Tendero.Pricing.Features.QuoteCart;
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
/// A slice's refusal, as it leaves the API (phase 13b, the wire's spec, W-4).
/// The slice names the refusal and hands over what it knows — its detail in both
/// languages, the part of the request it is about, what it carries — and the
/// host writes the problem: one shape, in the language asked. Here on two
/// refusals that between them use every part of that, over dispatchers that
/// answer what a database would.
/// </summary>
public sealed class RefusalsAreProblemsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (HttpClient Client, IDisposable Owner) Compose()
    {
        var root = new TenderoApiFactory();
        var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IQueryDispatcher>(new Refusing());
            services.AddSingleton<IPolicyEvaluator, LetEveryoneThrough>();
        }));
        return (factory.CreateClient(), root);
    }

    /// <summary>
    /// The quote refusing SKUs the shop does not sell: a `code` to branch on, the
    /// title and what it is about this time in the language asked, the JSONPath
    /// of the part of the request it concerns — and nothing of how it got there.
    /// </summary>
    [Fact]
    public async Task A_refusal_says_its_code_and_its_sentences_in_the_language_asked()
    {
        var (client, owner) = Compose();
        using var _ = owner;
        var body = new { lines = new[] { new { sku = "NOPE-1", quantity = 1 } } };

        var spanish = await client.PostAsJsonAsync("/api/pricing/quote?culture=es", body, Ct);
        var english = await client.PostAsJsonAsync("/api/pricing/quote?culture=en", body, Ct);
        var (es, en) = (JsonNode.Parse(await spanish.Content.ReadAsStringAsync(Ct))!, JsonNode.Parse(await english.Content.ReadAsStringAsync(Ct))!);

        Assert.Equal((HttpStatusCode.BadRequest, "application/problem+json"), (spanish.StatusCode, spanish.Content.Headers.ContentType?.MediaType));
        Assert.Equal(("UnknownSkus", 400, "$.lines"), (es["code"]!.GetValue<string>(), es["status"]!.GetValue<int>(), es["path"]!.GetValue<string>()));
        Assert.Equal("https://tendero.dev/problems/unknown-skus", es["type"]!.GetValue<string>());

        Assert.Equal(QuoteRefusals.UnknownSkus.Title.In("es"), es["title"]!.GetValue<string>());
        Assert.Equal(QuoteRefusals.UnknownSkus.Title.In("en"), en["title"]!.GetValue<string>());
        Assert.Equal(("No se venden: NOPE-1.", "Not on sale: NOPE-1."), (es["detail"]!.GetValue<string>(), en["detail"]!.GetValue<string>()));
        Assert.Equal(("es", "en"), (string.Join(',', spanish.Content.Headers.ContentLanguage), string.Join(',', english.Content.Headers.ContentLanguage)));
        // And a cache is told the answer depends on the language asked (ADR 0013): a
        // refusal in Spanish is not what an English reader is handed next.
        Assert.Contains("Accept-Language", spanish.Headers.Vary);

        // What waited in the problem to be said is not sent as it waited.
        Assert.DoesNotContain(es.AsObject(), member => member.Key.StartsWith('$'));
    }

    /// <summary>
    /// And one that hands something over: asked to restock a warehouse the shop
    /// does not have, the refusal carries the ones it does — as members of the
    /// problem itself, which is where a client that only knows RFC 9457 looks.
    /// </summary>
    [Fact]
    public async Task What_a_refusal_carries_travels_as_members_of_the_problem()
    {
        var (client, owner) = Compose();
        using var _ = owner;

        var response = await client.GetAsync("/api/dashboard/restock?warehouse=Madrid&culture=en", Ct);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

        Assert.Equal((HttpStatusCode.BadRequest, "UnknownWarehouse"), (response.StatusCode, problem["code"]!.GetValue<string>()));
        Assert.Equal(["BCN", "MAD"], problem["warehouses"]!.AsArray().Select(warehouse => warehouse!.GetValue<string>()));
        Assert.Equal("'Madrid' is not one of its own: BCN, MAD.", problem["detail"]!.GetValue<string>());
    }

    /// <summary>Who may ask is another test's: here anybody does.</summary>
    private sealed class LetEveryoneThrough : IPolicyEvaluator
    {
        public Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task<PolicyAuthorizationResult> AuthorizeAsync(
            AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object? resource) =>
            Task.FromResult(PolicyAuthorizationResult.Success());
    }

    /// <summary>Answers each question with the refusal a database would have led to.</summary>
    private sealed class Refusing : IQueryDispatcher
    {
        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
            Task.FromResult((TResult)(object)(query switch
            {
                QuoteCartQuery quote => new QuoteCartResult(QuoteOutcome.UnknownSkus, null, [.. quote.Lines.Select(line => line.Sku)]),
                GetRestockListQuery restock => new RestockList(restock.Warehouse, true, ["BCN", "MAD"], 5, 0, []),
                _ => throw new InvalidOperationException($"{query.GetType().Name} was not expected here."),
            }));
    }
}
