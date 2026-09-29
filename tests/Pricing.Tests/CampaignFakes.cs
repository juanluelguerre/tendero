using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>Deterministic fakes for the campaign slices: an in-memory table, the promotions as given, an image store holding the ids given.</summary>
internal sealed class InMemoryCampaigns(params Campaign[] seeded) : ICampaignRepository, ICampaignReader, IUnitOfWork
{
    private readonly List<Campaign> all = [.. seeded];

    public int Saves { get; private set; }

    public Task<Campaign?> FindAsync(CampaignCode code, CancellationToken cancellationToken = default) =>
        Task.FromResult(this.all.FirstOrDefault(campaign => campaign.Code == code));

    public Task<IReadOnlyList<Campaign>> AllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Campaign>>(this.all);

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => Task.FromResult(this.all.Count > 0);

    public void Add(Campaign campaign) => this.all.Add(campaign);

    public Task<IReadOnlyList<Campaign>> ScheduledNotOverAsync(DateTimeOffset at, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Campaign>>(
            [.. this.all.Where(campaign => campaign.Status == CampaignStatus.Scheduled && campaign.EndsAt > at)]);

    public Task SaveChangesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

internal sealed class GivenPromotions(params Promotion[] all) : IPromotionReader
{
    public Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Promotion>>(all);
}

internal sealed class GivenImages(params string[] ids) : IImageStore
{
    public Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default) =>
        throw new NotSupportedException("The campaign handlers never store an image.");

    public Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default) =>
        Task.FromResult(ids.Contains(id.Value) ? new StoredImage(new MemoryStream([1, 2, 3]), "image/webp") : null);
}
