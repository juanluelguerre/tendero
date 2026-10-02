using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.SetProductTranslation;

/// <param name="ProductId">Flat, as on the wire: the model writes JSON, never a record struct.</param>
public sealed record SetProductTranslationPayload(Guid ProductId, string Culture, string Name, string? Description);

/// <summary>
/// Makes the translation proposable (phase 12). "Now" is the product's text in
/// that one culture, so a proposal goes stale only when somebody wrote that
/// language meanwhile — a change to the Spanish name does not stop an English one.
/// </summary>
public sealed class SetProductTranslationProposal(IServiceProvider services, IProductRepository products)
    : ProposableCommand<SetProductTranslationPayload, SetProductTranslationCommand, SetProductTranslationResult>(services)
{
    public const string CommandName = "SetProductTranslation";

    public override string Name => CommandName;

    public override LocalizedText Label { get; } = new(new Dictionary<string, string>
    {
        ["en"] = "Translate a product",
        ["es"] = "Traducir un producto",
    });

    protected override SetProductTranslationCommand ToCommand(SetProductTranslationPayload payload) =>
        new(new ProductId(payload.ProductId), payload.Culture, payload.Name, payload.Description);

    protected override async Task<ProposalSubject?> ReadAsync(SetProductTranslationCommand command, CancellationToken cancellationToken)
    {
        // An archived product is out of the catalogue: no subject to translate, so
        // nothing is proposed for it, and archiving one makes its proposal stale.
        var product = await products.FindByIdAsync(command.ProductId, cancellationToken);
        if (product is null || product.Status == ProductStatus.Archived)
            return null;

        var culture = Culture.Normalize(command.Culture);
        var now = new TranslationNow(product.Name.Values.GetValueOrDefault(culture), product.Description?.Values.GetValueOrDefault(culture));

        // The shopkeeper reads "English name", never the command's name (spec, decision 7).
        var code = culture.ToUpperInvariant();
        List<ProposalChange> changes =
        [
            new(LocalizedText.Both($"Name · {code}", $"Nombre · {code}"), ProposalChange.Written(culture, now.Name), ProposalChange.Written(culture, command.Name)),
        ];
        if (command.Description is not null)
        {
            changes.Add(new(LocalizedText.Both($"Description · {code}", $"Descripción · {code}"),
                ProposalChange.Written(culture, now.Description), ProposalChange.Written(culture, command.Description)));
        }

        return new ProposalSubject(product.Code, now, new ProposalDisplay(product.Name, KindIn(culture), changes));
    }

    private static LocalizedText KindIn(string culture) => culture == "en"
        ? LocalizedText.Both("English name", "Nombre en inglés")
        : LocalizedText.Both("Spanish name", "Nombre en español");

    private sealed record TranslationNow(string? Name, string? Description);
}
