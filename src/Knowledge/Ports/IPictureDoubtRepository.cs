using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Ports;

/// <summary>
/// Knowledge's doubts about pictures (phase 13a, I-4d). Its own port, beside the
/// one for documents and claims: who raises and decides a doubt asks for nothing
/// else, and who proposes a claim never asks for a doubt.
/// </summary>
public interface IPictureDoubtRepository
{
    Task<PictureDoubt?> FindDoubtAsync(PictureDoubtId id, CancellationToken cancellationToken = default);

    /// <summary>Every doubt ever raised about one picture of a product, whatever became of it.</summary>
    Task<IReadOnlyList<PictureDoubt>> DoubtsAboutAsync(ProductId productId, ImageId picture, CancellationToken cancellationToken = default);

    /// <summary>A page of the doubts waiting for a person, the oldest first, with the doubt's id as the tie-break.</summary>
    Task<PictureDoubtPage> OpenDoubtsAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    void Add(PictureDoubt doubt);
}

/// <param name="Total">How many are open, not how many are on the page.</param>
public sealed record PictureDoubtPage(IReadOnlyList<PictureDoubt> Doubts, int Total);
