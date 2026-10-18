using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// What a REST contract says of itself, which this one did not (phase 13b, the
/// audit's *REST* findings): which version it is, who may call each operation,
/// and what a caller is answered when the answer is no. The document declared
/// no security at all — an agent reading it could not tell a public operation
/// from the shopkeeper's — and a 401, a 403 and a 429 had no body.
///
/// Held two ways, as the rest of the contract is: on the committed document,
/// which is what a client generates from, and on the running API.
/// </summary>
public sealed partial class WireConventionsContractTests(TenderoApiFactory factory) : IClassFixture<TenderoApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonDocument Committed() => JsonDocument.Parse(File.ReadAllText(OpenApiDocumentTests.CommittedDocumentPath()));

    private static IEnumerable<(string Id, JsonElement Operation)> Operations(JsonDocument document) =>
        from path in document.RootElement.GetProperty("paths").EnumerateObject()
        from method in path.Value.EnumerateObject()
        where method.Value.ValueKind == JsonValueKind.Object && method.Value.TryGetProperty("operationId", out _)
        select (method.Value.GetProperty("operationId").GetString()!, method.Value);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex ADate();

    /// <summary>
    /// A dated version, which a client can name and compare — and every answer
    /// repeats it, a refusal included, so a caller holding an old document
    /// knows it before it misreads a field.
    /// </summary>
    [Fact]
    public async Task The_contract_names_its_version_and_every_answer_repeats_it()
    {
        using var document = Committed();
        var version = document.RootElement.GetProperty("info").GetProperty("version").GetString()!;
        Assert.Matches(ADate(), version);
        using var client = factory.CreateClient();

        var answered = await client.GetAsync("/api/kits", Ct);
        var refused = await client.GetAsync("/api/orders", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal([version], answered.Headers.GetValues("Tendero-Version"));
        Assert.Equal([version], refused.Headers.GetValues("Tendero-Version"));
    }

    /// <summary>
    /// **Who may call it is part of the contract.** An operation that requires
    /// somebody declares the bearer scheme with the policies it asks for, and
    /// the two refusals that follow; one anybody may call declares none of
    /// security. Compared with what the running endpoint really requires, so the
    /// document cannot say public of an operation that is not.
    /// </summary>
    [Fact]
    public void An_operation_says_who_may_call_it_and_the_document_agrees_with_the_endpoint()
    {
        using var document = Committed();
        _ = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>() is not null)
            .ToDictionary(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName);

        Assert.Equal("http", document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearer").GetProperty("type").GetString());

        List<string> wrong = [];
        var guarded = 0;
        foreach (var (id, operation) in Operations(document))
        {
            var endpoint = endpoints[id];
            var policies = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                ? []
                : endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy ?? "").Where(policy => policy.Length > 0).Order().ToArray();

            var declared = operation.TryGetProperty("security", out var security) && security.GetArrayLength() > 0
                ? security[0].GetProperty("bearer").EnumerateArray().Select(role => role.GetString()!).Order().ToArray()
                : null;
            var refusals = operation.GetProperty("responses").EnumerateObject().Select(response => response.Name).ToHashSet();

            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null && endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null)
            {
                guarded++;
                if (declared is null || !declared.SequenceEqual(policies) || !refusals.Contains("401") || !refusals.Contains("403"))
                    wrong.Add($"{id}: requires [{string.Join(", ", policies)}] and declares [{(declared is null ? "nothing" : string.Join(", ", declared))}], with {(refusals.Contains("401") ? "" : "no ")}401 and {(refusals.Contains("403") ? "" : "no ")}403");
            }
            // A 401 of its own is not this: a webhook anybody may reach, and
            // refuses by its signature, says so without a bearer scheme.
            else if (declared is not null)
            {
                wrong.Add($"{id}: anybody may call it, and the document declares a security requirement");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        Assert.True(guarded > 20, $"Only {guarded} guarded operations were found: the comparison is not looking at the API.");
    }

    /// <summary>
    /// **What creates something a client can then read answers 201, and says
    /// where it is** (RFC 9110): an order, a campaign, a campaign's picture —
    /// each has an address of its own, and `Location` carries it. They answered
    /// 200, which says *done* and not *made*, and left the address to be
    /// assembled by whoever knew the routes.
    ///
    /// Only these: an agent's registration and a return are created too, and
    /// have no address of their own to point at — a 201 with nowhere to go
    /// would claim more than is there.
    /// </summary>
    [Theory]
    [InlineData("PlaceOrder")]
    [InlineData("CreateCampaign")]
    [InlineData("UploadCampaignImage")]
    public void What_creates_an_addressable_resource_answers_201_and_says_where(string operationId)
    {
        using var document = Committed();
        var responses = Operations(document).Single(operation => operation.Id == operationId).Operation.GetProperty("responses");

        Assert.False(responses.TryGetProperty("200", out _), $"{operationId} still answers 200.");
        Assert.True(responses.GetProperty("201").GetProperty("headers").TryGetProperty("Location", out _), $"{operationId}'s 201 does not declare where the resource is.");
    }

    /// <summary>And no other 201 goes without its address: the header is declared wherever the status is.</summary>
    [Fact]
    public void Every_201_declares_a_location()
    {
        using var document = Committed();

        var without = Operations(document)
            .Where(operation => operation.Operation.GetProperty("responses").TryGetProperty("201", out var created)
                && !(created.TryGetProperty("headers", out var headers) && headers.TryGetProperty("Location", out _)))
            .Select(operation => operation.Id)
            .ToArray();

        Assert.Empty(without);
    }

    /// <summary>
    /// **Every refusal the contract declares is a problem with a code** (RFC
    /// 9457; the wire's spec, W-4): `application/problem+json`, and a `code`
    /// that is one of a closed set declared for that operation and that status
    /// — what a client branches on. None is a bare string, which is a sentence
    /// in one language with nothing to branch on, and none is an empty body.
    /// On 2026-10-13 forty were strings and twenty-six empty.
    /// </summary>
    [Fact]
    public void Every_refusal_the_contract_declares_is_a_problem_with_a_code()
    {
        using var document = Committed();
        List<string> wrong = [];
        var declared = 0;

        foreach (var (id, operation) in Operations(document))
        {
            foreach (var response in operation.GetProperty("responses").EnumerateObject().Where(response => response.Name.StartsWith('4')))
            {
                declared++;
                if (!response.Value.TryGetProperty("content", out var content))
                {
                    wrong.Add($"{id} {response.Name}: no body");
                    continue;
                }

                var media = content.EnumerateObject().Select(entry => entry.Name).ToArray();
                if (media is not ["application/problem+json"])
                {
                    wrong.Add($"{id} {response.Name}: {string.Join(", ", media)}, not a problem");
                    continue;
                }

                var schema = content.GetProperty("application/problem+json").GetProperty("schema");
                var codes = schema.TryGetProperty("properties", out var own) && own.TryGetProperty("code", out var code) && code.TryGetProperty("enum", out var set)
                    ? set.GetArrayLength()
                    : 0;
                if (codes == 0)
                    wrong.Add($"{id} {response.Name}: a problem with no closed set of codes");
            }
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} of {declared}:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
        Assert.True(declared > 100, $"Only {declared} refusals were found: the walk is not looking at the document.");
    }

    /// <summary>
    /// **What a refusal carries goes with its code, not with its status.** A
    /// checkout's 409 is four different refusals: the price moved (and here is
    /// the new one), the shelf is short (and here is what there is), a key
    /// reused, a mandate exceeded. Declared as *a problem and everything any of
    /// them carries*, the document said a short shelf comes with a new quote —
    /// which a generated client then requires and never gets. Found when the
    /// second refusal with something to hand over arrived.
    /// </summary>
    [Fact]
    public void What_a_refusal_carries_is_declared_for_its_code_and_no_other()
    {
        using var document = Committed();
        var conflict = Operations(document).Single(operation => operation.Id == "PlaceOrder").Operation
            .GetProperty("responses").GetProperty("409").GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");

        string[] Carried(string code) =>
        [
            .. conflict.GetProperty("anyOf").EnumerateArray()
                .Single(branch => branch.GetProperty("properties").GetProperty("code").GetProperty("enum").EnumerateArray().Any(value => value.GetString() == code))
                is var branch && branch.TryGetProperty("allOf", out var with)
                    ? with.EnumerateArray().SelectMany(schema => schema.GetProperty("properties").EnumerateObject()).Select(member => member.Name)
                    : []
        ];

        Assert.Equal(["quoteHash", "total", "shipping", "taxTotal", "discountTotal"], Carried("PriceChanged"));
        Assert.Equal(["shortages"], Carried("InsufficientStock"));
        Assert.Empty(Carried("NotMandated"));
        // And nothing any of them carries is said of the status as a whole.
        Assert.Equal(["#/components/schemas/Problem"], conflict.GetProperty("allOf").EnumerateArray().Select(schema => schema.GetProperty("$ref").GetString()));
    }

    /// <summary>An operation with a limit says that it has one: a 429, and when to come back.</summary>
    [Fact]
    public void A_rate_limited_operation_declares_the_refusal_and_when_to_come_back()
    {
        using var document = Committed();
        _ = factory.CreateClient();

        var limited = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null)
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .OfType<string>()
            .ToHashSet();

        var operations = Operations(document).ToDictionary(operation => operation.Id, operation => operation.Operation);
        var documented = limited.Where(operations.ContainsKey).ToArray();
        Assert.True(documented.Length >= 3, $"Only {documented.Length} limited operations are in the document.");

        foreach (var (id, operation) in operations)
        {
            var declares = operation.GetProperty("responses").TryGetProperty("429", out var refusal);
            Assert.True(declares == limited.Contains(id), $"{id}: limited is {limited.Contains(id)}, and it declares a 429 is {declares}.");
            if (declares)
                Assert.True(refusal.GetProperty("headers").TryGetProperty("Retry-After", out _), $"{id}: its 429 does not say when to come back.");
        }
    }

    /// <summary>
    /// **A refusal the pipeline answers is a problem too** (RFC 9457): nobody
    /// signed in, somebody who may not, an address that names nothing. Each has
    /// a `code` a client branches on and a sentence in the language asked — the
    /// three used to be a status and an empty body.
    /// </summary>
    [Theory]
    [InlineData("/api/orders", null, 401, "Unauthenticated")]
    [InlineData("/api/orders", "ana", 403, "Forbidden")]
    [InlineData("/api/nothing-is-here", null, 404, "NotFound")]
    public async Task A_refusal_the_pipeline_answers_is_a_problem_with_a_code(string path, string? person, int status, string code)
    {
        using var client = factory.CreateClient();

        async Task<(HttpResponseMessage Response, JsonNode Body)> AskAsync(string language)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.AcceptLanguage.ParseAdd(language);
            if (person is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(client, person));

            var response = await client.SendAsync(request, Ct);
            return (response, JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!);
        }

        var (spanish, es) = await AskAsync("es");
        var (english, en) = await AskAsync("en");

        Assert.Equal((status, "application/problem+json"), ((int)spanish.StatusCode, spanish.Content.Headers.ContentType?.MediaType));
        Assert.Equal((status, code), (es["status"]!.GetValue<int>(), es["code"]!.GetValue<string>()));
        Assert.Equal(code, en["code"]!.GetValue<string>());
        // The type names the code, as a URI writes one: too-many-requests.
        Assert.EndsWith(code, es["type"]!.GetValue<string>().Replace("-", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);

        // The shop's sentence, in the language asked (invariant 6) — and said which.
        Assert.NotEqual(es["title"]!.GetValue<string>(), en["title"]!.GetValue<string>());
        Assert.Equal(("es", "en"), (string.Join(',', spanish.Content.Headers.ContentLanguage), string.Join(',', english.Content.Headers.ContentLanguage)));
    }

    /// <summary>Past the limit: a problem with its code, and `Retry-After` in seconds — a number a client can wait.</summary>
    [Fact]
    public async Task Past_a_limit_the_answer_is_a_problem_that_says_when_to_come_back()
    {
        using var client = factory.CreateClient();

        HttpResponseMessage? refused = null;
        for (var call = 0; call < 40 && refused is null; call++)
        {
            var response = await client.PostAsync("/api/kits/NO-SUCH-KIT/build", JsonContent(new { budget = new { amount = 1000, currency = "EUR" } }), Ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                refused = response;
        }

        Assert.NotNull(refused);
        var body = JsonNode.Parse(await refused.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal(("application/problem+json", "TooManyRequests"), (refused.Content.Headers.ContentType?.MediaType, body["code"]!.GetValue<string>()));
        // The limiter's own wait, not a floor: the window is a minute from the
        // first call, and the calls that fill it take seconds — so most of the
        // minute is left (Stryker, phase 13b's close: `Max(1, …)` read as
        // `Min(1, …)` said one second, and a range from one let it through).
        Assert.InRange(refused.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 30, 60);
    }

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    private static async Task<string> TokenAsync(HttpClient client, string subject)
    {
        using var form = new FormUrlEncodedContent([new("grant_type", "password"), new("username", subject)]);
        var response = await client.PostAsync("/dev-issuer/connect/token", form, Ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["access_token"]!.GetValue<string>();
    }
}
