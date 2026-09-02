using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Search.Features.ProjectProductToIndex;
using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Workers;

/// <summary>
/// Everything the worker registers, in a method a test can call.
///
/// It sat loose in <c>Program.cs</c>, which meant **nothing ever checked that
/// its container could be built**. It showed up the worst way: the indexer came
/// to need the attribute definitions, the API registered them because it calls
/// <c>AddCatalog</c>, the worker did not, and the process stopped starting. The
/// build was green and so were the tests, because the integration ones compose
/// their own container.
/// </summary>
public static class WorkerServices
{
    public static IHostApplicationBuilder AddTenderoWorker(this IHostApplicationBuilder builder)
    {
        // ValidateOnStart: an impossible configuration kills startup with a
        // readable message, instead of blowing up inside the BackgroundService
        // where nobody is looking.
        builder.Services.AddOptions<OutboxOptions>()
            .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
            .Validate(options => options.PollInterval > TimeSpan.Zero, "Outbox:PollInterval must be positive.")
            .Validate(options => options.BatchSize > 0, "Outbox:BatchSize must be positive.")
            .Validate(options => options.MaxAttempts > 0, "Outbox:MaxAttempts must be positive.")
            .ValidateOnStart();

        // Only Search's assembly is needed: the domain event handlers that exist
        // today are its projections into the index.
        builder.Services.AddTenderoCqrs(typeof(ProjectProductOnUpserted).Assembly);

        builder.Services.AddTenderoPersistence(
            builder.Configuration.GetRequiredConnectionString("tendero-db"));

        // The catalogue's READ half, and nothing else: the worker projects
        // products, it does not import them. It has no connectors and no image
        // store because it does not need them — but without the definitions it
        // cannot render "navy blue" into the English index, and without the
        // categories it cannot write the branch.
        builder.Services.AddCatalogReaders(builder.Configuration);

        builder.Services.AddLexicalSearch(
            builder.Configuration.GetRequiredConnectionString("elasticsearch"));

        // One single place creates products_es/products_en, and this is it.
        builder.Services.AddSearchIndexInitializer();

        if (builder.Environment.IsDevelopment())
            builder.Services.AddHostedService<SchemaMigrator>();

        builder.Services.AddHostedService<OutboxProcessor>();

        return builder;
    }
}
