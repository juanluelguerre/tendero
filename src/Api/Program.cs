using System.Threading.RateLimiting;
using Carter;
using ElGuerre.Tendero.Accounts;
using ElGuerre.Tendero.Accounts.Features.LinkIdentity;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Api;
using ElGuerre.Tendero.Assist;
using ElGuerre.Tendero.Assist.Features.DecideProposals;
using ElGuerre.Tendero.Assist.Steward;
using ElGuerre.Tendero.Assist.Tino;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.DevIssuer;
using ElGuerre.Tendero.Inventory;
using ElGuerre.Tendero.Inventory.Features.ListStock;
using ElGuerre.Tendero.Knowledge;
using ElGuerre.Tendero.Knowledge.Features.ReviewClaims;
using ElGuerre.Tendero.Mcp;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Features.Checkout;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Pricing;
using ElGuerre.Tendero.Pricing.Features.QuoteCart;
using ElGuerre.Tendero.Search;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.Search.Health;
using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp;
using ElGuerre.Tendero.Ucp.Catalog;
using ElGuerre.Tendero.Ucp.Checkout;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The assemblies scanned for handlers and validators. A new slice inside one of
// them does not touch this line.
builder.Services.AddTenderoCqrs(
    typeof(ImportProductsCommand).Assembly,
    typeof(SearchProductsQuery).Assembly,
    typeof(QuoteCartQuery).Assembly,
    typeof(ListStockQuery).Assembly,
    typeof(PlaceOrderCommand).Assembly,
    typeof(LinkIdentityCommand).Assembly,
    typeof(ReviewClaimsEndpoints).Assembly,
    typeof(ApproveProposalsCommand).Assembly);

builder.Services.AddTenderoPersistence(
    builder.Configuration.GetRequiredConnectionString("tendero-db"));

builder.Services.AddAccounts();

builder.Services.AddCatalog(builder.Configuration);

builder.Services.AddPricing(builder.Configuration);

builder.Services.AddInventory(builder.Configuration);

builder.Services.AddOrdering(builder.Configuration);

builder.Services.AddLexicalSearch(
    builder.Configuration.GetRequiredConnectionString("elasticsearch"));

// Hybrid search: the vector layer and the reranker when the AppHost handed them
// over (Tendero__Vectors), named stand-ins otherwise, and both strategies. Hybrid
// answers every search, and degrades to lexical by itself when a stage is missing
// or failing. Lexical search above is required; none of this is.
builder.Services.AddHybridSearch(builder.Configuration);

// Each stage's health through its own port, and the memory of when it last
// changed, for the dashboard's search status (R-9, D-7). Private checks: the
// open /health leaves them out.
builder.Services.AddSearchStatus();

// The development issuer is registered BEFORE authentication and only in
// Development. Its own AddDevIssuer throws when the environment is not
// Development, so the guard is on both sides.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevIssuer(builder.Configuration, builder.Environment);

    // The authority points at the issuer this very process publishes. When
    // Keycloak arrives this comes out of appsettings and this block disappears.
    builder.Configuration["Authentication:Authority"] ??=
        $"http://localhost:{builder.Configuration["ASPNETCORE_HTTP_PORT"] ?? "5130"}/dev-issuer";
    builder.Configuration["Authentication:AllowHttpMetadata"] ??= "true";

    // The seeded agents' platform profiles are published by this process in
    // Development (P11-1), so negotiation fetches from localhost and nowhere else.
    builder.Configuration["Ucp:TrustedPlatformHosts:0"] ??= "localhost";
}

builder.Services.AddTenderoAuthentication(builder.Configuration);

