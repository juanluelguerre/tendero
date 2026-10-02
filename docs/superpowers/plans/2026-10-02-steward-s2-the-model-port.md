# Steward S-2 · The model port — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One way to reach a model — profiles in configuration, registered as keyed `IChatClient`s, every call metered into `telemetry.AiInvocations` without holding the request — used today by the claim extractor, and by Steward from S-3; plus the `steward-model` container behind `Tendero__Steward`, sized from a measurement.

**Architecture:** A new project, `ElGuerre.Tendero.AI`, is not a context (it owns no aggregate, like `Mcp` and `Ucp`): it holds the shared chat factory moved out of Knowledge (`AI.Chat`, the only place besides Search's embeddings and Knowledge's scorer that may touch the OpenAI SDK), the profile options and their keyed registration, a metering `DelegatingChatClient`, the `AiInvocation` record with its writer port, an in-process channel and the worker that drains it. Persistence writes the rows on its own context, as it writes the audit. The pipeline per profile is OpenTelemetry (GenAI conventions) → metering → the model; S-3 adds function invocation outside it. The local profile points at a new llama.cpp container that serves the extractor's GGUF from the same volume with a slot of its own.

**Tech Stack:** .NET 11 preview, Microsoft.Extensions.AI (+ .OpenAI) and ONNX Runtime GenAI (already in the solution), System.Threading.Channels, EF Core + Npgsql, Aspire AppHost, llama.cpp server `v0.5.0`.

**Spec:** `docs/specs/steward.md` — *The models*, *AI observability*, S-2 in *Building it, in order*.

## Global Constraints

- "One port, `IChatClient`." Providers are profiles in configuration under `Steward:Models:<name>` with `Kind` (`OpenAICompatible | OnnxInProcess`), `Endpoint`, `Model`, `Auth` (`None | Bearer`), `ApiKey` ("an Aspire secret parameter, never in the repository"), `InputPrice`, `OutputPrice` (€ per million tokens, 0 for local), `ContextTokens`; `Steward:Model` names the profile that answers, `Steward:Fallback` the one tried when it does not. "A new provider is an entry, not a class."
- The pipeline, "the same for every profile": function invocation → OpenTelemetry (GenAI conventions) → "a metering client that records tokens, cost and latency into a channel, drained by a worker into `AiInvocations` without holding the request" → the model.
- `AiInvocations`: "one row per model call, Steward's and the claim extractor's — trace, conversation, operation (`steward.turn`, `knowledge.extract`), profile and model, prompt version, input and output tokens, cost in micro-euros at the profile's price at that moment, latency, outcome".
- "The local model gets its own container, the same GGUF as the claim extractor (Qwen3.5-4B Q8, ADR 0031), with its own slot … 8,192 tokens is an estimate, not a measurement … It starts with `Tendero__Steward=true`, explicit-start otherwise, sized from a measurement like every container."
- Measure first, local first: no hosted profile is called in S-2 (their tokens are paid; S-7).
- No new NuGet or npm dependency. `dotnet build -warnaserror` clean. No attribution trailers.
- Every container has `--memory` and `--cpus` sized from a measurement; heavy jobs one at a time; stop and remove my containers when done; never touch the author's (`postgres`, `elastic*`, `redis`, …).

## Rulings already taken

1. **`ElGuerre.Tendero.AI` is a new project, not a context and not the kernel.** The kernel must not carry the OpenAI SDK or ONNX; Knowledge cannot be the shared place, because Steward (Assist) must not depend on another context. It references SharedKernel only, an architecture rule says so, and an ADR (0053) records it. Cost if wrong: one more project to move from.
2. **`ChatYesNoSupportScorer` stays in `Knowledge.Chat`** — it reads OpenAI's raw log-probabilities and serves only Knowledge. The OpenAI fence becomes `Search.Embeddings`, `Knowledge.Chat` and `AI.Chat`.
3. **A profile's `Endpoint` is the full OpenAI base URL, `/v1` included** (Gemini's is `…/v1beta/openai/`). The old `Create(endpoint, model)` keeps appending `v1` for the extractor and SearchEval; profiles use `CreateAt(baseUrl, model, apiKey, timeout)`.
4. **A profile has a `TimeoutSeconds` (default 120)** the spec does not list: the contract suite's "a timeout surfaced as such" needs one per profile, and a hosted profile and the local CPU one need different ones.
5. **The call's operation, conversation and prompt version travel in `ChatOptions.AdditionalProperties`** (`tendero.operation`, `tendero.conversation`, `tendero.prompt_version`), set by `ModelCall.Options(...)`. Explicit at the call site, no ambient state. A call with no operation is metered as `unknown`.
6. **The channel is bounded (10,000) and drops the newest when full**, counting the drop on a metric: a row lost is better than a request held, which is the spec's own trade.
7. **`AiOutcome` is `Ok | Error | Timeout` in S-2;** `FallbackAnswered` arrives with the fallback in S-3.
8. **The contract suite (tool-call round trip, usage reported, timeout surfaced) runs against the `steward-model` container**, `Category=Model`, skipped when it is not running — a CI runner cannot hold a 4B model. The `Scripted` kind joins it in S-3.

## Review Focus

1. **A model call that throws** → a row with outcome `Error` (or `Timeout`) is still written, and the exception still reaches the caller unchanged. Pinned in Task 3.
2. **A model that reports no usage** (some servers omit it) → the row has 0 tokens and 0 cost, not a crash. Pinned in Task 3.
3. **The database is down when the worker drains** → the worker logs and keeps draining later rows; it never stops the host. Pinned in Task 5.
4. **The channel is full** → `Record` returns at once without blocking the request, and the drop is counted. Pinned in Task 3.
5. **A profile with a missing `Endpoint` or `Model`, or `Bearer` without a key** → the host fails at start with a message naming the profile, not at the first call. Pinned in Task 4.

---

### Task 1: The `AI` project, and the chat factory moved into it

**Files:**
- Create: `src/AI/ElGuerre.Tendero.AI.csproj`, `src/AI/stryker-config.json`
- Move: `src/Knowledge/Chat/InProcessOnnxChat.cs` → `src/AI/Chat/InProcessOnnxChat.cs` (namespace `ElGuerre.Tendero.AI.Chat`)
- Move: the `OpenAICompatibleChat` class out of `src/Knowledge/Chat/OpenAICompatibleChat.cs` → `src/AI/Chat/OpenAICompatibleChat.cs` (the file in Knowledge keeps `ChatYesNoSupportScorer` and is renamed `ChatYesNoSupportScorer.cs`)
- Modify: `src/Knowledge/ElGuerre.Tendero.Knowledge.csproj` (reference Ai; drop `Microsoft.ML.OnnxRuntimeGenAI`), `src/Knowledge/KnowledgeServiceCollectionExtensions.cs`, `tools/SearchEval/Suites/ClaimsSuite.cs`, `tools/SearchEval/Suites/SupportSuite.cs` (usings), `Tendero.slnx`, `src/SharedKernel/TelemetrySources.cs` (`AI`)
- Create: `tests/AI.Tests/ElGuerre.Tendero.AI.Tests.csproj`, `tests/AI.Tests/OpenAICompatibleChatTests.cs`

