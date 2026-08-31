using Carter;
using Tendero.Api;
using Tendero.Catalog;
using Tendero.Catalog.Features.ImportProducts;
using Tendero.Persistence;
using Tendero.Search.Elasticsearch;
using Tendero.Search.Features.SearchProducts;
using Tendero.ServiceDefaults;
using Tendero.SharedKernel;

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

builder.Services.AddCarter();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<SearchUnavailableExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapCarter();

app.Run();

// Visible para los tests de integración (WebApplicationFactory) más adelante.
public partial class Program;
