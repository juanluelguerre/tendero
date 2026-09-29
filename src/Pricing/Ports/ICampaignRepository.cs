using ElGuerre.Tendero.Pricing.Domain;

namespace ElGuerre.Tendero.Pricing.Ports;

/// <summary>
/// The campaigns a shopkeeper writes (ADR 0044): Pricing's first table. The port
/// is Pricing's and the adapter is Persistence's, as Accounts' agents and
/// Knowledge's claims already work — Pricing still does not know EF Core exists.
/// </summary>
public interface ICampaignRepository
{
    /// <summary>Tracked: the campaign a command is about to change.</summary>
    Task<Campaign?> FindAsync(CampaignCode code, CancellationToken cancellationToken = default);

    /// <summary>Every campaign, whatever its status: the backoffice's list. Untracked.</summary>
    Task<IReadOnlyList<Campaign>> AllAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether the table holds any campaign at all: the seeder's one question.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    void Add(Campaign campaign);
}

/// <summary>
/// What the shop reads of the campaigns: only the scheduled ones, untracked, and
/// never a write. A port of its own so the public list and the search's filter
/// ask a narrow question, as <c>IAgentDirectory</c> does beside the agents'
/// repository.
/// </summary>
public interface ICampaignReader
{
    /// <summary>
    /// The scheduled campaigns whose window has not closed at <paramref name="at"/>:
    /// the candidates for <see cref="CampaignLineup.Near"/>, which applies the
    /// horizon and the order. The window's end is a column, so this is one indexed
    /// read, not every campaign ever run.
    /// </summary>
    Task<IReadOnlyList<Campaign>> ScheduledNotOverAsync(DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>The campaign by its code, whatever its status; the caller asks <see cref="Campaign.IsLiveAt"/>.</summary>
    Task<Campaign?> FindAsync(CampaignCode code, CancellationToken cancellationToken = default);
}
