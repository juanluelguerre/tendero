using System.Text.Json;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// The base every proposable command derives: it reads the payload, runs the
/// command's own validators, snapshots what the command would change, and runs
/// the command through the dispatcher — so the model never builds one.
/// </summary>
public sealed class ProposableCommandTests
{
    [Fact]
    public async Task A_valid_payload_is_checked_with_its_subject_and_a_snapshot_of_now()
    {
        var (entry, _) = Entry(price: 10);

        var check = await entry.CheckAsync(Payload("""{"sku":"MUG-1","price":12}"""), TestContext.Current.CancellationToken);

        Assert.True(check.IsValid);
        Assert.Equal("MUG-1", check.Subject);
        Assert.Equal("""{"price":10}""", check.Now);
    }

    [Fact]
    public async Task The_commands_own_validator_refuses_before_anything_is_stored()
    {
        var (entry, _) = Entry(price: 10);

        var check = await entry.CheckAsync(Payload("""{"sku":"MUG-1","price":-1}"""), TestContext.Current.CancellationToken);

        Assert.False(check.IsValid);
        Assert.Contains("Price must be positive.", check.Errors);
    }

    [Fact]
    public async Task A_payload_that_is_not_the_commands_shape_is_refused_not_thrown()
    {
        var (entry, _) = Entry(price: 10);

        var check = await entry.CheckAsync(Payload("""{"sku":42}"""), TestContext.Current.CancellationToken);

        Assert.False(check.IsValid);
        Assert.Contains("RepriceItem", Assert.Single(check.Errors));
    }

    [Fact]
    public async Task A_subject_that_does_not_exist_is_refused()
    {
        var (entry, _) = Entry(price: null);

        var check = await entry.CheckAsync(Payload("""{"sku":"GONE","price":12}"""), TestContext.Current.CancellationToken);

        Assert.False(check.IsValid);
    }

    [Fact]
    public async Task Now_is_null_when_the_subject_has_gone()
    {
        var (entry, _) = Entry(price: null);

        Assert.Null(await entry.NowAsync(Payload("""{"sku":"GONE","price":12}"""), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Applying_runs_the_command_and_an_allowed_result_is_applied()
    {
        var (entry, handler) = Entry(price: 10);

        var application = await entry.ApplyAsync(Payload("""{"sku":"MUG-1","price":12}"""), TestContext.Current.CancellationToken);

        Assert.True(application.Applied);
        Assert.Equal(12, handler.Price);
    }

    [Fact]
    public async Task A_refusal_the_command_returns_is_a_failure_with_its_reason()
    {
        var (entry, _) = Entry(price: 10, refuse: "Locked");

        var application = await entry.ApplyAsync(Payload("""{"sku":"MUG-1","price":12}"""), TestContext.Current.CancellationToken);

        Assert.False(application.Applied);
        Assert.Equal("Locked", application.Reason);
    }

    // ---------- fixtures ----------

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static (IProposableCommand, RepriceHandler) Entry(decimal? price, string? refuse = null)
    {
        var handler = new RepriceHandler(price, refuse);
        var services = new ServiceCollection();
        services.AddTenderoCqrs(typeof(ProposableCommandTests).Assembly);
        // After the scan, so this instance wins over the type it registered: the
        // test reads the handler's state.
        services.AddSingleton<ICommandHandler<RepriceItem, RepriceResult>>(handler);
        services.AddScoped<RepriceProposal>();
        var provider = services.BuildServiceProvider().CreateScope().ServiceProvider;
        return (provider.GetRequiredService<RepriceProposal>(), handler);
    }

    private sealed record RepricePayload(string Sku, decimal Price);

    private sealed record RepriceItem(string Sku, decimal Price) : ICommand<RepriceResult>;

    private sealed record RepriceResult(string? Refusal) : IAuditedResult
    {
        AuditOutcome IAuditedResult.AuditOutcome => Refusal is null ? AuditOutcome.Allowed : AuditOutcome.Denied;
        string? IAuditedResult.AuditReason => Refusal;
    }

    private sealed class RepriceValidator : AbstractValidator<RepriceItem>
    {
        public RepriceValidator() => RuleFor(x => x.Price).GreaterThan(0).WithMessage("Price must be positive.");
    }

    private sealed class RepriceHandler(decimal? price, string? refuse) : ICommandHandler<RepriceItem, RepriceResult>
    {
        public decimal? Price { get; private set; } = price;

        public Task<RepriceResult> HandleAsync(RepriceItem command, CancellationToken cancellationToken)
        {
            if (refuse is null)
                Price = command.Price;
            return Task.FromResult(new RepriceResult(refuse));
        }
    }

    private sealed class RepriceProposal(IServiceProvider services, ICommandHandler<RepriceItem, RepriceResult> handler)
        : ProposableCommand<RepricePayload, RepriceItem, RepriceResult>(services)
    {
        public override string Name => "RepriceItem";

        public override LocalizedText Label { get; } = new(new Dictionary<string, string>
        {
            ["en"] = "Reprice an item",
            ["es"] = "Cambiar el precio",
        });

        protected override RepriceItem ToCommand(RepricePayload payload) => new(payload.Sku, payload.Price);

        protected override Task<ProposalSubject?> ReadAsync(RepriceItem command, CancellationToken cancellationToken) =>
            Task.FromResult(((RepriceHandler)handler).Price is { } price
                ? new ProposalSubject(command.Sku, new { price })
                : null);
    }
}
