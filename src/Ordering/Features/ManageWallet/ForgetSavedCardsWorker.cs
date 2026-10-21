using System.Diagnostics;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Ordering.Features.ManageWallet;

/// <summary>
/// Keeps the wallet's promise that **a token outlives nothing** (P15-4): a method
/// removed while its provider could not be reached left the wallet at once and
/// waits here, token and all, until the provider confirms forgetting it.
///
/// Once at start and then every <see cref="Every"/>. A pass that cannot reach the
/// provider leaves the token waiting for the next one; forgetting is idempotent,
/// so a pass that forgets a token twice does no harm.
/// </summary>
public sealed partial class ForgetSavedCardsWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<ForgetSavedCardsWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(15);

    /// <summary>Wallets per pass: laboratory scale, and a ceiling so a long outage does not become one long transaction.</summary>
    public const int Batch = 50;

    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every, clock);

        do
        {
            try
            {
                await ForgetAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogPassFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass: every waiting token offered to its provider again. Answers how many were forgotten.</summary>
    public async Task<int> ForgetAsync(CancellationToken ct)
    {
        using var activity = Telemetry.StartActivity("ordering.wallet.forget_waiting");
        await using var scope = scopes.CreateAsyncScope();

        var wallets = scope.ServiceProvider.GetRequiredService<IWalletRepository>();
        var payments = scope.ServiceProvider.GetRequiredService<IPaymentProviderRegistry>();
        var forgotten = 0;

        foreach (var wallet in await wallets.WithTokensToForgetAsync(Batch, ct))
        {
            foreach (var waiting in wallet.ToForget.ToList())
            {
                if ((await payments.Get(waiting.ProviderKey).ForgetInstrumentAsync(waiting.Token, ct)).Succeeded)
                {
                    wallet.Forgotten(clock, waiting.Token);
                    forgotten++;
                }
            }
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);

        activity?.SetTag("ordering.wallet.tokens_forgotten", forgotten);
        return forgotten;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Forgetting saved cards at their provider failed; it runs again at the next pass.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