**Interfaces:**
- Produces: `ElGuerre.Tendero.AI.Chat.OpenAICompatibleChat.Create(Uri endpoint, string model, PipelineTransport? transport = null)` (unchanged behaviour: appends `v1`) and `CreateAt(Uri baseUrl, string model, string? apiKey = null, TimeSpan? timeout = null, PipelineTransport? transport = null)`; `ElGuerre.Tendero.AI.Chat.InProcessOnnxChat.Create(string modelDirectory)`; `TelemetrySources.AI = "ElGuerre.Tendero.AI"`.

- [ ] **Step 1: Create the projects.** `src/AI/ElGuerre.Tendero.AI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- How Tendero reaches a model (phase 12, ADR 0053). Not a context: it owns no
       aggregate. The chat factory every model user shares, profiles from
       configuration as keyed IChatClients, and the metering that writes one
       telemetry row per call. The OpenAI SDK and ONNX Runtime stay inside
       ElGuerre.Tendero.AI.Chat; everything else sees IChatClient. -->
  <PropertyGroup>
    <RootNamespace>ElGuerre.Tendero.AI</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.AI" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" />
    <PackageReference Include="Microsoft.ML.OnnxRuntimeGenAI" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../SharedKernel/ElGuerre.Tendero.SharedKernel.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="ElGuerre.Tendero.AI.Tests" />
  </ItemGroup>

</Project>
```

Check `Directory.Packages.props` for `Microsoft.Extensions.Hosting.Abstractions`; if it is not centrally versioned, use `<FrameworkReference Include="Microsoft.AspNetCore.App" />` instead (as Knowledge does) — no new package either way. `tests/AI.Tests/ElGuerre.Tendero.AI.Tests.csproj` and `src/AI/stryker-config.json`: copies of Assist's with `Assist` → `AI`. Add both projects to `Tendero.slnx` by hand, in their folders (`dotnet sln add` did not write the file on 2026-10-02). `TelemetrySources`: `public const string AI = "ElGuerre.Tendero.AI";`, added to `All`.

- [ ] **Step 2: Write the failing test** — `tests/AI.Tests/OpenAICompatibleChatTests.cs`:

```csharp
using System.ClientModel.Primitives;
using System.Net;
using ElGuerre.Tendero.AI.Chat;
using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>Where a request goes and what it carries, read off the wire: a profile's
/// base URL is used as given, and a key travels as a bearer token.</summary>
public sealed class OpenAICompatibleChatTests
{
    [Fact]
    public async Task A_profile_base_url_is_used_as_given_with_its_key()
    {
        var wire = new RecordingHandler();
        var chat = OpenAICompatibleChat.CreateAt(
            new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"), "gemini-2.5-flash", "secret",
            transport: new HttpClientPipelineTransport(new HttpClient(wire)));

        await chat.GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", wire.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer secret", wire.Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task The_extractors_endpoint_still_gets_v1()
    {
        var wire = new RecordingHandler();
        var chat = OpenAICompatibleChat.Create(new Uri("http://localhost:38083"), "qwen", new HttpClientPipelineTransport(new HttpClient(wire)));

        await chat.GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("http://localhost:38083/v1/chat/completions", wire.Request!.RequestUri!.ToString());
    }

    internal sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"id":"1","object":"chat.completion","created":0,"model":"m",
                     "choices":[{"index":0,"message":{"role":"assistant","content":"hola"},"finish_reason":"stop"}],
                     "usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}
                    """, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
```

- [ ] **Step 3: Run it** — `dotnet test tests/AI.Tests`. Expected: build FAILS (`ElGuerre.Tendero.AI.Chat` does not exist).

- [ ] **Step 4: Move and extend.** `git mv src/Knowledge/Chat/InProcessOnnxChat.cs src/AI/Chat/InProcessOnnxChat.cs` and change its namespace to `ElGuerre.Tendero.AI.Chat`. Create `src/AI/Chat/OpenAICompatibleChat.cs` with the class moved from Knowledge (namespace `ElGuerre.Tendero.AI.Chat`, keep the fence comment) and the new method:

```csharp
    /// <summary>A profile's endpoint is the provider's OpenAI base URL, /v1 included,
    /// used as given (Gemini's is …/v1beta/openai/).</summary>
    public static IChatClient CreateAt(
        Uri baseUrl, string model, string? apiKey = null, TimeSpan? timeout = null, PipelineTransport? transport = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        var options = new OpenAIClientOptions { Endpoint = baseUrl, NetworkTimeout = timeout ?? TimeSpan.FromMinutes(10) };
        if (transport is not null)
            options.Transport = transport;

        return new ChatClient(model, new ApiKeyCredential(apiKey ?? UnusedApiKey), options).AsIChatClient();
    }
```

and make `Create(endpoint, model, transport)` call `CreateAt(new Uri(endpoint, "v1"), model, transport: transport)`. Rename the Knowledge file to `ChatYesNoSupportScorer.cs` (`git mv`), leaving only that class. Knowledge's csproj references `../AI/ElGuerre.Tendero.AI.csproj` and drops the ONNX package line; fix the `using`s in `KnowledgeServiceCollectionExtensions.cs` and both SearchEval suites (`using ElGuerre.Tendero.AI.Chat;`; SearchEval's csproj references Ai if Knowledge's reference does not flow).

- [ ] **Step 5: Run** — `dotnet build -warnaserror` then `dotnet test tests/AI.Tests tests/Knowledge.Tests tests/SearchEval.Tests`. Expected: clean; PASS.

- [ ] **Step 6: Commit** — `git commit -m "refactor(ai): one chat factory every model user shares, out of Knowledge (S-2)"`.

---

### Task 2: Profiles, as options

**Files:**
- Create: `src/AI/ModelProfile.cs`
- Test: `tests/AI.Tests/ModelProfileTests.cs`

**Interfaces:**
- Produces: `enum ModelKind { OpenAICompatible, OnnxInProcess }`; `enum ModelAuth { None, Bearer }`; `sealed class ModelProfile { ModelKind Kind; Uri? Endpoint; string? Model; ModelAuth Auth; string? ApiKey; string? Directory; decimal InputPrice; decimal OutputPrice; int ContextTokens = 8192; int TimeoutSeconds = 120; IReadOnlyList<string> Problems(string name); long CostMicroEuros(long inputTokens, long outputTokens); }`.

- [ ] **Step 1: Write the failing tests** — `tests/AI.Tests/ModelProfileTests.cs`:

