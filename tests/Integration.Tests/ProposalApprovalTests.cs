using System.Text.Json;
using ElGuerre.Tendero.Assist;
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Features.DecideProposals;
using ElGuerre.Tendero.Assist.Features.ProposeActions;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Features.SetProductTranslation;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// S-1 end to end on Postgres: a translation proposed, approved by a named
/// person, applied by Catalog's own handler, and the audit row that says which
/// proposal it was — the line "proposed by Steward, approved by Lucía" is read from.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProposalApprovalTests(PostgresFixture postgres)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task An_approved_translation_is_applied_as_the_approver_and_audited_under_its_proposal()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (provider, factory) = await ShopAsync("proposal_approval", ct);
        await using var _ = provider;
        var product = await FirstProductAsync(factory, ct);

        var proposal = await ProposeAsync(provider, product, "An English name the shop never had", ct);
        var decisions = await SendAsync(provider, new ApproveProposalsCommand([proposal]), ct);

        Assert.Equal(DecisionOutcome.Applied, Assert.Single(decisions.Rows).Outcome);
        await using var reread = factory.Create();
        var translated = await reread.Products.SingleAsync(p => p.Id == product, ct);
        Assert.Equal("An English name the shop never had", translated.Name.Values["en"]);
        var stored = await reread.Proposals.SingleAsync(p => p.Id == proposal, ct);
        Assert.Equal((ProposalStatus.Applied, "Lucía"), (stored.Status, stored.DecidedBy));
        var audited = await reread.AuditEntries.SingleAsync(entry => entry.CommandType == nameof(SetProductTranslationCommand), ct);
        Assert.Equal((proposal, "Lucía", AuditOutcome.Allowed), (audited.Proposal!.Value, audited.ActorName, audited.Outcome));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_translation_somebody_wrote_meanwhile_makes_the_proposal_stale()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (provider, factory) = await ShopAsync("proposal_stale", ct);
        await using var _ = provider;
        var product = await FirstProductAsync(factory, ct);

        var proposal = await ProposeAsync(provider, product, "From the model", ct);
        await SendAsync(provider, new SetProductTranslationCommand(product, "en", "From a person", null), ct);
        var decisions = await SendAsync(provider, new ApproveProposalsCommand([proposal]), ct);

        Assert.Equal(DecisionOutcome.Stale, Assert.Single(decisions.Rows).Outcome);
        await using var reread = factory.Create();
        Assert.Equal("From a person", (await reread.Products.SingleAsync(p => p.Id == product, ct)).Name.Values["en"]);
    }

    /// <summary>
    /// Two approvals of one proposal at once: each reads it Proposed, and only the
    /// first save may stand. The proposal's version (Postgres's xmin) refuses the
    /// second, and the unit of work says so as a ConcurrentWriteException — what
    /// the approval turns into NotWaiting (final review, I-2).
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_decision_made_on_an_old_copy_of_a_proposal_is_refused()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (provider, factory) = await ShopAsync("proposal_version", ct);
        await using var _ = provider;
        var proposal = await ProposeAsync(provider, await FirstProductAsync(factory, ct), "From the model", ct);

        await using var late = provider.CreateAsyncScope();
        var copy = (await late.ServiceProvider.GetRequiredService<IProposalRepository>().FindAsync(proposal, ct))!;
        await SendAsync(provider, new ApproveProposalsCommand([proposal]), ct);

        copy.Reject("Marta", "Too late", TimeProvider.System);
        await Assert.ThrowsAsync<ConcurrentWriteException>(() => late.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct));
    }

    // ---------- fixtures ----------

    private async Task<(ServiceProvider, TenderoDbContextFactory)> ShopAsync(string name, CancellationToken ct)
    {
        var factory = await postgres.CreateDatabaseAsync(name);
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var provider = Compose(factory.ConnectionString);
        await SendAsync(provider, new ImportProductsCommand("seed"), ct);
        return (provider, factory);
    }

    private static async Task<ProductId> FirstProductAsync(TenderoDbContextFactory factory, CancellationToken ct)
    {
        await using var context = factory.Create();
        return (await context.Products.OrderBy(p => p.Code).FirstAsync(ct)).Id;
    }

    private static async Task<ProposalId> ProposeAsync(ServiceProvider provider, ProductId product, string name, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToElement(new { productId = product.Value, culture = "en", name });
        var result = await SendAsync(provider, new ProposeActionsCommand(
            [new ProposedAction(SetProductTranslationProposal.CommandName, payload, "It has no English name.")],
            new ProposalSource("scripted", null, "steward-v1", "conv-1")), ct);
        return Assert.Single(result.Rows).Id!.Value;
    }

    private static async Task<T> SendAsync<T>(ServiceProvider provider, ICommand<T> command, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(command, ct);
    }

    private static ServiceProvider Compose(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Connectors:Seed:FilePath"] = Path.Combine(AppContext.BaseDirectory, "TestData", "products.sample.json"),
                ["Catalog:Attributes:FilePath"] = Path.Combine(AppContext.BaseDirectory, "TestData", "attributes.sample.json"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(ImportProductsCommand).Assembly, typeof(ApproveProposalsCommand).Assembly);
        services.AddTenderoPersistence(connectionString);
        services.AddCatalog(configuration);
        services.AddAssist();
        services.AddSingleton<IPrincipalAccessor>(new Lucia());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class Lucia : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = new(null, null, "lucia", "Lucía", ["shopkeeper"]);
    }
}
