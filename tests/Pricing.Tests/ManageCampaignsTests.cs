using ElGuerre.Tendero.Pricing.Adapters;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Features.ManageCampaigns;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;
using static ElGuerre.Tendero.Pricing.Tests.Build;
using static ElGuerre.Tendero.Pricing.Tests.Campaigns;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// The shopkeeper's changes to a campaign (H-1): each one a command whose
/// refusals are returned, audited as denials, and never thrown.
/// </summary>
public sealed class ManageCampaignsTests
{
    private static readonly GivenPromotions Known = new(
        Promotion("SUMMER25", new PercentOffLine(25)),
        Promotion("SHIP0", new FreeShipping()));

    private static readonly CampaignCode TenderoDays = new("TENDERO_DAYS");

    private static ManageCampaignsHandler Handler(InMemoryCampaigns campaigns, TestClock? clock = null) =>
        new(campaigns, Known, new GivenImages("stored-image"), campaigns, clock ?? Clock());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AuditOutcome Audited(CampaignResult result) => ((IAuditedResult)result).AuditOutcome;

    [Fact]
    public async Task Creating_saves_a_draft()
    {
        var campaigns = new InMemoryCampaigns();

        var result = await Handler(campaigns).HandleAsync(new CreateCampaignCommand(TenderoDays, Details()), Ct);

        Assert.Equal(CampaignChange.Created, result.Change);
        Assert.Equal((CampaignStatus.Draft, CampaignPhase.Upcoming), (result.Campaign!.Status, result.Campaign.Phase));
        Assert.Equal(1, campaigns.Saves);
        Assert.Equal(AuditOutcome.Allowed, Audited(result));
    }

    [Fact]
    public async Task A_code_already_taken_is_refused()
    {
        var campaigns = new InMemoryCampaigns(Draft());

        var result = await Handler(campaigns).HandleAsync(new CreateCampaignCommand(TenderoDays, Details()), Ct);

        Assert.Equal(CampaignChange.AlreadyExists, result.Change);
        Assert.Equal(AuditOutcome.Denied, Audited(result));
    }

    [Fact]
    public async Task A_promotion_pricing_does_not_know_is_refused_and_named()
    {
        var campaigns = new InMemoryCampaigns();

        var result = await Handler(campaigns).HandleAsync(
            new CreateCampaignCommand(TenderoDays, Details(promotions: ["SUMMER25", "NOPE"])), Ct);

        Assert.Equal(CampaignChange.UnknownPromotion, result.Change);
        Assert.Contains("NOPE", ((IAuditedResult)result).AuditReason);
        Assert.Equal(0, campaigns.Saves);
    }

    [Fact]
    public async Task An_image_the_store_does_not_hold_is_refused()
    {
        var image = new CampaignImage(new ImageId("never-uploaded"), Text("Toldo", "Awning"));

        var result = await Handler(new InMemoryCampaigns()).HandleAsync(
            new CreateCampaignCommand(TenderoDays, Details(image: image)), Ct);

        Assert.Equal(CampaignChange.UnknownImage, result.Change);
    }

    [Fact]
    public async Task A_window_already_closed_is_refused_where_it_is_set()
    {
        var result = await Handler(new InMemoryCampaigns()).HandleAsync(
            new CreateCampaignCommand(TenderoDays, Details(startsAt: Now.AddDays(-9), endsAt: Now.AddDays(-2))), Ct);

        Assert.Equal(CampaignChange.WindowClosed, result.Change);
        Assert.Equal(AuditOutcome.Denied, Audited(result));
    }

    [Fact]
    public async Task A_scheduled_campaign_is_not_edited_until_it_is_unscheduled()
    {
        var campaigns = new InMemoryCampaigns(Scheduled());
        var handler = Handler(campaigns);

        var refused = await handler.HandleAsync(new UpdateCampaignCommand(TenderoDays, Details(priority: 70)), Ct);
        await handler.HandleAsync(new UnscheduleCampaignCommand(TenderoDays), Ct);
        var edited = await handler.HandleAsync(new UpdateCampaignCommand(TenderoDays, Details(priority: 70)), Ct);

        Assert.Equal(CampaignChange.NotEditable, refused.Change);
        Assert.Equal((CampaignChange.Updated, 70), (edited.Change, edited.Campaign!.Priority));
    }

    [Fact]
    public async Task Scheduling_twice_is_not_a_second_change()
    {
        var campaigns = new InMemoryCampaigns(Draft());
        var handler = Handler(campaigns);

        var first = await handler.HandleAsync(new ScheduleCampaignCommand(TenderoDays), Ct);
        var second = await handler.HandleAsync(new ScheduleCampaignCommand(TenderoDays), Ct);

        Assert.Equal((CampaignChange.Scheduled, CampaignChange.Unchanged), (first.Change, second.Change));
        Assert.Equal(1, campaigns.Saves);
    }