// CORS: the two Angular apps run on a different origin from the API, so without
// this the browser blocks every authenticated call.
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
        ?? ["http://localhost:4200", "http://localhost:4201"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

// WithEmptyValidators: Carter scans for validators and registers them as
// SINGLETONS, and none is used here — validation lives in the dispatcher's
// ValidationStep, which resolves them from the scope. That scan was not neutral:
// it turned `ImportProductsValidator`, which injects the scoped
// `ICatalogSourceRegistry`, into a captive dependency, and the API stopped
// starting in Development with "Cannot consume scoped service … from singleton".
// A mechanism nobody uses should not be able to bring the process down.
// The claims layer's review side (P10-5). Its models are handed only to the
// worker; here, with no address for them, Knowledge registers the rules alone.
builder.Services.AddKnowledge(builder.Configuration);

// Steward's proposals and their decisions (phase 12, ADR 0052).
builder.Services.AddAssist(builder.Configuration);

// One row per model call, written off the request (ADR 0053).
builder.Services.AddAIInvocationLog();

// Twenty Steward turns a minute per person (the spec, *Security*): a loop or an
// abuse costs a bounded amount. Partitioned by the principal, never by address.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(StewardTurnEndpoints.TurnsPerPerson, http => RateLimitPartition.GetFixedWindowLimiter(
        http.RequestServices.GetRequiredService<IPrincipalAccessor>().Current.Subject ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    // Twelve turns a minute per client for Tino, which anyone may ask (phase 12b):
    // by the person when one is signed in, and by the address they call from when
    // nobody is — the only thing an anonymous caller has that is not theirs to choose.
    limiter.AddPolicy(TinoEndpoints.TurnsPerClient, http => RateLimitPartition.GetFixedWindowLimiter(
        http.RequestServices.GetRequiredService<IPrincipalAccessor>().Current.Subject is { Length: > 0 } subject
            ? "person:" + subject
            : "address:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// Steward's model profiles (ADR 0053), each on its own. The local one has no
// address of its own: the AppHost hands it one only when its container runs
// (Tendero__Steward=true), and without it that profile is left out — never a
// call to a container nobody started — while a hosted one with its key is still
// a client.
builder.Services.AddModelProfiles(builder.Configuration);

builder.Services.AddCarter(configurator: carter => carter.WithEmptyValidators());

// The document is the API's shape, and from it come the frontend's types
// (previously hand-written in shared-api, with nothing to detect the drift) and,
// in phase 9, the UCP capability schemas. It lives in the framework: neither
// Swashbuckle nor NSwag, which would be two dependencies for one job.
builder.Services.AddOpenApi(options =>
{
    options.AddSchemaTransformer<NumbersAreNumbersTransformer>();
    options.AddSchemaTransformer<EnumsAreTheirNamesTransformer>();
    options.AddOperationTransformer<ClosedSetsInTheQueryTransformer>();
});

// A closed set travels as its C# name and nothing else (ADR 0043): one converter
// for the whole API, so a slice passes the enum and never spells it, and the
// document declares every set, so the frontends' types are unions the compiler
// holds. Integers are refused, so a position in an enum is never the contract.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(WireJson.Enums()));

// The read-only MCP server, in this process and in its own assembly. Same
// process so the tools reuse the container that is already built; separate
// assembly so an architecture rule has something to point at when it says this
// project implements no handler of its own.
builder.Services.AddTenderoMcp();

// UCP's transactional half: its options and the negotiation port (P11-1).
builder.Services.AddTenderoUcp(builder.Configuration);

builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddExceptionHandler<SearchUnavailableExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSyntheticTrafficLabels();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapDefaultEndpoints();

// Always served, not only in Development: the contract test reads it from here,
// and an agent discovering the shop over UCP needs to reach it in production.
app.MapOpenApi();

app.MapCarter();

// After MapCarter and outside it: Carter mounts the slices, and MCP is not a
// slice. It is a second transport over the queries those slices already answer.
app.MapTenderoMcp();

// And the discovery profile the MCP binding is reached through. It is composed
// from the capability registry on every request, so it cannot describe a shop
// this process is not.
app.MapTenderoUcp();

// And UCP's REST binding, the second transport of the same capability. Its paths
// are the specification's; only the `/ucp` prefix is ours.
app.MapUcpCatalogRest();

// And checkout's REST binding (P11-1): the same prefix, an agent's only.
app.MapUcpCheckoutRest();

if (app.Environment.IsDevelopment())
    app.MapDevelopmentPlatformProfiles();

app.Run();

// Visible to the integration tests (WebApplicationFactory).
public partial class Program;
