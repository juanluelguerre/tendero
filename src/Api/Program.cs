using Carter;
using ElGuerre.Tendero.Api;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Los ensamblados que se escanean en busca de handlers y validadores. Un slice
// nuevo dentro de uno de ellos no toca esta línea.
builder.Services.AddTenderoCqrs(
    typeof(ImportProductsCommand).Assembly,
    typeof(SearchProductsQuery).Assembly);

builder.Services.AddTenderoPersistence(
    builder.Configuration.GetRequiredConnectionString("tendero-db"));

builder.Services.AddCatalog(builder.Configuration);

builder.Services.AddLexicalSearch(
    builder.Configuration.GetRequiredConnectionString("elasticsearch"));

// WithEmptyValidators: Carter escanea validadores y los registra como SINGLETON,
// y aquí no se usa ninguno — la validación vive en el ValidationStep del
// dispatcher, que los resuelve del scope. Ese escaneo no era neutro: convertía
// `ImportProductsValidator`, que inyecta el `ICatalogSourceRegistry` scoped, en
// una dependencia cautiva, y la API dejaba de arrancar en Development con
// "Cannot consume scoped service ... from singleton". Un mecanismo que nadie usa
// no debería poder tumbar el proceso.
builder.Services.AddCarter(configurator: carter => carter.WithEmptyValidators());

// El documento es la forma de la API, y de él salen los tipos del frontend
// (antes escritos a mano en shared-api, sin nada que detectase la deriva) y,
// en la fase 9, los esquemas de las capabilities de UCP. Va en el framework:
// ni Swashbuckle ni NSwag, que serían dos dependencias para lo mismo.
builder.Services.AddOpenApi(options =>
    options.AddSchemaTransformer<NumbersAreNumbersTransformer>());

builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<SearchUnavailableExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();

// Servido siempre, no sólo en Development: el test de contrato lo lee de aquí,
// y un agente que descubra la tienda por UCP necesita alcanzarlo en producción.
app.MapOpenApi();

app.MapCarter();

app.Run();

// Visible para los tests de integración (WebApplicationFactory) más adelante.
public partial class Program;
