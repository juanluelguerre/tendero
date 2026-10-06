# Steward S-1 · Proposals without a model — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A typed, audited path from "a proposal exists" to "a person approved it and the command ran as them" — `SetProductTranslation` in Catalog, the `Assist` context with `ActionProposal`, the registry of proposable commands, approve and reject, and the audit row that names the proposal — with no model anywhere.

**Architecture:** The SharedKernel gains `ProposalId`, the port `IProposableCommand` with a generic base `ProposableCommand<TPayload, TCommand, TResult>` that each owning context derives (Catalog: translation and withdrawal), and a scoped `ProposalScope` the audit step reads, so a command dispatched for a proposal writes its id into its audit row. `Assist` is the seventh context: it stores `ActionProposal` (a transition table), finds an entry by its stable name, checks staleness against the snapshot taken when proposed, and dispatches each approved row in its own DI scope as the approving person — one row's failure stops none of the others. Persistence maps the table and the audit column; the API exposes approve, reject and the translation.

**Tech Stack:** .NET 11 preview, C# 14, EF Core + Npgsql (jsonb), FluentValidation, Carter, xUnit v3, NetArchTest, Testcontainers (integration).

**Spec:** `docs/specs/steward.md` — S-1 in *Building it, in order*; *What Steward proposes*, *Approving*, *Security*, *Evaluation and tests*.

## Global Constraints

- `Assist.Domain` references SharedKernel only (invariant 4); `Assist` references SharedKernel and nothing else (as Accounts); an architecture rule computes it.
- A proposal carries values, never a reference to another context's entity: `Command`, `Payload` (JSON), `Subject` (a product's code) (invariant 1).
- `ActionProposal`'s table is the single source of its status changes: `Proposed → Approved → Applied | Failed`, `Proposed → Rejected`, `Proposed → Stale` (invariant 5).
- "An approval runs the command in the same request, as the person approving. Not through the outbox" (spec decision 9).
- "Approving or rejecting — a proposal or a claim — is never proposable."
- "The model never writes a command": proposals are built by code from a registry entry, which validates before anything is stored; "an invalid one … is not stored".
- `SetProductTranslation(ProductId, Culture, Name, Description?)`: a supported culture, a non-blank name within the catalogue's length; idempotent ("the same text twice is one change"); leaves the other cultures untouched.
- Endpoints are shopkeeper-only, nothing anonymous; the approver is taken from the principal, never from the body.
- User-facing strings in es AND en (invariant 6): the entries' labels and the backoffice's audit command names.
- No new NuGet or npm dependency. `dotnet build -warnaserror` clean. No attribution trailers in commits.
- The wire carries flat string ids, never a strongly-typed id record; every enum on the wire is declared as one (ADR 0043).
- After any change to a response: `UPDATE_OPENAPI=1 dotnet test tests/Api.Tests`, then `npm run generate:api-types` from `frontend/`.

## Rulings already taken (the spec is silent or leaves the choice)

