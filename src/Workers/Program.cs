using Tendero.Persistence;
using Tendero.Search.Elasticsearch;
using Tendero.Search.Features.ProjectProductToIndex;
using Tendero.ServiceDefaults;
using Tendero.SharedKernel;
using Tendero.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));

// Sólo hace falta el ensamblado de Search: los handlers de eventos de dominio
// que existen hoy son sus proyecciones al índice.
builder.Services.AddTenderoCqrs(typeof(ProjectProductOnUpserted).Assembly);

builder.Services.AddTenderoPersistence(
    builder.Configuration.GetConnectionString("tendero-db")
    ?? throw new InvalidOperationException("Connection string 'tendero-db' is missing."));

builder.Services.AddLexicalSearch(
    builder.Configuration.GetConnectionString("elasticsearch")
    ?? throw new InvalidOperationException("Connection string 'elasticsearch' is missing."));

// Un solo sitio crea products_es/products_en, y es este.
builder.Services.AddSearchIndexInitializer();

if (builder.Environment.IsDevelopment())
    builder.Services.AddHostedService<DevelopmentSchemaInitializer>();

builder.Services.AddHostedService<OutboxProcessor>();

var host = builder.Build();
host.Run();
