using System.Text.Json;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Returns;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>What the storefront's assistant may read of the shop's promise about returns (phase 12b).</summary>
public sealed class ReturnPolicyToolTests
{
    /// <summary>The dispatcher over the policy's own handler: the number is the domain's.</summary>
    private sealed class Shop : IQueryDispatcher
    {
        public async Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
            (TResult)(object)await new ReturnPolicyHandler().HandleAsync((ReturnPolicyQuery)(object)query, cancellationToken);
    }

    private static readonly int Days = (int)ReturnRequest.Window.TotalDays;

    private static Task<ReadAnswer> Read(string arguments) =>
        new ReturnPolicyTool(new Shop()).ReadAsync(JsonDocument.Parse(arguments).RootElement.Clone(), TestContext.Current.CancellationToken);

    [Fact]
    public void It_is_a_shoppers_read()
    {
        var tool = new ReturnPolicyTool(new Shop());

        Assert.Equal((ToolAudience.Shopper, "get_return_policy"), (tool.Audience, tool.Name));
    }

    [Theory]
    [InlineData("""{"culture":"es"}""", "Las devoluciones se aceptan durante {0} días desde la entrega.")]
    [InlineData("""{"culture":"en"}""", "Returns are accepted for {0} days from delivery.")]
    [InlineData("""{}""", "Las devoluciones se aceptan durante {0} días desde la entrega.")]
    public async Task The_policy_is_one_fact_about_the_shop_in_the_language_asked_for_with_the_domains_days(string arguments, string says)
    {
        var answer = await Read(arguments);

        var expected = string.Format(System.Globalization.CultureInfo.InvariantCulture, says, Days);
        Assert.Equal([new Fact(Fact.TheShop, FactSource.Policy, expected)], answer.Facts);
        Assert.Equal(new ReturnPolicyForShopper(Days, expected), answer.Data);
        Assert.Equal((1, (int?)null), (answer.Rows, answer.Total));
    }
}
