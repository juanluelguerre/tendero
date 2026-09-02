using ElGuerre.Tendero.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Workers;

/// <summary>
/// Aplica las migraciones pendientes al arrancar, para que
/// <c>dotnet run --project src/AppHost</c> siga funcionando recién clonado el
/// repositorio.
///
/// Antes esto era <c>EnsureCreatedAsync</c>, con una nota diciendo que las
/// migraciones llegarían cuando hubiera un esquema que preservar. El problema es
/// que <c>EnsureCreated</c> **no hace nada si el esquema ya existe**: no
/// compara, no avisa, no falla. El primer cambio aditivo del modelo habría
/// dejado toda base de datos de desarrollo ya creada en silencio incorrecta, y
/// el síntoma habría aparecido mucho más tarde, como una columna que no existe.
/// Es la misma clase de fallo que documenta ADR 0012: una promesa escrita sin
/// nada que la ejecute.
///
/// Sigue restringido a Development. En producción aplicar migraciones al
/// arrancar es una decisión de despliegue, no del proceso — y este proyecto no
/// tiene aún un destino de despliegue sobre el que decidirlo.
/// </summary>
public sealed class SchemaMigrator(
    IServiceScopeFactory scopeFactory,
    ILogger<SchemaMigrator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length == 0)
            return;

        // Nombrarlas al aplicarlas: cuando algo salga mal, lo primero que se
        // quiere saber es qué migración se estaba aplicando.
        logger.LogInformation(
            "Applying {Count} pending migration(s): {Migrations}", pending.Length, string.Join(", ", pending));

        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