```csharp
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

public sealed class ModelProfileTests
{
    /// <summary>€ per million tokens, so a token costs the price in micro-euros:
    /// 1,500 in at €0.30 and 342 out at €2.50 is 450 + 855 µ€.</summary>
    [Fact]
    public void The_cost_is_the_profiles_price_in_micro_euros()
    {
        var profile = new ModelProfile { InputPrice = 0.30m, OutputPrice = 2.50m };

        Assert.Equal(1305, profile.CostMicroEuros(1500, 342));
    }

    [Fact]
    public void A_local_profile_costs_nothing() =>
        Assert.Equal(0, new ModelProfile().CostMicroEuros(4000, 800));

    [Fact]
    public void A_cost_rounds_half_away_from_zero() =>
        Assert.Equal(1, new ModelProfile { InputPrice = 0.5m }.CostMicroEuros(1, 0));

    [Theory]
    [InlineData(ModelKind.OpenAICompatible, null, "m", ModelAuth.None, null, "Endpoint")]
    [InlineData(ModelKind.OpenAICompatible, "http://x/v1/", null, ModelAuth.None, null, "Model")]
    [InlineData(ModelKind.OpenAICompatible, "http://x/v1/", "m", ModelAuth.Bearer, null, "ApiKey")]
    public void A_profile_that_cannot_be_called_names_what_it_lacks(
        ModelKind kind, string? endpoint, string? model, ModelAuth auth, string? key, string lacking)
    {
        var profile = new ModelProfile
        {
            Kind = kind, Endpoint = endpoint is null ? null : new Uri(endpoint), Model = model, Auth = auth, ApiKey = key,
        };

        Assert.Contains(profile.Problems("gemini"), problem => problem.Contains(lacking) && problem.Contains("gemini"));
    }

    [Fact]
    public void An_onnx_profile_needs_a_directory_and_no_endpoint()
    {
        Assert.NotEmpty(new ModelProfile { Kind = ModelKind.OnnxInProcess }.Problems("phi"));
        Assert.Empty(new ModelProfile { Kind = ModelKind.OnnxInProcess, Directory = "/models/phi" }.Problems("phi"));
    }
}
```

- [ ] **Step 2: Run** — `dotnet test tests/AI.Tests`. Expected: build FAILS (`ModelProfile` missing).

- [ ] **Step 3: Implement** — `src/AI/ModelProfile.cs`:

```csharp
namespace ElGuerre.Tendero.AI;

public enum ModelKind
{
    OpenAICompatible,
    OnnxInProcess,
}

public enum ModelAuth
{
    None,
    Bearer,
}

/// <summary>
/// One way to reach a model, from configuration (`Steward:Models:&lt;name&gt;`): a new
/// provider is an entry, not a class. Prices are € per million tokens, so a token
/// costs the price in micro-euros, and a row keeps the price of its moment.
/// </summary>
public sealed class ModelProfile
{
    public ModelKind Kind { get; init; } = ModelKind.OpenAICompatible;

    /// <summary>The provider's OpenAI base URL, /v1 included.</summary>
    public Uri? Endpoint { get; init; }

    public string? Model { get; init; }

    public ModelAuth Auth { get; init; } = ModelAuth.None;

    /// <summary>An Aspire secret parameter, never the repository.</summary>
    public string? ApiKey { get; init; }

    /// <summary>The model's folder, for an in-process ONNX profile.</summary>
    public string? Directory { get; init; }

    public decimal InputPrice { get; init; }

    public decimal OutputPrice { get; init; }

    /// <summary>The window the profile is driven against.</summary>
    public int ContextTokens { get; init; } = 8192;

    public int TimeoutSeconds { get; init; } = 120;

    public long CostMicroEuros(long inputTokens, long outputTokens) =>
        (long)Math.Round(inputTokens * InputPrice + outputTokens * OutputPrice, MidpointRounding.AwayFromZero);

    /// <summary>What keeps this profile from being called, said at start rather
    /// than at the first call.</summary>
    public IReadOnlyList<string> Problems(string name)
    {
        List<string> problems = [];
        if (Kind == ModelKind.OpenAICompatible)
        {
            if (Endpoint is null)
                problems.Add($"Model profile '{name}' has no Endpoint.");
            if (string.IsNullOrWhiteSpace(Model))
                problems.Add($"Model profile '{name}' has no Model.");
            if (Auth == ModelAuth.Bearer && string.IsNullOrWhiteSpace(ApiKey))
                problems.Add($"Model profile '{name}' uses Bearer and has no ApiKey.");
        }
        else if (string.IsNullOrWhiteSpace(Directory))
        {
            problems.Add($"Model profile '{name}' is in-process and has no Directory.");
        }

        return problems;
    }
}
```

- [ ] **Step 4: Run** — `dotnet test tests/AI.Tests`. Expected: PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(ai): a model profile is an entry in configuration, with its price (S-2)"`.

---

### Task 3: The metering client and the channel

**Files:**
- Create: `src/AI/AiInvocation.cs`, `src/AI/ModelCall.cs`, `src/AI/AiInvocationLog.cs`, `src/AI/MeteringChatClient.cs`
- Test: `tests/AI.Tests/MeteringChatClientTests.cs`, `tests/AI.Tests/AiInvocationLogTests.cs`

**Interfaces:**
- Produces:
  - `enum AiOutcome { Ok, Error, Timeout }`
  - `sealed record AiInvocation(Guid Id, string Operation, string Profile, string Model, string? PromptVersion, string? ConversationId, string? TraceId, long InputTokens, long OutputTokens, long CostMicroEuros, int LatencyMs, AiOutcome Outcome, DateTimeOffset At)`
  - `interface IAiInvocationWriter { Task WriteAsync(IReadOnlyList<AiInvocation> rows, CancellationToken ct = default); }`
  - `static class ModelCall { const string Operation = "tendero.operation", Conversation = "tendero.conversation", PromptVersion = "tendero.prompt_version"; static ChatOptions Options(string operation, string? conversation = null, string? promptVersion = null, ChatOptions? options = null); }`
  - `sealed class AiInvocationLog(int capacity = 10_000) { bool Record(AiInvocation row); ChannelReader<AiInvocation> Reader { get; } }`
  - `sealed class MeteringChatClient(IChatClient inner, string profileName, ModelProfile profile, AiInvocationLog log, TimeProvider clock) : DelegatingChatClient`

- [ ] **Step 1: Write the failing tests** — `tests/AI.Tests/MeteringChatClientTests.cs`:

