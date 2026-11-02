using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.Channels.Tests.Contract;

namespace ElGuerre.Tendero.Channels.Tests;

/// <summary>The fake the slices run against passes the contract Shopify does.</summary>
public sealed class InMemorySalesChannelContractTests : SalesChannelContractTests
{
    protected override ISalesChannel CreateChannel() => new InMemorySalesChannel();

    protected override Task<ChannelProductStatus?> StatusOnChannelAsync(ISalesChannel channel, string code) =>
        Task.FromResult(((InMemorySalesChannel)channel).StatusOf(code));

    protected override string Location => "main";
}
