using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>Knowledge's doubts about pictures over EF Core.</summary>
internal sealed class EfPictureDoubtRepository(TenderoDbContext context) : IPictureDoubtRepository
{
    public Task<PictureDoubt?> FindDoubtAsync(PictureDoubtId id, CancellationToken cancellationToken = default) =>
        context.PictureDoubts.FirstOrDefaultAsync(doubt => doubt.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PictureDoubt>> DoubtsAboutAsync(ProductId productId, ImageId picture, CancellationToken cancellationToken = default)
    {
        var pictureId = picture.Value;
        return await context.PictureDoubts
            .Where(doubt => doubt.ProductId == productId && doubt.PictureId == pictureId)
            .ToListAsync(cancellationToken);
    }

    public async Task<PictureDoubtPage> OpenDoubtsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var open = context.PictureDoubts.AsNoTracking().Where(doubt => doubt.Status == PictureDoubtStatus.Open);

        return new PictureDoubtPage(
            await open.OrderBy(doubt => doubt.RaisedAt).ThenBy(doubt => doubt.Id)
                .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
                .ToListAsync(cancellationToken),
            await open.CountAsync(cancellationToken));
    }

    public void Add(PictureDoubt doubt) => context.PictureDoubts.Add(doubt);
}