```csharp
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>One row per model call, whatever the call did — the cost line of a
/// conversation and the Grafana panel are both sums of these rows.</summary>
public sealed class MeteringChatClientTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task A_call_leaves_a_row_with_its_tokens_cost_operation_and_model()
    {
        var log = new AiInvocationLog();
        var chat = Metered(new Answering(new UsageDetails { InputTokenCount = 1500, OutputTokenCount = 342 }), log,
            new ModelProfile { Model = "gemini-2.5-flash", InputPrice = 0.30m, OutputPrice = 2.50m });

        await chat.GetResponseAsync("hola", ModelCall.Options("steward.turn", "conv-1", "steward-v1"), TestContext.Current.CancellationToken);

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal(("steward.turn", "gemini", "gemini-2.5-flash", "conv-1", "steward-v1"), (row.Operation, row.Profile, row.Model, row.ConversationId, row.PromptVersion));
        Assert.Equal((1500L, 342L, 1305L, AiOutcome.Ok), (row.InputTokens, row.OutputTokens, row.CostMicroEuros, row.Outcome));
    }

    [Fact]
    public async Task A_model_that_reports_no_usage_costs_nothing_and_breaks_nothing()
    {
        var log = new AiInvocationLog();

        await Metered(new Answering(null), log, new ModelProfile { Model = "m", InputPrice = 1 })
            .GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal((0L, 0L, 0L, "unknown"), (row.InputTokens, row.OutputTokens, row.CostMicroEuros, row.Operation));
    }

    [Fact]
    public async Task A_call_that_fails_is_recorded_and_still_thrown()
    {
        var log = new AiInvocationLog();

        await Assert.ThrowsAsync<HttpRequestException>(() => Metered(new Failing(new HttpRequestException("503")), log, new ModelProfile { Model = "m" })
            .GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal(AiOutcome.Error, row.Outcome);
    }

    [Fact]
    public async Task A_call_that_times_out_is_recorded_as_a_timeout()
    {
        var log = new AiInvocationLog();

        await Assert.ThrowsAsync<TaskCanceledException>(() => Metered(new Failing(new TaskCanceledException("timeout")), log, new ModelProfile { Model = "m" })
            .GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(log.Reader.TryRead(out var row));
        Assert.Equal(AiOutcome.Timeout, row.Outcome);
    }

    private static IChatClient Metered(IChatClient inner, AiInvocationLog log, ModelProfile profile) =>
        new MeteringChatClient(inner, "gemini", profile, log, Clock);

    private sealed class Answering(UsageDetails? usage) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hola")) { Usage = usage });

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class Failing(Exception failure) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(failure);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
```

`tests/AI.Tests/AiInvocationLogTests.cs`:

```csharp
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

public sealed class AiInvocationLogTests
{
    /// <summary>A row lost is better than a request held: a full log refuses at
    /// once and says so, it never waits.</summary>
    [Fact]
    public void A_full_log_refuses_without_waiting()
    {
        var log = new AiInvocationLog(capacity: 1);

        Assert.True(log.Record(Row()));
        Assert.False(log.Record(Row()));
    }

    internal static AiInvocation Row() =>
        new(Guid.CreateVersion7(), "steward.turn", "local", "qwen", null, null, null, 1, 1, 0, 10, AiOutcome.Ok, DateTimeOffset.UnixEpoch);
}
```

Add `<Compile Include="../TestClock.cs" Link="TestClock.cs" />` to the Ai test project if the copy from Assist does not carry it.

- [ ] **Step 2: Run** — `dotnet test tests/AI.Tests`. Expected: build FAILS.

- [ ] **Step 3: Implement.** `src/AI/AiInvocation.cs`:

```csharp
namespace ElGuerre.Tendero.AI;

public enum AiOutcome
{
    Ok,
    Error,
    Timeout,
}

/// <summary>
/// One model call: what it was for, which profile and model answered, what it
/// cost at the profile's price of that moment, how long it took, how it ended.
/// No prompt and no answer — conversations are not kept (Steward spec, decision 6).
/// </summary>
public sealed record AiInvocation(
    Guid Id,
    string Operation,
    string Profile,
    string Model,
    string? PromptVersion,
    string? ConversationId,
    string? TraceId,
    long InputTokens,
    long OutputTokens,
    long CostMicroEuros,
    int LatencyMs,
    AiOutcome Outcome,
    DateTimeOffset At);

/// <summary>Where the rows go: Persistence, on a context of its own, as the audit.</summary>
public interface IAiInvocationWriter
{
    Task WriteAsync(IReadOnlyList<AiInvocation> rows, CancellationToken cancellationToken = default);
}
```

`src/AI/ModelCall.cs`:

```csharp
using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.AI;

/// <summary>
/// What a call is for, said where it is made: the metering reads it from the
/// options, so a row says which operation, conversation and prompt it belonged to
/// without anything ambient.
/// </summary>
public static class ModelCall
{
    public const string Operation = "tendero.operation";
    public const string Conversation = "tendero.conversation";
    public const string PromptVersion = "tendero.prompt_version";

    public static ChatOptions Options(string operation, string? conversation = null, string? promptVersion = null, ChatOptions? options = null)
    {
        options ??= new ChatOptions();
        options.AdditionalProperties ??= [];
        options.AdditionalProperties[Operation] = operation;
        if (conversation is not null)
            options.AdditionalProperties[Conversation] = conversation;
        if (promptVersion is not null)
            options.AdditionalProperties[PromptVersion] = promptVersion;
        return options;
    }

    internal static string? Read(ChatOptions? options, string key) =>
        options?.AdditionalProperties?.TryGetValue(key, out var value) == true ? value?.ToString() : null;
}
```

`src/AI/AiInvocationLog.cs`:

```csharp
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.AI;

/// <summary>
/// The rows on their way to the database, in process. Bounded, and full means the
/// newest row is dropped and counted: metering must never hold the request it
/// measures (Steward spec, *The models*).
/// </summary>
public sealed class AiInvocationLog(int capacity = 10_000)
{
    private static readonly Meter Meter = new(TelemetrySources.AI);

    private static readonly Counter<long> Dropped = Meter.CreateCounter<long>(
        "tendero.ai.invocations.dropped", unit: "{row}", description: "Model-call rows dropped because the log was full.");

    private readonly Channel<AiInvocation> channel = Channel.CreateBounded<AiInvocation>(
        new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ChannelReader<AiInvocation> Reader => this.channel.Reader;

    public bool Record(AiInvocation row)
    {
        if (this.channel.Writer.TryWrite(row))
            return true;

        Dropped.Add(1);
        return false;
    }
}
```

(`FullMode = Wait` with `TryWrite` is what makes a full channel refuse immediately; the other modes make `TryWrite` succeed by dropping some other row silently.)

