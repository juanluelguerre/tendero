using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// El contenedor del worker se puede construir.
///
/// Suena trivial y es el test que faltaba. El indexador pasó a necesitar las
/// definiciones de atributo; la API las registra porque llama a
/// <c>AddCatalog</c>, el worker no las registraba, y el proceso dejó de
/// arrancar con "Unable to resolve service for type
/// IAttributeDefinitionReader". El build estaba verde, los 149 tests también, y
/// los de integración no lo vieron porque montan su propio contenedor con lo
/// que necesitan.
///
/// Es la segunda vez que un fallo de composición pasa desapercibido — la
/// primera dejó la API sin arrancar día y medio. La diferencia entre las dos es
/// que aquella la encontró un test escrito para otra cosa, y ésta la encontró
/// el usuario al pulsar F5.
///
/// No hace falta ni Postgres ni Elasticsearch: construir el contenedor no abre
/// una conexión, y lo que se comprueba es la composición.
/// </summary>
public sealed class WorkerContainerTests
{
    [Fact]
    public void The_worker_container_builds()
    {
        using var provider = BuildWorkerServices();

        // ValidateOnBuild recorre TODOS los descriptores en vez de esperar a que
        // alguien resuelva el que falta, que es lo que convierte esto en una
        // comprobación y no en un muestreo.
        Assert.NotNull(provider);
    }

    /// <summary>
    /// Lo que el worker existe para hacer: proyectar productos al índice. Si
    /// alguno de los tres deja de resolverse, el outbox drena a ninguna parte.
    /// </summary>
    [Theory]
    [InlineData(typeof(IProductIndexer))]
    [InlineData(typeof(IDomainEventHandler<ProductUpserted>))]
    [InlineData(typeof(IDomainEventHandler<ProductArchived>))]
    public void Everything_the_outbox_dispatches_to_can_be_resolved(Type service)
    {
        using var provider = BuildWorkerServices();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService(service));
    }

    private static ServiceProvider BuildWorkerServices()
    {
        var builder = Host.CreateApplicationBuilder();

        // Las dos cadenas que el worker exige al arrancar. Ninguna se usa: ni el
        // DbContext ni el cliente de Elastic conectan al construirse.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:tendero-db"] = "Host=localhost;Database=worker-container-test",
            ["ConnectionStrings:elasticsearch"] = "http://localhost:9200"
        });

        builder.AddTenderoWorker();

        return builder.Services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
