using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>A customer's trail as an aggregate (R-8): what the commands load, change and save.</summary>
internal sealed class EfBrowsingHistoryRepository(TenderoDbContext context) : IBrowsingHistoryRepository
{
    // WITH tracking: whoever loads a trail does it to change it.
    public Task<BrowsingHistory?> FindAsync(CustomerId customer, CancellationToken ct = default) =>
        context.BrowsingHistories.FirstOrDefaultAsync(history => history.CustomerId == customer, ct);

    public void Add(BrowsingHistory history) => context.BrowsingHistories.Add(history);
}

/// <summary>
/// A customer's trail as the page reads it: untracked, never saved back, and
/// only what is kept at the moment asked — the aggregate's retention applied on
/// read, so a trail nobody wrote to for three months is empty before the sweep
/// gets to it.
/// </summary>
internal sealed class EfBrowsingHistoryReader(TenderoDbContext context) : IBrowsingHistoryReader
{
    public async Task<BrowsingTrail?> ForCustomerAsync(CustomerId customer, DateTimeOffset now, CancellationToken ct = default) =>
        await context.BrowsingHistories.AsNoTracking().FirstOrDefaultAsync(history => history.CustomerId == customer, ct)
            is { } history
            ? BrowsingTrail.Of(history, now)
            : null;
}

/// <summary>
/// The retention sweep's one statement (R-8): every entry older than the cutoff
/// removed from every trail, in SQL, without loading a trail — the aggregate's
/// rule applied in bulk to the customers who never came back to trigger it.
/// It reads the entries' <c>at</c>, which is why their jsonb shape is written
/// down beside the converter (<see cref="Jsonb.TrailConverter"/>).
/// </summary>
internal sealed class EfBrowsingRetention(TenderoDbContext context) : IBrowsingRetention
{
    public Task<int> ForgetBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default) =>
        context.Database.ExecuteSqlAsync($"""
            update accounts.browsing_histories set
                views = coalesce((select jsonb_agg(entry) from jsonb_array_elements(views) entry
                                  where (entry->>'at')::timestamptz >= {cutoff}), '[]'::jsonb),
                searches = coalesce((select jsonb_agg(entry) from jsonb_array_elements(searches) entry
                                     where (entry->>'at')::timestamptz >= {cutoff}), '[]'::jsonb)
            where exists (select 1 from jsonb_array_elements(views) entry where (entry->>'at')::timestamptz < {cutoff})
               or exists (select 1 from jsonb_array_elements(searches) entry where (entry->>'at')::timestamptz < {cutoff})
            """, ct);
}
