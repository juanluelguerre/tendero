using System.Diagnostics;
using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Accounts.Features.ManageTrail;

/// <summary>
/// Keeps the trail's promise for the customers who do not come back: every entry
/// older than <see cref="BrowsingHistory.Retention"/> is removed from the table,
/// not only hidden from the page (R-8, docs/privacy.md).
///
/// A customer who returns never sees an old entry — the aggregate filters on
/// read and drops on write — so this sweep exists only for the rows nobody
/// touches again. Once at start and then every <see cref="Every"/>: an entry
/// outlives its ninety days by at most that long.
/// </summary>
public sealed partial class ForgetOldBrowsingWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<ForgetOldBrowsingWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(6);

    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every, clock);

        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The next pass tries again; a sweep that stopped the worker would
                // take the outbox down with it.
                LogSweepFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass: what happened before the retention's cutoff is forgotten.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        using var activity = Telemetry.StartActivity("accounts.browsing.retention");
        await using var scope = scopes.CreateAsyncScope();

        var changed = await scope.ServiceProvider.GetRequiredService<IBrowsingRetention>()
            .ForgetBeforeAsync(clock.GetUtcNow() - BrowsingHistory.Retention, ct);

        activity?.SetTag("accounts.browsing.trails_swept", changed);
        return changed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The browsing trail's retention sweep failed; it runs again at the next pass.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