`src/AI/MeteringChatClient.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.AI;

/// <summary>
/// Records one row per call — tokens, cost at the profile's price, latency, outcome
/// — into the log, and lets everything else through untouched: a failure is
/// recorded and thrown on as it came.
/// </summary>
public sealed class MeteringChatClient(
    IChatClient inner, string profileName, ModelProfile profile, AiInvocationLog log, TimeProvider clock)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var started = clock.GetTimestamp();
        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            Record(options, started, response.Usage, AiOutcome.Ok);
            return response;
        }
        catch (Exception failure)
        {
            Record(options, started, null, failure is TaskCanceledException or TimeoutException && !cancellationToken.IsCancellationRequested
                ? AiOutcome.Timeout
                : AiOutcome.Error);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Streaming is a non-goal (the spec); metered all the same, from the usage
        // the last update carries.
        var started = clock.GetTimestamp();
        UsageDetails? usage = null;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var content in update.Contents.OfType<UsageContent>())
                usage = content.Details;
            yield return update;
        }

        Record(options, started, usage, AiOutcome.Ok);
    }

    private void Record(ChatOptions? options, long started, UsageDetails? usage, AiOutcome outcome)
    {
        var input = usage?.InputTokenCount ?? 0;
        var output = usage?.OutputTokenCount ?? 0;
        log.Record(new AiInvocation(
            Guid.CreateVersion7(clock.GetUtcNow()),
            ModelCall.Read(options, ModelCall.Operation) ?? "unknown",
            profileName,
            options?.ModelId ?? profile.Model ?? profileName,
            ModelCall.Read(options, ModelCall.PromptVersion),
            ModelCall.Read(options, ModelCall.Conversation),
            Activity.Current?.TraceId.ToString(),
            input,
            output,
            profile.CostMicroEuros(input, output),
            (int)clock.GetElapsedTime(started).TotalMilliseconds,
            outcome,
            clock.GetUtcNow()));
    }
}
```

- [ ] **Step 4: Run** — `dotnet test tests/AI.Tests`. Expected: PASS. Hand mutation for the full-log test: change `FullMode = BoundedChannelFullMode.Wait` to `DropOldest` → `A_full_log_refuses_without_waiting` red; restore.

- [ ] **Step 5: Commit** — `git commit -m "feat(ai): every model call leaves one row, and metering never holds the request (S-2)"` with the hand mutation named in the body.

---

### Task 4: Profiles registered as keyed clients, and the extractor metered

**Files:**
- Create: `src/AI/AiServiceCollectionExtensions.cs`
- Modify: `src/Knowledge/KnowledgeServiceCollectionExtensions.cs`, `src/Knowledge/Extraction/ChatClaimExtractor.cs` (the call's options), `tests/Knowledge.Tests/KnowledgeRegistrationTests.cs` (if it resolves the unkeyed `IChatClient`)
- Test: `tests/AI.Tests/ModelProfileRegistrationTests.cs`

**Interfaces:**
- Produces: `IServiceCollection AddModelProfiles(this IServiceCollection services, IConfiguration configuration, string section = "Steward:Models")` — one keyed singleton `IChatClient` per profile (key = profile name), OpenTelemetry (`TelemetrySources.AI`) → metering → model; throws `InvalidOperationException` listing every problem; registers `AiInvocationLog` (singleton) and `TimeProvider` if absent. `IChatClient Meter(this IChatClient inner, string profileName, ModelProfile profile, IServiceProvider services)` for a client built in code (the extractor). `ChatClaimExtractor` calls with `ModelCall.Options("knowledge.extract", promptVersion: PromptVersion, options)`.

- [ ] **Step 1: Write the failing tests** — `tests/AI.Tests/ModelProfileRegistrationTests.cs`:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

public sealed class ModelProfileRegistrationTests
{
    [Fact]
    public void Each_profile_is_a_keyed_client_that_meters()
    {
        using var provider = Compose(new()
        {
            ["Steward:Models:local:Endpoint"] = "http://localhost:38085/v1/",
            ["Steward:Models:local:Model"] = "qwen3.5-4b",
            ["Steward:Models:gemini:Endpoint"] = "https://generativelanguage.googleapis.com/v1beta/openai/",
            ["Steward:Models:gemini:Model"] = "gemini-2.5-flash",
            ["Steward:Models:gemini:Auth"] = "Bearer",
            ["Steward:Models:gemini:ApiKey"] = "secret",
        });

        Assert.NotNull(provider.GetRequiredKeyedService<IChatClient>("local").GetService<MeteringChatClient>());
        Assert.NotNull(provider.GetRequiredKeyedService<IChatClient>("gemini").GetService<MeteringChatClient>());
        Assert.Null(provider.GetKeyedService<IChatClient>("deepseek"));
    }

    [Fact]
    public void A_profile_that_cannot_be_called_stops_the_host_naming_it()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Compose(new()
        {
            ["Steward:Models:gemini:Endpoint"] = "https://generativelanguage.googleapis.com/v1beta/openai/",
            ["Steward:Models:gemini:Model"] = "gemini-2.5-flash",
            ["Steward:Models:gemini:Auth"] = "Bearer",
        }));

        Assert.Contains("gemini", failure.Message);
        Assert.Contains("ApiKey", failure.Message);
    }

    [Fact]
    public void No_profiles_is_no_clients_and_no_failure()
    {
        using var provider = Compose([]);

        Assert.Null(provider.GetKeyedService<IChatClient>("local"));
    }

    private static ServiceProvider Compose(Dictionary<string, string?> settings) =>
        new ServiceCollection()
            .AddModelProfiles(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();
}
```

- [ ] **Step 2: Run** — Expected: build FAILS (`AddModelProfiles` missing).

- [ ] **Step 3: Implement** — `src/AI/AiServiceCollectionExtensions.cs`:

```csharp
using ElGuerre.Tendero.AI.Chat;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.AI;

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// One keyed <see cref="IChatClient"/> per profile under <paramref name="section"/>,
    /// each the same pipeline: OpenTelemetry (GenAI conventions) → metering → the
    /// model. A profile that cannot be called stops the host here, naming itself,
    /// rather than failing at its first call.
    /// </summary>
    public static IServiceCollection AddModelProfiles(
        this IServiceCollection services, IConfiguration configuration, string section = "Steward:Models")
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AiInvocationLog>();

        var profiles = configuration.GetSection(section).GetChildren()
            .Select(child => (Name: child.Key, Profile: child.Get<ModelProfile>() ?? new ModelProfile()))
            .ToList();

        var problems = profiles.SelectMany(entry => entry.Profile.Problems(entry.Name)).ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));

        foreach (var (name, profile) in profiles)
            services.AddKeyedSingleton<IChatClient>(name, (provider, _) => Model(profile).Meter(name, profile, provider));

        return services;
    }

    /// <summary>The same metering and tracing for a client built in code — the claim
    /// extractor's, whose model the AppHost hands it directly.</summary>
    public static IChatClient Meter(this IChatClient inner, string profileName, ModelProfile profile, IServiceProvider services) =>
        inner.AsBuilder()
            .UseOpenTelemetry(sourceName: TelemetrySources.AI)
            .Use(model => new MeteringChatClient(
                model, profileName, profile,
                services.GetRequiredService<AiInvocationLog>(),
                services.GetService<TimeProvider>() ?? TimeProvider.System))
            .Build(services);

    private static IChatClient Model(ModelProfile profile) => profile.Kind switch
    {
        ModelKind.OpenAICompatible => OpenAICompatibleChat.CreateAt(
            profile.Endpoint!, profile.Model!,
            profile.Auth == ModelAuth.Bearer ? profile.ApiKey : null,
            TimeSpan.FromSeconds(profile.TimeoutSeconds)),
        ModelKind.OnnxInProcess => InProcessOnnxChat.Create(profile.Directory!),
    };
}
```

In `AddKnowledge`, replace the extractor's registration:

```csharp
            services.TryAddSingleton<AiInvocationLog>();
            services.AddChatClient(provider => OpenAICompatibleChat.Create(new Uri(extractor), Extractor.ModelId)
                .Meter("extractor", new ModelProfile { Model = Extractor.ModelId, ContextTokens = 4096 }, provider));