1. **Retrying an `Approved` proposal skips the staleness check.** After a crash between Approved and the result, the change may already be applied, so *now* equals the proposed value, not `Before`, and a staleness check would call an applied change stale. The command is idempotent, and staleness was checked at approval. Cost if wrong: an edit made by somebody else between the crash and the retry is overwritten.
2. **`Before` is a JSON snapshot of exactly the fields the command would change** (`{"name":…,"description":…}` for a translation, `{"status":…}` for a withdrawal), serialised by the base class with fixed options, and compared as text. Cost if wrong: a field outside the snapshot changes and the proposal is not marked stale.
3. **The Proposals list query and its endpoint go to S-4**, with the screen that reads them. S-1 proves the flow through the dispatcher and the two decision endpoints. Cost if wrong: S-1 cannot be walked from the API alone.
4. **The catalogue's text lengths become constants**: `Product.NameMaxLength = 200`, `Product.DescriptionMaxLength = 4000`. The seed's longest name is 56 characters and its longest description is 154. Cost if wrong: a supplier name longer than 200 characters can be imported but not translated.
5. **A batch is at most 100 proposals** (the spec's "a hundred English names"), for propose, approve and reject alike.
6. **Each approved row runs in its own DI scope.** A failing command can leave its scope's `DbContext` dirty; a fresh scope per row is what makes "one failure stops none of the others" true, not only for returned refusals.

## Review Focus

1. **The same id twice in one approval** → the second occurrence does nothing; the command runs once and one audit row is written (`Distinct` before the loop). Pinned in Task 6.
2. **A command that throws instead of returning a refusal** → that row is `Failed` with the exception's message; the next row is still applied, and the proposal is not left `Approved`. Pinned in Task 6.
3. **A product deleted or archived after the proposal** → the staleness check finds no product: `now` is null, so the proposal becomes `Stale` and nothing runs. Pinned in Task 6 (a missing subject) and Task 4 (null `now`).
4. **The payload of an unknown command name** (an entry removed between proposing and approving) → that row is `Failed` with "No proposable command is named …", not a 500. Pinned in Task 6.
5. **A rejection with a blank reason** → 400 from the validator; the reason is what a prompt change is judged against. Pinned in Task 6.

---

### Task 1: The proposal's id reaches the audit row

**Files:**
- Create: `src/SharedKernel/Proposals.cs`
- Modify: `src/SharedKernel/Audit.cs` (the `AuditEntry` record and `AuditEntry.For`)
- Modify: `src/SharedKernel/Cqrs/AuditStep.cs` (both `AuditEntry.For` calls)
- Modify: `src/SharedKernel/Cqrs/CqrsServiceCollectionExtensions.cs` (register `ProposalScope`)
- Modify: `src/Persistence/StronglyTypedIds.cs` (`ProposalId`), `src/Persistence/Configurations/AuditEntryConfiguration.cs` (the column's index); the migration `AuditProposal` under `src/Persistence/Migrations/` — in this task, because the audit's EF model must map the new property the moment it exists
- Test: `tests/SharedKernel.Tests/AuditStepTests.cs`

**Interfaces:**
- Produces: `public readonly record struct ProposalId(Guid Value)` with `static ProposalId New(DateTimeOffset at)` and `ToString()`; `public sealed class ProposalScope { ProposalId? Current; void Enter(ProposalId id); }` registered scoped; `AuditEntry.Proposal` (`ProposalId?`, last positional parameter, default `null`); `AuditEntry.For(..., DateTimeOffset at, ProposalId? proposal = null)`.

- [ ] **Step 1: Write the failing tests** — append to `AuditStepTests` (before `// ---------- fixtures ----------`):

```csharp
    /// <summary>
    /// A command run for an approved proposal says so in its row: that column is
    /// what lets the audit read "proposed by Steward, approved by Lucía".
    /// </summary>
    [Fact]
    public async Task A_command_dispatched_for_a_proposal_names_it_in_its_row()
    {
        var audit = new RecordingAuditWriter();
        await using var root = Compose(CommercePrincipal.Anonymous, audit);
        await using var scope = root.CreateAsyncScope();
        var proposal = ProposalId.New(Clock.GetUtcNow());

        scope.ServiceProvider.GetRequiredService<ProposalScope>().Enter(proposal);
        await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new Publish("a-product"), TestContext.Current.CancellationToken);

        Assert.Equal(proposal, Assert.Single(audit.Entries).Proposal);
    }

    [Fact]
    public async Task A_command_nobody_proposed_has_no_proposal_in_its_row()
    {
        var (dispatcher, audit) = Pipeline();

        await dispatcher.SendAsync(new Publish("a-product"), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(audit.Entries).Proposal);
    }

    /// <summary>One scope is one proposal: entering a second would file one
    /// command's row under somebody else's proposal.</summary>
    [Fact]
    public void A_scope_carries_one_proposal()
    {
        var scope = new ProposalScope();
        scope.Enter(ProposalId.New(Clock.GetUtcNow()));

        Assert.Throws<InvalidOperationException>(() => scope.Enter(ProposalId.New(Clock.GetUtcNow())));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/SharedKernel.Tests --filter "FullyQualifiedName~AuditStepTests"`
Expected: build FAILS — `ProposalId`, `ProposalScope` and `AuditEntry.Proposal` do not exist.

- [ ] **Step 3: Create `src/SharedKernel/Proposals.cs`**

```csharp
namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A proposal's identity. In the kernel and not in Assist because the audit log
/// carries it: a command run for an approved proposal writes this id into its
/// row, which is what lets the log say who proposed a change and who approved it.
/// </summary>
public readonly record struct ProposalId(Guid Value)
{
    public static ProposalId New(DateTimeOffset at) => new(Guid.CreateVersion7(at));

    public override string ToString() => Value.ToString();
}

/// <summary>
/// The proposal the commands of this DI scope run for, read by the audit step.
///
/// Approving runs each proposal in a scope of its own, so a scope carries one
/// proposal and a second one is refused: a row filed under the wrong proposal
/// would name the wrong approver for a change.
/// </summary>
public sealed class ProposalScope
{
    public ProposalId? Current { get; private set; }

    public void Enter(ProposalId id)
    {
        if (Current is { } current && current != id)
            throw new InvalidOperationException($"This scope already runs proposal {current}.");

        Current = id;
    }
}
```

- [ ] **Step 4: Add the column to `AuditEntry`** — in `src/SharedKernel/Audit.cs`, add a last positional parameter after `DateTimeOffset At`:

```csharp
    DateTimeOffset At,

    /// <summary>The approved proposal this command ran for, or null when a person
    /// or an agent sent it directly (phase 12, ADR 0052).</summary>
    ProposalId? Proposal = null)
```

and extend `For`:

```csharp
    public static AuditEntry For(
        string commandType,
        string payload,
        CommercePrincipal principal,
        AuditOutcome outcome,
        string? reason,
        string? traceId,
        DateTimeOffset at,
        ProposalId? proposal = null) =>
        new(Guid.CreateVersion7(at),
            commandType,
            payload,
            principal.Customer,
            principal.Agent,
            principal.Subject,
            principal.DisplayName,
            outcome,
            reason,
            traceId,
            at,
            proposal);
```

- [ ] **Step 5: Read the scope in `AuditStep.RecordAsync`** — after `var traceId = …;` add:

```csharp
        // Set only when Assist runs an approved proposal (ADR 0052).
        var proposal = services.GetService<ProposalScope>()?.Current;
```

and pass `proposal` as the last argument of both `AuditEntry.For(…)` calls (after `clock.GetUtcNow()`).

- [ ] **Step 6: Register the scope** — in `AddTenderoCqrs`, beside the dispatchers:

```csharp
        services.TryAddScoped<ProposalScope>();
```

- [ ] **Step 7: Map the column** — `StronglyTypedIds`: `conventions.Properties<ProposalId>().HaveConversion<ProposalIdConverter>();` and `private sealed class ProposalIdConverter() : ValueConverter<ProposalId, Guid>(id => id.Value, value => new ProposalId(value));`. `AuditEntryConfiguration`, after the `TraceId` line:

```csharp
        // The proposal a command ran for (phase 12): what lets a row say "proposed
        // by Steward, approved by Lucía". Filtered, as the agent's: most rows have none.
        builder.HasIndex(entry => entry.Proposal).HasFilter("proposal IS NOT NULL");
```

Run: `dotnet tool run dotnet-ef migrations add AuditProposal --project src/Persistence`
Expected: a migration that adds `audit."AuditEntries".proposal uuid NULL` and its filtered index, and nothing else. If it touches another table, stop: the snapshot was stale before this task.

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/SharedKernel.Tests` then `dotnet test tests/Persistence.Tests` then `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~AuditPagingTests|FullyQualifiedName~MigrationBaselineTests"` (Docker up)
Expected: PASS, every test.

- [ ] **Step 9: Commit**

```bash
git add src/SharedKernel src/Persistence tests/SharedKernel.Tests/AuditStepTests.cs
git commit -m "feat(audit): a command run for an approved proposal names it in its row (S-1)"
```

---

### Task 2: The port a context implements to make a command proposable

**Files:**
- Modify: `src/SharedKernel/Proposals.cs`
- Create: `tests/SharedKernel.Tests/ProposableCommandTests.cs`

**Interfaces:**
- Consumes: `ProposalId`, `IAuditedResult`, `ICommandDispatcher`.
- Produces:
  - `public interface IProposableCommand { string Name { get; } LocalizedText Label { get; } Task<ProposalCheck> CheckAsync(JsonElement payload, CancellationToken ct); Task<string?> NowAsync(JsonElement payload, CancellationToken ct); Task<ProposalApplication> ApplyAsync(JsonElement payload, CancellationToken ct); }`
  - `public sealed record ProposalCheck(string? Subject, string? Now, IReadOnlyList<string> Errors)` with `bool IsValid`, `static Valid(string subject, string now)`, `static Invalid(params string[] errors)`
  - `public sealed record ProposalApplication(bool Applied, string? Reason)` with `static readonly Done`, `static Failed(string reason)`
  - `public sealed record ProposalSubject(string Subject, object Now)`
  - `public abstract class ProposableCommand<TPayload, TCommand, TResult>(IServiceProvider services) : IProposableCommand` with `protected abstract TCommand ToCommand(TPayload payload)` and `protected abstract Task<ProposalSubject?> ReadAsync(TCommand command, CancellationToken ct)`

- [ ] **Step 1: Write the failing tests** — `tests/SharedKernel.Tests/ProposableCommandTests.cs`:

```csharp
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
```

Note: `AddTenderoCqrs(assembly)` registers the assembly's validators and handlers; the explicit `AddSingleton` for the handler wins because the test needs to read its state. If `AddTenderoCqrs` registers `RepriceHandler` itself (it scans private nested types), drop the explicit registration and resolve `ICommandHandler<RepriceItem, RepriceResult>` from the provider instead — record which one held as a ruling.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/SharedKernel.Tests --filter "FullyQualifiedName~ProposableCommandTests"`
Expected: build FAILS — `IProposableCommand`, `ProposableCommand<,,>`, `ProposalSubject` do not exist.

- [ ] **Step 3: Implement** — append to `src/SharedKernel/Proposals.cs` (add `using System.Text.Json;`, `using FluentValidation;`, `using Microsoft.Extensions.DependencyInjection;` at the top):

```csharp
/// <summary>
/// A command a model may propose and a person may approve (phase 12): its stable
/// name, its label, how to check a payload, how to read what it would change,
/// and how to run it.
///
/// A port in the kernel because the command belongs to its context — Catalog
/// knows what translating a product means — and Assist must not reference
/// Catalog. Adding a proposable command is a class in the owning context and
/// one registration (invariant 3). Approving or rejecting is never one: an
/// architecture rule holds that.
/// </summary>
public interface IProposableCommand
{
    /// <summary>Stable, stored in every proposal: renaming it orphans them.</summary>
    string Name { get; }

    /// <summary>What a person reads: "Translate a product".</summary>
    LocalizedText Label { get; }

    Task<ProposalCheck> CheckAsync(JsonElement payload, CancellationToken cancellationToken = default);

    /// <summary>A snapshot of what the command would change, as it is now; null
    /// when the subject no longer exists or the payload cannot be read.</summary>
    Task<string?> NowAsync(JsonElement payload, CancellationToken cancellationToken = default);

    Task<ProposalApplication> ApplyAsync(JsonElement payload, CancellationToken cancellationToken = default);
}

/// <param name="Subject">What the proposal is about, as a value (a product's code).</param>
/// <param name="Now">The snapshot <see cref="IProposableCommand.NowAsync"/> returns, taken when proposed.</param>
public sealed record ProposalCheck(string? Subject, string? Now, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static ProposalCheck Valid(string subject, string now) => new(subject, now, []);

    public static ProposalCheck Invalid(params string[] errors) => new(null, null, errors);
}

public sealed record ProposalApplication(bool Applied, string? Reason)
{
    public static readonly ProposalApplication Done = new(true, null);

    public static ProposalApplication Failed(string reason) => new(false, reason);
}

/// <param name="Now">The fields the command would change, as they are: serialised
/// into the snapshot staleness compares.</param>
public sealed record ProposalSubject(string Subject, object Now);

/// <summary>
/// What every proposable command does the same way: read the payload into the
/// command, run the command's own validators — the ones its endpoint runs — and
/// dispatch it, so the audit, the validation and the handler are the ones a
/// person's click goes through. A derived class says only how to build the
/// command and how to read its subject.
/// </summary>
public abstract class ProposableCommand<TPayload, TCommand, TResult>(IServiceProvider services) : IProposableCommand
    where TPayload : class
    where TCommand : ICommand<TResult>
    where TResult : IAuditedResult
{
    /// <summary>Fixed, because a snapshot is compared as text.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public abstract string Name { get; }

    public abstract LocalizedText Label { get; }

    protected abstract TCommand ToCommand(TPayload payload);

    /// <summary>The subject and what the command would change, or null when the
    /// subject does not exist.</summary>
    protected abstract Task<ProposalSubject?> ReadAsync(TCommand command, CancellationToken cancellationToken);

    public async Task<ProposalCheck> CheckAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        if (Read(payload) is not { } command)
            return ProposalCheck.Invalid($"The payload is not a {Name}.");

        var errors = new List<string>();
        foreach (var validator in services.GetServices<IValidator<TCommand>>())
            errors.AddRange((await validator.ValidateAsync(command, cancellationToken)).Errors.Select(error => error.ErrorMessage));

        if (errors.Count > 0)
            return ProposalCheck.Invalid([.. errors]);

        return await ReadAsync(command, cancellationToken) is { } subject
            ? ProposalCheck.Valid(subject.Subject, Snapshot(subject.Now))
            : ProposalCheck.Invalid($"The subject of this {Name} does not exist.");
    }

    public async Task<string?> NowAsync(JsonElement payload, CancellationToken cancellationToken = default) =>
        Read(payload) is { } command && await ReadAsync(command, cancellationToken) is { } subject
            ? Snapshot(subject.Now)
            : null;

    public async Task<ProposalApplication> ApplyAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        if (Read(payload) is not { } command)
            return ProposalApplication.Failed($"The payload is not a {Name}.");

        IAuditedResult result = await services.GetRequiredService<ICommandDispatcher>().SendAsync(command, cancellationToken);

        return result.AuditOutcome == AuditOutcome.Allowed
            ? ProposalApplication.Done
            : ProposalApplication.Failed(result.AuditReason ?? result.AuditOutcome.ToString());
    }

    private TCommand? Read(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<TPayload>(Json) is { } read ? ToCommand(read) : default;
        }
        catch (Exception unreadable) when (unreadable is JsonException or ArgumentException or NotSupportedException)
        {
            return default;
        }
    }

    private static string Snapshot(object now) => JsonSerializer.Serialize(now, Json);
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SharedKernel.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SharedKernel/Proposals.cs tests/SharedKernel.Tests/ProposableCommandTests.cs
git commit -m "feat(kernel): a port that makes a command proposable, checked and run as a person's click (S-1)"
```

---

### Task 3: `SetProductTranslation` in Catalog

**Files:**
- Create: `src/Catalog/Features/SetProductTranslation/SetProductTranslation.cs`
- Modify: `src/Catalog/Domain/Product.cs` (two constants)
- Test: `tests/Catalog.Tests/SetProductTranslationTests.cs`

**Interfaces:**
- Consumes: `IProductRepository`, `IUnitOfWork`, `Product.Localize(TimeProvider, string culture, string name, string? description)`, `CultureNegotiation.Supports`, `Culture.Normalize`.
- Produces: `SetProductTranslationCommand(ProductId ProductId, string Culture, string Name, string? Description) : ICommand<SetProductTranslationResult>`; `enum TranslationOutcome { Translated, Unchanged, Archived, NotFound }`; `SetProductTranslationResult(TranslationOutcome Outcome, ProductId ProductId) : IAuditedResult`; `PUT /api/catalog/products/{id:guid}/translations/{culture}` named `SetProductTranslation`; `Product.NameMaxLength`, `Product.DescriptionMaxLength`.

- [ ] **Step 1: Write the failing tests** — `tests/Catalog.Tests/SetProductTranslationTests.cs`:

```csharp
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.SetProductTranslation;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// Writes one culture's text and leaves the others alone — the command Steward
/// proposes for a product with no English name (phase 12).
/// </summary>
public sealed class SetProductTranslationTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task Adds_the_english_name_and_keeps_the_spanish_one()
    {
        var product = AProduct();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetProductTranslationCommand(product.Id, "en", "Italian moka pot, 12 cups", null), TestContext.Current.CancellationToken);

        Assert.Equal(TranslationOutcome.Translated, result.Outcome);
        Assert.Equal("Italian moka pot, 12 cups", product.Name.Values["en"]);
        Assert.Equal("Cafetera italiana 12 tazas", product.Name.Values["es"]);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Writes_the_description_in_that_culture_only_when_one_is_given()
    {
        var product = AProduct();
        var handler = HandlerOver(product, out _);

        await handler.HandleAsync(
            new SetProductTranslationCommand(product.Id, "en", "Moka pot", "Aluminium, for gas and electric hobs."), TestContext.Current.CancellationToken);

        Assert.Equal("Aluminium, for gas and electric hobs.", product.Description!.Values["en"]);
        Assert.Equal("Aluminio, para gas y vitrocerámica.", product.Description.Values["es"]);
    }

    /// <summary>"The same text twice is one change": no save, no event, so the
    /// index is not rewritten for nothing.</summary>
    [Fact]
    public async Task The_same_text_twice_is_one_change()
    {
        var product = AProduct();
        product.Localize(Clock, "en", "Moka pot");
        product.ClearDomainEvents();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetProductTranslationCommand(product.Id, "EN", "Moka pot", null), TestContext.Current.CancellationToken);

        Assert.Equal(TranslationOutcome.Unchanged, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public async Task An_archived_product_is_refused()
    {
        var product = AProduct();
        product.Archive(Clock);
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetProductTranslationCommand(product.Id, "en", "Moka pot", null), TestContext.Current.CancellationToken);

        Assert.Equal(TranslationOutcome.Archived, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_missing_product_is_not_found()
    {
        var handler = HandlerOver(AProduct(), out _);

        var result = await handler.HandleAsync(
            new SetProductTranslationCommand(new ProductId(Guid.NewGuid()), "en", "Moka pot", null), TestContext.Current.CancellationToken);

        Assert.Equal(TranslationOutcome.NotFound, result.Outcome);
    }

    [Theory]
    [InlineData("fr", "Moka pot", "culture")]
    [InlineData("en", "   ", "name")]
    [InlineData("en", "", "name")]
    public void The_validator_refuses_an_unsupported_culture_and_a_blank_name(string culture, string name, string field)
    {
        var errors = new SetProductTranslationValidator()
            .Validate(new SetProductTranslationCommand(new ProductId(Guid.NewGuid()), culture, name, null)).Errors;

        Assert.Contains(errors, error => error.PropertyName.Contains(field, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_validator_holds_the_catalogues_lengths()
    {
        var errors = new SetProductTranslationValidator().Validate(new SetProductTranslationCommand(
            new ProductId(Guid.NewGuid()), "en", new string('a', Product.NameMaxLength + 1), new string('b', Product.DescriptionMaxLength + 1))).Errors;

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void A_blank_description_is_refused_and_a_missing_one_is_not()
    {
        var validator = new SetProductTranslationValidator();
        var id = new ProductId(Guid.NewGuid());

        Assert.False(validator.Validate(new SetProductTranslationCommand(id, "en", "Moka pot", "  ")).IsValid);
        Assert.True(validator.Validate(new SetProductTranslationCommand(id, "en", "Moka pot", null)).IsValid);
    }

    private static SetProductTranslationHandler HandlerOver(Product product, out CountingUnitOfWork unitOfWork)
    {
        unitOfWork = new CountingUnitOfWork();
        return new SetProductTranslationHandler(new InMemoryProductRepository(product), unitOfWork, Clock);
    }

    private static Product AProduct() => Product.Create(
        Clock,
        LocalizedText.From("es", "Cafetera italiana 12 tazas"),
        new Money(29.90m, "EUR"),
        LocalizedText.From("es", "Aluminio, para gas y vitrocerámica."));
}
```

Check `Product.Create`'s parameter order in `src/Catalog/Domain/Product.cs:125` before running; the description is its optional `LocalizedText? description` — pass it by name (`description: …`) if it is not the fourth argument.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Catalog.Tests --filter "FullyQualifiedName~SetProductTranslationTests"`
Expected: build FAILS — the slice does not exist.

- [ ] **Step 3: Add the lengths to `Product`** — beside the other public members at the top of the class:

```csharp
    /// <summary>The longest name a translation may write. The seed's longest is 56
    /// characters; the column is jsonb, so this is the catalogue's rule, not the
    /// database's.</summary>
    public const int NameMaxLength = 200;

    public const int DescriptionMaxLength = 4000;
```

- [ ] **Step 4: Write the slice** — `src/Catalog/Features/SetProductTranslation/SetProductTranslation.cs`:

```csharp
using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.SetProductTranslation;

/// <summary>
/// Writes a product's name, and optionally its description, in one supported
/// culture, leaving the others alone. It is what Steward proposes for a product
/// with no English name (phase 12), and the first command a model may propose:
/// it adds the missing language and never rewrites the original.
/// </summary>
public sealed record SetProductTranslationCommand(ProductId ProductId, string Culture, string Name, string? Description)
    : ICommand<SetProductTranslationResult>;

public enum TranslationOutcome
{
    Translated,
    Unchanged,   // the same text again: one change, not two
    Archived,
    NotFound
}

public sealed record SetProductTranslationResult(TranslationOutcome Outcome, ProductId ProductId) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        TranslationOutcome.Translated or TranslationOutcome.Unchanged => AuditOutcome.Allowed,
        TranslationOutcome.Archived or TranslationOutcome.NotFound => AuditOutcome.Denied
    };

    string? IAuditedResult.AuditReason =>
        Outcome is TranslationOutcome.Translated or TranslationOutcome.Unchanged ? null : Outcome.ToString();
}

public sealed class SetProductTranslationValidator : AbstractValidator<SetProductTranslationCommand>
{
    public SetProductTranslationValidator()
    {
        RuleFor(x => x.ProductId.Value)
            .NotEqual(Guid.Empty)
            .WithMessage("ProductId must be a non-empty product identifier.");

        RuleFor(x => x.Culture)
            .Must(CultureNegotiation.Supports)
            .WithMessage($"Culture must be one the shop speaks: {string.Join(", ", CultureNegotiation.Supported)}.");

        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name must not be blank.")
            .MaximumLength(Product.NameMaxLength);

        RuleFor(x => x.Description)
            .Must(description => description is null || !string.IsNullOrWhiteSpace(description))
            .WithMessage("Description must be left out or have text.")
            .MaximumLength(Product.DescriptionMaxLength);
    }
}

public sealed class SetProductTranslationHandler(IProductRepository repository, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<SetProductTranslationCommand, SetProductTranslationResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<SetProductTranslationResult> HandleAsync(
        SetProductTranslationCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.translate");
        activity?.SetTag("catalog.product_id", command.ProductId.Value);
        activity?.SetTag("catalog.culture", command.Culture);

        var product = await repository.FindByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
            return Outcome(TranslationOutcome.NotFound, command.ProductId, activity);
        if (product.Status == ProductStatus.Archived)
            return Outcome(TranslationOutcome.Archived, product.Id, activity);

        var culture = Culture.Normalize(command.Culture);
        if (Says(product.Name, culture, command.Name)
            && (command.Description is null || Says(product.Description, culture, command.Description)))
            return Outcome(TranslationOutcome.Unchanged, product.Id, activity);

        product.Localize(clock, culture, command.Name, command.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Outcome(TranslationOutcome.Translated, product.Id, activity);
    }

    private static bool Says(LocalizedText? text, string culture, string value) =>
        text is not null && text.Values.TryGetValue(culture, out var current) && current == value;

    private static SetProductTranslationResult Outcome(TranslationOutcome outcome, ProductId id, Activity? activity)
    {
        activity?.SetTag("catalog.translate_outcome", outcome.ToString());
        return new SetProductTranslationResult(outcome, id);
    }
}

// ---------- The wire ----------

public sealed record SetProductTranslationRequest(string Name, string? Description);

public sealed record SetProductTranslationResponse(string ProductId, TranslationOutcome Outcome);

public sealed record SetProductTranslationConflict(string Title, string Detail);

public sealed class SetProductTranslationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // PUT /api/catalog/products/{id}/translations/en  { "name": "…", "description": "…" }
        app.MapPut("/api/catalog/products/{id:guid}/translations/{culture}",
            async Task<Results<Ok<SetProductTranslationResponse>, NotFound, Conflict<SetProductTranslationConflict>>> (
                   Guid id, string culture, SetProductTranslationRequest body, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(
                    new SetProductTranslationCommand(new ProductId(id), culture, body.Name, body.Description), ct);

                return result.Outcome switch
                {
                    TranslationOutcome.NotFound => TypedResults.NotFound(),
                    TranslationOutcome.Archived => TypedResults.Conflict(new SetProductTranslationConflict(
                        "The product is archived.",
                        "Archived products are out of the catalogue and are not translated.")),
                    _ => TypedResults.Ok(new SetProductTranslationResponse(result.ProductId.Value.ToString(), result.Outcome))
                };
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("SetProductTranslation");
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Catalog.Tests`
Expected: PASS.

- [ ] **Step 6: Keep the API's rules green in the same commit** — the endpoint is in the API from now on, so:
  - `tests/Api.Tests/EndpointAuthorizationTests.cs`: add `[InlineData("/api/catalog/products/{id:guid}/translations/{culture}")]` to `Writes_to_the_catalogue_require_the_shopkeeper`.
  - `frontend/apps/backoffice/public/i18n/en.json` and `es.json`, under `audit.commands`, in the file's key order: `SetProductTranslationCommand` → *Translate a product* / *Traducir un producto*.
  - Run: `UPDATE_OPENAPI=1 dotnet test tests/Api.Tests`, then from `frontend/` `npm run generate:api-types`.
  - Run: `dotnet test tests/Api.Tests` and `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~AuditCommandNamesTests"`. Expected: PASS; `docs/openapi/tendero.json` gains `SetProductTranslation`, `SetProductTranslationRequest`, `SetProductTranslationResponse` and `TranslationOutcome`.

- [ ] **Step 7: Commit**

```bash
git add src/Catalog tests/Catalog.Tests/SetProductTranslationTests.cs tests/Api.Tests docs/openapi frontend
git commit -m "feat(catalog): SetProductTranslation writes one culture and leaves the others alone (S-1)"
```

---

### Task 4: Catalog's two proposable commands

**Files:**
- Create: `src/Catalog/Features/SetProductTranslation/SetProductTranslationProposal.cs`
- Create: `src/Catalog/Features/WithdrawProduct/WithdrawProductProposal.cs`
- Modify: `src/Catalog/CatalogServiceCollectionExtensions.cs` (`AddCatalog`: two registrations)
- Test: `tests/Catalog.Tests/CatalogProposalsTests.cs`

**Interfaces:**
- Consumes: `ProposableCommand<,,>`, `ProposalSubject` (Task 2); the two commands.
- Produces: `SetProductTranslationProposal` (`Name` = `"SetProductTranslation"`, payload `{ productId, culture, name, description? }`, snapshot `{ name, description }` in that culture); `WithdrawProductProposal` (`Name` = `"WithdrawProduct"`, payload `{ productId }`, snapshot `{ status }`); both `services.AddScoped<IProposableCommand, …>()`.

- [ ] **Step 1: Write the failing tests** — `tests/Catalog.Tests/CatalogProposalsTests.cs`:

```csharp
using System.Text.Json;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.SetProductTranslation;
using ElGuerre.Tendero.Catalog.Features.WithdrawProduct;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// What Catalog lets Steward propose, read through the port Assist sees: the
/// subject is the product's code, and "now" is exactly what the command would
/// change, in the culture it would change it.
/// </summary>
[Collection(PublishMetricsCollection.Name)]
public sealed class CatalogProposalsTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task A_translation_is_about_the_products_code_and_snapshots_that_cultures_text()
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Cafetera"), new Money(10m, "EUR"));
        var entry = Entries(product).Single(e => e.Name == "SetProductTranslation");

        var check = await entry.CheckAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}","culture":"en","name":"Moka pot"}"""), TestContext.Current.CancellationToken);

        Assert.True(check.IsValid);
        Assert.Equal(product.Code, check.Subject);
        Assert.Equal("""{"name":null,"description":null}""", check.Now);
    }

    [Fact]
    public async Task A_translation_into_an_unsupported_culture_is_refused()
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Cafetera"), new Money(10m, "EUR"));
        var entry = Entries(product).Single(e => e.Name == "SetProductTranslation");

        var check = await entry.CheckAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}","culture":"fr","name":"Cafetière"}"""), TestContext.Current.CancellationToken);

        Assert.False(check.IsValid);
    }

    [Fact]
    public async Task Applying_a_translation_writes_it()
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Cafetera"), new Money(10m, "EUR"));
        var entry = Entries(product).Single(e => e.Name == "SetProductTranslation");

        var application = await entry.ApplyAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}","culture":"en","name":"Moka pot"}"""), TestContext.Current.CancellationToken);

        Assert.True(application.Applied);
        Assert.Equal("Moka pot", product.Name.Values["en"]);
    }

    [Fact]
    public async Task A_withdrawal_snapshots_the_status()
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Cafetera"), new Money(10m, "EUR"));
        product.Publish(Clock);
        var entry = Entries(product).Single(e => e.Name == "WithdrawProduct");

        var check = await entry.CheckAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}"}"""), TestContext.Current.CancellationToken);

        Assert.Equal("""{"status":"Active"}""", check.Now);
    }

    [Fact]
    public void Every_entry_has_a_label_in_both_languages()
    {
        foreach (var entry in Entries(Product.Create(Clock, LocalizedText.From("es", "Cafetera"), new Money(10m, "EUR"))))
            Assert.Equal(["en", "es"], entry.Label.Cultures.Order());
    }

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IReadOnlyList<IProposableCommand> Entries(Product product)
    {
        var services = new ServiceCollection();
        services.AddTenderoCqrs(typeof(SetProductTranslationCommand).Assembly);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IProductRepository>(new InMemoryProductRepository(product));
        services.AddSingleton<IUnitOfWork>(new CountingUnitOfWork());
        services.AddScoped<IProposableCommand, SetProductTranslationProposal>();
        services.AddScoped<IProposableCommand, WithdrawProductProposal>();
        return [.. services.BuildServiceProvider().CreateScope().ServiceProvider.GetServices<IProposableCommand>()];
    }
}
```

`Product.Publish` may require a price, an image or a category before it allows Active; if it throws, build the product the way `WithdrawProductTests.AnActiveProduct()` does and reuse that. Publishing needs `IActorAccessor` for `WithdrawProductHandler`: `AddTenderoCqrs` registers `SystemActorAccessor`.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Catalog.Tests --filter "FullyQualifiedName~CatalogProposalsTests"`
Expected: build FAILS — the two proposal classes do not exist.

- [ ] **Step 3: Write the translation's entry** — `src/Catalog/Features/SetProductTranslation/SetProductTranslationProposal.cs`:

```csharp
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.SetProductTranslation;

/// <param name="ProductId">Flat, as on the wire: the model writes JSON, never a record struct.</param>
public sealed record SetProductTranslationPayload(Guid ProductId, string Culture, string Name, string? Description);

/// <summary>
/// Makes the translation proposable (phase 12). "Now" is the product's text in
/// that one culture, so a proposal goes stale only when somebody wrote that
/// language meanwhile — a change to the Spanish name does not stop an English one.
/// </summary>
public sealed class SetProductTranslationProposal(IServiceProvider services, IProductRepository products)
    : ProposableCommand<SetProductTranslationPayload, SetProductTranslationCommand, SetProductTranslationResult>(services)
{
    public const string CommandName = "SetProductTranslation";

    public override string Name => CommandName;

    public override LocalizedText Label { get; } = new(new Dictionary<string, string>
    {
        ["en"] = "Translate a product",
        ["es"] = "Traducir un producto",
    });

    protected override SetProductTranslationCommand ToCommand(SetProductTranslationPayload payload) =>
        new(new ProductId(payload.ProductId), payload.Culture, payload.Name, payload.Description);

    protected override async Task<ProposalSubject?> ReadAsync(SetProductTranslationCommand command, CancellationToken cancellationToken)
    {
        var product = await products.FindByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
            return null;

        var culture = Culture.Normalize(command.Culture);
        return new ProposalSubject(product.Code, new TranslationNow(
            product.Name.Values.GetValueOrDefault(culture),
            product.Description?.Values.GetValueOrDefault(culture)));
    }

    private sealed record TranslationNow(string? Name, string? Description);
}
```

- [ ] **Step 4: Write the withdrawal's entry** — `src/Catalog/Features/WithdrawProduct/WithdrawProductProposal.cs`:

```csharp
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.WithdrawProduct;

public sealed record WithdrawProductPayload(Guid ProductId);

/// <summary>Makes withdrawing proposable (phase 12): stale once the product's status moved.</summary>
public sealed class WithdrawProductProposal(IServiceProvider services, IProductRepository products)
    : ProposableCommand<WithdrawProductPayload, WithdrawProductCommand, WithdrawProductResult>(services)
{
    public const string CommandName = "WithdrawProduct";

    public override string Name => CommandName;

    public override LocalizedText Label { get; } = new(new Dictionary<string, string>
    {
        ["en"] = "Withdraw a product",
        ["es"] = "Retirar un producto",
    });

    protected override WithdrawProductCommand ToCommand(WithdrawProductPayload payload) => new(new ProductId(payload.ProductId));

    protected override async Task<ProposalSubject?> ReadAsync(WithdrawProductCommand command, CancellationToken cancellationToken) =>
        await products.FindByIdAsync(command.ProductId, cancellationToken) is { } product
            ? new ProposalSubject(product.Code, new WithdrawalNow(product.Status.ToString()))
            : null;

    private sealed record WithdrawalNow(string Status);
}
```

- [ ] **Step 5: Register both in `AddCatalog`** — add `using ElGuerre.Tendero.Catalog.Features.SetProductTranslation;` and `using ElGuerre.Tendero.Catalog.Features.WithdrawProduct;`, then inside `AddCatalog` before `return services;`:

```csharp
        // What Steward may propose (phase 12): a class here and a line there.
        services.AddScoped<IProposableCommand, SetProductTranslationProposal>();
        services.AddScoped<IProposableCommand, WithdrawProductProposal>();
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Catalog.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Catalog tests/Catalog.Tests/CatalogProposalsTests.cs
git commit -m "feat(catalog): translating and withdrawing a product become proposable (S-1)"
```

---

### Task 5: The `Assist` context and `ActionProposal`

**Files:**
- Create: `src/Assist/ElGuerre.Tendero.Assist.csproj`, `src/Assist/AssemblyInfo.cs` (only if Catalog's carries something Assist needs; otherwise skip), `src/Assist/Domain/ActionProposal.cs`, `src/Assist/stryker-config.json`
- Create: `tests/Assist.Tests/ElGuerre.Tendero.Assist.Tests.csproj`, `tests/Assist.Tests/ActionProposalTests.cs`
- Modify: `Tendero.slnx` (both projects), `src/SharedKernel/TelemetrySources.cs` (`Assist`, and in `All`)

**Interfaces:**
- Produces: `enum ProposalStatus { Proposed, Approved, Applied, Failed, Rejected, Stale }`; `record ProposalProvenance(string? Profile, string? Model, string? PromptVersion, string? ConversationId, string? TraceId, string AskedBy)`; `class ActionProposal : AggregateRoot` with `Id`, `Command`, `Payload`, `Subject`, `Before`, `Reason`, `Provenance`, `ProposedAt`, `Status`, `DecidedBy`, `DecidedAt`, `Result`, and `static Propose(string command, string payload, string subject, string? before, string reason, ProposalProvenance provenance, TimeProvider clock)`, `Approve(string by, TimeProvider clock)`, `Reject(string by, string reason, TimeProvider clock)`, `MarkStale(string by, TimeProvider clock)`, `RecordApplied()`, `RecordFailed(string reason)`; `TelemetrySources.Assist = "ElGuerre.Tendero.Assist"`.

- [ ] **Step 1: Create the projects**

`src/Assist/ElGuerre.Tendero.Assist.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Bounded context Assist (phase 12, ADR 0052): what a model proposes and a
       person decides. It owns ActionProposal, which has a lifecycle, a writer and
       actors of its own — how a context earns its boundary (ADR 0014). It runs
       other contexts' commands only through IProposableCommand, so it references
       SharedKernel and nothing else; an architecture rule holds that. -->
  <PropertyGroup>
    <RootNamespace>ElGuerre.Tendero.Assist</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Carter" />
    <PackageReference Include="FluentValidation" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../SharedKernel/ElGuerre.Tendero.SharedKernel.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="ElGuerre.Tendero.Assist.Tests" />
  </ItemGroup>

</Project>
```

`tests/Assist.Tests/ElGuerre.Tendero.Assist.Tests.csproj`: a copy of `tests/Knowledge.Tests/ElGuerre.Tendero.Knowledge.Tests.csproj` with `Knowledge` replaced by `Assist` in `RootNamespace` and the project reference.

`src/Assist/stryker-config.json`: a copy of `src/Knowledge/stryker-config.json` with `Knowledge` replaced by `Assist` in `project` and `test-projects`.

Run: `dotnet sln Tendero.slnx add src/Assist/ElGuerre.Tendero.Assist.csproj tests/Assist.Tests/ElGuerre.Tendero.Assist.Tests.csproj`
Expected: both lines appear in `Tendero.slnx`, in the same folders as Knowledge's (move them by hand if `dotnet sln` puts them at the root).

In `src/SharedKernel/TelemetrySources.cs` add `public const string Assist = "ElGuerre.Tendero.Assist";` and put `Assist` first in `All`.

- [ ] **Step 2: Write the failing tests** — `tests/Assist.Tests/ActionProposalTests.cs`:

```csharp
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

/// <summary>A proposal's life is a table (invariant 5): every move it allows, and the ones it refuses.</summary>
public sealed class ActionProposalTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public void A_new_proposal_waits_for_review_with_its_provenance()
    {
        var proposal = AProposal();

        Assert.Equal(ProposalStatus.Proposed, proposal.Status);
        Assert.Equal("lucia", proposal.Provenance.AskedBy);
        Assert.Equal(Clock.GetUtcNow(), proposal.ProposedAt);
        Assert.Null(proposal.DecidedBy);
    }

    [Fact]
    public void Approving_then_applying_records_who_and_when()
    {
        var proposal = AProposal();

        proposal.Approve("Lucía", Clock);
        proposal.RecordApplied();

        Assert.Equal(ProposalStatus.Applied, proposal.Status);
        Assert.Equal("Lucía", proposal.DecidedBy);
        Assert.Equal(Clock.GetUtcNow(), proposal.DecidedAt);
        Assert.Null(proposal.Result);
    }

    [Fact]
    public void A_failure_keeps_its_reason()
    {
        var proposal = AProposal();
        proposal.Approve("Lucía", Clock);

        proposal.RecordFailed("Archived");

        Assert.Equal((ProposalStatus.Failed, "Archived"), (proposal.Status, proposal.Result));
    }

    [Fact]
    public void A_rejection_keeps_the_reason_a_prompt_change_is_judged_against()
    {
        var proposal = AProposal();

        proposal.Reject("Lucía", "Too literal", Clock);

        Assert.Equal((ProposalStatus.Rejected, "Too literal", "Lucía"), (proposal.Status, proposal.Result, proposal.DecidedBy));
    }

    [Fact]
    public void A_stale_proposal_says_who_found_it_stale()
    {
        var proposal = AProposal();

        proposal.MarkStale("Lucía", Clock);

        Assert.Equal((ProposalStatus.Stale, "Lucía"), (proposal.Status, proposal.DecidedBy));
    }

    [Fact]
    public void Nothing_is_applied_without_an_approval()
    {
        Assert.Throws<InvalidOperationException>(() => AProposal().RecordApplied());
    }

    [Fact]
    public void A_decided_proposal_is_not_decided_again()
    {
        var rejected = AProposal();
        rejected.Reject("Lucía", "No", Clock);

        Assert.Throws<InvalidOperationException>(() => rejected.Approve("Lucía", Clock));
    }

    [Theory]
    [InlineData("", "{}", "MOKA-1", "reason")]
    [InlineData("SetProductTranslation", " ", "MOKA-1", "reason")]
    [InlineData("SetProductTranslation", "{}", "", "reason")]
    [InlineData("SetProductTranslation", "{}", "MOKA-1", " ")]
    public void A_proposal_has_a_command_a_payload_a_subject_and_a_reason(string command, string payload, string subject, string reason)
    {
        Assert.Throws<ArgumentException>(() => ActionProposal.Propose(command, payload, subject, null, reason, Provenance(), Clock));
    }

    private static ActionProposal AProposal() => ActionProposal.Propose(
        "SetProductTranslation",
        """{"productId":"00000000-0000-0000-0000-000000000001","culture":"en","name":"Moka pot"}""",
        "MOKA-1",
        """{"name":null,"description":null}""",
        "It has no English name.",
        Provenance(),
        Clock);

    private static ProposalProvenance Provenance() => new("local", "qwen3.5-4b", "steward-v1", "conv-1", "trace-1", "lucia");
}
```

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test tests/Assist.Tests`
Expected: build FAILS — `ActionProposal` does not exist.

- [ ] **Step 4: Write the aggregate** — `src/Assist/Domain/ActionProposal.cs`:

```csharp
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Domain;

public enum ProposalStatus
{
    Proposed,

    /// <summary>A person said yes and the command is running — or the process died
    /// while it ran, and the list shows "approved, result unknown".</summary>
    Approved,

    Applied,
    Failed,
    Rejected,

    /// <summary>What the command would change changed after it was proposed.</summary>
    Stale,
}

/// <summary>Where a proposal came from. Who asked is the principal's name, never
/// a value from the request.</summary>
public sealed record ProposalProvenance(
    string? Profile,
    string? Model,
    string? PromptVersion,
    string? ConversationId,
    string? TraceId,
    string AskedBy);

/// <summary>
/// A command a model proposed, waiting for a person (phase 12). It carries values,
/// never a reference into another context (invariant 1): the command's registry
/// name, its payload as JSON, the subject's code, and a snapshot of what the
/// command would change as it was when proposed — which is how approving finds
/// it stale.
/// </summary>
public sealed class ActionProposal : AggregateRoot
{
    private static readonly TransitionTable<ProposalStatus> AllowedTransitions = new()
    {
        [ProposalStatus.Proposed] = [ProposalStatus.Approved, ProposalStatus.Rejected, ProposalStatus.Stale],
        [ProposalStatus.Approved] = [ProposalStatus.Applied, ProposalStatus.Failed],
        [ProposalStatus.Applied] = [],
        [ProposalStatus.Failed] = [],
        [ProposalStatus.Rejected] = [],
        [ProposalStatus.Stale] = [],
    };

    private ActionProposal() { }

    public ProposalId Id { get; private set; }
    public string Command { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string? Before { get; private set; }
    public string Reason { get; private set; } = null!;
    public ProposalProvenance Provenance { get; private set; } = null!;
    public DateTimeOffset ProposedAt { get; private set; }
    public ProposalStatus Status { get; private set; }
    public string? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Why it failed, or why it was rejected; null otherwise.</summary>
    public string? Result { get; private set; }

    public static ActionProposal Propose(
        string command, string payload, string subject, string? before, string reason,
        ProposalProvenance provenance, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(provenance);

        var at = clock.GetUtcNow();
        return new ActionProposal
        {
            Id = ProposalId.New(at),
            Command = command,
            Payload = payload,
            Subject = subject,
            Before = before,
            Reason = reason,
            Provenance = provenance,
            ProposedAt = at,
            Status = ProposalStatus.Proposed,
        };
    }

    public void Approve(string by, TimeProvider clock) => Decide(ProposalStatus.Approved, by, clock);

    public void Reject(string by, string reason, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Decide(ProposalStatus.Rejected, by, clock);
        Result = reason;
    }

    public void MarkStale(string by, TimeProvider clock) => Decide(ProposalStatus.Stale, by, clock);

    public void RecordApplied() => Move(ProposalStatus.Applied);

    public void RecordFailed(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Move(ProposalStatus.Failed);
        Result = reason;
    }

    private void Decide(ProposalStatus to, string by, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        Move(to);
        DecidedBy = by;
        DecidedAt = clock.GetUtcNow();
    }

    private void Move(ProposalStatus to)
    {
        AllowedTransitions.EnsureAllowed(Status, to, $"proposal {Id}");
        Status = to;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Assist.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Assist tests/Assist.Tests Tendero.slnx src/SharedKernel/TelemetrySources.cs
git commit -m "feat(assist): the seventh context, and a proposal whose life is a table (S-1)"
```

---

### Task 6: Propose, approve and reject

**Files:**
- Create: `src/Assist/Ports/AssistPorts.cs`, `src/Assist/ProposableCommands.cs`, `src/Assist/AssistServiceCollectionExtensions.cs`
- Create: `src/Assist/Features/ProposeActions/ProposeActions.cs`
- Create: `src/Assist/Features/DecideProposals/DecideProposals.cs`
- Test: `tests/Assist.Tests/InMemoryProposals.cs`, `tests/Assist.Tests/ProposeActionsTests.cs`, `tests/Assist.Tests/DecideProposalsTests.cs`, `tests/Assist.Tests/ProposableCommandsTests.cs`

**Interfaces:**
- Consumes: `ActionProposal` (Task 5); `IProposableCommand`, `ProposalScope` (Tasks 1–2).
- Produces:
  - `interface IProposalRepository { Task<ActionProposal?> FindAsync(ProposalId id, CancellationToken ct = default); void Add(ActionProposal proposal); }`
  - `sealed class ProposableCommands(IEnumerable<IProposableCommand> entries) { IProposableCommand? Find(string name); }` (scoped)
  - `ProposeActionsCommand(IReadOnlyList<ProposedAction> Actions, ProposalSource Source) : ICommand<ProposeActionsResult>`; `ProposedAction(string Command, JsonElement Payload, string Reason)`; `ProposalSource(string? Profile, string? Model, string? PromptVersion, string? ConversationId)`; `ProposedRow(int Index, ProposalId? Id, IReadOnlyList<string> Errors)`; `ProposeActionsResult(IReadOnlyList<ProposedRow> Rows)`
  - `ApproveProposalsCommand(IReadOnlyList<ProposalId> Ids) : ICommand<ProposalDecisions>`; `RejectProposalsCommand(IReadOnlyList<ProposalId> Ids, string Reason) : ICommand<ProposalDecisions>`; `enum DecisionOutcome { Applied, Failed, Stale, Rejected, NotFound, NotWaiting }`; `ProposalDecision(ProposalId Id, DecisionOutcome Outcome, string? Reason)`; `ProposalDecisions(IReadOnlyList<ProposalDecision> Rows)`
  - `POST /api/steward/proposals/approve` (`ApproveProposals`) and `POST /api/steward/proposals/reject` (`RejectProposals`), body ids as strings, response `ProposalDecisionsResponse(IReadOnlyList<ProposalDecisionView> Rows)`, `ProposalDecisionView(string Id, DecisionOutcome Outcome, string? Reason)`
  - `IServiceCollection AddAssist(this IServiceCollection services)`
  - `ProposalLimits.MaxBatch = 100`

- [ ] **Step 1: Write the test doubles** — `tests/Assist.Tests/InMemoryProposals.cs`:

```csharp
using System.Text.Json;
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Tests;

internal sealed class InMemoryProposals : IProposalRepository
{
    public List<ActionProposal> All { get; } = [];

    public Task<ActionProposal?> FindAsync(ProposalId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(All.SingleOrDefault(proposal => proposal.Id == id));

    public void Add(ActionProposal proposal) => All.Add(proposal);
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FixedPrincipal(string name) : IPrincipalAccessor
{
    public CommercePrincipal Current { get; } = new(null, null, name.ToLowerInvariant(), name, ["shopkeeper"]);
}

/// <summary>
/// A proposable command whose world is a dictionary: subject → value. What it
/// does on apply is set per subject, so one test can have a row that applies, a
/// row that refuses and a row that throws.
/// </summary>
internal sealed class FakeEntry(Dictionary<string, string> world, ProposalScope scope) : IProposableCommand
{
    public const string CommandName = "SetValue";

    public static HashSet<string> Refusing { get; } = [];
    public static HashSet<string> Throwing { get; } = [];
    public static List<(string Subject, ProposalId? Proposal)> Applied { get; } = [];

    public string Name => CommandName;

    public LocalizedText Label { get; } = new(new Dictionary<string, string> { ["en"] = "Set a value", ["es"] = "Poner un valor" });

    public Task<ProposalCheck> CheckAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var (subject, value) = Read(payload);
        if (string.IsNullOrWhiteSpace(value))
            return Task.FromResult(ProposalCheck.Invalid("Value must not be blank."));
        return Task.FromResult(world.TryGetValue(subject, out var now)
            ? ProposalCheck.Valid(subject, now)
            : ProposalCheck.Invalid($"No {subject}."));
    }

    public Task<string?> NowAsync(JsonElement payload, CancellationToken cancellationToken = default) =>
        Task.FromResult(world.GetValueOrDefault(Read(payload).Subject));

    public Task<ProposalApplication> ApplyAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var (subject, value) = Read(payload);
        if (Throwing.Contains(subject))
            throw new TimeoutException($"{subject} timed out.");
        if (Refusing.Contains(subject))
            return Task.FromResult(ProposalApplication.Failed("Archived"));

        world[subject] = value;
        Applied.Add((subject, scope.Current));
        return Task.FromResult(ProposalApplication.Done);
    }

    public static JsonElement Payload(string subject, string value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { subject, value })).RootElement.Clone();

    private static (string Subject, string Value) Read(JsonElement payload) =>
        (payload.GetProperty("subject").GetString()!, payload.GetProperty("value").GetString()!);
}
```

Because `FakeEntry`'s sets are static, put every test class that uses it in one xUnit collection: `[Collection(nameof(FakeEntry))]`, and clear the three sets in each test class's constructor.

- [ ] **Step 2: Write the failing tests for the registry and for proposing**

`tests/Assist.Tests/ProposableCommandsTests.cs`:

```csharp
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

[Collection(nameof(FakeEntry))]
public sealed class ProposableCommandsTests
{
    [Fact]
    public void Finds_an_entry_by_its_stable_name()
    {
        var entry = new FakeEntry([], new ProposalScope());

        Assert.Same(entry, new ProposableCommands([entry]).Find("SetValue"));
        Assert.Null(new ProposableCommands([entry]).Find("DropTable"));
    }

    /// <summary>Two entries with one name would let a proposal run whichever DI
    /// listed last: refused when the registry is built, not when a row is approved.</summary>
    [Fact]
    public void Two_entries_with_one_name_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ProposableCommands([new FakeEntry([], new ProposalScope()), new FakeEntry([], new ProposalScope())]));
    }
}
```

`tests/Assist.Tests/ProposeActionsTests.cs`:

```csharp
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Features.ProposeActions;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

/// <summary>Code builds every proposal: what the registry refuses goes back as an
/// error and is not stored.</summary>
[Collection(nameof(FakeEntry))]
public sealed class ProposeActionsTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task A_valid_action_is_stored_with_its_snapshot_and_whoever_asked()
    {
        var (handler, proposals, unitOfWork) = Handler(new() { ["MUG-1"] = "old" });

        var result = await handler.HandleAsync(Command(("MUG-1", "new")), TestContext.Current.CancellationToken);

        var stored = Assert.Single(proposals.All);
        Assert.Equal(stored.Id, Assert.Single(result.Rows).Id);
        Assert.Equal(("MUG-1", "old", "Lucía"), (stored.Subject, stored.Before, stored.Provenance.AskedBy));
        Assert.Equal("qwen3.5-4b", stored.Provenance.Model);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_invalid_action_comes_back_as_an_error_and_is_not_stored()
    {
        var (handler, proposals, _) = Handler(new() { ["MUG-1"] = "old" });

        var result = await handler.HandleAsync(Command(("MUG-1", "new"), ("GONE", "x"), ("MUG-1", " ")), TestContext.Current.CancellationToken);

        Assert.Single(proposals.All);
        Assert.Equal([0], result.Rows.Where(row => row.Id is not null).Select(row => row.Index));
        Assert.Equal("No GONE.", Assert.Single(result.Rows[1].Errors));
        Assert.Equal("Value must not be blank.", Assert.Single(result.Rows[2].Errors));
    }

    [Fact]
    public async Task An_action_no_entry_is_named_for_is_refused()
    {
        var (handler, proposals, _) = Handler(new() { ["MUG-1"] = "old" });

        var result = await handler.HandleAsync(new ProposeActionsCommand(
            [new ProposedAction("ApproveProposals", FakeEntry.Payload("MUG-1", "x"), "because")], Source()), TestContext.Current.CancellationToken);

        Assert.Empty(proposals.All);
        Assert.Equal("No proposable command is named ApproveProposals.", Assert.Single(Assert.Single(result.Rows).Errors));
    }

    [Fact]
    public void The_validator_bounds_a_batch_and_wants_a_reason()
    {
        var validator = new ProposeActionsValidator();

        Assert.False(validator.Validate(new ProposeActionsCommand([], Source())).IsValid);
        Assert.False(validator.Validate(new ProposeActionsCommand(
            [.. Enumerable.Repeat(new ProposedAction("SetValue", FakeEntry.Payload("a", "b"), "r"), ProposalLimits.MaxBatch + 1)], Source())).IsValid);
        Assert.False(validator.Validate(new ProposeActionsCommand(
            [new ProposedAction("SetValue", FakeEntry.Payload("a", "b"), " ")], Source())).IsValid);
    }

    private static ProposeActionsCommand Command(params (string Subject, string Value)[] actions) => new(
        [.. actions.Select(action => new ProposedAction(FakeEntry.CommandName, FakeEntry.Payload(action.Subject, action.Value), "It has no English name."))],
        Source());

    private static ProposalSource Source() => new("local", "qwen3.5-4b", "steward-v1", "conv-1");

    private static (ProposeActionsHandler, InMemoryProposals, CountingUnitOfWork) Handler(Dictionary<string, string> world)
    {
        var proposals = new InMemoryProposals();
        var unitOfWork = new CountingUnitOfWork();
        var handler = new ProposeActionsHandler(
            new ProposableCommands([new FakeEntry(world, new ProposalScope())]), proposals, unitOfWork, new FixedPrincipal("Lucía"), Clock);
        return (handler, proposals, unitOfWork);
    }
}
```

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test tests/Assist.Tests`
Expected: build FAILS — `ProposableCommands`, `ProposeActionsHandler`, `IProposalRepository` do not exist.

- [ ] **Step 4: Implement the port, the registry and proposing**

`src/Assist/Ports/AssistPorts.cs`:

```csharp
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Ports;

public interface IProposalRepository
{
    Task<ActionProposal?> FindAsync(ProposalId id, CancellationToken cancellationToken = default);

    void Add(ActionProposal proposal);
}
```

`src/Assist/ProposableCommands.cs`:

```csharp
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist;

/// <summary>
/// The closed vocabulary (phase 12): every command some context registered as
/// proposable, found by its stable name. What is not here cannot be proposed.
/// </summary>
public sealed class ProposableCommands
{
    private readonly Dictionary<string, IProposableCommand> entries;

    public ProposableCommands(IEnumerable<IProposableCommand> entries)
    {
        this.entries = [];
        foreach (var entry in entries)
        {
            if (!this.entries.TryAdd(entry.Name, entry))
                throw new InvalidOperationException($"Two proposable commands are named {entry.Name}.");
        }
    }

    public IProposableCommand? Find(string name) => this.entries.GetValueOrDefault(name);
}

public static class ProposalLimits
{
    /// <summary>"A hundred English names, each reviewed" (the spec): one batch, for
    /// proposing, approving and rejecting alike.</summary>
    public const int MaxBatch = 100;
}
```

`src/Assist/Features/ProposeActions/ProposeActions.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;

namespace ElGuerre.Tendero.Assist.Features.ProposeActions;

/// <param name="Payload">The command's fields as JSON — what a model's tool call
/// carries. The registry entry reads it; nothing here trusts it.</param>
public sealed record ProposedAction(string Command, JsonElement Payload, string Reason);

/// <summary>Which model, under which prompt, in which conversation. Who asked is
/// not here: it is the principal's.</summary>
public sealed record ProposalSource(string? Profile, string? Model, string? PromptVersion, string? ConversationId);

/// <summary>
/// Stores what a model proposed (phase 12), each action checked by the entry of
/// the context that owns its command. No endpoint: Steward's proposal tools (S-3)
/// dispatch it, and an invalid action goes back to the model as an error.
/// </summary>
public sealed record ProposeActionsCommand(IReadOnlyList<ProposedAction> Actions, ProposalSource Source)
    : ICommand<ProposeActionsResult>;

/// <param name="Index">The action's place in the command, so a model can tell which one failed.</param>
public sealed record ProposedRow(int Index, ProposalId? Id, IReadOnlyList<string> Errors);

public sealed record ProposeActionsResult(IReadOnlyList<ProposedRow> Rows);

public sealed class ProposeActionsValidator : AbstractValidator<ProposeActionsCommand>
{
    public ProposeActionsValidator()
    {
        RuleFor(x => x.Actions).NotEmpty();
        RuleFor(x => x.Actions.Count).LessThanOrEqualTo(ProposalLimits.MaxBatch);
        RuleForEach(x => x.Actions).ChildRules(action =>
        {
            action.RuleFor(a => a.Command).NotEmpty().MaximumLength(100);
            action.RuleFor(a => a.Reason)
                .Must(reason => !string.IsNullOrWhiteSpace(reason))
                .WithMessage("Every proposal says why.")
                .MaximumLength(2000);
        });
    }
}

public sealed class ProposeActionsHandler(
    ProposableCommands registry,
    IProposalRepository proposals,
    IUnitOfWork unitOfWork,
    IPrincipalAccessor principals,
    TimeProvider clock) : ICommandHandler<ProposeActionsCommand, ProposeActionsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Assist);

    public async Task<ProposeActionsResult> HandleAsync(ProposeActionsCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("assist.propose");
        activity?.SetTag("assist.actions", command.Actions.Count);

        var principal = principals.Current;
        var provenance = new ProposalProvenance(
            command.Source.Profile,
            command.Source.Model,
            command.Source.PromptVersion,
            command.Source.ConversationId,
            Activity.Current?.TraceId.ToString(),
            principal.DisplayName ?? principal.Subject ?? "unknown");

        var rows = new List<ProposedRow>(command.Actions.Count);
        foreach (var (action, index) in command.Actions.Select((action, index) => (action, index)))
        {
            if (registry.Find(action.Command) is not { } entry)
            {
                rows.Add(new ProposedRow(index, null, [$"No proposable command is named {action.Command}."]));
                continue;
            }

            var check = await entry.CheckAsync(action.Payload, cancellationToken);
            if (!check.IsValid)
            {
                rows.Add(new ProposedRow(index, null, check.Errors));
                continue;
            }

            var proposal = ActionProposal.Propose(
                entry.Name, action.Payload.GetRawText(), check.Subject!, check.Now, action.Reason, provenance, clock);
            proposals.Add(proposal);
            rows.Add(new ProposedRow(index, proposal.Id, []));
        }

        if (rows.Any(row => row.Id is not null))
            await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("assist.proposed", rows.Count(row => row.Id is not null));
        return new ProposeActionsResult(rows);
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Assist.Tests`
Expected: PASS.

- [ ] **Step 6: Write the failing tests for deciding** — `tests/Assist.Tests/DecideProposalsTests.cs`:

```csharp
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Features.DecideProposals;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

/// <summary>
/// Approving runs each row on its own, as the person approving: a stale row runs
/// nothing, a refusal or an exception fails that row alone, and each command runs
/// in a scope that names its proposal for the audit.
/// </summary>
[Collection(nameof(FakeEntry))]
public sealed class DecideProposalsTests
{
    private static readonly TestClock Clock = new();

    public DecideProposalsTests()
    {
        FakeEntry.Refusing.Clear();
        FakeEntry.Throwing.Clear();
        FakeEntry.Applied.Clear();
    }

    [Fact]
    public async Task An_approved_row_runs_as_the_approver_and_names_its_proposal()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");

        var result = await shop.Approve(proposal.Id);

        Assert.Equal(DecisionOutcome.Applied, Assert.Single(result.Rows).Outcome);
        Assert.Equal("new", shop.World["MUG-1"]);
        Assert.Equal(("MUG-1", (ProposalId?)proposal.Id), Assert.Single(FakeEntry.Applied));
        Assert.Equal((ProposalStatus.Applied, "Lucía"), (proposal.Status, proposal.DecidedBy));
    }

    [Fact]
    public async Task A_row_whose_subject_changed_is_stale_and_runs_nothing()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");
        shop.World["MUG-1"] = "edited by hand";

        var result = await shop.Approve(proposal.Id);

        Assert.Equal(DecisionOutcome.Stale, Assert.Single(result.Rows).Outcome);
        Assert.Equal("edited by hand", shop.World["MUG-1"]);
        Assert.Equal(ProposalStatus.Stale, proposal.Status);
    }

    [Fact]
    public async Task A_row_whose_subject_has_gone_is_stale()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");
        shop.World.Remove("MUG-1");

        Assert.Equal(DecisionOutcome.Stale, Assert.Single((await shop.Approve(proposal.Id)).Rows).Outcome);
    }

    [Fact]
    public async Task One_refusal_and_one_exception_fail_their_rows_and_stop_none_of_the_others()
    {
        var shop = Shop(("A", "1"), ("B", "1"), ("C", "1"));
        var a = shop.Propose("A", "2");
        var b = shop.Propose("B", "2");
        var c = shop.Propose("C", "2");
        FakeEntry.Refusing.Add("A");
        FakeEntry.Throwing.Add("B");

        var result = await shop.Approve(a.Id, b.Id, c.Id);

        Assert.Equal(
            [(DecisionOutcome.Failed, "Archived"), (DecisionOutcome.Failed, "B timed out."), (DecisionOutcome.Applied, null)],
            result.Rows.Select(row => (row.Outcome, row.Reason)));
        Assert.Equal([ProposalStatus.Failed, ProposalStatus.Failed, ProposalStatus.Applied], new[] { a, b, c }.Select(p => p.Status));
        Assert.Equal("2", shop.World["C"]);
    }

    [Fact]
    public async Task The_same_id_twice_runs_the_command_once()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");

        var result = await shop.Approve(proposal.Id, proposal.Id);

        Assert.Single(result.Rows);
        Assert.Single(FakeEntry.Applied);
    }

    [Fact]
    public async Task A_decided_proposal_is_not_waiting_and_a_missing_one_is_not_found()
    {
        var shop = Shop(("MUG-1", "old"));
        var rejected = shop.Propose("MUG-1", "new");
        rejected.Reject("Lucía", "No", Clock);

        var result = await shop.Approve(rejected.Id, ProposalId.New(Clock.GetUtcNow()));

        Assert.Equal([DecisionOutcome.NotWaiting, DecisionOutcome.NotFound], result.Rows.Select(row => row.Outcome));
        Assert.Empty(FakeEntry.Applied);
    }

    /// <summary>The process died after Approved: approving again finishes the row
    /// without the staleness check (ruling 1 of the plan).</summary>
    [Fact]
    public async Task An_approved_row_whose_result_is_unknown_can_be_approved_again()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");
        proposal.Approve("Lucía", Clock);
        shop.World["MUG-1"] = "new";   // it had run before the process died

        var result = await shop.Approve(proposal.Id);

        Assert.Equal(DecisionOutcome.Applied, Assert.Single(result.Rows).Outcome);
    }

    [Fact]
    public async Task A_command_the_registry_no_longer_knows_fails_its_row()
    {
        var shop = Shop(("MUG-1", "old"));
        var orphan = ActionProposal.Propose("Retired", "{}", "MUG-1", "old", "because",
            new ProposalProvenance(null, null, null, null, null, "lucia"), Clock);
        shop.Proposals.Add(orphan);

        var row = Assert.Single((await shop.Approve(orphan.Id)).Rows);

        Assert.Equal((DecisionOutcome.Failed, "No proposable command is named Retired."), (row.Outcome, row.Reason));
    }

    [Fact]
    public async Task Rejecting_records_the_reason_and_runs_nothing()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");

        var result = await shop.Reject("Too literal", proposal.Id);

        Assert.Equal(DecisionOutcome.Rejected, Assert.Single(result.Rows).Outcome);
        Assert.Equal(("Too literal", "old"), (proposal.Result, shop.World["MUG-1"]));
    }

    [Fact]
    public void A_rejection_needs_a_reason_and_a_batch_is_bounded()
    {
        var id = ProposalId.New(Clock.GetUtcNow());

        Assert.False(new RejectProposalsValidator().Validate(new RejectProposalsCommand([id], "  ")).IsValid);
        Assert.False(new ApproveProposalsValidator().Validate(new ApproveProposalsCommand([])).IsValid);
        Assert.False(new ApproveProposalsValidator().Validate(
            new ApproveProposalsCommand([.. Enumerable.Range(0, ProposalLimits.MaxBatch + 1).Select(_ => id)])).IsValid);
    }

    // ---------- fixtures ----------

    private static TestShop Shop(params (string Subject, string Value)[] world) => new(world.ToDictionary(w => w.Subject, w => w.Value));

    private sealed class TestShop
    {
        private readonly ServiceProvider provider;

        public TestShop(Dictionary<string, string> world)
        {
            World = world;
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IProposalRepository>(Proposals);
            services.AddSingleton<IUnitOfWork>(new CountingUnitOfWork());
            services.AddSingleton<IPrincipalAccessor>(new FixedPrincipal("Lucía"));
            services.AddScoped<ProposalScope>();
            services.AddScoped<IProposableCommand>(scope => new FakeEntry(World, scope.GetRequiredService<ProposalScope>()));
            services.AddAssist();
            provider = services.BuildServiceProvider(validateScopes: true);
        }

        public Dictionary<string, string> World { get; }

        public InMemoryProposals Proposals { get; } = new();

        public ActionProposal Propose(string subject, string value)
        {
            var proposal = ActionProposal.Propose(FakeEntry.CommandName, FakeEntry.Payload(subject, value).GetRawText(),
                subject, World[subject], "because", new ProposalProvenance(null, null, null, null, null, "lucia"), Clock);
            Proposals.Add(proposal);
            return proposal;
        }

        public Task<ProposalDecisions> Approve(params ProposalId[] ids) =>
            new ApproveProposalsHandler(Proposals, new CountingUnitOfWork(), provider.GetRequiredService<IServiceScopeFactory>(),
                    new FixedPrincipal("Lucía"), Clock)
                .HandleAsync(new ApproveProposalsCommand(ids), TestContext.Current.CancellationToken);

        public Task<ProposalDecisions> Reject(string reason, params ProposalId[] ids) =>
            new RejectProposalsHandler(Proposals, new CountingUnitOfWork(), new FixedPrincipal("Lucía"), Clock)
                .HandleAsync(new RejectProposalsCommand(ids, reason), TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 7: Run them to see them fail**

Run: `dotnet test tests/Assist.Tests --filter "FullyQualifiedName~DecideProposalsTests"`
Expected: build FAILS — `ApproveProposalsHandler`, `AddAssist` do not exist.

- [ ] **Step 8: Implement deciding** — `src/Assist/Features/DecideProposals/DecideProposals.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using Carter;
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Assist.Features.DecideProposals;

public sealed record ApproveProposalsCommand(IReadOnlyList<ProposalId> Ids) : ICommand<ProposalDecisions>;

public sealed record RejectProposalsCommand(IReadOnlyList<ProposalId> Ids, string Reason) : ICommand<ProposalDecisions>;

public enum DecisionOutcome
{
    Applied,
    Failed,
    Stale,
    Rejected,
    NotFound,

    /// <summary>Already decided: rejected, stale, applied or failed.</summary>
    NotWaiting,
}

public sealed record ProposalDecision(ProposalId Id, DecisionOutcome Outcome, string? Reason);

/// <summary>One row per distinct id, in the order asked: the review shows each
/// row's result in place.</summary>
public sealed record ProposalDecisions(IReadOnlyList<ProposalDecision> Rows);

public sealed class ApproveProposalsValidator : AbstractValidator<ApproveProposalsCommand>
{
    public ApproveProposalsValidator()
    {
        RuleFor(x => x.Ids).NotEmpty();
        RuleFor(x => x.Ids.Count).LessThanOrEqualTo(ProposalLimits.MaxBatch);
    }
}

public sealed class RejectProposalsValidator : AbstractValidator<RejectProposalsCommand>
{
    public RejectProposalsValidator()
    {
        RuleFor(x => x.Ids).NotEmpty();
        RuleFor(x => x.Ids.Count).LessThanOrEqualTo(ProposalLimits.MaxBatch);
        RuleFor(x => x.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("A rejection says why: the reason is what a prompt change is judged against.")
            .MaximumLength(1000);
    }
}

/// <summary>
/// Approves proposals and runs their commands in the same request, as the person
/// approving (spec decision 9). Each row has its own DI scope: its command's
/// unit of work is its own, so a row that throws leaves nothing half-written for
/// the next one to save, and its scope names the proposal for the audit row.
/// </summary>
public sealed class ApproveProposalsHandler(
    IProposalRepository proposals,
    IUnitOfWork unitOfWork,
    IServiceScopeFactory scopes,
    IPrincipalAccessor principals,
    TimeProvider clock) : ICommandHandler<ApproveProposalsCommand, ProposalDecisions>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Assist);

    public async Task<ProposalDecisions> HandleAsync(ApproveProposalsCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("assist.approve");
        var approver = Decider.Of(principals);
        var rows = new List<ProposalDecision>();

        foreach (var id in command.Ids.Distinct())
            rows.Add(await ApproveAsync(id, approver, cancellationToken));

        activity?.SetTag("assist.rows", rows.Count);
        activity?.SetTag("assist.applied", rows.Count(row => row.Outcome == DecisionOutcome.Applied));
        return new ProposalDecisions(rows);
    }

    private async Task<ProposalDecision> ApproveAsync(ProposalId id, string approver, CancellationToken cancellationToken)
    {
        var proposal = await proposals.FindAsync(id, cancellationToken);
        if (proposal is null)
            return new ProposalDecision(id, DecisionOutcome.NotFound, null);
        if (proposal.Status is not (ProposalStatus.Proposed or ProposalStatus.Approved))
            return new ProposalDecision(id, DecisionOutcome.NotWaiting, null);

        await using var scope = scopes.CreateAsyncScope();
        var entry = scope.ServiceProvider.GetRequiredService<ProposableCommands>().Find(proposal.Command);
        var payload = JsonDocument.Parse(proposal.Payload).RootElement;

        if (proposal.Status == ProposalStatus.Proposed)
        {
            // Stale when what the command would change is not what was proposed
            // against. An entry that no longer exists cannot say, and fails below.
            if (entry is not null && await entry.NowAsync(payload, cancellationToken) != proposal.Before)
            {
                proposal.MarkStale(approver, clock);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new ProposalDecision(id, DecisionOutcome.Stale, null);
            }

            // Saved before running: if the process dies now, the list shows
            // "approved, result unknown" and approving again finishes it.
            proposal.Approve(approver, clock);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var application = await ApplyAsync(entry, proposal, payload, scope.ServiceProvider, cancellationToken);
        if (application.Applied)
            proposal.RecordApplied();
        else
            proposal.RecordFailed(application.Reason!);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProposalDecision(id, application.Applied ? DecisionOutcome.Applied : DecisionOutcome.Failed, application.Reason);
    }

    private static async Task<ProposalApplication> ApplyAsync(
        IProposableCommand? entry, ActionProposal proposal, JsonElement payload, IServiceProvider scope, CancellationToken cancellationToken)
    {
        if (entry is null)
            return ProposalApplication.Failed($"No proposable command is named {proposal.Command}.");

        scope.GetRequiredService<ProposalScope>().Enter(proposal.Id);
        try
        {
            return await entry.ApplyAsync(payload, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // One row's failure stops none of the others; its reason is the row's.
            return ProposalApplication.Failed(failure.Message);
        }
    }
}

public sealed class RejectProposalsHandler(
    IProposalRepository proposals,
    IUnitOfWork unitOfWork,
    IPrincipalAccessor principals,
    TimeProvider clock) : ICommandHandler<RejectProposalsCommand, ProposalDecisions>
{
    public async Task<ProposalDecisions> HandleAsync(RejectProposalsCommand command, CancellationToken cancellationToken)
    {
        var rejecter = Decider.Of(principals);
        var rows = new List<ProposalDecision>();

        foreach (var id in command.Ids.Distinct())
        {
            var proposal = await proposals.FindAsync(id, cancellationToken);
            if (proposal is null)
            {
                rows.Add(new ProposalDecision(id, DecisionOutcome.NotFound, null));
                continue;
            }
            if (proposal.Status != ProposalStatus.Proposed)
            {
                rows.Add(new ProposalDecision(id, DecisionOutcome.NotWaiting, null));
                continue;
            }

            proposal.Reject(rejecter, command.Reason, clock);
            rows.Add(new ProposalDecision(id, DecisionOutcome.Rejected, command.Reason));
        }

        if (rows.Any(row => row.Outcome == DecisionOutcome.Rejected))
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProposalDecisions(rows);
    }
}

/// <summary>Whoever the request authenticated as, never a value from the body —
/// so the proposal and the audit row name the same person.</summary>
internal static class Decider
{
    public static string Of(IPrincipalAccessor principals) =>
        principals.Current.DisplayName ?? principals.Current.Subject ?? "unknown";
}

// ---------- The wire ----------

public sealed record ApproveProposalsRequest(IReadOnlyList<Guid> Ids);

public sealed record RejectProposalsRequest(IReadOnlyList<Guid> Ids, string Reason);

public sealed record ProposalDecisionView(string Id, DecisionOutcome Outcome, string? Reason);

public sealed record ProposalDecisionsResponse(IReadOnlyList<ProposalDecisionView> Rows);

public sealed class DecideProposalsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/steward/proposals/approve",
                async Task<Ok<ProposalDecisionsResponse>> (ApproveProposalsRequest body, ICommandDispatcher dispatcher, CancellationToken ct) =>
                    View(await dispatcher.SendAsync(new ApproveProposalsCommand([.. body.Ids.Select(id => new ProposalId(id))]), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Steward")
            .WithName("ApproveProposals");

        app.MapPost("/api/steward/proposals/reject",
                async Task<Ok<ProposalDecisionsResponse>> (RejectProposalsRequest body, ICommandDispatcher dispatcher, CancellationToken ct) =>
                    View(await dispatcher.SendAsync(new RejectProposalsCommand([.. body.Ids.Select(id => new ProposalId(id))], body.Reason), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Steward")
            .WithName("RejectProposals");
    }

    private static Ok<ProposalDecisionsResponse> View(ProposalDecisions decisions) =>
        TypedResults.Ok(new ProposalDecisionsResponse(
            [.. decisions.Rows.Select(row => new ProposalDecisionView(row.Id.ToString(), row.Outcome, row.Reason))]));
}
```

`src/Assist/AssistServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Assist;

public static class AssistServiceCollectionExtensions
{
    /// <summary>The registry, scoped like the entries it gathers: an entry reads
    /// through its context's repository, which is scoped to the request.</summary>
    public static IServiceCollection AddAssist(this IServiceCollection services)
    {
        services.AddScoped<ProposableCommands>();
        return services;
    }
}
```

- [ ] **Step 9: Run the tests**

Run: `dotnet test tests/Assist.Tests`
Expected: PASS. Then do the hand mutation for the per-row isolation: in `ApplyAsync`, change `catch (Exception failure) when (failure is not OperationCanceledException)` to `catch (InvalidOperationException failure)`; run `DecideProposalsTests` — `One_refusal_and_one_exception_fail_their_rows_and_stop_none_of_the_others` must go red; restore it and see green. Note the mutation for the commit message.

- [ ] **Step 10: Commit**

```bash
git add src/Assist tests/Assist.Tests
git commit -m "feat(assist): propose, approve and reject — each approved row runs alone, as the approver (S-1)

Hand mutation: catching only InvalidOperationException in ApplyAsync turns
One_refusal_and_one_exception_fail_their_rows_and_stop_none_of_the_others red."
```

---

### Task 7: Persistence, the API, and the flow against Postgres

**Files:**
- Create: `src/Persistence/Configurations/AssistConfiguration.cs`, `src/Persistence/EfProposalRepository.cs`, the migration `AddAssistProposals` under `src/Persistence/Migrations/`
- Modify: `src/Persistence/ElGuerre.Tendero.Persistence.csproj` (reference Assist), `src/Persistence/TenderoDbContext.cs` (`DbSet<ActionProposal> Proposals`), `src/Persistence/PersistenceServiceCollectionExtensions.cs` (`IProposalRepository`) — the audit column and `ProposalId`'s conversion are already there from Task 1
- Modify: `src/Api/ElGuerre.Tendero.Api.csproj` (reference Assist), `src/Api/Program.cs` (`AddTenderoCqrs` gets the Assist assembly; `builder.Services.AddAssist();` beside `AddKnowledge`)
- Modify: `src/Accounts/Features/ListAuditEntries/ListAuditEntries.cs` (`AuditEntryView.ProposalId`)
- Modify: `frontend/apps/backoffice/public/i18n/en.json`, `es.json` (four audit command names)
- Regenerate: `docs/openapi/tendero.json`, `frontend/libs/shared/api/src/generated/schema.ts`
- Test: `tests/Integration.Tests/ProposalApprovalTests.cs` (and add an Assist project reference to `tests/Integration.Tests/ElGuerre.Tendero.Integration.Tests.csproj` if Persistence's does not flow it)

**Interfaces:**
- Consumes: everything above.
- Produces: table `assist.ActionProposals`; column `audit.AuditEntries.proposal`; `AuditEntryView(…, DateTimeOffset At, string? ProposalId)`; the API serving both decision endpoints and the translation.

- [ ] **Step 1: Write the failing integration test** — `tests/Integration.Tests/ProposalApprovalTests.cs`:

```csharp
using System.Text.Json;
using ElGuerre.Tendero.Assist;
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Features.DecideProposals;
using ElGuerre.Tendero.Assist.Features.ProposeActions;
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
        var product = await FirstProductAsync(factory, ct);

        var proposal = await ProposeAsync(provider, product, "From the model", ct);
        await SendAsync(provider, new SetProductTranslationCommand(product, "en", "From a person", null), ct);
        var decisions = await SendAsync(provider, new ApproveProposalsCommand([proposal]), ct);

        Assert.Equal(DecisionOutcome.Stale, Assert.Single(decisions.Rows).Outcome);
        await using var reread = factory.Create();
        Assert.Equal("From a person", (await reread.Products.SingleAsync(p => p.Id == product, ct)).Name.Values["en"]);
    }

    // ---------- fixtures ----------

    private async Task<(ServiceProvider, PostgresDatabase)> ShopAsync(string name, CancellationToken ct)
    {
        var factory = await postgres.CreateDatabaseAsync(name);
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var provider = Compose(factory.ConnectionString);
        await SendAsync(provider, new ImportProductsCommand("seed"), ct);
        return (provider, factory);
    }

    private static async Task<ProductId> FirstProductAsync(PostgresDatabase factory, CancellationToken ct)
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
```

Two names here are guesses to confirm against `tests/Integration.Tests/ApprovedClaimToCatalogTests.cs` and `PostgresFixture.cs` before running: the type `CreateDatabaseAsync` returns (written `PostgresDatabase` above), and whether `AddCatalog` needs `AddHybridSearch` and an `IProductIndexer` to import (that test registers both; copy its `NoIndex` if the import needs one). Use the real names; the test's assertions stay as written.

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~ProposalApprovalTests"`
Expected: build FAILS — `Proposals` is not a `DbSet`. (Without Docker the tests skip; Docker Desktop is needed for this task.)

- [ ] **Step 3: Map it** — `src/Persistence/Configurations/AssistConfiguration.cs`:

```csharp
using ElGuerre.Tendero.Assist.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>Assist's one table, in its own schema like every context's (ADR 0052).</summary>
internal sealed class ActionProposalConfiguration : IEntityTypeConfiguration<ActionProposal>
{
    public void Configure(EntityTypeBuilder<ActionProposal> builder)
    {
        builder.ToTable("ActionProposals", "assist");

        builder.HasKey(proposal => proposal.Id);
        builder.Property(proposal => proposal.Id).ValueGeneratedNever();

        builder.Property(proposal => proposal.Command).HasMaxLength(100).IsRequired();

        // jsonb, as the audit's payload: the command and its snapshot are read
        // back field by field by the review.
        builder.Property(proposal => proposal.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(proposal => proposal.Before).HasColumnType("jsonb");

        builder.Property(proposal => proposal.Subject).HasMaxLength(200).IsRequired();
        builder.Property(proposal => proposal.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(proposal => proposal.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(proposal => proposal.DecidedBy).HasMaxLength(200);
        builder.Property(proposal => proposal.Result).HasMaxLength(1000);
        builder.Property(proposal => proposal.ProposedAt).IsRequired();

        builder.ComplexProperty(proposal => proposal.Provenance, provenance => provenance.ToJson("provenance"));

        // What the Proposals list asks (S-4): one status, the newest first.
        builder.HasIndex(proposal => new { proposal.Status, proposal.ProposedAt }).IsDescending(false, true);

        builder.Ignore(proposal => proposal.DomainEvents);
    }
}
```

`src/Persistence/EfProposalRepository.cs`:

```csharp
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Persistence;

internal sealed class EfProposalRepository(TenderoDbContext context) : IProposalRepository
{
    public async Task<ActionProposal?> FindAsync(ProposalId id, CancellationToken cancellationToken = default) =>
        await context.Proposals.FindAsync([id], cancellationToken);

    public void Add(ActionProposal proposal) => context.Proposals.Add(proposal);
}
```

Then:
- `TenderoDbContext`: `public DbSet<ActionProposal> Proposals => Set<ActionProposal>();` beside `Claims`.
- `PersistenceServiceCollectionExtensions`: `services.AddScoped<IProposalRepository, EfProposalRepository>();` beside `IKnowledgeRepository`.
- `ElGuerre.Tendero.Persistence.csproj`: `<ProjectReference Include="../Assist/ElGuerre.Tendero.Assist.csproj" />`.

- [ ] **Step 4: Add the migration**

Run: `dotnet tool run dotnet-ef migrations add AddAssistProposals --project src/Persistence`
Expected: a migration that creates `assist."ActionProposals"` (snake-cased columns, `provenance` jsonb) with its index — and nothing else. If it touches any other table, stop: the model snapshot was stale before this task.

- [ ] **Step 5: Wire the API** — `src/Api/ElGuerre.Tendero.Api.csproj`: reference `../Assist/ElGuerre.Tendero.Assist.csproj`. `src/Api/Program.cs`: add `typeof(ApproveProposalsCommand).Assembly` to `AddTenderoCqrs(…)`, and `builder.Services.AddAssist();` on the line after `builder.Services.AddKnowledge(builder.Configuration);` (with the two `using`s).

- [ ] **Step 6: Put the proposal on the audit's wire** — in `ListAuditEntries.cs` add a last parameter to `AuditEntryView`:

```csharp
    DateTimeOffset At,
    /// <summary>The approved proposal the command ran for (phase 12), or null.</summary>
    string? ProposalId);
```

and `entry.Proposal?.ToString()` as the last argument in `View`.

- [ ] **Step 7: Name Assist's three commands in the backoffice** — in `frontend/apps/backoffice/public/i18n/en.json` and `es.json`, under `audit.commands`, in the file's key order (`AuditCommandNamesTests` sees them once Task 8 puts Assist in `Solution.All`):

| Key | en | es |
|---|---|---|
| `ApproveProposalsCommand` | Approve proposals | Aprobar propuestas |
| `ProposeActionsCommand` | Propose changes | Proponer cambios |
| `RejectProposalsCommand` | Reject proposals | Rechazar propuestas |

- [ ] **Step 8: Run the integration tests**

Run: `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~ProposalApprovalTests|FullyQualifiedName~AuditPagingTests|FullyQualifiedName~MigrationBaselineTests"`
Expected: PASS (the Testcontainers Postgres is the suite's own, never the author's `postgres` container).

- [ ] **Step 9: Regenerate the contract and the frontend's types**

Run: `UPDATE_OPENAPI=1 dotnet test tests/Api.Tests` then, from `frontend/`, `npm run generate:api-types`
Expected: `docs/openapi/tendero.json` gains `ApproveProposals`, `RejectProposals`, `DecisionOutcome` and `AuditEntryView.proposalId`; `git diff --stat` shows only those two files changed by the commands. Then `dotnet test tests/Api.Tests` (without the variable) — PASS — and, from `frontend/`, `npx vitest run` — PASS.

- [ ] **Step 10: Commit**

```bash
git add src tests/Integration.Tests docs/openapi frontend
git commit -m "feat(assist): proposals stored, decided over the API, and audited under their id (S-1)"
```

---

### Task 8: The rules that hold it

**Files:**
- Modify: `tests/Architecture.Tests/ElGuerre.Tendero.Architecture.Tests.csproj` (reference Assist), `tests/Architecture.Tests/Solution.cs`, `tests/Architecture.Tests/ArchitectureRules.cs`, `tests/Architecture.Tests/DomainModelDocument.cs`
- Modify: `tests/Api.Tests/EndpointAuthorizationTests.cs`
- Regenerate: `docs/domain-model.md`

**Interfaces:**
- Consumes: `Solution.Assist`, `IProposableCommand`, `ProposableCommand<,,>`.

- [ ] **Step 1: Register the context** — `Solution.cs`: `public static readonly Assembly Assist = typeof(ActionProposal).Assembly;` (with `using ElGuerre.Tendero.Assist.Domain;`); add `Assist` to `Contexts` and to `All`. `DomainModelDocument.cs`: add `("Assist", Solution.Assist),` to its `Contexts`, in the position the document should show it (after Knowledge).

- [ ] **Step 2: Write the two new rules** — in `ArchitectureRules.cs`, after `Accounts_knows_only_the_shared_kernel`:

```csharp
    /// <summary>
    /// Assist runs other contexts' commands and references none of them: it reaches
    /// them only through <see cref="IProposableCommand"/>, which each context
    /// implements for its own commands (ADR 0052). A reference to Catalog here
    /// would be Assist learning what a product is.
    /// </summary>
    [Fact]
    public void Assist_knows_only_the_shared_kernel()
    {
        var sharedKernel = Solution.SharedKernel.GetName().Name;

        var contexts = Solution.Assist.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("ElGuerre.Tendero.", StringComparison.Ordinal) && name != sharedKernel)
            .ToArray();

        Assert.True(contexts.Length == 0,
            $"Assist references {string.Join(", ", contexts)}. It may reference only {sharedKernel}: "
            + "it runs commands through IProposableCommand, never through another context's types.");
    }

    /// <summary>
    /// "Approving or rejecting — a proposal or a claim — is never proposable" (the
    /// Steward spec): no AI approves what an AI proposed. Every entry derives the
    /// typed base, so the command it runs can be read, and none runs a decision.
    /// </summary>
    [Fact]
    public void No_proposable_command_approves_rejects_or_reviews()
    {
        var entries = Solution.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && typeof(IProposableCommand).IsAssignableFrom(type))
            .ToList();

        Assert.True(entries.Count >= 2, $"Only {entries.Count} proposable commands were found; the scan is not seeing Catalog's.");

        var offending = entries
            .Select(type => (Type: type, Command: CommandOf(type)))
            .Where(entry => entry.Command is null
                            || entry.Command.Name.StartsWith("Approve", StringComparison.Ordinal)
                            || entry.Command.Name.StartsWith("Reject", StringComparison.Ordinal)
                            || entry.Command.Name.StartsWith("Review", StringComparison.Ordinal))
            .Select(entry => $"{entry.Type.FullName} runs {entry.Command?.Name ?? "a command it does not declare"}")
            .ToList();

        Assert.True(offending.Count == 0, "Proposable commands that decide, or that hide their command:\n" + string.Join('\n', offending));

        static Type? CommandOf(Type type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ProposableCommand<,,>))
                    return current.GetGenericArguments()[1];
            return null;
        }
    }
```

- [ ] **Step 3: See the second rule fail by mutation, then pass** — temporarily add to `WithdrawProductProposal.cs` a class `internal sealed class ReviewProductProposal(IServiceProvider s) : ProposableCommand<WithdrawProductPayload, ReviewProductCommand, ReviewProductResult>(s)` (with the members stubbed) — or, if `ReviewProduct`'s command and result names differ, rename `WithdrawProductProposal`'s generic command argument to any command whose name starts with `Review`. Run: `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~No_proposable_command"`. Expected: FAIL naming it. Remove the mutation; Expected: PASS. Then mutate `Assist_knows_only_the_shared_kernel` the same way: add a `ProjectReference` to Catalog in `ElGuerre.Tendero.Assist.csproj` and a `typeof(ElGuerre.Tendero.Catalog.Domain.Product)` use in `ProposableCommands.cs` (an unused reference is trimmed from the assembly's references) → red; remove → green.

- [ ] **Step 4: The endpoints' policies** — in `EndpointAuthorizationTests.cs` (the translation's route is already there from Task 3):

```csharp
    [Theory]
    [InlineData("/api/steward/proposals/approve")]
    [InlineData("/api/steward/proposals/reject")]
    public void Stewards_proposals_are_decided_by_the_shopkeeper(string route) => Writes_to_the_catalogue_require_the_shopkeeper(route);
```

- [ ] **Step 5: Regenerate the domain model and run everything**

Run: `UPDATE_DOMAIN_MODEL=1 dotnet test --project tests/Architecture.Tests`, then `dotnet build -warnaserror`, then `dotnet test`
Expected: `docs/domain-model.md` gains an Assist section with `ActionProposal` and its state machine; the build is clean; every test passes (integration tests skip only if Docker is down — run them with Docker up).

- [ ] **Step 6: Commit**

```bash
git add tests/Architecture.Tests tests/Api.Tests docs/domain-model.md
git commit -m "test(architecture): Assist sees only the kernel, and no proposable command decides (S-1)

Hand mutations: an entry whose command is named Review* turns
No_proposable_command_approves_rejects_or_reviews red; a Catalog reference
from Assist turns Assist_knows_only_the_shared_kernel red."
```

---

### Task 9: The decision on record

**Files:**
- Create: `docs/adr/0052-assist-is-a-context.md`
- Modify: `docs/adr/README.md`, `CLAUDE.md`, `docs/delivery-plan.md`, `docs/specs/steward.md` (S-1 *as built*), `docs/blog/notebook.md`

- [ ] **Step 1: Write ADR 0052** — `docs/adr/0052-assist-is-a-context.md`, in the shape of ADR 0048:

```markdown
# 0052 — Assist is a context: a proposal has a life of its own

**Status**: accepted (the author approved the Steward spec on 2026-10-02, which proposed it)

## Context

CLAUDE.md said `Mcp`, `Ucp` and `Assist` are not contexts because they own no
state. Steward's proposals changed that for Assist: an `ActionProposal` has a
lifecycle (`Proposed → Approved → Applied | Failed`, `Rejected`, `Stale`), a
writer (the proposal tools), and actors of its own (whoever asked, whoever
decided). ADR 0014 says that is how a context earns its boundary.

A proposal runs another context's command. If Assist referenced Catalog to do
it, every new proposable command would add a reference, and Assist would learn
what a product is.

## Decision

1. `Assist` is the seventh context. It owns `ActionProposal`, references the
   SharedKernel and nothing else, and an architecture rule computes that.
2. A context makes one of its commands proposable by implementing
   `IProposableCommand` — through `ProposableCommand<TPayload, TCommand, TResult>`
   — and registering it. The entry checks a payload with the command's own
   validators, snapshots what the command would change, and runs it through
   `ICommandDispatcher`, so a proposal goes through the validation, the audit and
   the handler a person's click does.
3. Approving runs each command in the same request as the approving person, in
   a DI scope of its own, and the audit row of that command carries the
   proposal's id (`AuditEntry.Proposal`).
4. Approving or rejecting is never proposable; a rule reads every entry's command.

## Consequences

- Adding a proposable command is a class and a registration in its context.
- The audit can say who proposed a change and who approved it, without joining.
- A proposal stores values (the command's name, its payload, the subject's
  code), never a reference into another context (invariant 1).
- Retrying an approval whose result is unknown skips the staleness check, which
  relies on the commands being idempotent; a proposable command that is not
  idempotent would need this decision reopened.
```

Add its line to `docs/adr/README.md`'s index in the format the other lines use.

- [ ] **Step 2: Update CLAUDE.md** — three edits:
  - The root namespace list: add `` `ElGuerre.Tendero.Assist` `` after `ElGuerre.Tendero.Search`.
  - Invariant 1's list: "Today: `Catalog`, `Pricing`, `Inventory`, `Ordering`, `Accounts`, `Knowledge` (…), `Assist` (phase 12: proposals a model makes and a person decides; it runs other contexts' commands only through `IProposableCommand` — ADR 0052)."
  - The ADR 0014 box: "`Mcp` and `Ucp` are not contexts; they own no state. `Assist` became one in phase 12 (ADR 0052): a proposal has a lifecycle, a writer and actors of its own."

- [ ] **Step 3: Update the board and the spec** — `docs/delivery-plan.md`, phase 12: mark `P12-4` and `P12-5` done with "(S-1, 2026-10-02)" and note under the phase that S-1 is built; `docs/specs/steward.md`: under S-1, a short *As built* paragraph naming the six rulings of this plan.

- [ ] **Step 4: The notebook** — `docs/blog/notebook.md`, a new entry at the end: `### 2026-10-02 · Steward S-1: a proposal is a command a person runs` — what was built, the numbers (tests added per project, the migration), the per-row scope and why (the dirty `DbContext`), the retry ruling, and the line for the article: *the audit row that names the proposal is what turns "the AI changed it" into "Lucía approved what Steward proposed"*. End with `Book:` and `Blog:` pointers (chapter 30; the phase 12 article).

- [ ] **Step 5: Run the whole gate once more and commit**

Run: `dotnet build -warnaserror` then `dotnet test`
Expected: clean and green.

```bash
git add docs CLAUDE.md
git commit -m "docs(assist): ADR 0052, Assist as the seventh context, and S-1 on the board (S-1)"
```
