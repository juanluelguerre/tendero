using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Search.Features.ProjectProductToIndex;
using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// ValidateOnStart: una configuración imposible mata el arranque con un mensaje
// legible, en vez de reventar dentro del BackgroundService donde nadie mira.
builder.Services.AddOptions<OutboxOptions>()
    .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
    .Validate(options => options.PollInterval > TimeSpan.Zero, "Outbox:PollInterval must be positive.")
    .Validate(options => options.BatchSize > 0, "Outbox:BatchSize must be positive.")
    .Validate(options => options.MaxAttempts > 0, "Outbox:MaxAttempts must be positive.")
    .ValidateOnStart();

// Sólo hace falta el ensamblado de Search: los handlers de eventos de dominio
// que existen hoy son sus proyecciones al índice.
builder.Services.AddTenderoCqrs(typeof(ProjectProductOnUpserted).Assembly);

builder.Services.AddTenderoPersistence(
    builder.Configuration.GetRequiredConnectionString("tendero-db"));

builder.Services.AddLexicalSearch(
    builder.Configuration.GetRequiredConnectionString("elasticsearch"));

// Un solo sitio crea products_es/products_en, y es este.
builder.Services.AddSearchIndexInitializer();

if (builder.Environment.IsDevelopment())
    builder.Services.AddHostedService<DevelopmentSchemaInitializer>();

builder.Services.AddHostedService<OutboxProcessor>();

var host = builder.Build();
host.Run();
