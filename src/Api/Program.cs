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