    [Fact]
    public async Task A_window_that_closed_while_it_was_a_draft_cannot_be_scheduled()
    {
        var campaigns = new InMemoryCampaigns(Draft());

        var result = await Handler(campaigns, Clock(DayAfter.AddDays(8))).HandleAsync(new ScheduleCampaignCommand(TenderoDays), Ct);

        Assert.Equal(CampaignChange.WindowClosed, result.Change);
        Assert.Equal(CampaignStatus.Draft, result.Campaign!.Status);
    }

    [Fact]
    public async Task Ending_early_is_final()
    {
        var campaigns = new InMemoryCampaigns(Scheduled());
        var handler = Handler(campaigns, Clock(DayAfter.AddDays(1)));

        var ended = await handler.HandleAsync(new EndCampaignCommand(TenderoDays), Ct);
        var rescheduled = await handler.HandleAsync(new ScheduleCampaignCommand(TenderoDays), Ct);

        Assert.Equal(CampaignChange.Ended, ended.Change);
        Assert.Equal((CampaignStatus.Ended, CampaignPhase.Running), (ended.Campaign!.Status, ended.Campaign.Phase));
        Assert.Equal(CampaignChange.NotAllowed, rescheduled.Change);
        Assert.Equal(AuditOutcome.Denied, Audited(rescheduled));
    }

    [Fact]
    public async Task A_campaign_nobody_created_is_not_found()
    {
        var result = await Handler(new InMemoryCampaigns()).HandleAsync(new EndCampaignCommand(TenderoDays), Ct);

        Assert.Equal(CampaignChange.NotFound, result.Change);
    }

    [Fact]
    public async Task The_backoffice_reads_every_campaign_with_its_status_and_phase()
    {
        var ended = Scheduled("ENDED");
        ended.End(Clock());
        var handler = new ManagedCampaignsHandler(new InMemoryCampaigns(Draft("DRAFT"), Scheduled("LIVE"), ended), Clock(DayAfter));

        var all = await handler.HandleAsync(new ListManagedCampaignsQuery(), Ct);

        Assert.Equal(
            [("DRAFT", CampaignStatus.Draft), ("LIVE", CampaignStatus.Scheduled), ("ENDED", CampaignStatus.Ended)],
            all.Select(view => (view.Code, view.Status)));
        Assert.All(all, view => Assert.Equal(CampaignPhase.Running, view.Phase));
        Assert.Equal(new BilingualText("Tendero Days", "Tendero Days"), all[0].Name);
    }
}

/// <summary>The form checks, before any rule: both languages, a window that ends after it starts, something to group.</summary>
public sealed class CampaignDetailsValidatorTests
{
    private static readonly CampaignDetailsValidator Validator = new();

    [Fact]
    public void A_complete_campaign_is_valid()
    {
        Assert.True(Validator.Validate(Details()).IsValid);
    }

    [Fact]
    public void A_name_in_one_language_is_refused()
    {
        var details = Details() with { Name = LocalizedText.From("es", "Días Tendero") };

        Assert.Contains(Validator.Validate(details).Errors, error => error.PropertyName == nameof(CampaignDetails.Name));
    }

    [Fact]
    public void A_window_that_ends_before_it_starts_is_refused()
    {
        var details = Details(startsAt: DayAfter, endsAt: DayAfter.AddHours(-1));

        Assert.Contains(Validator.Validate(details).Errors, error => error.PropertyName == nameof(CampaignDetails.EndsAt));
    }

    [Fact]
    public void A_campaign_that_groups_nothing_is_refused()
    {
        Assert.False(Validator.Validate(Details(promotions: [])).IsValid);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void A_priority_is_between_zero_and_a_thousand(int priority, bool valid)
    {
        Assert.Equal(valid, Validator.Validate(Details(priority: priority)).IsValid);
    }

    [Fact]
    public void An_image_without_alt_text_in_both_languages_is_refused()
    {
        var image = new CampaignImage(new ImageId("abc"), LocalizedText.From("es", "Toldo"));

        Assert.False(Validator.Validate(Details(image: image)).IsValid);
    }

    /// <summary>Present in both languages is not enough: a blank one is as missing as an absent one.</summary>
    [Fact]
    public void A_name_blank_in_one_language_is_refused()
    {
        var details = Details() with { Name = Text("Días Tendero", "   ") };

        Assert.Contains(Validator.Validate(details).Errors, error => error.PropertyName == nameof(CampaignDetails.Name));
    }

    [Theory]
    [InlineData(CampaignDetailsValidator.MaxName, true)]
    [InlineData(CampaignDetailsValidator.MaxName + 1, false)]
    public void A_name_is_at_most_eighty_characters_in_each_language(int length, bool valid)
    {
        var details = Details() with { Name = Text(new string('a', length), "Tendero Days") };

        Assert.Equal(valid, Validator.Validate(details).IsValid);
    }
}

/// <summary>The file that seeds a fresh shop's calendar is the file that is checked.</summary>
public sealed class CampaignSeedFileTests
{
    private static readonly string SeedFolder = Path.Combine(AppContext.BaseDirectory, "seed");