```

(the old `.UseOpenTelemetry(sourceName: TelemetrySources.Knowledge)` goes: `Meter` traces it under `TelemetrySources.AI`). In `ChatClaimExtractor`, wrap the options it builds: `var options = ModelCall.Options("knowledge.extract", promptVersion: PromptVersion, options: new ChatOptions { … });` — read the file first; keep every option it sets.

- [ ] **Step 4: Run** — `dotnet test tests/AI.Tests tests/Knowledge.Tests`. Expected: PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(ai): profiles are keyed clients behind one pipeline, and the extractor is metered (S-2)"`.

---

### Task 5: The worker, the table, and the rows written

**Files:**
- Create: `src/AI/AiInvocationsWorker.cs`; `src/Persistence/Configurations/AiInvocationConfiguration.cs`; `src/Persistence/EfAiInvocationWriter.cs`; the migration `AddAiInvocations`
- Modify: `src/AI/AiServiceCollectionExtensions.cs` (`AddAiInvocationLog`), `src/Persistence/ElGuerre.Tendero.Persistence.csproj` (reference Ai), `src/Persistence/TenderoDbContext.cs` (`DbSet<AiInvocation> AiInvocations` **and** `ApplyConfiguration(new AiInvocationConfiguration())` in its explicit list), `src/Persistence/PersistenceServiceCollectionExtensions.cs`, `src/Api/Program.cs` and `src/Workers/WorkerServices.cs` (`AddAiInvocationLog()`), `src/Api/ElGuerre.Tendero.Api.csproj`, `src/Workers/ElGuerre.Tendero.Workers.csproj` (reference Ai if not transitive)
- Test: `tests/AI.Tests/AiInvocationsWorkerTests.cs`, `tests/Integration.Tests/AiInvocationRowsTests.cs`

**Interfaces:**
- Produces: `sealed class AiInvocationsWorker(AiInvocationLog log, IServiceScopeFactory scopes, ILogger<AiInvocationsWorker> logger) : BackgroundService` — reads up to 100 rows at a time, writes them through a scoped `IAiInvocationWriter`, logs and continues on failure; `IServiceCollection AddAiInvocationLog(this IServiceCollection services)` (log + worker); table `telemetry.ai_invocations`.

- [ ] **Step 1: Write the failing tests.** `tests/AI.Tests/AiInvocationsWorkerTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

public sealed class AiInvocationsWorkerTests
{
    [Fact]
    public async Task The_worker_writes_what_was_logged()
    {
        var (log, writer, worker) = Compose();
        log.Record(AiInvocationLogTests.Row());
        log.Record(AiInvocationLogTests.Row());

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await writer.Seen(2);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, writer.Rows.Count);
    }

    /// <summary>The database down for a while: a batch is lost, the worker keeps
    /// draining, and the host never stops over bookkeeping.</summary>
    [Fact]
    public async Task A_write_that_fails_is_logged_and_the_next_rows_still_go()
    {
        var (log, writer, worker) = Compose();
        writer.FailNext = true;
        log.Record(AiInvocationLogTests.Row());

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await writer.Attempted(1);
        log.Record(AiInvocationLogTests.Row());
        await writer.Seen(1);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Single(writer.Rows);
    }

    private static (AiInvocationLog, RecordingWriter, AiInvocationsWorker) Compose()
    {
        var log = new AiInvocationLog();
        var writer = new RecordingWriter();
        var scopes = new ServiceCollection().AddSingleton<IAiInvocationWriter>(writer).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return (log, writer, new AiInvocationsWorker(log, scopes, NullLogger<AiInvocationsWorker>.Instance));
    }

    private sealed class RecordingWriter : IAiInvocationWriter
    {
        private int attempts;

        public List<AiInvocation> Rows { get; } = [];

        public bool FailNext { get; set; }

        public Task WriteAsync(IReadOnlyList<AiInvocation> rows, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this.attempts);
            if (FailNext)
            {
                FailNext = false;
                throw new TimeoutException("the database went away");
            }

            lock (Rows)
                Rows.AddRange(rows);
            return Task.CompletedTask;
        }

        public Task Seen(int count) => Until(() => { lock (Rows) return Rows.Count >= count; });

        public Task Attempted(int count) => Until(() => Volatile.Read(ref this.attempts) >= count);

        private static async Task Until(Func<bool> condition)
        {
            for (var tries = 0; tries < 200 && !condition(); tries++)
                await Task.Delay(10);
            Assert.True(condition());
        }
    }
}
```

`tests/Integration.Tests/AiInvocationRowsTests.cs` — on Postgres, as `AuditPagingTests`: migrate a database, `AddTenderoPersistence(connectionString)`, resolve `IAiInvocationWriter` from a scope, write two rows (one `Ok` with a cost, one `Timeout`), read them back from `context.AiInvocations` ordered by `At`, and assert every field round-trips, the outcome as text.

- [ ] **Step 2: Run** — Expected: build FAILS (`AiInvocationsWorker`, `AiInvocations` missing).

- [ ] **Step 3: Implement.** `src/AI/AiInvocationsWorker.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.AI;

/// <summary>
/// Drains the log into the table, a batch at a time, off the request. A batch that
/// cannot be written is logged and dropped — the audit's trade, for a log nobody
/// reads in real time — and the worker goes on with the next one.
/// </summary>
public sealed partial class AiInvocationsWorker(AiInvocationLog log, IServiceScopeFactory scopes, ILogger<AiInvocationsWorker> logger)
    : BackgroundService
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AiInvocation>(BatchSize);
        try
        {
            while (await log.Reader.WaitToReadAsync(stoppingToken))
            {
                while (batch.Count < BatchSize && log.Reader.TryRead(out var row))
                    batch.Add(row);

                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IAiInvocationWriter>().WriteAsync(batch, stoppingToken);
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    LogLost(logger, failure, batch.Count);
                }

                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost {Count} model-call rows")]
    private static partial void LogLost(ILogger logger, Exception failure, int count);
}
```

In `AiServiceCollectionExtensions`:

