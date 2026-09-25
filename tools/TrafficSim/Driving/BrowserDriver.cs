using System.Text.RegularExpressions;
using ElGuerre.Tendero.TrafficSim.Model;
using ElGuerre.Tendero.TrafficSim.Scheduling;
using Microsoft.Playwright;

namespace ElGuerre.Tendero.TrafficSim.Driving;

/// <summary>
/// A person, in a headless browser, through the real storefront — so the Web
/// Vitals, the JavaScript errors and the trace from a click to the API are the
/// storefront's own (ADR 0037, decision 1).
///
/// A journey says which kind of page comes next; this finds a way there the way
/// a person would — a link on the page, the search box, the cart icon — and falls
/// back to the address bar only when the page offers none. Which link, among
/// several, comes from the session's own random stream, so a run replays the same
/// clicks over the same catalogue. A step that cannot be done (a product sold
/// out, a cart that is empty) is skipped and tagged, never retried: the journey
/// was drawn up front and the shop's answer does not change it.
/// </summary>
public sealed class BrowserDriver(IBrowser browser, Uri storefront, string userAgent, PlacedOrders orders)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>How long a person looks for something on a page before deciding it is not there.</summary>
    private static readonly TimeSpan Glance = TimeSpan.FromSeconds(8);

    public async Task RunAsync(Journey journey, SyntheticLabel label, CancellationToken cancellationToken)
    {
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = storefront.ToString(),
            Locale = journey.Culture == "en" ? "en-GB" : "es-ES",
            ExtraHTTPHeaders = label.Headers,
            UserAgent = $"{userAgent} {SyntheticLabel.UserAgentSuffix}",
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
        });
        context.SetDefaultTimeout((float)Patience.TotalMilliseconds);

        var page = await context.NewPageAsync();
        var random = new Random(journey.Seed ^ 0x5BD1E995);
        var queries = new Queue<string>(journey.Queries);
        var declines = new Queue<bool>(journey.CardDeclines);
        var culture = journey.Culture;

        foreach (var step in journey.Steps)
        {
            await Task.Delay(step.ThinkTime, cancellationToken);
            using var activity = Simulation.Telemetry.StartActivity($"step {step.Step}");

            try
            {
                var done = step.Step switch
                {
                    Step.Home => await HomeAsync(page, culture, journey.GrantsConsent),
                    Step.Search => await SearchAsync(page, culture, queries.Count > 0 ? queries.Dequeue() : ""),
                    Step.Browse => await FollowAsync(page, random, "a[href*='/c/']", $"/{culture}"),
                    Step.Product => await FollowAsync(page, random, "a[href*='/p/']", $"/{culture}"),
                    Step.AddToCart => await AddToCartAsync(page),
                    Step.Cart => await GoAsync(page, $"/{culture}/cart"),
                    Step.Checkout => await CheckoutAsync(page, culture),
                    Step.Pay => await PayAsync(page, declines, orders),
                    Step.OpenOrder => await OpenOrderAsync(page, orders.Pick(random)),
                    Step.RequestReturn => await RequestReturnAsync(page, journey.ReturnReason),
                    _ => false,
                };

                if (!done)
                    activity?.SetTag("synthetic.step.skipped", true);
            }
            catch (TimeoutException exception)
            {
                // A page that did not answer in time is what a person would
                // have given up on too; the session goes on to its next step.
                activity?.SetTag("synthetic.step.timeout", exception.Message);
            }
        }
    }

    private static async Task<bool> HomeAsync(IPage page, string culture, bool grantsConsent)
    {
        await page.GotoAsync($"/{culture}");

        // The analytics question, answered once per session (ADR 0036).
        var answer = page.GetByRole(AriaRole.Button, new()
        {
            Name = (culture, grantsConsent) switch
            {
                ("en", true) => "Allow",
                ("en", false) => "Don't allow",
                (_, true) => "Permitir",
                _ => "No permitir",
            },
            Exact = true,
        });

        if (await AppearsAsync(answer, TimeSpan.FromSeconds(3)))
            await answer.ClickAsync();

        return true;
    }

    private static async Task<bool> SearchAsync(IPage page, string culture, string query)
    {
        var box = page.GetByRole(AriaRole.Searchbox);
        if (!await AppearsAsync(box, Glance))
            await page.GotoAsync($"/{culture}");

        await box.FillAsync(query);
        await box.PressAsync("Enter");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return true;
    }

    /// <summary>One of the links of a kind on this page, or the fallback page's.</summary>
    private static async Task<bool> FollowAsync(IPage page, Random random, string selector, string fallback)
    {
        var links = page.Locator(selector);
        if (await links.CountAsync() == 0)
        {
            await page.GotoAsync(fallback);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        var count = await links.CountAsync();
        if (count == 0)
            return false;

        await links.Nth(random.Next(count)).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return true;
    }

    private static async Task<bool> AddToCartAsync(IPage page)
    {
        // The product page's own button; a sold-out product has none, or a disabled one.
        var add = page.Locator("main button", new() { HasTextRegex = new Regex("añadir al carrito|add to cart", RegexOptions.IgnoreCase) }).First;
        if (!await AppearsAsync(add, Glance) || !await add.IsEnabledAsync())
            return false;

        await add.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return true;
    }

    private static async Task<bool> GoAsync(IPage page, string path)
    {
        await page.GotoAsync(path);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return true;
    }

    /// <summary>
    /// The address a person types, and the first delivery option. The details
    /// are fixed and fictional: what a checkout form costs to fill is not what
    /// the simulation measures.
    /// </summary>
    private static async Task<bool> CheckoutAsync(IPage page, string culture)
    {
        await GoAsync(page, $"/{culture}/checkout");

        var name = page.Locator("input[name='recipientName']");
        if (!await AppearsAsync(name, Glance))
            return false; // an empty cart has no checkout

        await name.FillAsync("Ana Simulada");
        await page.Locator("input[name='line1']").FillAsync("Calle Mayor 1");
        await page.Locator("input[name='postalCode']").FillAsync("28013");
        await page.Locator("input[name='city']").FillAsync("Madrid");
        await name.PressAsync("Tab");

        var shipping = page.Locator("input[name='shipping']").First;
        await shipping.WaitForAsync();
        await shipping.CheckAsync();
        return true;
    }

    /// <summary>The card the journey drew, then Pay; after a decline, the next one if there is one.</summary>
    private static async Task<bool> PayAsync(IPage page, Queue<bool> declines, PlacedOrders placed)
    {
        var paid = false;

        while (declines.TryDequeue(out var declined))
        {
            var card = page.Locator($"input[name='instrument'][value='{(declined ? "card-declined" : "card-ok")}']");
            if (!await AppearsAsync(card, Glance))
                return paid;

            await card.CheckAsync();
            await page.Locator("button.button--block").ClickAsync();

            // Placed: the order page. Declined: the checkout stays, and says so.
            try
            {
                await page.WaitForURLAsync("**/orders/**", new() { Timeout = (float)Glance.TotalMilliseconds });
                placed.Add(new Uri(page.Url).PathAndQuery);
                paid = true;
                break;
            }
            catch (TimeoutException)
            {
                // Still on the checkout: the card was declined, or the price moved.
            }
        }

        return paid;
    }

    /// <summary>A customer comes back to an order this run placed, by its link.</summary>
    private static async Task<bool> OpenOrderAsync(IPage page, string? path)
    {
        if (path is null)
            return false; // nothing placed yet: nothing to come back to

        await GoAsync(page, path);
        return true;
    }

    /// <summary>
    /// Sends back the first thing left to return, for the reason the journey
    /// drew. Only a delivered order offers the form: until the shopkeeper has
    /// delivered it, the step is skipped.
    /// </summary>
    private static async Task<bool> RequestReturnAsync(IPage page, string? reason)
    {
        var pick = page.Locator(".returnable__pick input[type='checkbox']").First;
        if (reason is null || !await AppearsAsync(pick, Glance))
            return false;

        await pick.CheckAsync();
        await page.Locator(".returnable__detail select").First.SelectOptionAsync(reason);
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("request the return|solicitar la devolución", RegexOptions.IgnoreCase) }).ClickAsync();
        return await AppearsAsync(page.Locator("p.notice--good[role='status']"), Glance);
    }

    /// <summary>Whether something shows up within a while: a page renders after its data arrives.</summary>
    private static async Task<bool> AppearsAsync(ILocator locator, TimeSpan within)
    {
        try
        {
            await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = (float)within.TotalMilliseconds });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
