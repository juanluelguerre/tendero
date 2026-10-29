using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Pricing.Domain;
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
        conventions.Properties<CheckoutSessionId>().HaveConversion<CheckoutSessionIdConverter>();
        conventions.Properties<ReturnRequestId>().HaveConversion<ReturnRequestIdConverter>();
        conventions.Properties<CustomerId>().HaveConversion<CustomerIdConverter>();
        conventions.Properties<AddressEntryId>().HaveConversion<AddressEntryIdConverter>();
        conventions.Properties<SavedMethodId>().HaveConversion<SavedMethodIdConverter>();
        conventions.Properties<ImageId>().HaveConversion<ImageIdConverter>();
        conventions.Properties<AgentId>().HaveConversion<AgentIdConverter>();
        conventions.Properties<ClaimId>().HaveConversion<ClaimIdConverter>();
        conventions.Properties<PictureDoubtId>().HaveConversion<PictureDoubtIdConverter>();
        conventions.Properties<SourceDocumentId>().HaveConversion<SourceDocumentIdConverter>();
        conventions.Properties<CampaignCode>().HaveConversion<CampaignCodeConverter>();
        conventions.Properties<ProposalId>().HaveConversion<ProposalIdConverter>();
        conventions.Properties<ImportRunId>().HaveConversion<ImportRunIdConverter>();
    }

    private sealed class ProductIdConverter() : ValueConverter<ProductId, Guid>(id => id.Value, value => new ProductId(value));
    private sealed class VariantIdConverter() : ValueConverter<VariantId, Guid>(id => id.Value, value => new VariantId(value));
    private sealed class OrderIdConverter() : ValueConverter<OrderId, Guid>(id => id.Value, value => new OrderId(value));
    private sealed class CartIdConverter() : ValueConverter<CartId, Guid>(id => id.Value, value => new CartId(value));
    private sealed class CheckoutSessionIdConverter() : ValueConverter<CheckoutSessionId, Guid>(id => id.Value, value => new CheckoutSessionId(value));
    private sealed class ReturnRequestIdConverter() : ValueConverter<ReturnRequestId, Guid>(id => id.Value, value => new ReturnRequestId(value));
    private sealed class CustomerIdConverter() : ValueConverter<CustomerId, Guid>(id => id.Value, value => new CustomerId(value));
    private sealed class SavedMethodIdConverter() : ValueConverter<SavedMethodId, Guid>(id => id.Value, value => new SavedMethodId(value));
    private sealed class AddressEntryIdConverter() : ValueConverter<AddressEntryId, Guid>(id => id.Value, value => new AddressEntryId(value));
    private sealed class ImageIdConverter() : ValueConverter<ImageId, string>(id => id.Value, value => new ImageId(value));
    private sealed class AgentIdConverter() : ValueConverter<AgentId, string>(id => id.Value, value => new AgentId(value));
    private sealed class ClaimIdConverter() : ValueConverter<ClaimId, Guid>(id => id.Value, value => new ClaimId(value));
    private sealed class PictureDoubtIdConverter() : ValueConverter<PictureDoubtId, Guid>(id => id.Value, value => new PictureDoubtId(value));
    private sealed class SourceDocumentIdConverter() : ValueConverter<SourceDocumentId, string>(id => id.Value, value => new SourceDocumentId(value));
    private sealed class CampaignCodeConverter() : ValueConverter<CampaignCode, string>(code => code.Value, value => new CampaignCode(value));
    private sealed class ProposalIdConverter() : ValueConverter<ProposalId, Guid>(id => id.Value, value => new ProposalId(value));
    private sealed class ImportRunIdConverter() : ValueConverter<ImportRunId, Guid>(id => id.Value, value => new ImportRunId(value));
}
