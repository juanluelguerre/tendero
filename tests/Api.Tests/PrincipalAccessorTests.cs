using System.Security.Claims;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The principal is where the segment has to come from, so the principal is
/// what has to carry it. This drives the accessor the API actually registers,
/// with only the directory replaced.
/// </summary>
public sealed class PrincipalAccessorTests
{
    [Fact]
    public void A_signed_in_customer_carries_the_segment_accounts_holds_for_them()
    {
        var marta = CustomerId.New();

        using var factory = new TenderoApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<ICustomerDirectory>(_ =>
                    new OneCustomerDirectory("marta", new KnownCustomer(marta, "vip")))));

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "marta")], authenticationType: "test"))
        };

        var principal = scope.ServiceProvider.GetRequiredService<IPrincipalAccessor>().Current;

        Assert.Equal(marta, principal.Customer);
        Assert.Equal("vip", principal.Segment);
    }

    private sealed class OneCustomerDirectory(string subject, KnownCustomer customer) : ICustomerDirectory
    {
        public Task<KnownCustomer?> ForSubjectAsync(string requested, CancellationToken ct = default) =>
            Task.FromResult(requested == subject ? customer : null);
    }
}