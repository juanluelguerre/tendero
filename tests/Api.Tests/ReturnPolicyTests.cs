using System.Net;
using System.Net.Http.Json;
using ElGuerre.Tendero.Ordering.Domain;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The return window is a number the shop says out loud in two places — the
/// product page promises it before you buy, the order page repeats it after the
/// parcel arrives — and it is enforced in exactly one: <see cref="ReturnRequest"/>.
///
/// **It was already said twice and guarded nowhere.** The aggregate held
/// fourteen days and the order page wrote `{ days: 14 }` into its template, so
/// shortening the window would have left a screen promising the old one, with
/// nothing red anywhere. Publishing it is the same decision `/api/auth/config`
/// records for the issuer: a value the server owns travels from the server, or
/// the two copies eventually disagree.
///
/// This test is what makes the endpoint unable to drift from the rule.
/// </summary>
public sealed class ReturnPolicyTests
{
    private sealed record ReturnPolicyResponse(int WindowDays);

    [Fact]
    public async Task The_published_window_is_the_one_the_aggregate_enforces()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/returns/policy", TestContext.Current.CancellationToken);

        // No token, and it must answer: somebody reading a product page has not
        // signed in, and the endpoint carries an explicit AllowAnonymous.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var policy = await response.Content.ReadFromJsonAsync<ReturnPolicyResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(policy);
        Assert.Equal((int)ReturnRequest.Window.TotalDays, policy.WindowDays);
    }
}
