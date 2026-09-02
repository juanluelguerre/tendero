using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Cómo construye <c>dotnet ef</c> el contexto sin arrancar la aplicación.
///
/// La alternativa habitual — <c>--startup-project src/Api</c> — no sirve aquí:
/// la API exige dos cadenas de conexión al arrancar y las inyecta el AppHost,
/// así que las herramientas de diseño fallarían con un error de configuración
/// que no tiene nada que ver con el esquema.
///
/// La cadena de abajo NUNCA se conecta. Generar y comparar migraciones sólo
/// necesita el proveedor para saber traducir el modelo a SQL de PostgreSQL;
/// para aplicarlas de verdad está <c>MigrateAsync</c>, con la cadena real.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TenderoDbContext>
{
    public TenderoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenderoDbContext>()
            .UseNpgsql("Host=localhost;Database=tendero-design-time")
            .Options;

        return new TenderoDbContext(options);
    }
}
