using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Search.Features.ProjectProductToIndex;
using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Workers;

/// <summary>
/// Todo lo que el worker registra, en un método al que se puede llamar desde un
/// test.
///
/// Vivía suelto en <c>Program.cs</c>, y eso significaba que **nada comprobaba
/// nunca que su contenedor se pudiera construir**. Se notó de la peor manera:
/// el indexador pasó a necesitar las definiciones de atributo, la API las
/// registraba porque llama a <c>AddCatalog</c>, el worker no, y el proceso
/// dejó de arrancar. El build estaba verde y los tests también, porque los de
/// integración montan su propio contenedor.
/// </summary>
public static class WorkerServices
{
    public static IHostApplicationBuilder AddTenderoWorker(this IHostApplicationBuilder builder)
    {
        // ValidateOnStart: una configuración imposible mata el arranque con un
        // mensaje legible, en vez de reventar dentro del BackgroundService donde
        // nadie mira.
        builder.Services.AddOptions<OutboxOptions>()
            .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
            .Validate(options => options.PollInterval > TimeSpan.Zero, "Outbox:PollInterval must be positive.")
            .Validate(options => options.BatchSize > 0, "Outbox:BatchSize must be positive.")
            .Validate(options => options.MaxAttempts > 0, "Outbox:MaxAttempts must be positive.")
            .ValidateOnStart();

        // Sólo hace falta el ensamblado de Search: los handlers de eventos de
        // dominio que existen hoy son sus proyecciones al índice.
        builder.Services.AddTenderoCqrs(typeof(ProjectProductOnUpserted).Assembly);

        builder.Services.AddTenderoPersistence(
            builder.Configuration.GetRequiredConnectionString("tendero-db"));

        // La mitad de LECTURA del catálogo, y nada más: el worker proyecta
        // productos, no los importa. No tiene conectores ni almacén de imágenes
        // porque no los necesita — pero sin las definiciones no puede renderizar
        // "navy blue" en el índice inglés, y sin las categorías no puede escribir
        // la rama.
        builder.Services.AddCatalogReaders(builder.Configuration);

        builder.Services.AddLexicalSearch(
            builder.Configuration.GetRequiredConnectionString("elasticsearch"));

        // Un solo sitio crea products_es/products_en, y es este.
        builder.Services.AddSearchIndexInitializer();

        if (builder.Environment.IsDevelopment())
            builder.Services.AddHostedService<SchemaMigrator>();

        builder.Services.AddHostedService<OutboxProcessor>();

        return builder;
    }
}
