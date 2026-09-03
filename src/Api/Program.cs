using Carter;
using ElGuerre.Tendero.Api;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.DevIssuer;
using ElGuerre.Tendero.Inventory;
using ElGuerre.Tendero.Inventory.Features.ListStock;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Features.Checkout;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Pricing;
using ElGuerre.Tendero.Pricing.Features.QuoteCart;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The assemblies scanned for handlers and validators. A new slice inside one of
// them does not touch this line.
builder.Services.AddTenderoCqrs(
    typeof(ImportProductsCommand).Assembly,
    typeof(SearchProductsQuery).Assembly,
    typeof(QuoteCartQuery).Assembly,
    typeof(ListStockQuery).Assembly,
    typeof(PlaceOrderCommand).Assembly);

builder.Services.AddTenderoPersistence(
    builder.Configuration.GetRequiredConnectionString("tendero-db"));

builder.Services.AddCatalog(builder.Configuration);

builder.Services.AddPricing(builder.Configuration);

builder.Services.AddInventory(builder.Configuration);

builder.Services.AddOrdering(builder.Configuration);

builder.Services.AddLexicalSearch(
    builder.Configuration.GetRequiredConnectionString("elasticsearch"));

// WithEmptyValidators: Carter scans for validators and registers them as
// SINGLETONS, and none is used here — validation lives in the dispatcher's
// ValidationStep, which resolves them from the scope. That scan was not neutral:
// it turned `ImportProductsValidator`, which injects the scoped
// `ICatalogSourceRegistry`, into a captive dependency, and the API stopped
// starting in Development with "Cannot consume scoped service … from singleton".
// A mechanism nobody uses should not be able to bring the process down.
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
}

builder.Services.AddTenderoAuthentication(builder.Configuration);

// CORS: the two Angular apps run on a different origin from the API, so without
// this the browser blocks every authenticated call.
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
        ?? ["http://localhost:4200", "http://localhost:4201"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddCarter(configurator: carter => carter.WithEmptyValidators());

// The document is the API's shape, and from it come the frontend's types
// (previously hand-written in shared-api, with nothing to detect the drift) and,
// in phase 9, the UCP capability schemas. It lives in the framework: neither
// Swashbuckle nor NSwag, which would be two dependencies for one job.
builder.Services.AddOpenApi(options =>
    options.AddSchemaTransformer<NumbersAreNumbersTransformer>());

builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<SearchUnavailableExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();

// Always served, not only in Development: the contract test reads it from here,
// and an agent discovering the shop over UCP needs to reach it in production.
app.MapOpenApi();

app.MapCarter();

app.Run();

// Visible to the integration tests (WebApplicationFactory).
public partial class Program;
