using System.Text.RegularExpressions;
using ElGuerre.Tendero.TrafficSim.Model;
using ElGuerre.Tendero.TrafficSim.Scheduling;
using Microsoft.Playwright;

namespace ElGuerre.Tendero.TrafficSim.Driving;

/// <summary>
/// The shopkeeper, in the backoffice, in a headless browser: signs in through
/// whichever issuer the API trusts, moves the orders that are waiting, and looks
/// at the review queue.
///
/// It is what keeps the rest of the simulation honest: without somebody shipping
/// and delivering, no reservation is ever committed and no order can ever come
/// back. It moves at most a few orders a visit, the likely move first — ship
/// what is confirmed, mark delivered what was shipped — and never cancels: a
/// cancellation is a decision about a person, not a routine.
/// </summary>
public sealed class BackofficeDriver(IBrowser browser, Uri backoffice, string userAgent)
{
    /// <summary>The seeded shopkeeper (src/DevIssuer, keycloak/realms): name and subject.</summary>
    private const string ShopkeeperName = "Juan Luis";
    private const string ShopkeeperSubject = "juanlu";

    /// <summary>Orders moved in one visit, at most.</summary>
    private const int MovesPerVisit = 3;

    private static readonly TimeSpan Glance = TimeSpan.FromSeconds(8);

    private static readonly Regex Moves = new("^(ship|enviar|delivered|entregado)$", RegexOptions.IgnoreCase);

    public async Task RunAsync(Journey journey, SyntheticLabel label, CancellationToken cancellationToken)
    {
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = backoffice.ToString(),
            ExtraHTTPHeaders = label.Headers,
            UserAgent = $"{userAgent} {SyntheticLabel.UserAgentSuffix}",
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });
        context.SetDefaultTimeout(20_000);
        var page = await context.NewPageAsync();

        foreach (var step in journey.Steps)
        {
            await Task.Delay(step.ThinkTime, cancellationToken);
            using var activity = Simulation.Telemetry.StartActivity($"step {step.Step}");

            try
            {
                var done = step.Step switch
                {
                    Step.SignIn => await SignInAsync(page),
                    Step.MoveOrders => await MoveOrdersAsync(page) > 0,
                    Step.ReviewQueue => await PublishOneAsync(page),
                    _ => false,
                };

                if (!done)
                    activity?.SetTag("synthetic.step.skipped", true);
            }
            catch (TimeoutException exception)
            {
                activity?.SetTag("synthetic.step.timeout", exception.Message);
            }
        }
    }

    /// <summary>The same two branches as the backoffice e2e: the development issuer's link, or Keycloak's form.</summary>
    private static async Task<bool> SignInAsync(IPage page)
    {
        await page.GotoAsync("/sign-in");
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("continue|continuar", RegexOptions.IgnoreCase) }).ClickAsync();

        if (page.Url.Contains("/realms/", StringComparison.Ordinal))
        {
            await page.Locator("input[name='username']").FillAsync(ShopkeeperSubject);
            await page.Locator("input[name='password']").FillAsync(ShopkeeperSubject);
            await page.Locator("input[type='submit'], button[type='submit']").First.ClickAsync();
        }
        else
        {
            await page.GetByRole(AriaRole.Link, new() { Name = ShopkeeperName }).ClickAsync();
        }

        // In when the bar offers a way out: a token exists only then.
        var signOut = page.GetByRole(AriaRole.Banner).GetByRole(AriaRole.Button, new() { NameRegex = new Regex("sign out|cerrar sesión", RegexOptions.IgnoreCase) });
        return await AppearsAsync(signOut);
    }

    private static async Task<int> MoveOrdersAsync(IPage page)
    {
        await page.GotoAsync("/orders");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var moved = 0;
        while (moved < MovesPerVisit)
        {
            var move = page.GetByRole(AriaRole.Button, new() { NameRegex = Moves }).First;
            if (!await AppearsAsync(move))
                break;

            await move.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            moved++;
        }

        return moved;
    }

    private static async Task<bool> PublishOneAsync(IPage page)
    {
        await page.GotoAsync("/review");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var publish = page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(publish|publicar)$", RegexOptions.IgnoreCase) }).First;
        if (!await AppearsAsync(publish))
            return false; // an empty queue: everything imported is already on sale

        await publish.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return true;
    }

    private static async Task<bool> AppearsAsync(ILocator locator)
    {
        try
        {
            await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = (float)Glance.TotalMilliseconds });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
