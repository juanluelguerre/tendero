using System.Net;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
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
/// How <c>?period=</c> reaches the dashboard's queries (ADR 0043). It used to be
/// parsed by hand in every endpoint, with its own 400 and its own wording; now
/// it is bound like every other closed set in a query string, so what is pinned
/// here is the one thing the dashboard adds — a week when nobody asks — and that
/// the shared rule is the one actually in front of it.
///
/// The endpoints are the shopkeeper's, and who may read them is
/// <c>EndpointAuthorizationTests</c>' question, not this one's: authorization
/// is let through so the request reaches the binding.
/// </summary>
public sealed class DashboardPeriodBindingTests
{
    private sealed class RecordingDispatcher : IQueryDispatcher
    {
        public GetSalesSummaryQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = query as GetSalesSummaryQuery;
            return Task.FromResult((TResult)(object)DashboardFigures.Sales(Asked!.Period, [], []));
        }
    }

    private sealed class LetEveryoneThrough : IPolicyEvaluator
    {
        public Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task<PolicyAuthorizationResult> AuthorizeAsync(
            AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object? resource) =>
            Task.FromResult(PolicyAuthorizationResult.Success());
    }

    [Theory]
    [InlineData(null, DashboardPeriod.Week)]
    [InlineData("day", DashboardPeriod.Day)]
    [InlineData("MONTH", DashboardPeriod.Month)]
    public async Task A_period_is_read_by_its_name_and_is_a_week_by_default(string? period, DashboardPeriod expected)
    {
        var (status, asked) = await Sales(period);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(expected, asked?.Period);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("year")]
    [InlineData("Day, Week")]
    public async Task A_number_a_combination_or_a_name_it_does_not_have_is_refused(string period)
    {
        var (status, asked) = await Sales(period);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(asked);
    }

    private static async Task<(HttpStatusCode Status, GetSalesSummaryQuery? Asked)> Sales(string? period)
    {
        var dispatcher = new RecordingDispatcher();
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IQueryDispatcher>(dispatcher);
            services.AddSingleton<IPolicyEvaluator, LetEveryoneThrough>();
        }));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            period is null ? "/api/dashboard/sales" : $"/api/dashboard/sales?period={Uri.EscapeDataString(period)}",
            TestContext.Current.CancellationToken);

        return (response.StatusCode, dispatcher.Asked);
    }
}
