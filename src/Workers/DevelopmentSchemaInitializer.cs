using ElGuerre.Tendero.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Workers;

/// <summary>
/// En desarrollo la base se crea desde el modelo para que `dotnet run --project
/// src/AppHost` funcione recién clonado el repo. Las migraciones llegan cuando
/// exista un esquema que preservar; hasta entonces serían ceremonia vacía.
/// </summary>
public sealed class DevelopmentSchemaInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<DevelopmentSchemaInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();

        if (await context.Database.EnsureCreatedAsync(cancellationToken))
            logger.LogInformation("Development schema created from the EF model");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