```csharp
    /// <summary>The log and the worker that drains it — in every host that calls a model.</summary>
    public static IServiceCollection AddAiInvocationLog(this IServiceCollection services)
    {
        services.TryAddSingleton<AiInvocationLog>();
        services.AddHostedService<AiInvocationsWorker>();
        return services;
    }
```

`src/Persistence/Configurations/AiInvocationConfiguration.cs`:

```csharp
using ElGuerre.Tendero.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>One row per model call (phase 12), in a schema of its own like the
/// audit's: every context's model calls land here and none owns it.</summary>
internal sealed class AiInvocationConfiguration : IEntityTypeConfiguration<AiInvocation>
{
    public void Configure(EntityTypeBuilder<AiInvocation> builder)
    {
        builder.ToTable("AiInvocations", "telemetry");

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();

        builder.Property(row => row.Operation).HasMaxLength(100).IsRequired();
        builder.Property(row => row.Profile).HasMaxLength(100).IsRequired();
        builder.Property(row => row.Model).HasMaxLength(200).IsRequired();
        builder.Property(row => row.PromptVersion).HasMaxLength(100);
        builder.Property(row => row.ConversationId).HasMaxLength(100);
        builder.Property(row => row.TraceId).HasMaxLength(64);
        builder.Property(row => row.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();

        // What the cost line and the panel ask: a conversation's rows, and a day's.
        builder.HasIndex(row => row.ConversationId).HasFilter("conversation_id IS NOT NULL");
        builder.HasIndex(row => row.At).IsDescending();
    }
}
```

`src/Persistence/EfAiInvocationWriter.cs`:

```csharp
using ElGuerre.Tendero.AI;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>On a context of its own, as the audit writer: the rows are no part of
/// any request's transaction.</summary>
internal sealed class EfAiInvocationWriter(IDbContextFactory<TenderoDbContext> contexts) : IAiInvocationWriter
{
    public async Task WriteAsync(IReadOnlyList<AiInvocation> rows, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.AiInvocations.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken);
    }
}
```

Register `services.AddScoped<IAiInvocationWriter, EfAiInvocationWriter>();` beside the audit writer. `TenderoDbContext`: `public DbSet<AiInvocation> AiInvocations => Set<AiInvocation>();` and `modelBuilder.ApplyConfiguration(new AiInvocationConfiguration());` beside `AuditEntryConfiguration`. Api `Program.cs` and `WorkerServices.cs`: `services.AddAiInvocationLog();`.

Run: `dotnet tool run dotnet-ef migrations add AddAiInvocations --project src/Persistence`. Expected: creates `telemetry.ai_invocations` with its two indexes, and nothing else.

- [ ] **Step 4: Run** — `dotnet test tests/AI.Tests`, `dotnet test tests/Persistence.Tests`, `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~AiInvocationRowsTests|FullyQualifiedName~MigrationBaselineTests"` (Docker up), `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HostedServiceNaming"`. Expected: PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(ai): the model-call rows reach telemetry.ai_invocations off the request (S-2)"`.

---

### Task 6: The `steward-model` container, measured

