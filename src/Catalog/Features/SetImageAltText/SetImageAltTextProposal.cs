using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.SetImageAltText;

/// <param name="ProductId">Flat, as on the wire: a model writes JSON, never a record struct.</param>
/// <param name="ImageId">Which of the product's pictures: its content's hash, as the shop serves it.</param>
public sealed record SetImageAltTextPayload(Guid ProductId, string ImageId, string Spanish, string English);

/// <summary>
/// Makes a picture's alt text proposable (phase 13a, I-3). "Now" is that one
/// picture's words, so a proposal goes stale when somebody described that picture
/// meanwhile, and not when another picture or the product's name changed. The
/// display carries which picture it is, because whoever approves a description
/// has to see what is described.
/// </summary>
public sealed class SetImageAltTextProposal(IServiceProvider services, IProductRepository products)
    : ProposableCommand<SetImageAltTextPayload, SetImageAltTextCommand, SetImageAltTextResult>(services)
{
    public const string CommandName = "SetImageAltText";

    public override string Name => CommandName;

    public override LocalizedText Label { get; } = LocalizedText.Both("Describe a product picture", "Describir una imagen de producto");

    /// <summary>
    /// Describing a picture takes seeing it, and Steward's turn carries none: this
    /// is proposed by the job that reads pictures, never by a chat that cannot
    /// (the author, 2026-10-06).
    /// </summary>
    public override bool OnlyFromAJob => true;

    protected override SetImageAltTextCommand ToCommand(SetImageAltTextPayload payload) =>
        new(new ProductId(payload.ProductId), new ImageId(payload.ImageId), payload.Spanish, payload.English);

    protected override async Task<ProposalSubject?> ReadAsync(SetImageAltTextCommand command, CancellationToken cancellationToken)
    {
        // An archived product is out of the catalogue, and a picture that is not the
        // product's is nobody's to describe: no subject, so nothing is proposed, and
        // a proposal whose picture left the gallery goes stale.
        var product = await products.FindByIdAsync(command.ProductId, cancellationToken);
        if (product is null || product.Status == ProductStatus.Archived
            || product.Images.FirstOrDefault(image => image.Id == command.ImageId) is not { } picture)
            return null;

        var now = new AltNow(picture.Id.Value, picture.Alt?.Values.GetValueOrDefault("es"), picture.Alt?.Values.GetValueOrDefault("en"));

        return new ProposalSubject(product.Code, now, new ProposalDisplay(
            product.Name,
            LocalizedText.Both("Alt text", "Texto alternativo"),
            [
                new(LocalizedText.Both("Alt text · ES", "Texto alternativo · ES"),
                    ProposalChange.Written("es", now.Spanish), ProposalChange.Written("es", command.Spanish), "spanish"),
                new(LocalizedText.Both("Alt text · EN", "Texto alternativo · EN"),
                    ProposalChange.Written("en", now.English), ProposalChange.Written("en", command.English), "english"),
            ],
            picture.Id.Value));
    }

    private sealed record AltNow(string Image, string? Spanish, string? English);
}
