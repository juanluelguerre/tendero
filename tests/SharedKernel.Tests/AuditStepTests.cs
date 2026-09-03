using ElGuerre.Tendero.Tests;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// The dispatcher's second pipeline step, and the first one that writes.
///
/// What is asserted here is mostly about the rows that are NOT successes,
/// because those are the ones three later features are built on: the audit
/// screen, phase 11's agent activity panel and phase 12's copilot. An audit log
/// full of "somebody published a product" and empty of "a rule said no" would be
/// a changelog, not an audit.
/// </summary>
public sealed class AuditStepTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task A_command_that_succeeds_leaves_one_allowed_row()
    {
        var (dispatcher, audit) = Pipeline();

        await dispatcher.SendAsync(new Publish("a-product"), TestContext.Current.CancellationToken);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(nameof(Publish), entry.CommandType);
        Assert.Equal(AuditOutcome.Allowed, entry.Outcome);
        Assert.Contains("a-product", entry.Payload);
    }

    /// <summary>
    /// The row this whole thing exists for.
    ///
    /// A domain invariant refusing is the shop WORKING — "cannot publish an
    /// archived product" is a rule, not a fault — and it has to be
    /// distinguishable from the database being down. Phase 11's panel opens on
    /// exactly these rows.
    /// </summary>
    [Fact]
    public async Task A_domain_refusal_is_recorded_as_DENIED_and_still_thrown()
    {
        var (dispatcher, audit) = Pipeline();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.SendAsync(
                new Refuse("Cannot publish an archived product."),
                TestContext.Current.CancellationToken));

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal("Cannot publish an archived product.", entry.Reason);
    }

    /// <summary>And a real fault is NOT a denial. The two look identical in a
    /// stack trace and mean opposite things to whoever reads the table.</summary>
    [Fact]
    public async Task A_fault_is_recorded_as_FAILED()
    {
        var (dispatcher, audit) = Pipeline();

        await Assert.ThrowsAsync<TimeoutException>(
            () => dispatcher.SendAsync(new Break(), TestContext.Current.CancellationToken));

        Assert.Equal(AuditOutcome.Failed, Assert.Single(audit.Entries).Outcome);
    }

    /// <summary>
    /// A cart token is a 256-bit bearer credential, and an audit table is
    /// exactly the kind of thing that gets exported to a spreadsheet. The value
    /// is REPLACED rather than dropped, so the row still shows the field was
    /// there.
    /// </summary>
    [Fact]
    public async Task A_credential_never_reaches_the_log()
    {
        var (dispatcher, audit) = Pipeline();

        await dispatcher.SendAsync(
            new Claim("secret-cart-token", "ana"), TestContext.Current.CancellationToken);

        var entry = Assert.Single(audit.Entries);
        Assert.DoesNotContain("secret-cart-token", entry.Payload);
        Assert.Contains("[redacted]", entry.Payload);
        // The rest of the command still travels: a redaction that took the whole
        // payload with it would be a log that records nothing.
        Assert.Contains("ana", entry.Payload);
    }

    [Fact]
    public async Task The_row_names_who_acted()
    {
        var principal = new CommercePrincipal(
            new CustomerId(Guid.CreateVersion7()), new AgentId("claude-desktop"), "ana", ["shopper"]);

        var (dispatcher, audit) = Pipeline(principal);

        await dispatcher.SendAsync(new Publish("a-product"), TestContext.Current.CancellationToken);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(principal.Customer, entry.Customer);
        Assert.Equal(principal.Agent, entry.Agent);
        Assert.Equal("ana", entry.Subject);
    }

    /// <summary>
    /// A query is not an action. Auditing reads would bury the hundred rows that
    /// matter under a hundred thousand that do not.
    /// </summary>
    [Fact]
    public async Task A_query_leaves_no_row()
    {
        var (_, audit, queries) = FullPipeline();

        await queries.SendAsync(new Look(), TestContext.Current.CancellationToken);

        Assert.Empty(audit.Entries);
    }

    /// <summary>
    /// A command that fails VALIDATION never reached a rule — it reached a form
    /// check. Recording it would fill the table with mistyped requests and bury
    /// the denials.
    /// </summary>
    [Fact]
    public async Task A_command_rejected_by_validation_leaves_no_row()
    {
        var (dispatcher, audit) = Pipeline();

        await Assert.ThrowsAsync<ValidationException>(
            () => dispatcher.SendAsync(new Publish(""), TestContext.Current.CancellationToken));

        Assert.Empty(audit.Entries);
    }

    /// <summary>
    /// The one deliberate exception swallow in the repository. The command
    /// already happened; failing the request now would tell a shopper their
    /// order did not go through when it did.
    /// </summary>
    [Fact]
    public async Task A_broken_audit_writer_does_not_break_the_command()
    {
        var services = Compose(CommercePrincipal.Anonymous, new ThrowingAuditWriter());
        var dispatcher = services.GetRequiredService<ICommandDispatcher>();

        var result = await dispatcher.SendAsync(new Publish("a-product"), TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    /// <summary>
    /// A process with no audit writer runs commands and says nothing, rather
    /// than refusing to start. `tools/SearchEval` composes its own container and
    /// has no database — and a pipeline step that demanded one would be the
    /// fifth composition failure in this repository's history.
    /// </summary>
    [Fact]
    public async Task A_process_with_no_writer_still_runs_commands()
    {
        var services = new ServiceCollection();
        services.AddTenderoCqrs(typeof(AuditStepTests).Assembly);
        services.AddSingleton<TimeProvider>(Clock);

        var dispatcher = services.BuildServiceProvider().GetRequiredService<ICommandDispatcher>();

        Assert.True(await dispatcher.SendAsync(new Publish("a-product"), TestContext.Current.CancellationToken));
    }

    // ---------- fixtures ----------

    private static (ICommandDispatcher, RecordingAuditWriter) Pipeline(
        CommercePrincipal? principal = null)
    {
        var audit = new RecordingAuditWriter();
        var services = Compose(principal ?? CommercePrincipal.Anonymous, audit);

        return (services.GetRequiredService<ICommandDispatcher>(), audit);
    }

    private static (ICommandDispatcher, RecordingAuditWriter, IQueryDispatcher) FullPipeline()
    {
        var audit = new RecordingAuditWriter();
        var services = Compose(CommercePrincipal.Anonymous, audit);

        return (services.GetRequiredService<ICommandDispatcher>(),
                audit,
                services.GetRequiredService<IQueryDispatcher>());
    }

    private static ServiceProvider Compose(CommercePrincipal principal, IAuditWriter audit)
    {
        var services = new ServiceCollection();

        services.AddTenderoCqrs(typeof(AuditStepTests).Assembly);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(audit);
        services.AddSingleton<IPrincipalAccessor>(new FixedPrincipal(principal));

        return services.BuildServiceProvider();
    }

    private sealed record Publish(string Slug) : ICommand<bool>;
    private sealed record Refuse(string Because) : ICommand<bool>;
    private sealed record Break : ICommand<bool>;
    private sealed record Claim([property: AuditRedacted] string CartToken, string Subject)
        : ICommand<bool>;
    private sealed record Look : IQuery<bool>;

    private sealed class PublishValidator : AbstractValidator<Publish>
    {
        public PublishValidator() => RuleFor(command => command.Slug).NotEmpty();
    }

    private sealed class PublishHandler : ICommandHandler<Publish, bool>
    {
        public Task<bool> HandleAsync(Publish command, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class RefuseHandler : ICommandHandler<Refuse, bool>
    {
        public Task<bool> HandleAsync(Refuse command, CancellationToken ct) =>
            throw new InvalidOperationException(command.Because);
    }

    private sealed class BreakHandler : ICommandHandler<Break, bool>
    {
        public Task<bool> HandleAsync(Break command, CancellationToken ct) =>
            throw new TimeoutException("the database went away");
    }

    private sealed class ClaimHandler : ICommandHandler<Claim, bool>
    {
        public Task<bool> HandleAsync(Claim command, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class LookHandler : IQueryHandler<Look, bool>
    {
        public Task<bool> HandleAsync(Look query, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FixedPrincipal(CommercePrincipal principal) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = principal;
    }

    /// <summary>A list, not a mock: what matters about an audit writer is what
    /// reached it, and a list says that more clearly than a verification.</summary>
    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the audit table is gone");
    }
}