    [Fact]
    public async Task Every_seed_campaign_groups_promotions_that_exist_and_passes_the_form_checks()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeds = await CampaignSeedFile.LoadAsync(Path.Combine(SeedFolder, "campaigns.sample.json"), ct);
        var promotions = (await PricingSeedFile.LoadPromotionsAsync(Path.Combine(SeedFolder, "promotions.sample.json"), ct))
            .Select(promotion => promotion.Code)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(["OTONO_EN_TENDERO", "TENDERO_DAYS", "BLACK_FRIDAY"], seeds.Select(seed => seed.Code.Value));
        Assert.All(seeds, seed =>
        {
            Assert.All(seed.Details.PromotionCodes, code => Assert.Contains(code, promotions));
            Assert.True(new CampaignDetailsValidator().Validate(seed.Details).IsValid, seed.Code.Value);
            Assert.Equal(CampaignStatus.Scheduled, seed.Status);
        });
    }

    /// <summary>
    /// The seed fills an empty calendar once (phase 13c's close, written down: the
    /// stack's start and the assistant suite's laboratory ran it, and no unit test).
    /// A campaign whose window has closed is left out and said to be; one the seed
    /// schedules is scheduled; and a calendar with anything in it is left alone.
    /// </summary>
    [Fact]
    public async Task The_seed_fills_an_empty_calendar_once_and_leaves_out_what_has_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(SeedFolder, "campaigns.sample.json");
        var calendar = new InMemoryCampaigns();
        // 10 October 2026: Tendero Days ended on the 7th; autumn runs; Black Friday is to come.
        var clock = new TestClock(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        List<string> skipped = [];

        var seeded = await CampaignSeedFile.SeedAsync(path, calendar, calendar, clock, seed => skipped.Add(seed.Code.Value), ct);

        Assert.Equal(2, seeded);
        Assert.Equal(["TENDERO_DAYS"], skipped);
        var all = await calendar.AllAsync(ct);
        Assert.Equal(["OTONO_EN_TENDERO", "BLACK_FRIDAY"], all.Select(campaign => campaign.Code.Value));
        Assert.All(all, campaign => Assert.Equal(CampaignStatus.Scheduled, campaign.Status));
        Assert.Equal(1, calendar.Saves);

        Assert.Equal(0, await CampaignSeedFile.SeedAsync(path, calendar, calendar, clock, cancellationToken: ct));
        Assert.Equal(2, (await calendar.AllAsync(ct)).Count);
    }

    /// <summary>On the day this was written the season runs and Tendero Days is two days away — and leads the moment it opens.</summary>
    /// <summary>A seed file that says <c>null</c> seeds nothing rather than failing the start.</summary>
    [Fact]
    public async Task A_seed_file_that_says_null_seeds_nothing()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "null", TestContext.Current.CancellationToken);

            Assert.Empty(await CampaignSeedFile.LoadAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_fresh_shop_has_a_season_running_and_an_event_coming()
    {
        var seeds = await CampaignSeedFile.LoadAsync(Path.Combine(SeedFolder, "campaigns.sample.json"), TestContext.Current.CancellationToken);
        var created = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(created);
        Campaign[] campaigns = [.. seeds.Select(seed => Campaign.Create(clock, seed.Code, seed.Details))];
        foreach (var campaign in campaigns)
            campaign.Schedule(clock);

        Assert.Equal(["OTONO_EN_TENDERO", "TENDERO_DAYS"], CampaignLineup.Near(campaigns, created).Select(campaign => campaign.Code.Value));
        Assert.Equal("TENDERO_DAYS", CampaignLineup.Near(campaigns, new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero))[0].Code.Value);
    }
}

public sealed class ImageSignatureTests
{
    [Theory]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0, 0, 0, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66 }, "image/avif")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, null)]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, null)]
    // A RIFF container that is not WebP (a WAVE file) is not an image.
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 }, null)]
    // The shortest JPEG signature, and nothing after it.
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg")]
    public void An_image_is_recognised_by_its_bytes_and_a_gif_or_svg_is_not(byte[] bytes, string? contentType)
    {
        Assert.Equal(contentType, ImageSignature.ContentTypeOf(bytes));
    }
}