**Files:**
- Modify: `src/AppHost/AppHost.cs` (the container, its port constant, the flag, the API's environment), `src/Api/appsettings.json` (the `local` profile and `Steward:Model`), `docs/c4/tendero.c4` (the container), regenerated C4 diagrams
- Create: `docs/analysis/steward-model-sizing.md` (the measurement)

- [ ] **Step 1: Measure.** With the GGUF already in the `tendero-extractor-models` volume, start the server the way the AppHost will, alone on the machine, with a generous limit:

```bash
MSYS_NO_PATHCONV=1 docker run -d --name tendero-steward-sizing --memory=10g --memory-swap=10g --cpus=12 \
  -v tendero-extractor-models:/models:ro -p 38095:8080 ghcr.io/ggml-org/llama.cpp:server-v0.5.0 \
  -m /models/qwen3.5-4b-q8_0.gguf --host 0.0.0.0 --port 8080 -c 8192 -np 1 -t 12 --jinja --load-mode none \
  --chat-template-kwargs '{"enable_thinking":false}'
```

Wait for `/health` to answer 200; send one request whose prompt is ~6,500 tokens (a system message plus six tool definitions and a long user message — a synthetic file under the scratchpad) with `max_tokens: 800`; sample `docker stats --no-stream tendero-steward-sizing` every second during it and keep the peak. Record in `docs/analysis/steward-model-sizing.md`: idle memory after load, peak during the long request, prompt and generated tokens, prompt and generation tokens per second (from the server's `timings`), and the limit chosen (**the peak plus 0.5 GB, rounded up to the next 0.5 GB**). Then `docker rm -f tendero-steward-sizing`.

- [ ] **Step 2: Declare it** — in `AppHost.cs`, beside the extractor, with `const int stewardModelPort = 38085;` among the pinned ports:

```csharp
// STEWARD'S LOCAL MODEL, declared always and started on demand (phase 12).
//
//   Tendero__Steward=true dotnet run --project src/AppHost
//
// The extractor's GGUF from the extractor's volume — the same file, so nothing is
// downloaded twice — served by a container of its own: the extractor's one slot of
// 4,096 tokens is taken, and Steward's tool definitions alone are 1,000–1,500. Its
// window is 8,192, an estimate S-6 replaces with a measurement; its limit is the
// one docs/analysis/steward-model-sizing.md measured.
var steward = string.Equals(builder.Configuration["Tendero:Steward"], "true", StringComparison.OrdinalIgnoreCase);

var stewardModel = builder.AddContainer("steward-model", "ghcr.io/ggml-org/llama.cpp", "server-" + llamaCppRelease)
    .WithArgs("-m", "/models/qwen3.5-4b-q8_0.gguf", "--host", "0.0.0.0", "--port", "8080",
        "-c", "8192", "-np", "1", "-t", "12", "--jinja", "--load-mode", "none",
        "--chat-template-kwargs", "{\"enable_thinking\":false}")
    .WithHttpEndpoint(targetPort: 8080, port: stewardModelPort, name: "http")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithVolume("tendero-extractor-models", "/models", isReadOnly: true)
    .WithContainerRuntimeArgs("--memory=<measured>", "--memory-swap=<measured>", Cpus(12))
    .WaitForCompletion(extractorModel);

if (!steward)
    stewardModel.WithExplicitStart();
```

with `<measured>` replaced by the limit from Step 1, and on the API resource:

```csharp
    .WithEnvironment("Steward__Models__local__Endpoint", ReferenceExpression.Create($"{stewardModel.GetEndpoint("http")}/v1/"))
```

`src/Api/appsettings.json`:

```json
  "Steward": {
    "Model": "local",
    "Models": {
      "local": {
        "Kind": "OpenAICompatible",
        "Model": "qwen3.5-4b-q8_0",
        "Auth": "None",
        "InputPrice": 0,
        "OutputPrice": 0,
        "ContextTokens": 8192,
        "TimeoutSeconds": 180
      }
    }
  }
```

The `local` profile has no `Endpoint` in `appsettings.json` — the AppHost gives it one — so `AddModelProfiles` must not run in a host that has the section without the AppHost: in `Program.cs`, call `builder.Services.AddModelProfiles(builder.Configuration)` only when `builder.Configuration["Steward:Models:local:Endpoint"]` is set, as Knowledge registers its extractor only when its endpoint is. Record that as how "no model" looks in S-2.

`docs/c4/tendero.c4`: add `stewardModel = service 'steward-model' { … }` beside `extractor`, and `tendero.api -> tendero.stewardModel 'asks Steward's questions'`; run `node tools/c4.mjs`.

- [ ] **Step 3: Check** — `dotnet build -warnaserror`; `dotnet test tests/Architecture.Tests` (port, collector and wiring tests); `node tools/c4.mjs` (validates against the AppHost). Expected: clean; PASS.

- [ ] **Step 4: Commit** — `git commit -m "feat(apphost): Steward's local model, its own slot of the extractor's GGUF, sized from a measurement (S-2)"`.

---

### Task 7: The contract suite against the real container

**Files:**
- Create: `tests/AI.Tests/ChatProfileContractTests.cs`, `tests/AI.Tests/LlamaCppProfileContractTests.cs`

- [ ] **Step 1: Write the suite.**

```csharp
using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>
/// What every profile kind must do for Steward (the spec, *Evaluation and tests*):
/// call a tool and read its result, report its usage, and surface a timeout as a
/// timeout. Each kind inherits it; S-3's Scripted kind joins.
/// </summary>
public abstract class ChatProfileContractTests
{
    protected abstract IChatClient? Client(TimeSpan timeout);

    [Fact]
    [Trait("Category", "Model")]
    public async Task A_tool_call_round_trips()
    {
        var chat = Client(TimeSpan.FromMinutes(3));
        Assert.SkipWhen(chat is null, "No model is running for this profile.");

        var called = false;
        var tool = AIFunctionFactory.Create(() => { called = true; return 12; }, "count_products_missing_english",
            "How many products have no English name.");
        var answer = await chat!.AsBuilder().UseFunctionInvocation().Build().GetResponseAsync(
            "How many products have no English name? Use the tool.",
            new ChatOptions { Tools = [tool], Temperature = 0 }, TestContext.Current.CancellationToken);

        Assert.True(called);
        Assert.Contains("12", answer.Text);
    }

    [Fact]
    [Trait("Category", "Model")]
    public async Task Usage_is_reported()
    {
        var chat = Client(TimeSpan.FromMinutes(3));
        Assert.SkipWhen(chat is null, "No model is running for this profile.");

        var answer = await chat!.GetResponseAsync("Say hello.", new ChatOptions { MaxOutputTokens = 10 }, TestContext.Current.CancellationToken);

        Assert.True(answer.Usage?.InputTokenCount > 0);
        Assert.True(answer.Usage?.OutputTokenCount > 0);
    }

    [Fact]
    [Trait("Category", "Model")]
    public async Task A_timeout_surfaces_as_one()
    {
        var chat = Client(TimeSpan.FromMilliseconds(1));
        Assert.SkipWhen(chat is null, "No model is running for this profile.");

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => chat!.GetResponseAsync(
            "Write a long story.", cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(failure is TaskCanceledException or TimeoutException || failure.InnerException is TimeoutException,
            $"A timeout came back as {failure.GetType().Name}: {failure.Message}");
    }
}
```

`LlamaCppProfileContractTests.cs`: derives it; `Client(timeout)` returns `null` unless `GET http://localhost:38085/health` answers 200 within two seconds, and otherwise `OpenAICompatibleChat.CreateAt(new Uri("http://localhost:38085/v1/"), "qwen3.5-4b-q8_0", timeout: timeout)`.

- [ ] **Step 2: Run it against the container** — start `steward-model` as Task 6 declared (`docker run` with the measured limit on port 38085, or the AppHost with `Tendero__Steward=true`), run `dotnet test tests/AI.Tests --filter "Category=Model"`, record pass/fail and the tool-call answer in `docs/analysis/steward-model-sizing.md`, then stop and remove the container. Expected: 3 PASS. If the 4B does not call the tool with the default chat template, that is a finding for S-3's prompt, recorded — not a reason to weaken the test.

- [ ] **Step 3: Without the container** — `dotnet test tests/AI.Tests`. Expected: the three skip, the rest pass.

- [ ] **Step 4: Commit** — `git commit -m "test(ai): the contract every chat profile meets, run against Steward's local model (S-2)"`.

---

### Task 8: The rules, the ADR, the board

**Files:**
- Modify: `tests/Architecture.Tests/Solution.cs` (`AI` in `All`, not in `Contexts`), `tests/Architecture.Tests/ArchitectureRules.cs` (the OpenAI fence gains `ElGuerre.Tendero.AI.Chat`; a new rule `AI_knows_only_the_shared_kernel`; ONNX Runtime fenced to `AI.Chat`), the Architecture test project's references
- Create: `docs/adr/0053-one-model-port.md`; modify `docs/adr/README.md`, `CLAUDE.md` (the namespace list; `AI` beside `Mcp` and `Ucp` as not a context), `docs/delivery-plan.md` (`P12-2` done), `docs/specs/steward.md` (*S-2 as built*), `docs/blog/notebook.md`

- [ ] **Step 1:** Write `AI_knows_only_the_shared_kernel` as `Assist_knows_only_the_shared_kernel` is written; add `ElGuerre.Tendero.AI.Chat` to the OpenAI fence's allowed namespaces and a twin rule for `Microsoft.ML.OnnxRuntimeGenAI` allowed only there. Hand mutation each: an `OpenAI` type used in `src/AI/ModelProfile.cs` → the fence red; a Knowledge reference from Ai → the new rule red; restore both.
- [ ] **Step 2:** ADR 0053 — *One model port: profiles in configuration, one pipeline, one row per call* — context (Knowledge owned the only factory; Steward and Tino need the same; hosted profiles cost money), decision (the `AI` project and its fence; profiles as keyed clients; OTel → metering → model; the channel and its trade; `telemetry.ai_invocations`), consequences (a provider is an entry; the extractor is metered too; a full log drops rows, counted).
- [ ] **Step 3:** The docs above, and a notebook entry with the sizing numbers and the contract run's result.
- [ ] **Step 4:** `dotnet build -warnaserror`; `dotnet test` (Docker up); `npx nx run-many -t lint test` from `frontend/` only if a response changed (it should not). Expected: clean and green.
- [ ] **Step 5: Commit** — `git commit -m "docs(ai): ADR 0053, one model port, and S-2 on the board (S-2)"`.
