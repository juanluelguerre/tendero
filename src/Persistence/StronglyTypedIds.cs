using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// How every strongly-typed id reaches a column: as the bare value it wraps.
///
/// It used to be written per property — the same two lambdas fourteen times
/// across seven configurations, with a nullable variant that had to remember
/// <c>id!.Value.Value</c>. EF Core's pre-convention configuration says it once
/// per TYPE, and it covers the nullable form and the properties inside a JSON
/// column too, so a new id is one line here and none anywhere else.
///
/// Column names, lengths and <c>ValueGeneratedNever</c> stay in the
/// configurations: they are facts about a property, not about the type.
/// </summary>
internal static class StronglyTypedIds
{
    public static void Configure(ModelConfigurationBuilder conventions)
    {
        conventions.Properties<ProductId>().HaveConversion<ProductIdConverter>();
        conventions.Properties<VariantId>().HaveConversion<VariantIdConverter>();
        conventions.Properties<OrderId>().HaveConversion<OrderIdConverter>();
        conventions.Properties<CartId>().HaveConversion<CartIdConverter>();
        conventions.Properties<ReturnRequestId>().HaveConversion<ReturnRequestIdConverter>();
        conventions.Properties<CustomerId>().HaveConversion<CustomerIdConverter>();
        conventions.Properties<ImageId>().HaveConversion<ImageIdConverter>();
        conventions.Properties<AgentId>().HaveConversion<AgentIdConverter>();
    }

    private sealed class ProductIdConverter() : ValueConverter<ProductId, Guid>(id => id.Value, value => new ProductId(value));
    private sealed class VariantIdConverter() : ValueConverter<VariantId, Guid>(id => id.Value, value => new VariantId(value));
    private sealed class OrderIdConverter() : ValueConverter<OrderId, Guid>(id => id.Value, value => new OrderId(value));
    private sealed class CartIdConverter() : ValueConverter<CartId, Guid>(id => id.Value, value => new CartId(value));
    private sealed class ReturnRequestIdConverter() : ValueConverter<ReturnRequestId, Guid>(id => id.Value, value => new ReturnRequestId(value));
    private sealed class CustomerIdConverter() : ValueConverter<CustomerId, Guid>(id => id.Value, value => new CustomerId(value));
    private sealed class ImageIdConverter() : ValueConverter<ImageId, string>(id => id.Value, value => new ImageId(value));
    private sealed class AgentIdConverter() : ValueConverter<AgentId, string>(id => id.Value, value => new AgentId(value));
}
