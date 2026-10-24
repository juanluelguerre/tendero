using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// A store's migration asked of a source no step can bring from (P16-21): refused
/// with its code before anything runs, rather than five steps each answering "no
/// such source". Who may ask is <c>EndpointAuthorizationTests</c>' question, so
/// authorization is let through.
/// </summary>
public sealed class StoreMigrationEndpointTests
{
    private sealed class LetEveryoneThrough : IPolicyEvaluator
    {
        public Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task<PolicyAuthorizationResult> AuthorizeAsync(
            AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object? resource) =>
            Task.FromResult(PolicyAuthorizationResult.Success());
    }

    [Fact]
    public async Task A_source_no_step_can_bring_from_is_refused_with_its_code()
    {
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IPolicyEvaluator, LetEveryoneThrough>()));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/migration?culture=en", new MigrateStoreRequest("nowhere", Rehearse: true), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("UnknownSource", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("$.source", problem.RootElement.GetProperty("path").GetString());
    }
}
